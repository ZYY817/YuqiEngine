using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32;
using GameOptimizer.Core.SystemRestore;

namespace GameOptimizer.Infrastructure.Windows.SystemRestore;

[SupportedOSPlatform("windows")]
public sealed class WindowsSystemRestorePointPlatform : ISystemRestorePointPlatform
{
    private const string SystemRestoreRegistryPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore";
    private const string SystemRestorePolicyPath = @"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore";
    private const int DefaultCreationCooldownMinutes = 1_440;
    private readonly ISystemRestorePointWriterProcessRunner _writer;

    public WindowsSystemRestorePointPlatform(ISystemRestorePointWriterProcessRunner writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public Task<SystemRestorePointProbe> ProbeAsync(CancellationToken cancellationToken) =>
        Task.Run(() => Probe(cancellationToken), cancellationToken);

    public Task<SystemRestorePointMutationResult> CreateRestorePointAsync(
        SystemRestorePointCreateCommand command,
        CancellationToken cancellationToken) => _writer.RunAsync(command, cancellationToken);

    private SystemRestorePointProbe Probe(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            return SystemRestorePointProbe.Unsupported("System Restore is available only on Windows.");
        }

        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
        if (string.IsNullOrWhiteSpace(systemRoot) || !TryGetVolumeIdentity(systemRoot, out var volumeIdentity))
        {
            return new SystemRestorePointProbe(
                true,
                Environment.OSVersion.Version.Build,
                IsAdministrator(),
                SystemProtectionState.Unknown,
                string.Empty,
                Array.Empty<SystemRestorePointIdentity>(),
                TimeSpan.FromMinutes(DefaultCreationCooldownMinutes),
                null,
                false,
                HasPendingRestart(),
                false,
                "SYSTEM_RESTORE.VOLUME_IDENTITY_UNAVAILABLE",
                "The system-volume GUID identity could not be read.");
        }

        var points = ReadRestorePoints(volumeIdentity, cancellationToken, out var providerError);
        var protection = ReadProtectionState(providerError, out var protectionMessage);
        var cooldown = ReadCreationCooldown();
        var latest = points.OrderByDescending(point => point.CreatedAtUtc).FirstOrDefault();
        DateTimeOffset? nextAllowed = cooldown > TimeSpan.Zero && latest is not null
            ? latest.CreatedAtUtc.Add(cooldown)
            : null;
        var creationApiAvailable = TryResolveCreationApi();
        var creationBoundary = _writer.IsAvailable
            ? SystemRestorePointCreationBoundary.IsolatedAndTerminable
            : SystemRestorePointCreationBoundary.InProcessNonCancellable;
        var message = protectionMessage ?? providerError;
        return new SystemRestorePointProbe(
            true,
            Environment.OSVersion.Version.Build,
            IsAdministrator(),
            protection,
            volumeIdentity,
            points,
            cooldown,
            nextAllowed,
            creationApiAvailable,
            HasPendingRestart(),
            IsRestoreExecutionPolicyAllowed: false,
            providerError is not null
                ? "SYSTEM_RESTORE.PROVIDER_QUERY_FAILED"
                : protection is SystemProtectionState.Unknown or SystemProtectionState.Unsupported
                    ? "SYSTEM_RESTORE.PROTECTION_STATE_UNVERIFIED"
                    : null,
            message,
            creationBoundary);
    }

    private static IReadOnlyList<SystemRestorePointIdentity> ReadRestorePoints(
        string volumeIdentity,
        CancellationToken cancellationToken,
        out string? error)
    {
        var points = new List<SystemRestorePointIdentity>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\default",
                "SELECT SequenceNumber, Description, CreationTime, RestorePointType FROM SystemRestore");
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sequence = Convert.ToInt64(item["SequenceNumber"], System.Globalization.CultureInfo.InvariantCulture);
                var description = item["Description"]?.ToString() ?? string.Empty;
                var creationText = item["CreationTime"]?.ToString();
                var createdLocal = string.IsNullOrWhiteSpace(creationText)
                    ? DateTime.MinValue
                    : ManagementDateTimeConverter.ToDateTime(creationText);
                var createdUtc = new DateTimeOffset(createdLocal).ToUniversalTime();
                var type = Convert.ToInt32(item["RestorePointType"], System.Globalization.CultureInfo.InvariantCulture);
                points.Add(new SystemRestorePointIdentity(volumeIdentity, sequence, createdUtc, description, type));
            }

            error = null;
            return points
                .OrderBy(point => point.VolumeIdentity, StringComparer.OrdinalIgnoreCase)
                .ThenBy(point => point.SequenceNumber)
                .ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return Array.Empty<SystemRestorePointIdentity>();
        }
    }

    private static SystemProtectionState ReadProtectionState(
        string? providerError,
        out string? message)
    {
        try
        {
            using var policy = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(SystemRestorePolicyPath, writable: false);
            if (ConvertRegistryDword(policy?.GetValue("DisableSR")) == 1)
            {
                message = "System Protection is disabled by machine policy.";
                return SystemProtectionState.PolicyDisabled;
            }

            using var configuration = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(SystemRestoreRegistryPath, writable: false);
            var disabled = ConvertRegistryDword(configuration?.GetValue("DisableSR"));
            if (disabled == 1)
            {
                message = "System Protection is disabled.";
                return SystemProtectionState.Disabled;
            }

            if (disabled == 0)
            {
                message = null;
                return SystemProtectionState.Enabled;
            }

            message = providerError is null
                ? "System Protection could not be proven enabled from an authoritative policy/configuration signal."
                : $"The SystemRestore provider could not be queried: {providerError}";
            return SystemProtectionState.Unknown;
        }
        catch (Exception ex)
        {
            message = $"System Protection policy/configuration could not be read: {ex.Message}";
            return SystemProtectionState.Unknown;
        }
    }

    private static TimeSpan ReadCreationCooldown()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(SystemRestoreRegistryPath, writable: false);
            var configured = ConvertRegistryDword(key?.GetValue("SystemRestorePointCreationFrequency"));
            return TimeSpan.FromMinutes(configured ?? DefaultCreationCooldownMinutes);
        }
        catch
        {
            return TimeSpan.FromMinutes(DefaultCreationCooldownMinutes);
        }
    }

    private static bool HasPendingRestart()
    {
        try
        {
            using var cbs = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending",
                writable: false);
            using var update = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired",
                writable: false);
            using var session = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Session Manager",
                writable: false);
            return cbs is not null
                || update is not null
                || session?.GetValue("PendingFileRenameOperations") is string[] { Length: > 0 };
        }
        catch
        {
            return true;
        }
    }

    private static bool TryResolveCreationApi()
    {
        if (!NativeLibrary.TryLoad("srclient.dll", out var library))
        {
            return false;
        }

        try
        {
            return NativeLibrary.TryGetExport(library, "SRSetRestorePointW", out _);
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    private static int? ConvertRegistryDword(object? value)
    {
        try
        {
            return value is null ? null : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static bool TryGetVolumeIdentity(string root, out string volumeIdentity)
    {
        var buffer = new char[261];
        if (GetVolumeNameForVolumeMountPoint(root, buffer, buffer.Length))
        {
            volumeIdentity = new string(buffer).TrimEnd('\0');
            return !string.IsNullOrWhiteSpace(volumeIdentity);
        }

        volumeIdentity = string.Empty;
        return false;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPoint(
        string volumeMountPoint,
        [Out] char[] volumeName,
        int bufferLength);

}
