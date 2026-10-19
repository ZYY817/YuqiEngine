using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace GameOptimizer.Core.Optimization.Operations;

public sealed class NetworkAdapterAdvancedPropertyOperation : IOptimizationOperation
{
    private const string ClassRoot = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

    private static readonly NetworkInterfaceType[] PreferredTypes =
    {
        NetworkInterfaceType.Ethernet,
        NetworkInterfaceType.GigabitEthernet,
        NetworkInterfaceType.FastEthernetFx,
        NetworkInterfaceType.FastEthernetT,
        NetworkInterfaceType.Wireless80211
    };

    public readonly record struct AdapterPropertyDefinition(string ValueName, object DesiredValue, RegistryValueKind ValueKind);

    private record PropertySnapshot(
        string AdapterKeyName,
        AdapterPropertyDefinition Definition,
        bool ValueExisted,
        string? SerializedValue,
        RegistryValueKind? OriginalKind);

    private record OperationSnapshot(IReadOnlyList<PropertySnapshot> Entries);
    private record AdapterRegistryEntry(string SubKeyName, string FriendlyName);
    private record TargetEntry(string AdapterKeyName, string FriendlyName, AdapterPropertyDefinition Definition);
    private record OperationPlan(IReadOnlyList<PropertySnapshot> Snapshots, IReadOnlyList<TargetEntry> ChangedTargets);

    private readonly IReadOnlyList<AdapterPropertyDefinition> _definitions;

    public NetworkAdapterAdvancedPropertyOperation(
        string id,
        string title,
        string description,
        string category,
        IEnumerable<AdapterPropertyDefinition> definitions)
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
    public string RegistryPath => ClassRoot;
    public string RegistryValueName => string.Join(";", _definitions.Select(static definition => definition.ValueName));
    public bool RequiresAdmin => true;
    public bool RequiresSnapshotForRestore => true;

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            var adapters = EnumerateTargetAdapters();
            if (adapters.Count == 0)
            {
                return Task.FromResult(new OperationStatus(true, "No eligible physical network adapters were found."));
            }

            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
            using var classKey = hive.OpenSubKey(ClassRoot, writable: false);
            if (classKey is null)
            {
                return Task.FromResult(new OperationStatus(false, string.Concat("Unable to open adapter class registry path: ", ClassRoot)));
            }

            var nonCompliant = 0;
            foreach (var adapter in adapters)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var key = classKey.OpenSubKey(adapter.SubKeyName, writable: false);
                if (key is null)
                {
                    nonCompliant++;
                    continue;
                }

