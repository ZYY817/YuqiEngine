namespace GameOptimizer.Tests.Safety;

/// <summary>
/// Source-level safety contracts for the legacy high-risk entry points. These tests
/// intentionally inspect source only: no command, registry, or deletion API is invoked.
/// </summary>
public sealed class HighRiskExecutionSafetyRegressionTests
{
    [Fact]
    public void Transparency_command_probe_remains_read_only_gated_and_bounded()
    {
        var source = Read("GameOptimizer.Shell/Views/OperationTransparencyDialog.xaml.cs");

        var gate = Slice(source, "private static bool CanExecuteReadOnlyVerify", "private static bool IsReadOnlyCommandSegment");
        Assert.Contains("segments.All(IsReadOnlyCommandSegment)", gate, StringComparison.Ordinal);
        Assert.DoesNotContain("reg add", gate, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reg delete", gate, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Remove-Item", gate, StringComparison.OrdinalIgnoreCase);

        var runner = Slice(source, "private static async Task<ProbeResult> RunReadOnlyCommandAsync", "private static string ExtractCurrentValue");
        Assert.Contains("FileName = \"cmd.exe\"", runner, StringComparison.Ordinal);
        Assert.Contains("Arguments = $\"/d /c {command}\"", runner, StringComparison.Ordinal);
        Assert.Contains("UseShellExecute = false", runner, StringComparison.Ordinal);
        Assert.Contains("RedirectStandardOutput = true", runner, StringComparison.Ordinal);
        Assert.Contains("RedirectStandardError = true", runner, StringComparison.Ordinal);
        Assert.Contains("process.WaitForExit(timeoutMs)", runner, StringComparison.Ordinal);
        Assert.Contains("process.Kill(entireProcessTree: true)", runner, StringComparison.Ordinal);
    }

    [Fact]
    public void Transparency_never_invents_a_rollback_for_missing_snapshot_or_composite_registry_values()
    {
        var source = Read("GameOptimizer.Shell/Views/OperationTransparencyDialog.xaml.cs");
        var xaml = Read("GameOptimizer.Shell/Views/OperationTransparencyDialog.xaml");

        var rollback = Slice(source, "private static string BuildRollbackCommand", "private static string BuildExecutionAction");
        Assert.Contains("toggle.RequiresSnapshotForRestore || string.IsNullOrWhiteSpace(toggle.DefaultValue)", rollback, StringComparison.Ordinal);
        Assert.DoesNotContain("reg delete", rollback, StringComparison.OrdinalIgnoreCase);

        var oneClick = Slice(source, "private async void OneClickRollbackButton_Click", "private void CopyPathButton_Click");
        Assert.Contains("!_toggle.IsApplied || !_toggle.CanRestore", oneClick, StringComparison.Ordinal);

        var apply = Slice(source, "private static string BuildApplyCommand", "private static string BuildVerifyCommand");
        Assert.Contains("IsCompositeRegistryValue", apply, StringComparison.Ordinal);
        Assert.Contains("透明卡不伪造单条 reg 命令", apply, StringComparison.Ordinal);

        Assert.Contains("x:Name=\"CopyRollbackButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Grid Margin=\"0,10,0,0\">", xaml, StringComparison.Ordinal);
        Assert.Contains("<Expander Header=\"技术详情（可选）\" IsExpanded=\"False\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Recoverable_cleanup_cannot_bypass_preview_confirmation_or_quarantine_restore()
    {
        var source = Read("GameOptimizer.Core/Optimization/Operations/OrchestratedFileCleanupOperation.cs");

        Assert.Contains("CreatePreviewAsync", source, StringComparison.Ordinal);
        Assert.Contains("_authorizationGate.AuthorizeAsync", source, StringComparison.Ordinal);
        Assert.Contains("CleanupPreviewConfirmation.Create", source, StringComparison.Ordinal);
        Assert.Contains("CleanPreviewAsync", source, StringComparison.Ordinal);
        Assert.Contains("_quarantineManager.CreateRestorePreviewAsync", source, StringComparison.Ordinal);
        Assert.Contains("_quarantineManager.RestoreAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("File.Delete(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Directory.Delete(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Typed_registry_capabilities_keep_prepare_restore_and_compensation_boundaries()
    {
        foreach (var relativePath in new[]
                 {
                     "GameOptimizer.Infrastructure.Windows/Registry/RegistryDwordCapability.cs",
                     "GameOptimizer.Infrastructure.Windows/Registry/RegistryStringCapability.cs",
                     "GameOptimizer.Infrastructure.Windows/Registry/RegistryValueSetCapability.cs",
                     "GameOptimizer.Infrastructure.Windows/Registry/RegistryValueDeleteCapability.cs"
                 })
        {
            var source = Read(relativePath);
            Assert.Contains("PrepareAsync", source, StringComparison.Ordinal);
            Assert.Contains("RestoreAsync", source, StringComparison.Ordinal);
            Assert.Contains("BeforePayload", source, StringComparison.Ordinal);
            Assert.Contains("state.drift", source, StringComparison.Ordinal);
            Assert.Contains("RestoreCurrentUser", source, StringComparison.Ordinal);
        }
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Could not locate source slice: {startMarker}");
        return source[start..end];
    }

    private static string Read(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GameOptimizer.sln")))
            directory = directory.Parent;

        var root = directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
        return File.ReadAllText(Path.Combine(root, relativePath));
    }
}
