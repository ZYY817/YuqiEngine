using System.Security.Principal;
using GameOptimizer.Core.Services;
using GameOptimizer.Core.Services.Memory;

namespace GameOptimizer.Infrastructure.Windows.Services;

public sealed class WindowsMmAgentAccessor : IMmAgentAccessor
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(20);
    private static readonly object AvailabilityLock = new();
    private static bool? _cmdletAvailable;

    public async Task<MmAgentPlatformAssessment> ProbeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            return new MmAgentPlatformAssessment(false, "os.unsupported", "需要 Windows 10 2004 或更高版本。");

        // Why: 部分 SKU（如 Home 版）没有 MMAgent cmdlet，仅按 OS 版本+权限
        // 报"可用"会让 apply 在运行时才发现 cmdlet 不存在。
        var available = await EnsureCmdletAvailableAsync(cancellationToken).ConfigureAwait(false);
        if (!available)
        {
            return new MmAgentPlatformAssessment(
                false,
                "platform.mmagent-unavailable",
                "当前系统不存在 MMAgent 管理接口（如 Windows Home 版），内存压缩能力不可用。");
        }

        var administrator = IsAdministrator();
        return new MmAgentPlatformAssessment(
            administrator,
            administrator ? "applicable" : "permission.administrator-required",
            administrator
                ? "当前进程已提升，可读写 MMAgent 内存压缩状态。"
                : "修改内存压缩需要管理员权限。");
    }

    private static async Task<bool> EnsureCmdletAvailableAsync(CancellationToken cancellationToken)
    {
        lock (AvailabilityLock)
        {
            if (_cmdletAvailable.HasValue)
            {
                return _cmdletAvailable.Value;
            }
        }

        bool available;
        try
        {
            var result = await ProcessRunner.RunPowerShellAsync(
                "Get-Command Get-MMAgent -ErrorAction SilentlyContinue",
                CommandTimeout,
                cancellationToken).ConfigureAwait(false);
            available = result.ExitCode == 0
                && result.Output.Contains("Get-MMAgent", StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // 探测自身失败时按不可用处理（fail-closed）。
            available = false;
        }

        lock (AvailabilityLock)
        {
            _cmdletAvailable = available;
        }
        return available;
    }

    public async Task<MmAgentStateSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await ProcessRunner.RunPowerShellAsync(
            "(Get-MMAgent).MemoryCompression",
            CommandTimeout,
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
            throw new InvalidOperationException($"无法读取 MMAgent 内存压缩状态：{(result.Error ?? result.Output).Trim()}");

        var output = result.Output.Trim();
        if (output.EndsWith("True", StringComparison.OrdinalIgnoreCase))
            return new MmAgentStateSnapshot(MemoryCompressionEnabled: true);
        if (output.EndsWith("False", StringComparison.OrdinalIgnoreCase))
            return new MmAgentStateSnapshot(MemoryCompressionEnabled: false);

        throw new InvalidOperationException($"MMAgent 内存压缩状态输出无法解析：{output}");
    }

    public async Task<MmAgentMutationResult> ApplyAsync(bool memoryCompressionEnabled, CancellationToken cancellationToken)
    {
        var before = await ReadAsync(cancellationToken).ConfigureAwait(false);
        if (before.MemoryCompressionEnabled == memoryCompressionEnabled)
            return new MmAgentMutationResult(true, false, "no-change", "内存压缩状态已符合目标。");

        var cmdlet = memoryCompressionEnabled ? "Enable-MMAgent" : "Disable-MMAgent";
        var result = await ProcessRunner.RunPowerShellAsync(
            $"{cmdlet} -MemoryCompression",
            CommandTimeout,
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            return new MmAgentMutationResult(false, false, "mmagent.failed", (result.Error ?? result.Output).Trim());

        var after = await ReadAsync(cancellationToken).ConfigureAwait(false);
        return new MmAgentMutationResult(
            after.MemoryCompressionEnabled == memoryCompressionEnabled,
            true,
            after.MemoryCompressionEnabled == memoryCompressionEnabled ? "ok" : "verification.mismatch",
            "内存压缩状态已切换。");
    }

    public Task<MmAgentMutationResult> RestoreAsync(MmAgentStateSnapshot original, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(original);
        return ApplyAsync(original.MemoryCompressionEnabled, cancellationToken);
    }

    private static bool IsAdministrator() => GameOptimizer.Core.Services.WindowsElevation.IsElevated;
}
