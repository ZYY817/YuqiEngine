using System.Diagnostics;

namespace GameOptimizer.Core.Services.Memory;

/// <summary>
/// Non-UI guard for the irreversible process-termination capability.  The UI may
/// categorize processes for guidance, but this guard is also enforced by the
/// Windows execution boundary before it opens or kills a process.
/// </summary>
public static class ProcessTerminationSafety
{
    public static readonly IReadOnlySet<string> SystemCriticalProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso",
        "smss", "dwm", "fontdrvhost", "sihost", "taskhostw", "explorer", "ctfmon",
        "conhost", "RuntimeBroker", "dllhost", "WmiPrvSE", "svchost", "spoolsv",
        "SearchIndexer", "SecurityHealthService", "MsMpEng", "NisSrv", "WUDFHost",
        "audiodg", "dasHost", "Memory Compression", "Registry", "Secure System"
    };

    public static bool TryValidate(MemoryProcessIdentity identity, out string reasonCode, out string message)
    {
        if (!identity.IsUsable || identity.ProcessId <= 4)
        {
            reasonCode = "process.termination-target-invalid";
            message = "目标进程身份不完整或不是普通用户进程，未执行结束操作。";
            return false;
        }

        if (SystemCriticalProcessNames.Contains(identity.ExecutableName))
        {
            reasonCode = "process.termination-system-protected";
            message = "系统关键进程受保护，拒绝结束。";
            return false;
        }

        using var current = Process.GetCurrentProcess();
        if (identity.ProcessId == current.Id
            || string.Equals(identity.ExecutableName, current.ProcessName, StringComparison.OrdinalIgnoreCase))
        {
            reasonCode = "process.termination-self-protected";
            message = "不能通过内存优化结束宇奇引擎自身或同名宿主进程。";
            return false;
        }

        reasonCode = "process.termination-eligible";
        message = "目标进程通过基础安全核验。";
        return true;
    }
}