                var matched = true;
                foreach (var definition in _definitions)
                {
                    var current = ReadValue(key, definition.ValueName);
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
                ? "All adapter advanced properties match the target state."
                : string.Concat(adapters.Count - nonCompliant, "/", adapters.Count, " adapters match the target state.");
            return Task.FromResult(new OperationStatus(compliant, details));
        }
        catch (SecurityException ex)
        {
            return Task.FromResult(new OperationStatus(false, string.Concat("Access to adapter advanced properties is blocked. ", ex.Message)));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Task.FromResult(new OperationStatus(false, string.Concat("Access to adapter advanced properties is blocked. ", ex.Message)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new OperationStatus(false, string.Concat("Failed to inspect adapter advanced properties. ", ex.Message)));
        }
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        IReadOnlyList<AdapterRegistryEntry> adapters;
        try
        {
            adapters = EnumerateTargetAdapters();
        }
        catch (Exception ex)
        {
            return new OperationInvokeResult(false, false, string.Concat("Failed to enumerate target adapters. ", ex.Message));
        }

        if (adapters.Count == 0)
        {
            return new OperationInvokeResult(true, false, "No eligible physical network adapters were found.");
        }

        using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
        using var classKey = hive.OpenSubKey(ClassRoot, writable: true);
        if (classKey is null)
        {
            return new OperationInvokeResult(false, false, string.Concat("Unable to open adapter class registry path: ", ClassRoot));
        }

        OperationPlan plan;
        try
        {
            plan = BuildOperationPlan(classKey, adapters, cancellationToken);
        }
        catch (Exception ex)
        {
            return new OperationInvokeResult(false, false, string.Concat("Failed to capture adapter advanced property snapshots. ", ex.Message));
        }

        if (plan.ChangedTargets.Count == 0)
        {
            return new OperationInvokeResult(true, false, "All adapter advanced properties are already at the target state.");
        }

        var snapshot = new OperationSnapshot(plan.Snapshots);
        await context.SaveSnapshotAsync(Id, snapshot, cancellationToken).ConfigureAwait(false);

        try
        {
            foreach (var target in plan.ChangedTargets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!WriteDesiredValue(classKey, target))
                {
                    return await BuildApplyFailureResultAsync(
                        context,
                        snapshot,
                        string.Concat("Failed to apply adapter property ", FormatEntryLabel(target.FriendlyName, target.Definition.ValueName), "."),
                        cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            return await BuildApplyFailureResultAsync(
                context,
                snapshot,
                string.Concat("An exception occurred while applying adapter advanced properties. ", ex.Message),
                cancellationToken).ConfigureAwait(false);
        }

        return new OperationInvokeResult(true, true, string.Concat("Applied ", plan.ChangedTargets.Count, " adapter advanced property changes."));
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var snapshot = await context.LoadSnapshotAsync<OperationSnapshot>(Id, cancellationToken).ConfigureAwait(false);
        if (snapshot is null || snapshot.Entries.Count == 0)
        {
            return new OperationInvokeResult(false, false, "Network adapter restore snapshot is missing or empty.");
        }

        using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
        using var classKey = hive.OpenSubKey(ClassRoot, writable: true);
        if (classKey is null)
        {
            return new OperationInvokeResult(false, false, string.Concat("Unable to open adapter class registry path: ", ClassRoot));
        }

        var restored = 0;
        var failed = new List<string>();
        foreach (var entry in snapshot.Entries.Reverse())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!RestoreSnapshotEntry(classKey, entry))
            {
                failed.Add(FormatEntryLabel(entry.AdapterKeyName, entry.Definition.ValueName));
                continue;
            }

            restored++;
        }

        if (failed.Count > 0)
        {
            return new OperationInvokeResult(false, restored > 0, string.Concat("Restored ", restored, " adapter properties, failed items: ", string.Join(", ", failed.Take(5)), "."));
        }

        return new OperationInvokeResult(true, restored > 0, string.Concat("Restored ", restored, " adapter advanced properties."));
    }

