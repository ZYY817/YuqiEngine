using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Infrastructure.Windows.Registry;

public sealed record PerInterfaceRegistryTargetValue(
    string ValueName,
    PerInterfaceScalarValueKind Kind,
    string? StringValue,
    int? DwordValue)
{
    public static PerInterfaceRegistryTargetValue String(string valueName, string value) =>
        new(valueName, PerInterfaceScalarValueKind.String, value, null);

    public static PerInterfaceRegistryTargetValue DWord(string valueName, int value) =>
        new(valueName, PerInterfaceScalarValueKind.DWord, null, value);
}

public sealed record PerInterfaceRegistryCapabilityDefinition(
    string CapabilityId,
    string DefinitionVersion,
    string RootPath,
    IReadOnlyList<PerInterfaceRegistryTargetValue> Targets,
    ChangeRisk Risk,
    bool RequiresExplicitConfirmation = false,
    bool RequiresRestart = false,
    string? ResourceKey = null,
    // 可选厂商过滤器：设置后仅对“选择器值”命中所含任一子串的子键生效（如 GPU 类根下
    // ProviderName 含 AMD/ATI）。未设置时保持原有“全部子键”语义，行为不变。
    string? VendorSelectorValueName = null,
    IReadOnlyList<string>? VendorSelectorContains = null);

public sealed record PerInterfaceRegistryPayload(IReadOnlyList<PerInterfaceValueState> Values);

internal sealed class PerInterfaceRegistryCodec
{
    private const string StringKind = "String";
    private const string DwordKind = "DWord";

    private readonly PerInterfaceRegistryTargetValue[] _targets;

    public PerInterfaceRegistryCodec(IReadOnlyList<PerInterfaceRegistryTargetValue> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Count < 1)
        {
            throw new ArgumentException("每接口注册表能力至少需要一个目标值。", nameof(targets));
        }

        _targets = targets
            .Select(CloneAndValidateTarget)
            .OrderBy(target => target.ValueName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(target => target.ValueName, StringComparer.Ordinal)
            .ToArray();
        if (_targets.Select(target => target.ValueName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != _targets.Length)
        {
            throw new ArgumentException("每接口注册表能力不能包含重复值名。", nameof(targets));
        }
    }

    public IReadOnlyList<PerInterfaceRegistryTargetValue> Targets => _targets;

    public PerInterfaceRegistryPayload ParseCanonicalPayload(string payload)
    {
        var parsed = JsonSerializer.Deserialize<PerInterfaceRegistryPayload>(payload)
            ?? throw new InvalidDataException("每接口注册表 payload 无法解析。");
        var normalized = NormalizeAndValidatePayload(parsed);
        if (!string.Equals(normalized, payload, StringComparison.Ordinal))
        {
            throw new InvalidDataException("每接口注册表 payload 不是规范序列化格式。");
        }

        return JsonSerializer.Deserialize<PerInterfaceRegistryPayload>(normalized)!;
    }

    public string NormalizeAndValidatePayload(PerInterfaceRegistryPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload.Values);
        var values = payload.Values
            .OrderBy(value => value.SubKeyName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.SubKeyName, StringComparer.Ordinal)
            .ThenBy(value => value.ValueName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.ValueName, StringComparer.Ordinal)
            .ToArray();

        foreach (var value in values)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value.SubKeyName);
            ArgumentException.ThrowIfNullOrWhiteSpace(value.ValueName);
            if (!_targets.Any(target => string.Equals(target.ValueName, value.ValueName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException($"每接口注册表状态包含定义外的值 {value.ValueName}。");
            }

            ValidateEntryState(value);
        }

        return Serialize(new PerInterfaceRegistryPayload(values));
    }

    public void EnsureObservedKindsMatchDefinition(PerInterfaceRegistryPayload payload)
    {
        foreach (var value in payload.Values)
        {
            if (!value.ValueExists)
            {
                continue;
            }

            var target = _targets.First(target => string.Equals(target.ValueName, value.ValueName, StringComparison.OrdinalIgnoreCase));
            if (!string.Equals(value.ValueKind, KindName(target.Kind), StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"每接口注册表值 {value.SubKeyName}\\{value.ValueName} 的实际类型为 {value.ValueKind}，目标要求 {KindName(target.Kind)}，已阻断隐式覆盖。");
            }
        }
    }

    public string BuildTargetPayload(IReadOnlyList<string> subKeyNames) =>
        Serialize(new PerInterfaceRegistryPayload(
            subKeyNames
                .SelectMany(subKey => _targets.Select(target => ToTargetState(subKey, target)))
                .ToArray()));

    public static string Fingerprint(string payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

    private static PerInterfaceValueState ToTargetState(string subKeyName, PerInterfaceRegistryTargetValue target) =>
        new(subKeyName, target.ValueName, true, KindName(target.Kind), target.StringValue, target.DwordValue);

    private static PerInterfaceRegistryTargetValue CloneAndValidateTarget(PerInterfaceRegistryTargetValue target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target.ValueName);
        var valid = target.Kind switch
        {
            PerInterfaceScalarValueKind.String => target.StringValue is not null && target.DwordValue is null,
            PerInterfaceScalarValueKind.DWord => target.StringValue is null && target.DwordValue is not null,
            _ => false
        };
        if (!valid)
        {
            throw new ArgumentException($"目标 {target.ValueName} 的类型与载荷不一致。", nameof(target));
        }

        return target with { };
    }

    private static void ValidateEntryState(PerInterfaceValueState state)
    {
        if (!state.ValueExists)
        {
            if (state.ValueKind is not null || state.StringValue is not null || state.DwordValue is not null)
            {
                throw new InvalidDataException($"每接口注册表值 {state.ValueName} 声明不存在但仍包含类型或内容。");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(state.ValueKind))
        {
            throw new InvalidDataException($"每接口注册表值 {state.ValueName} 存在但缺少值类型。");
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
            throw new InvalidDataException($"每接口注册表值 {state.ValueName} 的类型与内容不一致。");
        }
    }

    private static string KindName(PerInterfaceScalarValueKind kind) => kind switch
    {
        PerInterfaceScalarValueKind.String => StringKind,
        PerInterfaceScalarValueKind.DWord => DwordKind,
        _ => throw new InvalidDataException($"不支持的每接口注册表类型：{kind}。")
    };

    private static string Serialize(PerInterfaceRegistryPayload payload) => JsonSerializer.Serialize(payload);
}
