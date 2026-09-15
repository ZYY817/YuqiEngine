using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using GameOptimizer.Core.SystemRestore;

namespace GameOptimizer.Shell.Services;

internal static class SystemRestorePointWriterEntryPoint
{
    private const int MaximumInputCharacters = 8 * 1024;
    private const int BeginSystemChange = 100;
    private const int EndSystemChange = 101;
    private const int ModifySettings = 12;

    public static int Run(TextReader input, TextWriter output, TextWriter error)
        => Run(input, output, error, new WindowsSystemRestoreNativeApi());

    public static int RunFiles(string requestPath, string responsePath)
        => RunFiles(requestPath, responsePath, new WindowsSystemRestoreNativeApi());

    internal static int RunFiles(
        string requestPath,
        string responsePath,
        ISystemRestoreNativeApi native)
    {
        try
        {
            var requestFullPath = Path.GetFullPath(requestPath);
            var responseFullPath = Path.GetFullPath(responsePath);
            if (!string.Equals(Path.GetDirectoryName(requestFullPath), Path.GetDirectoryName(responseFullPath), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetFileName(requestFullPath), "request.json", StringComparison.Ordinal)
                || !string.Equals(Path.GetFileName(responseFullPath), "response.json", StringComparison.Ordinal))
            {
                return 2;
            }
            using var input = new StreamReader(requestFullPath);
            using var output = new StreamWriter(responseFullPath, append: false);
            return Run(input, output, TextWriter.Null, native);
        }
        catch
        {
            return 2;
        }
    }

    internal static int Run(
        TextReader input,
        TextWriter output,
        TextWriter error,
        ISystemRestoreNativeApi native)
    {
        try
        {
            if (!native.IsWindows)
            {
                return WriteFailure(output, "System Restore writer is available only on Windows.");
            }
            if (!native.IsAdministrator)
            {
                return WriteFailure(output, "Administrator permission is required by Windows System Restore.");
            }

            var line = input.ReadLine();
            if (string.IsNullOrWhiteSpace(line) || line.Length > MaximumInputCharacters)
            {
                return WriteFailure(output, "The writer request is empty or exceeds its protocol limit.");
            }
            var request = JsonSerializer.Deserialize<SystemRestorePointWriterRequest>(line);
            if (!IsValid(request, out var validationError))
            {
                return WriteFailure(output, validationError);
            }

            var command = request!.Command;
            if (!native.TryGetSystemVolumeIdentity(out var actualVolume)
                || !string.Equals(actualVolume, command.VolumeIdentity, StringComparison.OrdinalIgnoreCase))
            {
                return WriteFailure(output, "The frozen system-volume identity no longer matches this machine.");
            }

            var beginStatus = native.Begin(command.Description);
            if (!beginStatus.Succeeded)
            {
                return Write(output, new(
                    SystemRestorePointWriterRequest.CurrentProtocolVersion,
                    SystemRestorePointMutationOutcome.Failed,
                    null,
                    beginStatus.NativeStatus,
                    SystemRestorePointMutationState.NotChanged,
                    "Windows rejected BEGIN_SYSTEM_CHANGE."));
            }

            var sequence = beginStatus.SequenceNumber;
            var endStatus = native.End(command.Description, sequence);
            if (!endStatus.Succeeded)
            {
                return Write(output, new(
                    SystemRestorePointWriterRequest.CurrentProtocolVersion,
                    SystemRestorePointMutationOutcome.Unknown,
                    sequence > 0 ? sequence : null,
                    endStatus.NativeStatus,
                    SystemRestorePointMutationState.Unknown,
                    "BEGIN_SYSTEM_CHANGE succeeded but END_SYSTEM_CHANGE failed; authoritative readback is required."));
            }

            var finalSequence = endStatus.SequenceNumber > 0 ? endStatus.SequenceNumber : sequence;
            return Write(output, new(
                SystemRestorePointWriterRequest.CurrentProtocolVersion,
                SystemRestorePointMutationOutcome.Succeeded,
                finalSequence > 0 ? finalSequence : null,
                endStatus.NativeStatus,
                SystemRestorePointMutationState.Changed,
                "Windows completed BEGIN_SYSTEM_CHANGE and END_SYSTEM_CHANGE."));
        }
        catch (Exception ex)
        {
            error.WriteLine($"writer-error:{ex.GetType().Name}");
            return Write(output, new(
                SystemRestorePointWriterRequest.CurrentProtocolVersion,
                SystemRestorePointMutationOutcome.Unknown,
                null,
                null,
                SystemRestorePointMutationState.Unknown,
                "The isolated writer failed after invocation; authoritative readback is required."));
        }
    }

