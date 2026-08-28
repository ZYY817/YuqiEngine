using System.Xml.Linq;

namespace GameOptimizer.Tests.Visual;

public sealed class NetworkTestVisualContractTests
{
    private const string XamlPath = "GameOptimizer.Shell/Views/NetworkTestPage.xaml";
    private const string CodePath = "GameOptimizer.Shell/Views/NetworkTestPage.xaml.cs";

    [Fact]
    public void NetworkTestExposesRealSpeedFlowActionsAndEvidenceLandmarks()
    {
        var xaml = Read(XamlPath);
        var document = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace);
        var xNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

        Assert.Equal("NetworkTestPage", document.Root?.Attribute(xNamespace + "Class")?.Value.Split('.').Last());
        foreach (var name in new[]
                 {
                     "PageHeader", "BackupNetworkBtn", "FlushDnsBtn", "ResetNetworkBtn",
                     "AvgLatency", "PacketLoss", "Jitter", "MinLatency", "ConnectionStatus",
                     "RunTestBtn", "CancelTestBtn", "ProgressPanel", "NodeSelector", "CustomNodeInput",
                     "DownloadSpeed", "UploadSpeed", "DnsResolution", "NodeReachability", "ResultEvidencePanel", "FailureRecoveryPanel"
                 })
        {
            Assert.Contains($"x:Name=\"{name}\"", xaml, StringComparison.Ordinal);
        }

