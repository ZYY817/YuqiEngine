using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.ProcessTuning;

namespace GameOptimizer.Infrastructure.Windows.ProcessTuning;

/// <summary>
/// Windows-only runtime process adapter. Every mutation re-reads identity and state
/// on the same Process instance that is subsequently used for the write.
/// </summary>
public sealed class WindowsProcessTuningPlatform : IProcessTuningPlatform
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint ProcessSetInformation = 0x0200;
    private const uint TokenQuery = 0x0008;
    private const ushort AllProcessorGroups = 0xFFFF;

    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "smss", "csrss", "wininit", "services", "lsass",
        "winlogon", "fontdrvhost", "dwm", "audiodg"
    };

    public async Task<CapabilityAssessment> ProbeAsync(
        int processId,
        ProcessTuningOperation operation,
        ChangeIntent intent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            return Unsupported("platform.not-windows", "当前平台不是 Windows。" );
        }

        if (operation == ProcessTuningOperation.IdealProcessor)
        {
            return Unsupported(
                "ideal-processor.readback-unavailable",
                "Windows 平台适配器没有可验证的 IdealProcessor 读取与恢复语义，已阻断。" );
        }

        var read = await ReadAsync(processId, cancellationToken).ConfigureAwait(false);
        if (!read.Succeeded || read.State is null)
        {
            return FromReadFailure(read);
        }

        var identity = read.State.Identity;
        if (ProtectedNames.Contains(identity.ExecutableName))
        {
            return new CapabilityAssessment(
                CapabilityApplicability.NotApplicable,
                false,
                "process.protected",
                "受保护或关键 Windows 进程不允许进程调优。" );
        }

        if (string.Equals(identity.Architecture, "unknown", StringComparison.Ordinal)
            || string.Equals(identity.TopologyFingerprint, "unknown", StringComparison.Ordinal))
        {
            return Unsupported("process.identity-incomplete", "进程架构或 CPU 拓扑指纹不可验证。" );
        }

        if (GetActiveProcessorGroupCount() != 1 || GetActiveProcessorCount(AllProcessorGroups) > 63)
        {
            return Unsupported(
                "topology.processor-group-unsupported",
                "当前处理器组拓扑无法由正 long 亲和性掩码可靠表达，已阻断。" );
        }

        if (!CanSetInformation(processId, out var accessError))
        {
            return new CapabilityAssessment(
                CapabilityApplicability.Applicable,
                false,
                "process.permission-denied",
                accessError ?? "无法取得目标进程的 PROCESS_SET_INFORMATION 权限。" );
        }

        return new CapabilityAssessment(
            CapabilityApplicability.Applicable,
            true,
            "process.applicable",
            "进程 identity、拓扑和最小写入权限已通过探测。" );
    }

    public Task<ProcessTuningPlatformReadResult> ReadAsync(int processId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(ProcessTuningPlatformReadResult.Failure("platform.not-windows", "当前平台不是 Windows。"));
        }

        if (processId <= 4)
        {
            return Task.FromResult(ProcessTuningPlatformReadResult.Failure("process.not-applicable", "系统 PID 不能作为进程调优目标。"));
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            return Task.FromResult(ReadFromProcess(process));
        }
        catch (ArgumentException)
        {
            return Task.FromResult(ProcessTuningPlatformReadResult.Failure("process.exited", "目标进程已退出。"));
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
        {
            return Task.FromResult(ProcessTuningPlatformReadResult.Failure("process.permission-denied", "读取目标进程状态被拒绝。"));
        }
        catch (InvalidOperationException)
        {
            return Task.FromResult(ProcessTuningPlatformReadResult.Failure("process.exited", "目标进程已退出或状态不可读取。"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ProcessTuningPlatformReadResult.Failure("process.read-failed", ex.GetType().Name));
        }
    }

    public Task<PlatformExecutionResult> ApplyAsync(
        ProcessTuningOperation operation,
        ProcessTuningState expectedCurrent,
        ProcessTuningState desired,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (operation == ProcessTuningOperation.IdealProcessor)
        {
            return Task.FromResult(new PlatformExecutionResult(false, false, "ideal-processor.readback-unavailable", "IdealProcessor 未具备可验证恢复链，未执行写入。"));
        }

        try
        {
            using var process = Process.GetProcessById(expectedCurrent.Identity.ProcessId);
            var current = ReadFromProcess(process);
            if (!current.Succeeded || current.State is null)
            {
                return Task.FromResult(new PlatformExecutionResult(false, false, current.ReasonCode, current.Message));
            }

            if (current.State.Identity != expectedCurrent.Identity)
            {
                return Task.FromResult(new PlatformExecutionResult(false, false, "process.identity-drift", "目标 PID 已复用、可执行文件/用户/session/架构或拓扑已变化，未执行写入。"));
            }

            if (current.State != expectedCurrent)
            {
                return Task.FromResult(new PlatformExecutionResult(false, false, "process.state-drift", "进程运行时状态在执行前发生变化，未执行写入。"));
            }

            return Task.FromResult(ApplyMutation(
                process,
                operation,
                desired,
                "process.write-issued",
                "平台写入已发起，等待独立回读验证。",
                cancellationToken));
        }
        catch (ArgumentException)
        {
            return Task.FromResult(new PlatformExecutionResult(false, false, "process.exited", "目标进程已退出，未执行写入。"));
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
        {
            return Task.FromResult(new PlatformExecutionResult(false, false, "process.permission-denied", "写入目标进程状态被拒绝。"));
        }
        catch (InvalidOperationException)
        {
            return Task.FromResult(new PlatformExecutionResult(false, false, "process.exited", "目标进程在写入期间退出。"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new PlatformExecutionResult(false, false, "process.write-failed", ex.GetType().Name));
        }
    }

    public Task<PlatformExecutionResult> RestoreAsync(
        ProcessTuningOperation operation,
        string expectedTargetFingerprint,
        ProcessTuningState original,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var process = Process.GetProcessById(original.Identity.ProcessId);
            var current = ReadFromProcess(process);
            if (!current.Succeeded || current.State is null)
            {
                return Task.FromResult(new PlatformExecutionResult(false, false, current.ReasonCode, current.Message));
            }

            if (current.State.Identity != original.Identity)
            {
                return Task.FromResult(new PlatformExecutionResult(false, false, "process.identity-drift", "目标 PID 已复用或进程 identity 已变化，恢复被阻断。"));
            }

            if (!string.Equals(ProcessTuningStateFingerprint.Hash(current.State), expectedTargetFingerprint, StringComparison.Ordinal))
            {
                return Task.FromResult(new PlatformExecutionResult(false, false, "process.state-drift", "当前状态不等于本次应用目标，恢复不会覆盖外部变化。"));
            }

            return Task.FromResult(ApplyMutation(
                process,
                operation,
                original,
                "process.restore-issued",
                "恢复平台写入已发起，等待独立回读验证。",
                cancellationToken));
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
        {
            return Task.FromResult(new PlatformExecutionResult(false, false, "process.permission-denied", "恢复目标进程状态被拒绝。"));
        }
        catch (ArgumentException)
        {
            return Task.FromResult(new PlatformExecutionResult(false, false, "process.exited", "目标进程已退出。"));
        }
        catch (InvalidOperationException)
        {
            return Task.FromResult(new PlatformExecutionResult(false, false, "process.exited", "目标进程已退出。"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new PlatformExecutionResult(false, false, "process.restore-failed", ex.GetType().Name));
        }
    }

    private static ProcessTuningPlatformReadResult ReadFromProcess(Process process)
    {
        if (process.HasExited)
        {
            return ProcessTuningPlatformReadResult.Failure("process.exited", "目标进程已退出。");
        }

        var identityResult = TryCaptureIdentity(process);
        if (identityResult.Identity is null)
        {
            return ProcessTuningPlatformReadResult.Failure(identityResult.ReasonCode, identityResult.Message);
        }

        return ProcessTuningPlatformReadResult.Success(new ProcessTuningState(
            identityResult.Identity,
            process.ProcessorAffinity.ToInt64(),
            process.PriorityClass,
            IdealProcessor: null,
            IsIdealProcessorReadbackReliable: false));
    }

    private static PlatformExecutionResult ApplyMutation(
        Process process,
        ProcessTuningOperation operation,
        ProcessTuningState desired,
        string successCode,
        string successMessage,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (operation)
        {
            case ProcessTuningOperation.Affinity:
                process.ProcessorAffinity = (IntPtr)desired.AffinityMask;
                break;
            case ProcessTuningOperation.Priority:
                process.PriorityClass = desired.Priority;
                break;
            default:
                return new PlatformExecutionResult(false, false, "ideal-processor.readback-unavailable", "IdealProcessor 未具备可靠回读与恢复链，保持阻断。");
        }

        return new PlatformExecutionResult(true, true, successCode, successMessage);
    }

    private static CapabilityAssessment FromReadFailure(ProcessTuningPlatformReadResult read) => read.ReasonCode switch
    {
        "process.exited" or "process.not-applicable" or "process.protected" => new(
            CapabilityApplicability.NotApplicable, false, read.ReasonCode, read.Message),
        "process.permission-denied" => new(
            CapabilityApplicability.Applicable, false, read.ReasonCode, read.Message),
        _ => Unsupported(read.ReasonCode, read.Message)
    };

    private static CapabilityAssessment Unsupported(string reasonCode, string message) =>
        new(CapabilityApplicability.Unsupported, false, reasonCode, message);

    private static bool CanSetInformation(int processId, out string? error)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation | ProcessSetInformation, false, processId);
        if (handle != IntPtr.Zero)
        {
            CloseHandle(handle);
            error = null;
            return true;
        }

        var code = Marshal.GetLastWin32Error();
        error = code == 5 ? "没有目标进程的最小写入权限。" : $"OpenProcess 失败（Win32={code}）。";
        return false;
    }

    private static (ProcessIdentity? Identity, string ReasonCode, string Message) TryCaptureIdentity(Process process)
    {
        try
        {
            var executableName = process.ProcessName;
            var executableHash = TryGetExecutableHash(process, out var hashAvailable);
            var userSid = TryGetUserSecurityIdentifier(process, out var userError);
            if (string.IsNullOrWhiteSpace(userSid))
            {
                return (null, "process.user-unavailable", userError ?? "目标进程所属用户不可读取。" );
            }

            var architecture = GetArchitecture(process);
            var topology = GetTopologyFingerprint();
            return (new ProcessIdentity(
                process.Id,
                process.StartTime.ToUniversalTime().Ticks,
                executableName,
                executableHash,
                hashAvailable,
                process.SessionId,
                userSid,
                architecture,
                topology),
                "process.identity-ok",
                "进程 identity 已读取。");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
        {
            return (null, "process.permission-denied", "读取目标进程 identity 被拒绝。" );
        }
        catch (InvalidOperationException)
        {
            return (null, "process.exited", "目标进程已退出。" );
        }
        catch (Exception ex)
        {
            return (null, "process.identity-unavailable", ex.GetType().Name);
        }
    }

    private static string? TryGetExecutableHash(Process process, out bool available)
    {
        available = false;
        try
        {
            var path = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            available = true;
            return hash;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetUserSecurityIdentifier(Process process, out string? error)
    {
        error = null;
        if (!OpenProcessToken(process.Handle, TokenQuery, out var token))
        {
            error = "无法读取目标进程 token。";
            return null;
        }

        try
        {
            using var identity = new WindowsIdentity(token);
            return identity.User?.Value;
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name;
            return null;
        }
        finally
        {
            CloseHandle(token);
        }
    }

    private static string GetArchitecture(Process process)
    {
        if (!IsWow64Process2(process.Handle, out var processMachine, out var nativeMachine))
        {
            return "unknown";
        }

        return $"proc:{processMachine:X4};native:{nativeMachine:X4}";
    }

    private static string GetTopologyFingerprint()
    {
        var groups = GetActiveProcessorGroupCount();
        var activeProcessors = GetActiveProcessorCount(AllProcessorGroups);
        return groups == 0 || activeProcessors == 0
            ? "unknown"
            : $"groups:{groups};logical:{activeProcessors};os64:{Environment.Is64BitOperatingSystem}";
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWow64Process2(IntPtr processHandle, out ushort processMachine, out ushort nativeMachine);

    [DllImport("kernel32.dll")]
    private static extern ushort GetActiveProcessorGroupCount();

    [DllImport("kernel32.dll")]
    private static extern uint GetActiveProcessorCount(ushort groupNumber);
}