    private async Task<OperationInvokeResult> BuildApplyFailureResultAsync(
        OperationExecutionContext context,
        OperationSnapshot snapshot,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        var rollback = await TryRestoreOriginalStateAsync(context, snapshot, cancellationToken).ConfigureAwait(false);
        var message = rollback.Success
            ? failureMessage + " Original network adapter registry state remained intact or was restored."
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
            "Original network adapter registry state remained intact.",
            "Original network adapter registry state restored.",
            cancellationToken);
    }

    private static (bool Success, string Message) RestoreSnapshotState(IEnumerable<PropertySnapshot> snapshots)
    {
        using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
        using var classKey = hive.OpenSubKey(ClassRoot, writable: true);
        if (classKey is null)
        {
            return (false, string.Concat("Unable to open adapter class registry path for rollback: ", ClassRoot));
        }

        return RestoreSnapshotSet(classKey, snapshots)
            ? (true, string.Empty)
            : (false, "Network adapter registry rollback verification failed.");
    }

    private bool MatchesSnapshotSet(IEnumerable<PropertySnapshot> snapshots)
    {
        using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
        using var classKey = hive.OpenSubKey(ClassRoot, writable: false);
        if (classKey is null)
        {
            return false;
        }

        foreach (var snapshot in snapshots)
        {
            if (!MatchesSnapshotEntry(classKey, snapshot))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesSnapshotEntry(RegistryKey classKey, PropertySnapshot snapshot)
    {
        using var key = classKey.OpenSubKey(snapshot.AdapterKeyName, writable: false);
        if (key is null)
        {
            return !snapshot.ValueExisted;
        }

        if (!snapshot.ValueExisted)
        {
            return key.GetValue(snapshot.Definition.ValueName) is null;
        }

        if (snapshot.OriginalKind is not RegistryValueKind expectedKind || snapshot.SerializedValue is not string expectedValue)
        {
            return false;
        }

        var actual = key.GetValue(snapshot.Definition.ValueName);
        if (actual is null)
        {
            return false;
        }

        var actualKind = key.GetValueKind(snapshot.Definition.ValueName);
        return actualKind == expectedKind
            && string.Equals(Normalize(actual, actualKind), expectedValue, StringComparison.OrdinalIgnoreCase);
    }

    private OperationPlan BuildOperationPlan(
        RegistryKey classKey,
        IReadOnlyList<AdapterRegistryEntry> adapters,
        CancellationToken cancellationToken)
    {
        var snapshots = new List<PropertySnapshot>(adapters.Count * _definitions.Count);
        var changedTargets = new List<TargetEntry>();

        foreach (var adapter in adapters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var key = classKey.OpenSubKey(adapter.SubKeyName, writable: false);
            if (key is null)
            {
                throw new InvalidOperationException(string.Concat("Unable to read adapter registry key: ", adapter.SubKeyName));
            }

            foreach (var definition in _definitions)
            {
                var current = ReadValue(key, definition.ValueName);
                snapshots.Add(new PropertySnapshot(
                    adapter.SubKeyName,
                    definition,
                    current.Exists,
                    current.Exists ? Normalize(current.Value, current.Kind) : null,
                    current.Exists ? current.Kind : null));

                if (!IsDesiredState(current, definition))
                {
                    changedTargets.Add(new TargetEntry(adapter.SubKeyName, adapter.FriendlyName, definition));
                }
            }
        }

        return new OperationPlan(snapshots, changedTargets);
    }

    private static (bool Exists, object? Value, RegistryValueKind Kind) ReadValue(RegistryKey key, string valueName)
    {
        var value = key.GetValue(valueName);
        if (value is null)
        {
            return (false, null, RegistryValueKind.Unknown);
        }

        return (true, value, key.GetValueKind(valueName));
    }

    private static bool IsDesiredState((bool Exists, object? Value, RegistryValueKind Kind) current, AdapterPropertyDefinition definition)
    {
        return current.Exists
            && current.Kind == definition.ValueKind
            && string.Equals(Normalize(current.Value, current.Kind), Normalize(definition.DesiredValue, definition.ValueKind), StringComparison.OrdinalIgnoreCase);
    }

    private static bool WriteDesiredValue(RegistryKey classKey, TargetEntry target)
    {
        using var key = classKey.OpenSubKey(target.AdapterKeyName, writable: true);
        if (key is null)
        {
            return false;
        }

        key.SetValue(target.Definition.ValueName, ConvertForRegistry(target.Definition.DesiredValue, target.Definition.ValueKind), target.Definition.ValueKind);
        var actual = key.GetValue(target.Definition.ValueName);
        if (actual is null)
        {
            return false;
        }

        var actualKind = key.GetValueKind(target.Definition.ValueName);
        return actualKind == target.Definition.ValueKind
            && string.Equals(Normalize(actual, actualKind), Normalize(target.Definition.DesiredValue, target.Definition.ValueKind), StringComparison.OrdinalIgnoreCase);
    }

    private static bool RestoreSnapshotSet(RegistryKey classKey, IEnumerable<PropertySnapshot> snapshots)
    {
        var success = true;
        foreach (var snapshot in snapshots.Reverse())
        {
            if (!RestoreSnapshotEntry(classKey, snapshot))
            {
                success = false;
            }
        }

        return success;
    }

    private static bool RestoreSnapshotEntry(RegistryKey classKey, PropertySnapshot snapshot)
    {
        try
        {
            using var key = classKey.OpenSubKey(snapshot.AdapterKeyName, writable: true);
            if (key is null)
            {
                return false;
            }

            if (snapshot.ValueExisted)
            {
                if (snapshot.OriginalKind is not RegistryValueKind originalKind || snapshot.SerializedValue is not string serializedValue)
                {
                    return false;
                }

                key.SetValue(snapshot.Definition.ValueName, Parse(serializedValue, originalKind), originalKind);
            }
            else
            {
                key.DeleteValue(snapshot.Definition.ValueName, throwOnMissingValue: false);
            }

            if (!snapshot.ValueExisted)
            {
                return key.GetValue(snapshot.Definition.ValueName) is null;
            }

            if (snapshot.OriginalKind is not RegistryValueKind expectedKind || snapshot.SerializedValue is not string expectedValue)
            {
                return false;
            }

            var actual = key.GetValue(snapshot.Definition.ValueName);
            if (actual is null)
            {
                return false;
            }

            var actualKind = key.GetValueKind(snapshot.Definition.ValueName);
            return actualKind == expectedKind
                && string.Equals(Normalize(actual, actualKind), expectedValue, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<AdapterRegistryEntry> EnumerateTargetAdapters()
    {
        var activeInterfaces = NetworkInterface
            .GetAllNetworkInterfaces()
            .Where(IsCandidateInterface)
            .ToDictionary(nic => nic.Id, nic => nic, StringComparer.OrdinalIgnoreCase);

        if (activeInterfaces.Count == 0)
        {
            return Array.Empty<AdapterRegistryEntry>();
        }

        using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
        using var classKey = hive.OpenSubKey(ClassRoot, writable: false);
        if (classKey is null)
        {
            return Array.Empty<AdapterRegistryEntry>();
        }

        var result = new List<AdapterRegistryEntry>();
        foreach (var subKeyName in classKey.GetSubKeyNames())
        {
            using var key = classKey.OpenSubKey(subKeyName, writable: false);
            if (key is null)
            {
                continue;
            }

            var netCfgInstanceId = key.GetValue("NetCfgInstanceId") as string;
            if (string.IsNullOrWhiteSpace(netCfgInstanceId) || !activeInterfaces.TryGetValue(netCfgInstanceId, out var nic))
            {
                continue;
            }

            var friendlyName = key.GetValue("DriverDesc") as string;
            result.Add(new AdapterRegistryEntry(subKeyName, string.IsNullOrWhiteSpace(friendlyName) ? nic.Name : friendlyName));
        }

        return result;
    }

    private static bool IsCandidateInterface(NetworkInterface nic)
    {
        if (nic.OperationalStatus != OperationalStatus.Up)
        {
            return false;
        }

        if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback || nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
        {
            return false;
        }

        return PreferredTypes.Contains(nic.NetworkInterfaceType);
    }

    private static string FormatEntryLabel(string adapterName, string valueName)
    {
        return string.IsNullOrWhiteSpace(adapterName)
            ? valueName
            : string.Concat(adapterName, "::", valueName);
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

    private static object ConvertForRegistry(object value, RegistryValueKind kind)
    {
        return kind switch
        {
            RegistryValueKind.DWord => Convert.ToInt32(value, CultureInfo.InvariantCulture),
            RegistryValueKind.QWord => Convert.ToInt64(value, CultureInfo.InvariantCulture),
            RegistryValueKind.MultiString => value is string[] array ? array : Array.Empty<string>(),
            RegistryValueKind.Binary => value is byte[] bytes ? bytes : Array.Empty<byte>(),
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
