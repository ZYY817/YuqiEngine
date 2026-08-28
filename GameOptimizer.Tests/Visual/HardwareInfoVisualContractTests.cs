using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace GameOptimizer.Tests.Visual;

public sealed class HardwareInfoVisualContractTests
{
    private const string PagePath = "GameOptimizer.Shell/Views/HardwareInfoPage.xaml";
    private const string CodeBehindPath = "GameOptimizer.Shell/Views/HardwareInfoPage.xaml.cs";

    [Fact]
    public void HardwarePageKeepsLegacyInformationHierarchy()
    {
        var names = Load(PagePath).Root!
            .DescendantsAndSelf()
            .Attributes(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))
            .Select(attribute => attribute.Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("OsVersion", names);
        Assert.Contains("Uptime", names);
        Assert.Contains("Motherboard", names);
        Assert.Contains("CpuName", names);
        Assert.Contains("GpuName", names);
        Assert.Contains("MemoryTotal", names);
        Assert.Contains("DiskListPanel", names);

        var xaml = Read(PagePath);
        Assert.Contains("Web 母版的细顶栏", xaml, StringComparison.Ordinal);
        Assert.Contains("整机摘要", xaml, StringComparison.Ordinal);
        Assert.Contains("WebCard", xaml, StringComparison.Ordinal);
        Assert.Contains("磁盘明细", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CpuVendorText\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void HardwarePageExposesLoadingEmptyAndErrorStates()
    {
        var names = Load(PagePath).Root!
            .DescendantsAndSelf()
            .Attributes(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))
            .Select(attribute => attribute.Value)
            .ToHashSet(StringComparer.Ordinal);
        var codeBehind = Read(CodeBehindPath);

        Assert.Contains("StatusBanner", names);
        Assert.Contains("StatusProgress", names);
        Assert.Contains("StatusRetryButton", names);
        Assert.Contains("HardwareContent", names);
        Assert.Contains("DiskEmptyText", names);
        Assert.Contains("HardwarePageState.Loading", codeBehind, StringComparison.Ordinal);
        Assert.Contains("HardwarePageState.Empty", codeBehind, StringComparison.Ordinal);
        Assert.Contains("HardwarePageState.Error", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void HardwareRefreshAndLifetimeReuseExistingDetectionBoundary()
    {
        var codeBehind = Read(CodeBehindPath);

        Assert.Contains("HardwareInfoPage(IHardwareDetectionService hardwareDetectionService)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("_hardwareDetectionService.GetOrDetectAsync()", codeBehind, StringComparison.Ordinal);
        Assert.Contains("_hardwareDetectionService.InvalidateCache()", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("Application.Current as App", codeBehind, StringComparison.Ordinal);
        Assert.Contains("WaitAsync(cancellation.Token)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("OperationCanceledException", codeBehind, StringComparison.Ordinal);
        Assert.Contains("CpuVendorText.Text = _hardwareInfo.GetCpuVendorDisplayName()", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Unloaded += OnUnloaded", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void HardwareSummaryKeepsItsReferenceSystemInformationEntry()
    {
        var page = Read(PagePath);
        var codeBehind = Read(CodeBehindPath);

        Assert.Contains("OpenSystemInfoButton", page, StringComparison.Ordinal);
        Assert.Contains("OpenSystemInfoButton_Click", page, StringComparison.Ordinal);
        Assert.Contains("ExportReportButton", page, StringComparison.Ordinal);
        Assert.Contains("ExportReportButton_Click", page, StringComparison.Ordinal);
        Assert.Contains("FileName = \"msinfo32.exe\"", codeBehind, StringComparison.Ordinal);
        Assert.Contains("UseShellExecute = true", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void DiskRowsKeepReadableThemeForegroundsAndSeparateUsageFromProgress()
    {
        var page = Read(PagePath);

        Assert.Contains("x:Key=\"DiskPrimaryText\"", page, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"DiskSecondaryText\"", page, StringComparison.Ordinal);
        Assert.Contains("Theme.Brush.TextPrimary", page, StringComparison.Ordinal);
        Assert.Contains("Theme.Brush.TextSecondary", page, StringComparison.Ordinal);
        Assert.Contains("Width=\"220\"", page, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding UsagePercentText}\"", page, StringComparison.Ordinal);
        Assert.Contains("Grid.Row=\"0\" Value=\"{Binding UsagePercent}\"", page, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding FreeSpaceText}\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void HardwarePageDoesNotInferUnsupportedGpuCapabilities()
    {
        var page = Read(PagePath);
        var codeBehind = Read(CodeBehindPath);
        var combined = page + codeBehind;

        Assert.DoesNotContain("Ray Tracing", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" · RDNA", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GpuVendor.NVIDIA => \"RTX\"", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("GpuVendor.AMD => \"RX\"", codeBehind, StringComparison.Ordinal);
        Assert.Contains("GpuBadgeText\" Text=\"--\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void HardwareStatusBadgesCenterTheirCaptionsWithinTheBackground()
    {
        var page = Read(PagePath);

        Assert.Contains("x:Key=\"HardwareStatusBadge\"", page, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"HardwareStatusBadgeText\"", page, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"VerticalAlignment\" Value=\"Center\"/>", page, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"TextAlignment\" Value=\"Center\"/>", page, StringComparison.Ordinal);
        Assert.Equal(5, Regex.Matches(page, "Style=\"\\{StaticResource HardwareStatusBadge\\}\"").Count);
        Assert.Equal(5, Regex.Matches(page, "Style=\"\\{StaticResource HardwareStatusBadgeText\\}\"").Count);
    }

    private static XDocument Load(string relativePath)
        => XDocument.Load(RepoPath(relativePath), LoadOptions.PreserveWhitespace);

    private static string Read(string relativePath)
        => File.ReadAllText(RepoPath(relativePath));

    private static string RepoPath(string relativePath, [CallerFilePath] string sourcePath = "")
    {
        var root = FindRepoRoot(Path.GetDirectoryName(sourcePath))
            ?? FindRepoRoot(Environment.CurrentDirectory)
            ?? FindRepoRoot(AppContext.BaseDirectory)
            ?? throw new DirectoryNotFoundException("Repository root not found.");

        return Path.Combine(root.FullName, relativePath);
    }

    private static DirectoryInfo? FindRepoRoot(string? startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath))
            return null;

        var current = new DirectoryInfo(startPath);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "GameOptimizer.sln")))
            current = current.Parent;

        return current;
    }
}
