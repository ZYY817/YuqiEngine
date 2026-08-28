using System.Security.Principal;
using GameOptimizer.Core.Services;
using GameOptimizer.Core.Services.Memory;

namespace GameOptimizer.Infrastructure.Windows.Services;

public sealed class WindowsMmAgentAccessor : IMmAgentAccessor
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(20);

    public Task<MmAgentPlatformAssessment> ProbeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            return Task.FromResult(new MmAgentPlatformAssessment(false, "os.unsupported", "需要 Windows 10 2004 或更高版本。"));

        var administrator = IsAdministrator();
        return Task.FromResult(new MmAgentPlatformAssessment(
            administrator,
            administrator ? "applicable" : "permission.administrator-required",
            administrator
                ? "当前进程已提升，可读写 MMAgent 内存压缩状态。"
                : "修改内存压缩需要管理员权限。"));
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

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