        Assert.Contains("测速节点 / 档位", xaml, StringComparison.Ordinal);
        Assert.Contains("StatusText=\"正在读取连接\"", xaml, StringComparison.Ordinal);
        Assert.Contains("StatusDetail=\"节点未测试\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding", xaml, StringComparison.Ordinal) ;
        Assert.Contains("Visibility=\"Collapsed\"><TextBlock x:Name=\"AvgLatency\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StabilityValuePanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DnsValuePanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ResultEvidencePanel\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("结果建议", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PathGeometry", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PointCollection", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PingPolyline", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void NetworkTestKeepsCancellationErrorsAndControlledNetworkOwnerBoundary()
    {
        var xaml = Read(XamlPath);
        var code = Read(CodePath);

        foreach (var (name, handler) in new[]
                 {
                     ("RunTestBtn", "RunTest_Click"),
                     ("CancelTestBtn", "CancelTest_Click"),
                     ("NodeSelector", "NodeSelector_SelectionChanged"),
                     ("BackupNetworkBtn", "BackupNetwork_Click"),
                     ("FlushDnsBtn", "FlushDns_Click"),
                     ("ResetNetworkBtn", "ResetNetwork_Click")
                 })
        {
            Assert.Contains($"x:Name=\"{name}\"", xaml, StringComparison.Ordinal);
            Assert.Contains($"{handler}", xaml, StringComparison.Ordinal);
        }

        Assert.Contains("_cts?.Cancel()", code, StringComparison.Ordinal);
        Assert.Contains("catch (OperationCanceledException)", code, StringComparison.Ordinal);
        Assert.Contains("catch (Exception ex)", code, StringComparison.Ordinal);
        Assert.Contains("PreviewMouseLeftButtonDown=\"NodeSelector_PreviewMouseLeftButtonDown\"", xaml, StringComparison.Ordinal);
        Assert.Contains("private void NodeSelector_PreviewMouseLeftButtonDown", code, StringComparison.Ordinal);
        Assert.Contains("TryOpenNodeSelectorDropDown(selector)", code, StringComparison.Ordinal);
        Assert.Contains("internal bool TryOpenNodeSelectorDropDown", code, StringComparison.Ordinal);
        Assert.Contains("selector.IsDropDownOpen = true", code, StringComparison.Ordinal);
        Assert.Contains("selector.Focus()", code, StringComparison.Ordinal);
        Assert.Contains("e.Handled = true", code, StringComparison.Ordinal);
        Assert.Contains("INetworkResetOwner", code, StringComparison.Ordinal);
        Assert.Contains("SucceededVerified", code, StringComparison.Ordinal);
        Assert.Contains("SetRunTestButtonText(loc.Get(\"Network.CancelTest\"))", code, StringComparison.Ordinal);
        Assert.Contains("SetRunTestButtonText(loc.Get(\"Network.StartTest\"))", code, StringComparison.Ordinal);
        Assert.Contains("CreateLinkedTokenSource(userCts.Token, timeoutCts.Token)", code, StringComparison.Ordinal);
        Assert.Contains("timeoutCts.IsCancellationRequested && !userCts.IsCancellationRequested", code, StringComparison.Ordinal);
        Assert.Contains("_speedTestService.RunAsync(node, progress, ct)", code, StringComparison.Ordinal);
        Assert.Contains("ShowCompletedResult(result, isAcceptanceEvidence: false)", code, StringComparison.Ordinal);
        Assert.Contains("不保留不完整结果", code, StringComparison.Ordinal);
        Assert.Contains("应用不会尝试提权", code, StringComparison.Ordinal);

        foreach (var forbidden in new[]
                 {
                     "netsh winsock reset", "netsh int ip reset", "advfirewall reset",
                     "Arguments = \"/flushdns\"", "FileName = \"shutdown\""
                 })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void NetworkTestKeepsDarkThemeReadabilityCenteredPulseAndFinalResultGate()
    {
        var xaml = Read(XamlPath);
        var code = Read(CodePath);

        Assert.Contains("<Style TargetType=\"TextBlock\"><Setter Property=\"Foreground\" Value=\"{DynamicResource Theme.Brush.TextPrimary}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PulseRingOuter\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PulseRingInner\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RadarSweep\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RunTestBtn\" Click=\"RunTest_Click\" Width=\"112\" Height=\"112\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Storyboard.TargetProperty=\"(UIElement.RenderTransform).(RotateTransform.Angle)\"", xaml, StringComparison.Ordinal);

        var run = code.IndexOf("_speedTestService.RunAsync(node, progress, ct)", StringComparison.Ordinal);
        var completed = code.IndexOf("ShowCompletedResult(result, isAcceptanceEvidence: false)", run, StringComparison.Ordinal);
        Assert.True(run >= 0 && completed > run, "Final metrics must be revealed only after the complete service result returns.");
        Assert.Contains("ResetMeasurementPresentation", code, StringComparison.Ordinal);
        Assert.Contains("AvgLatencyValuePanel.Visibility = Visibility.Collapsed", code, StringComparison.Ordinal);
        Assert.Contains("StabilityValuePanel.Visibility = Visibility.Collapsed", code, StringComparison.Ordinal);
        Assert.Contains("DnsValuePanel.Visibility = Visibility.Collapsed", code, StringComparison.Ordinal);
        Assert.Contains("ResultEvidencePanel.Visibility = Visibility.Collapsed", code, StringComparison.Ordinal);
        Assert.Contains("StartRadarAnimation();", code, StringComparison.Ordinal);
        Assert.Contains("StopRadarAnimation();", code, StringComparison.Ordinal);
        Assert.Contains("CurrentNodeText.Text", code, StringComparison.Ordinal);
    }

    [Fact]
    public void NetworkTestDoesNotExposeIntermediateOrAcceptanceRunningSamplesAsFinalMetrics()
    {
        var code = Read(CodePath);
        var progressStart = code.IndexOf("private void ApplySpeedTestProgress", StringComparison.Ordinal);
        var progressEnd = code.IndexOf("private void ResetMeasurementPresentation", progressStart, StringComparison.Ordinal);
        var progressMethod = code[progressStart..progressEnd];
        var acceptanceStart = code.IndexOf("private void ApplyAcceptanceVisualState", StringComparison.Ordinal);
        var acceptanceMethod = code[acceptanceStart..];

        Assert.DoesNotContain("DownloadSpeed.Text = mbps", progressMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("UploadSpeed.Text = mbps", progressMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("DownloadSpeed.Text = \"86.4\"", acceptanceMethod, StringComparison.Ordinal);
        Assert.Contains("未生成最终结果", acceptanceMethod, StringComparison.Ordinal);
        Assert.Contains("ShowCompletedResult", code, StringComparison.Ordinal);
    }

    private static string Read(string relativePath) => File.ReadAllText(RepoPath(relativePath));

    private static string RepoPath(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameOptimizer.Main.sln")))
                return Path.Combine(directory.FullName, relativePath);
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
