using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Collections.Concurrent;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.Memory;

namespace GameOptimizer.Infrastructure.Windows.Memory;

/// <summary>
/// Windows boundary for a deliberately narrow memory action. Only a user-selected,
/// identity-verified process working set can be trimmed. Machine-wide standby-list
/// purge remains fail-closed because it has no truthful recovery path.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsMemoryCleaningPlatform : IMemoryCleaningPlatform
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint ProcessSetQuota = 0x0100;
    private const uint ProcessTerminate = 0x0001;
    private const uint TokenQuery = 0x0008;
    private const ushort AllProcessorGroups = 0xFFFF;
    private static readonly ConcurrentDictionary<string, long> TrimBaselines = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<int, MemoryProcessIdentity> TerminatedProcesses = new();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusBuffer buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessWorkingSetSize(
        IntPtr process,
        IntPtr minimumWorkingSetSize,
        IntPtr maximumWorkingSetSize);

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

    public Task<CapabilityAssessment> ProbeAsync(
        MemoryCleaningCapabilityKind capability,
        TargetScope scope,
        ChangeIntent intent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(new CapabilityAssessment(
                CapabilityApplicability.Unsupported, false, "platform.not-windows", "当前平台不是 Windows。"));
        }

        if (capability == MemoryCleaningCapabilityKind.StandbyListPurge)
        {
            return Task.FromResult(new CapabilityAssessment(
                CapabilityApplicability.Unsupported,
                false,
                "memory.recovery-unavailable",
                "待机列表是瞬态机器状态，没有可持久化的原始材料和真实逆操作；已保持禁用。"));
        }

        if (scope.Kind != TargetScopeKind.Process || !int.TryParse(scope.Identity, out var processId) || processId <= 4)
        {
            return Task.FromResult(new CapabilityAssessment(
                CapabilityApplicability.NotApplicable, false, "memory.process-scope-invalid", "必须选择一个当前存在的普通进程。"));
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            var identity = ReadProcessIdentity(process);
            if (!identity.IsUsable)
            {
                return Task.FromResult(new CapabilityAssessment(
                    CapabilityApplicability.Unsupported, false, "memory.process-identity-unavailable", "无法完整核验目标进程身份。"));
            }

            if (capability == MemoryCleaningCapabilityKind.ProcessTermination
                && !ProcessTerminationSafety.TryValidate(identity, out var safetyCode, out var safetyMessage))
            {
                return Task.FromResult(new CapabilityAssessment(
                    CapabilityApplicability.NotApplicable, false, safetyCode, safetyMessage));
            }

            var access = capability == MemoryCleaningCapabilityKind.ProcessTermination
                ? ProcessQueryLimitedInformation | ProcessTerminate
                : ProcessQueryLimitedInformation | ProcessSetQuota;
            var handle = OpenProcess(access, false, processId);
            if (handle == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                return Task.FromResult(new CapabilityAssessment(
                    CapabilityApplicability.Applicable,
                    false,
                    error == 5 ? "memory.process-permission-denied" : "memory.process-access-denied",
                    capability == MemoryCleaningCapabilityKind.ProcessTermination
                        ? (error == 5 ? "没有结束目标进程的权限。" : "无法打开目标进程以执行结束操作。")
                        : (error == 5 ? "没有修改目标进程工作集的权限。" : "无法打开目标进程以执行工作集释放。")));
            }

            CloseHandle(handle);
            return Task.FromResult(new CapabilityAssessment(
                CapabilityApplicability.Applicable,
                true,
                capability == MemoryCleaningCapabilityKind.ProcessTermination
                    ? "memory.process-termination-applicable"
                    : "memory.working-set-applicable",
                capability == MemoryCleaningCapabilityKind.ProcessTermination
                    ? "目标进程身份、系统保护规则和结束权限已通过核验；结束后不可恢复。"
                    : "目标进程身份和最小写入权限已通过核验；此操作不可回滚。"));
        }
        catch (ArgumentException)
        {
            return Task.FromResult(new CapabilityAssessment(CapabilityApplicability.NotApplicable, false, "memory.process-exited", "目标进程已退出。"));
        }
        catch (Exception)
        {
            return Task.FromResult(new CapabilityAssessment(CapabilityApplicability.Unsupported, false, "memory.process-read-failed", "无法读取目标进程状态。"));
        }
    }

    public Task<StateObservation> ReadAsync(
        MemoryCleaningCapabilityKind capability,
        TargetScope scope,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var memory = ReadMemoryStatus();
        MemoryProcessIdentity? identity = null;
        long workingSetBytes = 0;
        var terminated = false;
        if (scope.Kind == TargetScopeKind.Process)
        {
            if (!int.TryParse(scope.Identity, out var processId) || processId <= 4)
            {
                throw new InvalidOperationException("memory.process-scope-invalid");
            }

            try
            {
                using var process = Process.GetProcessById(processId);
                workingSetBytes = process.WorkingSet64;
                identity = ReadProcessIdentity(process);
            }
            catch (ArgumentException) when (capability == MemoryCleaningCapabilityKind.ProcessTermination
                                            && TerminatedProcesses.TryGetValue(processId, out var completedIdentity))
            {
                identity = completedIdentity;
                terminated = true;
            }
        }

        var markerKey = identity is null ? null : MarkerKey(identity);
        var trimmed = markerKey is not null
            && TrimBaselines.TryGetValue(markerKey, out var baseline)
            && workingSetBytes <= baseline;
        var observed = new MemoryCleaningObservedState(
            capability,
            scope,
            memory,
            identity,
            workingSetBytes,
            terminated ? "terminated" : trimmed ? "trimmed" : "observed",
            RecoveryCapable: false);
        var payload = MemoryCleaningPayloadCodec.SerializeObserved(observed);
        return Task.FromResult(new StateObservation(
            scope,
            payload,
            terminated && identity is not null
                ? MemoryCleaningPayloadCodec.ProcessTerminationFingerprint(identity)
                : capability == MemoryCleaningCapabilityKind.ProcessTermination && identity?.IsUsable == true
                    ? MemoryCleaningPayloadCodec.ProcessTerminationObservedFingerprint(identity)
                : capability == MemoryCleaningCapabilityKind.WorkingSetTrim && identity?.IsUsable == true
                    ? trimmed
                        ? MemoryCleaningPayloadCodec.WorkingSetTrimFingerprint(identity)
                        : MemoryCleaningPayloadCodec.WorkingSetObservedFingerprint(identity)
                    : MemoryCleaningPayloadCodec.Hash(payload),
            DateTimeOffset.UtcNow,
            "windows-memory-read-only-platform",
            identity?.IsUsable == true ? "memory-and-process-identity" : "memory-only-or-incomplete-process-identity"));
    }

    public Task<PlatformExecutionResult> ApplyAsync(
        MemoryCleaningCapabilityKind capability,
        PreparedCapabilityChange change,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (capability == MemoryCleaningCapabilityKind.StandbyListPurge)
        {
            return Task.FromResult(Blocked(capability, "memory.recovery-unavailable"));
        }

        try
        {
            var target = MemoryCleaningPayloadCodec.DeserializeTarget(change.TargetPayload);
            var expected = target.ProcessIdentity;
            if (target.Capability != capability || expected?.IsUsable != true)
            {
                return Task.FromResult(new PlatformExecutionResult(false, false, "memory.target-invalid", "目标进程身份不完整，未执行任何操作。"));
            }

            using var process = Process.GetProcessById(expected.ProcessId);
            var actual = ReadProcessIdentity(process);
            if (actual != expected)
            {
                return Task.FromResult(new PlatformExecutionResult(false, false, "memory.process-identity-drift", "目标进程已退出、PID 已复用或身份发生变化，未执行任何操作。"));
            }

            if (capability == MemoryCleaningCapabilityKind.ProcessTermination)
            {
                if (!ProcessTerminationSafety.TryValidate(actual, out var safetyCode, out var safetyMessage))
                {
                    return Task.FromResult(new PlatformExecutionResult(false, false, safetyCode, safetyMessage));
                }

                process.Kill(entireProcessTree: false);
                var exited = process.WaitForExit(5000);
                if (exited || process.HasExited)
                {
                    TerminatedProcesses[expected.ProcessId] = expected;
                    return Task.FromResult(new PlatformExecutionResult(
                        true,
                        true,
                        "memory.process-terminated",
                        "目标进程已结束，正在通过独立回读确认。"));
                }

                return Task.FromResult(new PlatformExecutionResult(
                    false,
                    true,
                    "memory.process-termination-timeout",
                    "已发出结束请求，但等待目标进程退出超时；结果将以独立回读为准。"));
            }

            if (capability != MemoryCleaningCapabilityKind.WorkingSetTrim)
            {
                return Task.FromResult(new PlatformExecutionResult(false, false, "memory.capability-invalid", "不支持的进程操作，未执行任何操作。"));
            }

            // Do not use Process.Handle here. Depending on the runtime it can
            // request substantially broader access than the trim itself needs,
            // causing an execution-time denial after ProbeAsync already passed
            // with PROCESS_SET_QUOTA. Keep the mutation handle identical to the
            // least-privilege probe handle.
            var handle = OpenProcess(ProcessQueryLimitedInformation | ProcessSetQuota, false, expected.ProcessId);
            if (handle == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                return Task.FromResult(new PlatformExecutionResult(
                    false,
                    false,
                    error == 5 ? "memory.process-permission-denied" : "memory.process-access-denied",
                    error == 5 ? "没有修改目标进程工作集的权限，未执行任何操作。" : "无法打开目标进程以执行工作集释放。"));
            }

            var before = process.WorkingSet64;
            bool trimmed;
            int trimError;
            try
            {
                trimmed = SetProcessWorkingSetSize(handle, new IntPtr(-1), new IntPtr(-1));
                trimError = trimmed ? 0 : Marshal.GetLastWin32Error();
            }
            finally
            {
                CloseHandle(handle);
            }

            if (!trimmed)
            {
                return Task.FromResult(new PlatformExecutionResult(
                    false,
                    false,
                    trimError == 5 ? "memory.process-permission-denied" : "memory.working-set-trim-failed",
                    trimError == 5 ? "没有修改目标进程工作集的权限，未执行任何操作。" : "Windows 拒绝释放目标进程工作集，未报告成功。"));
            }

            TrimBaselines[MarkerKey(expected)] = before;
            return Task.FromResult(new PlatformExecutionResult(
                true,
                true,
                "memory.working-set-trim-issued",
                "已请求释放目标进程工作集，正在读取结果。"));
        }
        catch (ArgumentException)
        {
            return Task.FromResult(new PlatformExecutionResult(false, false, "memory.process-exited", "目标进程已退出，未执行任何操作。"));
        }
        catch (Exception)
        {
            return Task.FromResult(new PlatformExecutionResult(false, false, "memory.working-set-trim-failed", "释放目标进程工作集失败，未报告成功。"));
        }
    }

    public Task<PlatformExecutionResult> RestoreAsync(
        MemoryCleaningCapabilityKind capability,
        RecoveryMaterial material,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PlatformExecutionResult(
            false,
            false,
            capability is MemoryCleaningCapabilityKind.WorkingSetTrim or MemoryCleaningCapabilityKind.ProcessTermination ? "memory.not-reversible" : "memory.recovery-unavailable",
            capability == MemoryCleaningCapabilityKind.ProcessTermination
                ? "结束进程不可恢复；不会伪造恢复能力。"
                : capability == MemoryCleaningCapabilityKind.WorkingSetTrim
                    ? "工作集释放是不可回滚操作，无法恢复到先前工作集。"
                : "待机列表没有真实恢复材料，未执行系统写入。"));
    }

    private static PlatformExecutionResult Blocked(
        MemoryCleaningCapabilityKind capability,
        string reasonCode) =>
        new(
            Success: false,
            MutationOccurred: false,
            reasonCode,
            capability == MemoryCleaningCapabilityKind.StandbyListPurge
                ? "Standby List 没有真实恢复材料，未执行系统写入。"
                : capability == MemoryCleaningCapabilityKind.ProcessTermination
                    ? "进程结束未通过执行前核验，未执行任何操作。"
                    : "Working Set 没有真实恢复材料，未执行进程写入。");

    private static MemoryStatus ReadMemoryStatus()
    {
        var buffer = new MemoryStatusBuffer
        {
            Length = (uint)Marshal.SizeOf<MemoryStatusBuffer>()
        };
        if (!GlobalMemoryStatusEx(ref buffer))
        {
            return new MemoryStatus(0, 0, 0, 0, 0);
        }

        var totalGb = buffer.TotalPhysical / (1024.0 * 1024 * 1024);
        var availableGb = buffer.AvailablePhysical / (1024.0 * 1024 * 1024);
        return new MemoryStatus(
            totalGb - availableGb,
            totalGb,
            availableGb,
            (int)buffer.MemoryLoad,
            0);
    }

    private static MemoryProcessIdentity ReadProcessIdentity(Process process)
    {
        IntPtr queryHandle = OpenProcess(ProcessQueryLimitedInformation, false, process.Id);
        try
        {
            string? userSecurityIdentifier = queryHandle == IntPtr.Zero
                ? null
                : TryGetUserSecurityIdentifier(queryHandle);
            string architecture = queryHandle == IntPtr.Zero
                ? "unknown"
                : GetArchitecture(queryHandle);
            return new MemoryProcessIdentity(
                process.Id,
                process.StartTime.ToUniversalTime().Ticks,
                process.ProcessName,
                TryGetExecutableHash(process, out var hashAvailable),
                hashAvailable,
                process.SessionId,
                userSecurityIdentifier ?? string.Empty,
                architecture,
                GetTopologyFingerprint());
        }
        finally
        {
            if (queryHandle != IntPtr.Zero)
            {
                CloseHandle(queryHandle);
            }
        }
    }

    private static string MarkerKey(MemoryProcessIdentity identity) =>
        string.Join(':', identity.ProcessId, identity.CreationTimeUtcTicks, identity.SessionId, identity.ExecutableName);

    private static string? TryGetExecutableHash(Process process, out bool available)
    {
        available = false;
        try
        {
            var path = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            available = true;
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch { return null; }
    }

    private static string? TryGetUserSecurityIdentifier(IntPtr processHandle)
    {
        if (!OpenProcessToken(processHandle, TokenQuery, out var token)) return null;
        try
        {
            using var identity = new WindowsIdentity(token);
            return identity.User?.Value;
        }
        catch { return null; }
        finally { CloseHandle(token); }
    }

    private static string GetArchitecture(IntPtr processHandle) =>
        IsWow64Process2(processHandle, out var processMachine, out var nativeMachine)
            ? $"proc:{processMachine:X4};native:{nativeMachine:X4}"
            : "unknown";

    private static string GetTopologyFingerprint()
    {
        var groups = GetActiveProcessorGroupCount();
        var logical = GetActiveProcessorCount(AllProcessorGroups);
        return groups == 0 || logical == 0
            ? "unknown"
            : $"groups:{groups};logical:{logical};os64:{Environment.Is64BitOperatingSystem}";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusBuffer
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }
}
