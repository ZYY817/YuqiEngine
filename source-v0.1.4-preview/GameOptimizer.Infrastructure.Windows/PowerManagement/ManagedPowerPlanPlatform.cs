using System.Diagnostics;
using System.Runtime.Versioning;

namespace GameOptimizer.Infrastructure.Windows.PowerManagement;

public sealed record ManagedPowerCommandResult(bool Success, int ExitCode, string Output, string Error);

public interface IManagedPowerPlanPlatform
{
    Task<ManagedPowerCommandResult> ExportAsync(Guid schemeId, string destinationPath, CancellationToken cancellationToken);
    Task<ManagedPowerCommandResult> DuplicateAsync(Guid sourceSchemeId, Guid targetSchemeId, CancellationToken cancellationToken);
    Task<ManagedPowerCommandResult> RenameAsync(Guid schemeId, string name, CancellationToken cancellationToken);
    Task<ManagedPowerCommandResult> DeleteAsync(Guid schemeId, CancellationToken cancellationToken);
    Task<ManagedPowerCommandResult> ImportAsync(string sourcePath, Guid schemeId, CancellationToken cancellationToken);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsManagedPowerPlanPlatform : IManagedPowerPlanPlatform
{
    public Task<ManagedPowerCommandResult> ExportAsync(Guid schemeId, string destinationPath, CancellationToken cancellationToken)
    {
        ValidateScheme(schemeId);
        var path = ValidateFullyQualifiedPath(destinationPath);
        return RunAsync(["/export", path, schemeId.ToString("D")], cancellationToken);
    }

    public Task<ManagedPowerCommandResult> DuplicateAsync(Guid sourceSchemeId, Guid targetSchemeId, CancellationToken cancellationToken)
    {
        ValidateScheme(sourceSchemeId);
        ValidateScheme(targetSchemeId);
        if (sourceSchemeId == targetSchemeId)
            throw new ArgumentException("源计划和目标计划不能相同。", nameof(targetSchemeId));
        return RunAsync(["/duplicatescheme", sourceSchemeId.ToString("D"), targetSchemeId.ToString("D")], cancellationToken);
    }

    public Task<ManagedPowerCommandResult> RenameAsync(Guid schemeId, string name, CancellationToken cancellationToken)
    {
        ValidateScheme(schemeId);
        var safeName = ValidateName(name);
        return RunAsync(["/changename", schemeId.ToString("D"), safeName], cancellationToken);
    }

    public Task<ManagedPowerCommandResult> DeleteAsync(Guid schemeId, CancellationToken cancellationToken)
    {
        ValidateScheme(schemeId);
        return RunAsync(["/delete", schemeId.ToString("D")], cancellationToken);
    }

    public Task<ManagedPowerCommandResult> ImportAsync(string sourcePath, Guid schemeId, CancellationToken cancellationToken)
    {
        ValidateScheme(schemeId);
        var path = ValidateFullyQualifiedPath(sourcePath);
        if (!File.Exists(path))
            throw new FileNotFoundException("受管电源计划备份不存在。", path);
        return RunAsync(["/import", path, schemeId.ToString("D")], cancellationToken);
    }

    private static async Task<ManagedPowerCommandResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            return new(false, -1, string.Empty, "仅 Windows 支持电源计划操作。");

        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "powercfg.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            return new(false, -1, string.Empty, "无法启动 powercfg.exe。");

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);
        return new(process.ExitCode == 0, process.ExitCode, output, error);
    }

    private static void ValidateScheme(Guid schemeId)
    {
        if (schemeId == Guid.Empty)
            throw new ArgumentException("电源计划 GUID 不能为空。", nameof(schemeId));
    }

    private static string ValidateFullyQualifiedPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException("电源计划备份路径必须是绝对路径。", nameof(path));
        return Path.GetFullPath(path);
    }

    private static string ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var value = name.Trim();
        if (value.Length > 80 || value.Any(char.IsControl))
            throw new ArgumentException("电源计划名称无效。", nameof(name));
        return value;
    }
}
