using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace GameOptimizer.Core.Services.Memory;

[SupportedOSPlatform("windows")]
public interface IMemoryCleanerService
{
    MemoryStatus GetCurrentStatus();
    Task<CleanResult> SmartBoostAsync();
    Task<CleanResult> ClearStandbyListAsync();
    Task<CleanResult> FlushWorkingSetsAsync();
    bool IsAdministrator { get; }
}

/// <summary>
/// Compatibility facade retained for read-only memory monitoring. All mutation
/// methods fail closed and must not become a second writer beside
/// <see cref="IMemoryCleaningOwner"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MemoryCleanerService : IMemoryCleanerService
{
    public const string MutationBlockedReason =
        "内存清理缺少可逆恢复材料；请通过唯一 typed Memory Owner 核验。当前未执行系统或进程写入，也未生成 RecoveryId。";

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusBuffer buffer);

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

    public bool IsAdministrator
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public MemoryStatus GetCurrentStatus()
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
            StandbyBytes: 0);
    }

    public Task<CleanResult> SmartBoostAsync() => BlockedAsync();

    public Task<CleanResult> ClearStandbyListAsync() => BlockedAsync();

    public Task<CleanResult> FlushWorkingSetsAsync() => BlockedAsync();

    private static Task<CleanResult> BlockedAsync() =>
        Task.FromResult(new CleanResult(false, 0, MutationBlockedReason));
}
