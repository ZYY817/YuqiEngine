using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GameOptimizer.Infrastructure.Windows.Registry;

internal sealed class RegistryValueSetPayloadCodec
{
    private const string StringKind = "String";
    private const string DwordKind = "DWord";
    private readonly IReadOnlyDictionary<string, RegistryTypedTargetConstraint> _constraints;

    public RegistryValueSetPayloadCodec(
        IReadOnlyList<RegistryTypedTargetValue> targets,
        IReadOnlyList<RegistryTypedTargetConstraint>? constraints)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Count < 2)
        {
            throw new ArgumentException("同键多值能力至少需要两个目标值。", nameof(targets));
        }

        Targets = targets
            .Select(CloneAndValidateTarget)
            .OrderBy(target => target.ValueName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(target => target.ValueName, StringComparer.Ordinal)
            .ToArray();
        if (Targets.Select(target => target.ValueName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Targets.Count)
        {
            throw new ArgumentException("同键多值能力不能包含重复值名。", nameof(targets));
        }

        _constraints = BuildConstraints(constraints, Targets);
        ValidateTargetRanges(Targets);
        ValueNames = Targets.Select(target => target.ValueName).ToArray();
        DefaultTargetPayload = Serialize(new RegistryValueSetPayload(Targets.Select(ToTargetState).ToArray()));
        DefaultTargetFingerprint = Fingerprint(DefaultTargetPayload);
    }

    public IReadOnlyList<RegistryTypedTargetValue> Targets { get; }
    public IReadOnlyList<string> ValueNames { get; }
    public string DefaultTargetPayload { get; }
    public string DefaultTargetFingerprint { get; }
    public IReadOnlyList<RegistryTypedTargetConstraint> Constraints =>
        _constraints.Values.OrderBy(value => value.ValueName, StringComparer.OrdinalIgnoreCase).ToArray();

    public RegistryValueSetPayload ParseCanonicalPayload(string payload)
    {
        var parsed = JsonSerializer.Deserialize<RegistryValueSetPayload>(payload)
            ?? throw new InvalidDataException("同键多值注册表 payload 无法解析。");
        var normalized = NormalizeAndValidatePayload(parsed);
        if (!string.Equals(normalized, payload, StringComparison.Ordinal))
        {
            throw new InvalidDataException("同键多值注册表 payload 不是规范序列化格式。");
        }

        return JsonSerializer.Deserialize<RegistryValueSetPayload>(normalized)!;
    }

    public string NormalizeAndValidatePayload(RegistryValueSetPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload.Values);
        var values = payload.Values
            .OrderBy(value => value.ValueName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.ValueName, StringComparer.Ordinal)
            .ToArray();
        if (values.Length != Targets.Count
            || values.Select(value => value.ValueName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Length)
        {
            throw new InvalidDataException("同键多值状态的值数量或唯一性不符合能力定义。");
        }

        var keyExists = values[0].KeyExists;
        for (var index = 0; index < values.Length; index++)
        {
            var state = values[index];
            if (!string.Equals(state.ValueName, Targets[index].ValueName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("同键多值状态包含定义外的注册表值。");
            }

            if (state.KeyExists != keyExists || (!state.KeyExists && state.ValueExists))
            {
                throw new InvalidDataException("同键多值状态中的键存在性不一致。");
            }

            ValidateEntryState(state);
        }

        return Serialize(new RegistryValueSetPayload(values));
    }

    public void EnsureObservedKindsMatchDefinition(RegistryValueSetPayload payload)
    {
        var states = payload.Values.ToDictionary(value => value.ValueName, StringComparer.OrdinalIgnoreCase);
        foreach (var target in Targets)
        {
            var state = states[target.ValueName];
            if (state.ValueExists && !string.Equals(state.ValueKind, KindName(target.Kind), StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"注册表值 {state.ValueName} 的实际类型为 {state.ValueKind}，目标要求 {KindName(target.Kind)}，已阻断隐式覆盖。");
            }
        }
    }

    public IReadOnlyList<RegistryTypedTargetValue> ValidateFrozenTarget(
        string targetPayload,
        string targetFingerprint)
    {
        if (!string.Equals(Fingerprint(targetPayload), targetFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidDataException("执行计划中的目标载荷与 fingerprint 不一致。");
        }

        var targets = ParseTargetValues(targetPayload);
        ValidateTargetRanges(targets);
        return targets;
    }

    public (string Payload, string Fingerprint) CreateFrozenTarget(string requestPayload)
    {
        var request = JsonSerializer.Deserialize<RegistryValueSetTargetRequest>(requestPayload)
            ?? throw new InvalidDataException("动态目标请求无法解析。");
        ArgumentNullException.ThrowIfNull(request.Values);
        var targets = request.Values
            .Select(CloneAndValidateTarget)
            .OrderBy(target => target.ValueName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(target => target.ValueName, StringComparer.Ordinal)
            .ToArray();
        ValidateTargetRanges(targets);
        var payload = Serialize(new RegistryValueSetPayload(targets.Select(ToTargetState).ToArray()));
        return (payload, Fingerprint(payload));
    }

    public static string Fingerprint(string payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

    public static RegistryTypedTargetValue ToTargetValue(
        RegistryValueSetEntryState state,
        RegistryScalarValueKind? declaredKind = null) =>
        !state.ValueExists
            ? declaredKind is null
                ? throw new InvalidDataException($"删除目标 {state.ValueName} 缺少声明类型。")
                : RegistryTypedTargetValue.Delete(state.ValueName, declaredKind.Value)
            : string.Equals(state.ValueKind, StringKind, StringComparison.Ordinal)
            ? RegistryTypedTargetValue.String(state.ValueName, state.StringValue!)
            : string.Equals(state.ValueKind, DwordKind, StringComparison.Ordinal)
                ? RegistryTypedTargetValue.DWord(state.ValueName, state.DwordValue!.Value)
                : throw new InvalidDataException($"注册表值 {state.ValueName} 不是可写的受支持类型。");

    private IReadOnlyList<RegistryTypedTargetValue> ParseTargetValues(string payload)
    {
        var states = ParseCanonicalPayload(payload).Values;
        EnsureObservedKindsMatchDefinition(new RegistryValueSetPayload(states));
        return states.Select((state, index) => ToTargetValue(state, Targets[index].Kind)).ToArray();
    }

    private void ValidateTargetRanges(IReadOnlyList<RegistryTypedTargetValue> targets)
    {
        if (targets.Count != Targets.Count
            || targets.Select(target => target.ValueName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != targets.Count)
        {
            throw new InvalidDataException("动态目标必须完整且不能包含重复值。");
        }

        for (var index = 0; index < targets.Count; index++)
        {
            var target = targets[index];
            if (!string.Equals(target.ValueName, Targets[index].ValueName, StringComparison.OrdinalIgnoreCase)
                || target.Kind != Targets[index].Kind)
            {
                throw new InvalidDataException("动态目标的值名或类型与能力定义不一致。");
            }

            var constraint = _constraints[target.ValueName];
            if (constraint.MinimumNumericValue is null && constraint.MaximumNumericValue is null)
            {
                continue;
            }

            if (target.Action == RegistryTargetAction.Delete)
            {
                continue;
            }

            var numericValue = target.Kind switch
            {
                RegistryScalarValueKind.DWord => target.DwordValue,
                RegistryScalarValueKind.String when int.TryParse(
                    target.StringValue,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var parsed) => parsed,
                _ => null
            };
            if (numericValue is null
                || (constraint.MinimumNumericValue is not null && numericValue < constraint.MinimumNumericValue)
                || (constraint.MaximumNumericValue is not null && numericValue > constraint.MaximumNumericValue))
            {
                throw new InvalidDataException(
                    $"动态目标 {target.ValueName} 超出允许范围 {constraint.MinimumNumericValue}..{constraint.MaximumNumericValue}。");
            }
        }
    }

    private static IReadOnlyDictionary<string, RegistryTypedTargetConstraint> BuildConstraints(
        IReadOnlyList<RegistryTypedTargetConstraint>? constraints,
        IReadOnlyList<RegistryTypedTargetValue> targets)
    {
        var result = (constraints ?? [])
            .ToDictionary(constraint => constraint.ValueName, StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            if (!result.TryGetValue(target.ValueName, out var constraint))
            {
                result[target.ValueName] = new RegistryTypedTargetConstraint(target.ValueName, target.Kind);
                continue;
            }

            if (constraint.Kind != target.Kind
                || (constraint.MinimumNumericValue is not null
                    && constraint.MaximumNumericValue is not null
                    && constraint.MinimumNumericValue > constraint.MaximumNumericValue))
            {
                throw new ArgumentException($"目标 {target.ValueName} 的范围约束无效。", nameof(constraints));
            }
        }

        if (result.Count != targets.Count)
        {
            throw new ArgumentException("范围约束包含能力定义外的值名。", nameof(constraints));
        }

        return result;
    }

    private static RegistryValueSetEntryState ToTargetState(RegistryTypedTargetValue target) =>
        target.Action == RegistryTargetAction.Delete
            ? new(target.ValueName, true, false, null, null, null)
            : new(target.ValueName, true, true, KindName(target.Kind), target.StringValue, target.DwordValue);

    private static RegistryTypedTargetValue CloneAndValidateTarget(RegistryTypedTargetValue target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target.ValueName);
        var valid = target.Action switch
        {
            RegistryTargetAction.Delete => target.StringValue is null && target.DwordValue is null,
            RegistryTargetAction.Set when target.Kind == RegistryScalarValueKind.String =>
                target.StringValue is not null && target.DwordValue is null,
            RegistryTargetAction.Set when target.Kind == RegistryScalarValueKind.DWord =>
                target.StringValue is null && target.DwordValue is not null,
            _ => false
        };
        if (!valid)
        {
            throw new ArgumentException($"目标 {target.ValueName} 的类型与载荷不一致。", nameof(target));
        }

        return target with { };
    }

    private static void ValidateEntryState(RegistryValueSetEntryState state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state.ValueName);
        if (!state.ValueExists)
        {
            if (state.ValueKind is not null || state.StringValue is not null || state.DwordValue is not null)
            {
                throw new InvalidDataException($"注册表值 {state.ValueName} 声明不存在但仍包含类型或内容。");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(state.ValueKind))
        {
            throw new InvalidDataException($"注册表值 {state.ValueName} 存在但缺少值类型。");
        }

        var validSupported = string.Equals(state.ValueKind, StringKind, StringComparison.Ordinal)
            ? state.StringValue is not null && state.DwordValue is null
            : string.Equals(state.ValueKind, DwordKind, StringComparison.Ordinal)
              && state.StringValue is null
              && state.DwordValue is not null;
        var validUnsupported = !string.Equals(state.ValueKind, StringKind, StringComparison.Ordinal)
            && !string.Equals(state.ValueKind, DwordKind, StringComparison.Ordinal)
            && state.StringValue is null
            && state.DwordValue is null;
        if (!validSupported && !validUnsupported)
        {
            throw new InvalidDataException($"注册表值 {state.ValueName} 的类型与内容不一致。");
        }
    }

    private static string KindName(RegistryScalarValueKind kind) => kind switch
    {
        RegistryScalarValueKind.String => StringKind,
        RegistryScalarValueKind.DWord => DwordKind,
        _ => throw new InvalidDataException($"不支持的注册表类型：{kind}。")
    };

    private static string Serialize(RegistryValueSetPayload payload) => JsonSerializer.Serialize(payload);
}
