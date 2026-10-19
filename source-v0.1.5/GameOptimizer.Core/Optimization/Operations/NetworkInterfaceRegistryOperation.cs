using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace GameOptimizer.Core.Optimization.Operations;

public sealed class NetworkInterfaceRegistryOperation : IOptimizationOperation
{
    private const string InterfacesRoot = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";

    public readonly record struct InterfaceValueDefinition(string Name, int DesiredValue);

    private record ValueSnapshot(
        string InterfaceKey,
        string ValueName,
        bool ValueExisted,
        string? SerializedValue = null,
        RegistryValueKind? OriginalKind = null,
        int? PreviousValue = null);

    private record OperationSnapshot(IReadOnlyList<ValueSnapshot> Entries);
    private record TargetEntry(string InterfaceKey, InterfaceValueDefinition Definition);
    private record OperationPlan(IReadOnlyList<ValueSnapshot> Snapshots, IReadOnlyList<TargetEntry> ChangedTargets);

    private readonly IReadOnlyList<InterfaceValueDefinition> _definitions;

    public NetworkInterfaceRegistryOperation(
        string id,
        string title,
        string description,
        string category,
        IEnumerable<InterfaceValueDefinition> definitions)
    {
        Id = id;
        Title = title;
        Description = description;
        Category = category;
        _definitions = definitions.ToArray();
    }

    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public string Category { get; }
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Balanced;
    public string? Benefit { get; init; }
    public string? Drawback { get; init; }
    public OperationType OperationType => OperationType.Registry;
    public string RegistryHiveText => "HKLM";
    public string RegistryPath => InterfacesRoot;
    public string RegistryValueName => string.Join(";", _definitions.Select(static definition => definition.Name));
    public bool RequiresAdmin => true;
    public bool RequiresSnapshotForRestore => true;

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var interfaces = GetCandidateInterfaces();
        if (interfaces.Count == 0)
        {
            return Task.FromResult(new OperationStatus(true, "No active network interfaces matched the optimization scope."));
        }

        var nonCompliant = 0;
        using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
        foreach (var interfaceKey in interfaces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var key = hive.OpenSubKey($@"{InterfacesRoot}\{interfaceKey}", writable: false);
            if (key is null)
            {
                nonCompliant++;
                continue;
            }

            var matched = true;
            foreach (var definition in _definitions)
            {
                var current = ReadValue(key, definition.Name);
                if (!IsDesiredState(current, definition))
                {
                    matched = false;
                    break;
                }
            }

            if (!matched)
            {
                nonCompliant++;
            }
        }

        var compliant = nonCompliant == 0;
        var details = compliant
            ? "All network interface registry values match the target state."
            : string.Concat(nonCompliant, "/", interfaces.Count, " interface sets have not reached the target state.");
        return Task.FromResult(new OperationStatus(compliant, details));
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var interfaces = GetCandidateInterfaces();
        if (interfaces.Count == 0)
        {
            return new OperationInvokeResult(true, false, "No network interfaces require optimization.");
        }

        OperationPlan plan;
        try
        {
            plan = BuildOperationPlan(interfaces, cancellationToken);
        }
        catch (Exception ex)
        {
            return new OperationInvokeResult(false, false, string.Concat("Failed to capture network interface snapshots. ", ex.Message));
        }

        if (plan.ChangedTargets.Count == 0)
        {
            return new OperationInvokeResult(true, false, "All network interface registry values are already at the target state.");
        }

        var snapshot = new OperationSnapshot(plan.Snapshots);
        await context.SaveSnapshotAsync(Id, snapshot, cancellationToken).ConfigureAwait(false);

        try
        {
            foreach (var target in plan.ChangedTargets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!WriteDesiredValue(target.InterfaceKey, target.Definition))
                {
                    return await BuildApplyFailureResultAsync(
                        context,
                        snapshot,
                        string.Concat("Failed to apply network interface value ", target.Definition.Name, "."),
                        cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            return await BuildApplyFailureResultAsync(
                context,
                snapshot,
                string.Concat("An exception occurred while applying network interface values. ", ex.Message),
                cancellationToken).ConfigureAwait(false);
        }

        return new OperationInvokeResult(true, true, string.Concat("Applied ", plan.ChangedTargets.Count, " network interface registry changes."));
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var snapshot = await context.LoadSnapshotAsync<OperationSnapshot>(Id, cancellationToken).ConfigureAwait(false);
        if (snapshot is null || snapshot.Entries.Count == 0)
        {
            return new OperationInvokeResult(false, false, "Network interface restore snapshot is missing or empty.");
        }

        var restored = 0;
        var failed = new List<string>();
        foreach (var entry in snapshot.Entries.Reverse())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!RestoreSnapshotEntry(entry))
            {
                failed.Add(string.Concat(entry.InterfaceKey, "::", entry.ValueName));
                continue;
            }

            restored++;
        }