    private static bool IsValid(SystemRestorePointWriterRequest? request, out string error)
    {
        if (request is null || request.ProtocolVersion != SystemRestorePointWriterRequest.CurrentProtocolVersion)
        {
            error = "The writer protocol version is invalid.";
            return false;
        }
        var command = request.Command;
        if (command is null)
        {
            error = "The frozen writer command is missing.";
            return false;
        }
        if (command.PlanId == Guid.Empty
            || command.PlanHash is null
            || command.PlanHash.Length != 64
            || !command.PlanHash.All(Uri.IsHexDigit)
            || string.IsNullOrWhiteSpace(command.VolumeIdentity)
            || string.IsNullOrWhiteSpace(command.Description)
            || command.Description.Length > 255
            || command.Description.Any(char.IsControl))
        {
            error = "The frozen writer command is invalid.";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private static int WriteFailure(TextWriter output, string message) => Write(output, new(
        SystemRestorePointWriterRequest.CurrentProtocolVersion,
        SystemRestorePointMutationOutcome.Failed,
        null,
        null,
        SystemRestorePointMutationState.NotChanged,
        message));

    private static int Write(TextWriter output, SystemRestorePointWriterResponse response, int exitCode = 0)
    {
        output.WriteLine(JsonSerializer.Serialize(response));
        output.Flush();
        return exitCode;
    }

    internal readonly record struct NativeCallResult(bool Succeeded, int NativeStatus, long SequenceNumber);

    internal interface ISystemRestoreNativeApi
    {
        bool IsWindows { get; }
        bool IsAdministrator { get; }
        bool TryGetSystemVolumeIdentity(out string identity);
        NativeCallResult Begin(string description);
        NativeCallResult End(string description, long sequenceNumber);
    }

    private sealed class WindowsSystemRestoreNativeApi : ISystemRestoreNativeApi
    {
        public bool IsWindows => OperatingSystem.IsWindows();
        public bool IsAdministrator => GameOptimizer.Core.Services.WindowsElevation.IsElevated;

        public bool TryGetSystemVolumeIdentity(out string identity)
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory);
            var buffer = new char[261];
            if (!string.IsNullOrWhiteSpace(root)
                && GetVolumeNameForVolumeMountPointW(root, buffer, buffer.Length))
            {
                identity = new string(buffer).TrimEnd('\0');
                return !string.IsNullOrWhiteSpace(identity);
            }
            identity = string.Empty;
            return false;
        }

        public NativeCallResult Begin(string description) => Invoke(BeginSystemChange, description, 0);
        public NativeCallResult End(string description, long sequenceNumber) => Invoke(EndSystemChange, description, sequenceNumber);

        private static NativeCallResult Invoke(int eventType, string description, long sequenceNumber)
        {
            var info = new RestorePointInfo
            {
                EventType = eventType,
                RestorePointType = ModifySettings,
                SequenceNumber = sequenceNumber,
                Description = description
            };
            var succeeded = SRSetRestorePointW(ref info, out var status);
            return new(succeeded, status.Status, status.SequenceNumber);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RestorePointInfo
    {
        public int EventType;
        public int RestorePointType;
        public long SequenceNumber;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Description;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StateManagerStatus
    {
        public int Status;
        public long SequenceNumber;
    }

    [DllImport("srclient.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SRSetRestorePointW(
        ref RestorePointInfo restorePointInfo,
        out StateManagerStatus stateManagerStatus);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPointW(
        string volumeMountPoint,
        [Out] char[] volumeName,
        int bufferLength);
}
