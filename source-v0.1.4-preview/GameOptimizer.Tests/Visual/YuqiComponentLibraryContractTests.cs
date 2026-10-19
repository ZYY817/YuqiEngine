using System.Xml.Linq;
using System.Runtime.CompilerServices;

namespace GameOptimizer.Tests.Visual;

public sealed class YuqiComponentLibraryContractTests
{
    [Fact]
    public void ComponentLibraryIsMergedAfterCompatibilityOverrides()
    {
        var app = Read("GameOptimizer.Shell/App.xaml");
        var compatibility = app.IndexOf("ShellCompatibilityResources.xaml", StringComparison.Ordinal);
        var library = app.IndexOf("YuqiComponentLibrary.xaml", StringComparison.Ordinal);

        Assert.True(compatibility >= 0, "The legacy compatibility resources must remain available.");
        Assert.True(library > compatibility, "The component library must be merged last so native controls keep a single visual authority.");
    }

    [Fact]
    public void LibraryDefinesCompleteNativeControlStates()
    {
        var xaml = Read("GameOptimizer.Shell/Themes/YuqiComponentLibrary.xaml");

        foreach (var token in new[]
        {
            "Yuqi.Card", "Yuqi.Card.Elevated", "Yuqi.Card.Interactive", "Yuqi.Card.Info", "Yuqi.Card.Success", "Yuqi.Card.Warning",
            "Yuqi.ToolButton", "PrimaryButtonStyle", "SecondaryButtonStyle", "DangerButtonStyle",
            "TargetType=\"TextBox\"", "TargetType=\"ComboBox\"", "PART_Popup", "TargetType=\"ComboBoxItem\"",
            "TargetType=\"ToggleButton\"", "TargetType=\"ListBoxItem\"", "TargetType=\"ScrollBar\"", "PART_Track",
            "Yuqi.RailToggle", "Yuqi.SelectionCheckBox", "Yuqi.Progress"
        })
            Assert.Contains(token, xaml, StringComparison.Ordinal);

        Assert.DoesNotContain("CornerRadius=\"4\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CornerRadius=\"8\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PlacementTarget=\"{Binding RelativeSource={RelativeSource TemplatedParent}}\"", xaml, StringComparison.Ordinal);
        _ = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace);
    }

    [Fact]
    public void StaticCardsDoNotGainInteractiveChromeOnPointerHover()
    {
        var library = XDocument.Parse(
            Read("GameOptimizer.Shell/Themes/YuqiComponentLibrary.xaml"),
            LoadOptions.PreserveWhitespace);
        var presentation = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation");
        var xNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

        XElement Style(string key) => library.Descendants(presentation + "Style")
            .Single(element => element.Attribute(xNamespace + "Key")?.Value == key);

        Assert.Equal("{StaticResource Yuqi.Card}", Style("SectionCardStyle").Attribute("BasedOn")?.Value);
        Assert.Equal("{StaticResource Yuqi.Card}", Style("InnerCardStyle").Attribute("BasedOn")?.Value);
        Assert.Empty(Style("Yuqi.Card").Descendants(presentation + "Trigger"));

        var interactive = Style("Yuqi.Card.Interactive").ToString(SaveOptions.DisableFormatting);
        Assert.Contains("IsMouseOver", interactive, StringComparison.Ordinal);
        Assert.Contains("Theme.Brush.CardBorderHover", interactive, StringComparison.Ordinal);
        Assert.Contains("IsKeyboardFocusWithin", interactive, StringComparison.Ordinal);
        Assert.Contains("Theme.Brush.ControlAccent", interactive, StringComparison.Ordinal);
    }

    [Fact]
    public void CpuConsoleUsesComponentLayerAndKeepsTwoExclusiveRealWorkflows()
    {
        var xaml = Read("GameOptimizer.Shell/Views/CpuCoreManagementPage.xaml");

        Assert.Contains("x:Key=\"SwissTab\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"CoreCell\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"EditorialRow\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectTabCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Profiles}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("RefreshProcessesCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("ApplySingleProfileCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("DeleteProfileCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("StartAccelerateCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("controls:EmptyState", xaml, StringComparison.Ordinal);
        Assert.Contains("ActionCommand=\"{Binding AddProcessCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemContainerStyle=\"{StaticResource EditorialRow}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LiveWorkspace\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ProfileWorkspace\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LiveCommandBar\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ProfileCommandBar\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"NoProfileSelectedState\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ProfileCorePreview\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ProcessListBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TopologyScrollViewer\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ActionCard", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Panel.ZIndex=\"10\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void CpuConsoleKeepsIndependentProcessAndTopologyScrollRegions()
    {
        var xaml = Read("GameOptimizer.Shell/Views/CpuCoreManagementPage.xaml");
        var view = Read("GameOptimizer.Shell/Views/CpuCoreManagementPage.xaml.cs");
        var viewModel = Read("GameOptimizer.Shell/ViewModels/CpuCoreManagementViewModel.cs");

        Assert.Contains("ScrollViewer.VerticalScrollBarVisibility=\"Auto\"", xaml, StringComparison.Ordinal);
        Assert.Contains("UpdateWorkspaceLayout", view, StringComparison.Ordinal);
        Assert.Contains("ActualWidth < 900", view, StringComparison.Ordinal);
        Assert.Contains("HasUsableTopology", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("Unknown CPU", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void CpuConsoleKeepsTheReferenceWorkbenchSeparatedAndSearchIsViewOnly()
    {
        var xaml = Read("GameOptimizer.Shell/Views/CpuCoreManagementPage.xaml");
        var view = Read("GameOptimizer.Shell/Views/CpuCoreManagementPage.xaml.cs");

        Assert.Contains("x:Name=\"ProcessSearchBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ProcessFilterBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Padding=\"10,7,10,7\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"当前目标：\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"可配置进程\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectTabCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("ActiveTab", view, StringComparison.Ordinal);
        Assert.Contains("UpdateProfilePreviewState", view, StringComparison.Ordinal);
        Assert.DoesNotContain("ActionCard", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Panel.ZIndex", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("TargetProgramLabel", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("选择要加速的游戏", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CoreAccel.TargetProgram", view, StringComparison.Ordinal);
        Assert.Contains("CollectionViewSource.GetDefaultView", view, StringComparison.Ordinal);
        Assert.Contains("_processListView.Filter = FilterProcess", view, StringComparison.Ordinal);
        Assert.DoesNotContain("ProcessList.Clear", view, StringComparison.Ordinal);
        Assert.DoesNotContain("RunInteractiveAsync", view, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GameOptimizer.Shell/Views/NetworkTestPage.xaml")]
    [InlineData("GameOptimizer.Shell/Views/PowerPlanEditorPage.xaml")]
    public void GeneralToolPagesUseTheSharedCompactToolTopbar(string path)
    {
        var page = Read(path);
        var header = Read("GameOptimizer.Shell/Controls/ToolTopbar.xaml");

        Assert.Contains("controls:ToolTopbar", page, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"42\"", header, StringComparison.Ordinal);
        Assert.Contains("CornerRadius=\"{DynamicResource Token.Radius.Card}\"", header, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GameOptimizer.Shell/Views/NetworkTestPage.xaml")]
    public void HeaderActionsHaveASeparateGridColumnInsteadOfOverlayingTheTitle(string path)
    {
        var page = Read(path);

        Assert.Contains("<controls:ToolTopbar x:Name=\"PageHeader\"", page, StringComparison.Ordinal);
        Assert.Contains("StatusText=", page, StringComparison.Ordinal);
        var header = Read("GameOptimizer.Shell/Controls/ToolTopbar.xaml");
        Assert.Contains("<ColumnDefinition Width=\"*\"/>", header, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"Auto\"/>", header, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding Actions, ElementName=Root}\"", header, StringComparison.Ordinal);
    }

    private static string Read(string relativePath, [CallerFilePath] string sourcePath = "")
    {
        static DirectoryInfo? FindRoot(string path)
        {
            var directory = new DirectoryInfo(path);
            while (directory is not null &&
                   !File.Exists(Path.Combine(directory.FullName, "GameOptimizer.Main.sln")))
                directory = directory.Parent;

            return directory;
        }

        var directory = FindRoot(Path.GetDirectoryName(sourcePath)!)
            ?? FindRoot(AppContext.BaseDirectory)
            ?? FindRoot(Environment.CurrentDirectory);

        return File.ReadAllText(Path.Combine(
            (directory ?? throw new DirectoryNotFoundException("Repository root was not found.")).FullName,
            relativePath));
    }
}