        if (failed.Count > 0)
        {
            return new OperationInvokeResult(false, restored > 0, string.Concat("Restored ", restored, " interface values, failed items: ", string.Join(", ", failed.Take(5)), "."));
        }

        return new OperationInvokeResult(true, restored > 0, string.Concat("Restored ", restored, " network interface values."));
    }

    private async Task<OperationInvokeResult> BuildApplyFailureResultAsync(
        OperationExecutionContext context,
        OperationSnapshot snapshot,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        var rollback = await TryRestoreOriginalStateAsync(context, snapshot, cancellationToken).ConfigureAwait(false);
        var message = rollback.Success
            ? failureMessage + " Original network interface registry state remained intact or was restored."
            : failureMessage + " Automatic rollback failed. " + rollback.Message;
        return new OperationInvokeResult(false, false, message);
    }

    private Task<(bool Success, string Message)> TryRestoreOriginalStateAsync(
        OperationExecutionContext context,
        OperationSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        return OperationRollbackHelper.TryRestoreOriginalStateAsync(
            context,
            Id,
            () => MatchesSnapshotSet(snapshot.Entries),
            () => RestoreSnapshotState(snapshot.Entries),
            "Original network interface registry state remained intact.",
            "Original network interface registry state restored.",
            cancellationToken);
    }

    private static (bool Success, string Message) RestoreSnapshotState(IEnumerable<ValueSnapshot> snapshots)
    {
        return RestoreSnapshotSet(snapshots)
            ? (true, string.Empty)
            : (false, "Network interface registry rollback verification failed.");
    }

    private static bool MatchesSnapshotSet(IEnumerable<ValueSnapshot> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            if (!MatchesSnapshotEntry(snapshot))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesSnapshotEntry(ValueSnapshot snapshot)
    {
        using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
        using var key = hive.OpenSubKey($@"{InterfacesRoot}\{snapshot.InterfaceKey}", writable: false);
        if (key is null)
        {
            return !snapshot.ValueExisted;
        }

        if (!snapshot.ValueExisted)
        {
            return key.GetValue(snapshot.ValueName) is null;
        }

        var actual = key.GetValue(snapshot.ValueName);
        if (actual is null)
        {
            return false;
        }

        var actualKind = key.GetValueKind(snapshot.ValueName);
        var expectedKind = ResolveSnapshotKind(snapshot);
        return expectedKind.HasValue
            && actualKind == expectedKind.Value
            && string.Equals(Normalize(actual, actualKind), ResolveSnapshotSerializedValue(snapshot) ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private OperationPlan BuildOperationPlan(IReadOnlyList<string> interfaces, CancellationToken cancellationToken)
    {
        using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
        var snapshots = new List<ValueSnapshot>(interfaces.Count * _definitions.Count);
        var changedTargets = new List<TargetEntry>();

        foreach (var interfaceKey in interfaces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var key = hive.OpenSubKey($@"{InterfacesRoot}\{interfaceKey}", writable: false);
            if (key is null)
            {
                throw new InvalidOperationException(string.Concat("Unable to read network interface registry path: ", interfaceKey));
            }

            foreach (var definition in _definitions)
            {
                var current = ReadValue(key, definition.Name);
                var previousValue = current.Exists && current.Kind == RegistryValueKind.DWord
                    ? TryReadDword(current.Value)
                    : null;

                snapshots.Add(new ValueSnapshot(
                    interfaceKey,
                    definition.Name,
                    current.Exists,
                    current.Exists ? Normalize(current.Value, current.Kind) : null,
                    current.Exists ? current.Kind : null,
                    previousValue));

                if (!IsDesiredState(current, definition))
                {
                    changedTargets.Add(new TargetEntry(interfaceKey, definition));
                }
            }
        }

        return new OperationPlan(snapshots, changedTargets);
    }

    private static (bool Exists, object? Value, RegistryValueKind Kind) ReadValue(RegistryKey key, string name)
    {
        var value = key.GetValue(name);
        if (value is null)
        {
            return (false, null, RegistryValueKind.Unknown);
        }

        return (true, value, key.GetValueKind(name));
    }

    private static bool IsDesiredState((bool Exists, object? Value, RegistryValueKind Kind) current, InterfaceValueDefinition definition)
    {
        return current.Exists
            && current.Kind == RegistryValueKind.DWord
            && string.Equals(Normalize(current.Value, current.Kind), Normalize(definition.DesiredValue, RegistryValueKind.DWord), StringComparison.OrdinalIgnoreCase);
    }

    private static bool WriteDesiredValue(string interfaceKey, InterfaceValueDefinition definition)
    {
        using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
        using var key = hive.OpenSubKey($@"{InterfacesRoot}\{interfaceKey}", writable: true);
        if (key is null)
        {
            return false;
        }

        key.SetValue(definition.Name, definition.DesiredValue, RegistryValueKind.DWord);
        var actual = key.GetValue(definition.Name);
        if (actual is null)
        {
            return false;
        }

        var actualKind = key.GetValueKind(definition.Name);
        return actualKind == RegistryValueKind.DWord
            && string.Equals(Normalize(actual, actualKind), Normalize(definition.DesiredValue, RegistryValueKind.DWord), StringComparison.OrdinalIgnoreCase);
    }

    private static bool RestoreSnapshotSet(IEnumerable<ValueSnapshot> snapshots)
    {
        var success = true;
        foreach (var snapshot in snapshots.Reverse())
        {
            if (!RestoreSnapshotEntry(snapshot))
            {
                success = false;
            }
        }

        return success;
    }

    private static bool RestoreSnapshotEntry(ValueSnapshot snapshot)
    {
        try
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
            using var key = hive.OpenSubKey($@"{InterfacesRoot}\{snapshot.InterfaceKey}", writable: true);
            if (key is null)
            {
                return false;
            }

            if (snapshot.ValueExisted)
            {
                var kind = ResolveSnapshotKind(snapshot);
                var serializedValue = ResolveSnapshotSerializedValue(snapshot);
                if (!kind.HasValue || serializedValue is null)
                {
                    return false;
                }

                key.SetValue(snapshot.ValueName, Parse(serializedValue, kind.Value), kind.Value);
            }
            else
            {
                key.DeleteValue(snapshot.ValueName, throwOnMissingValue: false);
            }

            if (!snapshot.ValueExisted)
            {
                return key.GetValue(snapshot.ValueName) is null;
            }

            var actual = key.GetValue(snapshot.ValueName);
            if (actual is null)
            {
                return false;
            }

            var actualKind = key.GetValueKind(snapshot.ValueName);
            var expectedKind = ResolveSnapshotKind(snapshot);
            return expectedKind.HasValue
                && actualKind == expectedKind.Value
                && string.Equals(Normalize(actual, actualKind), ResolveSnapshotSerializedValue(snapshot) ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static RegistryValueKind? ResolveSnapshotKind(ValueSnapshot snapshot)
    {
        if (snapshot.OriginalKind.HasValue)
        {
            return snapshot.OriginalKind.Value;
        }

        return snapshot.PreviousValue.HasValue ? RegistryValueKind.DWord : null;
    }

    private static string? ResolveSnapshotSerializedValue(ValueSnapshot snapshot)
    {
        if (snapshot.SerializedValue is not null)
        {
            return snapshot.SerializedValue;
        }

        return snapshot.PreviousValue.HasValue
            ? Normalize(snapshot.PreviousValue.Value, RegistryValueKind.DWord)
            : null;
    }

    private static int? TryReadDword(object? value)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    private static List<string> GetCandidateInterfaces()
    {
        using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
        using var root = hive.OpenSubKey(InterfacesRoot, writable: false);
        if (root is null)
        {
            return new List<string>();
        }

        var result = new List<string>();
        foreach (var name in root.GetSubKeyNames())
        {
            using var key = root.OpenSubKey(name, writable: false);
            if (key is null)
            {
                continue;
            }

            if (HasUsableIp(key))
            {
                result.Add(name);
            }
        }

        return result;
    }

    private static bool HasUsableIp(RegistryKey key)
    {
        static bool IsValid(string? candidate)
        {
            return !string.IsNullOrWhiteSpace(candidate)
                && !candidate.Equals("0.0.0.0", StringComparison.OrdinalIgnoreCase);
        }

        var dhcpValue = key.GetValue("DhcpIPAddress");
        switch (dhcpValue)
        {
            case string single when IsValid(single):
                return true;
            case string[] array when array.Any(IsValid):
                return true;
        }

        var addressValue = key.GetValue("IPAddress");
        return addressValue switch
        {
            string single when IsValid(single) => true,
            string[] array when array.Any(IsValid) => true,
            _ => false
        };
    }

    private static string Normalize(object? value, RegistryValueKind kind)
    {
        if (value is null)
        {
            return string.Empty;
        }

        return kind switch
        {
            RegistryValueKind.DWord => Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            RegistryValueKind.QWord => Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            RegistryValueKind.MultiString => string.Join(",", (string[])value),
            RegistryValueKind.Binary => Convert.ToBase64String((byte[])value),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    private static object Parse(string serializedValue, RegistryValueKind kind)
    {
        return kind switch
        {
            RegistryValueKind.DWord => int.Parse(serializedValue, CultureInfo.InvariantCulture),
            RegistryValueKind.QWord => long.Parse(serializedValue, CultureInfo.InvariantCulture),
            RegistryValueKind.MultiString => serializedValue.Length == 0 ? Array.Empty<string>() : serializedValue.Split(','),
            RegistryValueKind.Binary => Convert.FromBase64String(serializedValue),
            _ => serializedValue
        };
    }
}
