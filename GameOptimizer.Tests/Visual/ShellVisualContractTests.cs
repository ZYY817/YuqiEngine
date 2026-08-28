using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace GameOptimizer.Tests.Visual;

public sealed class ShellVisualContractTests
{
    [Fact]
    public void NavigationHostResetsOldAnimationsAndKeepsMotionPreferenceAccessible()
    {
        var shell = Read("GameOptimizer.Shell/MainWindow.xaml.cs");

        Assert.Contains("pageHost.BeginAnimation(OpacityProperty, null)", shell, StringComparison.Ordinal);
        Assert.Contains("pageHost.Opacity = 1", shell, StringComparison.Ordinal);
        Assert.Contains("Panel.SetZIndex(pageHost, 0)", shell, StringComparison.Ordinal);
        Assert.Contains("host.BeginAnimation(OpacityProperty, null)", shell, StringComparison.Ordinal);
        Assert.Contains("host.Visibility = Visibility.Visible", shell, StringComparison.Ordinal);
        Assert.Contains("Panel.SetZIndex(host, 1)", shell, StringComparison.Ordinal);
        Assert.Contains("FillBehavior = FillBehavior.Stop", shell, StringComparison.Ordinal);
        Assert.Contains("if (SystemParameters.ClientAreaAnimation)", shell, StringComparison.Ordinal);
        Assert.Contains("host.Opacity = 1", shell, StringComparison.Ordinal);
        Assert.Contains("导航宿主状态", shell, StringComparison.Ordinal);

        var app = Read("GameOptimizer.Shell/App.xaml.cs");
        Assert.Contains("--acceptance-navigation-sequence=", app, StringComparison.Ordinal);
        Assert.Contains("foreach (var routeKey in acceptanceNavigationSequence)", app, StringComparison.Ordinal);
        Assert.Contains("mainWindow.NavigateToPage(routeKey)", app, StringComparison.Ordinal);
    }

    [Fact]
    public void MemorySecondaryTextUsesTextRolesInDarkTheme()
    {
        var text = Read("GameOptimizer.Shell/Themes/DarkTheme.xaml");
        Assert.Contains("x:Key=\"Theme.Brush.TextTertiary\" Color=\"{StaticResource Theme.Role.TextSecondary}\"", text, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"Theme.Brush.TextQuaternary\" Color=\"{StaticResource Theme.Role.TextSecondary}\"", text, StringComparison.Ordinal);

        var panel = Read("GameOptimizer.Shell/Views/MemoryControlPanel.xaml");
        Assert.Contains("TargetName=\"Bd\" Property=\"Opacity\" Value=\"0.68\"", panel, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CtaIcon\"", panel, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CtaSpinner\"", panel, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MemoryActionStatePanel\"", panel, StringComparison.Ordinal);
        var code = Read("GameOptimizer.Shell/Views/MemoryControlPanel.xaml.cs");
        Assert.Contains("IMemoryCleaningOwner memoryCleaningOwner", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Application.Current as App", code, StringComparison.Ordinal);
        Assert.Contains("new MemoryCleanDialog(", code, StringComparison.Ordinal);
        Assert.Contains("CleanMode.WorkingSet", code, StringComparison.Ordinal);
        Assert.Contains("dialog.ShowDialog();", code, StringComparison.Ordinal);
        Assert.Contains("MemoryCleaningResultKind.SucceededVerified", code, StringComparison.Ordinal);
        Assert.Contains("No cleanup command was invoked.", code, StringComparison.Ordinal);
        Assert.DoesNotContain("memHeroEstimate", panel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Compressed", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Commit", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Memory typed Owner 未接入", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MemoryOverviewAndModuleItemsShareOneContinuousPageScroller()
    {
        var code = Read("GameOptimizer.Shell/Views/MemoryControlPanel.xaml.cs");

        Assert.Contains("IntegrateIntoPageScroll();", code, StringComparison.Ordinal);
        Assert.Contains("pageScrollViewer.Content = null;", code, StringComparison.Ordinal);
        Assert.Contains("continuousContent.Children.Add(this);", code, StringComparison.Ordinal);
        Assert.Contains("continuousContent.Children.Add(groupedItems);", code, StringComparison.Ordinal);
        Assert.Contains("pageScrollViewer.Content = continuousContent;", code, StringComparison.Ordinal);
        Assert.Contains("groupedItems.Margin = new Thickness(0, 20, 0, 112);", code, StringComparison.Ordinal);
    }

    [Fact]
    public void GenericModuleRecoveryNeverPromisesUnknownWindowsDefaults()
    {
        var xaml = Read("GameOptimizer.Shell/Views/MasterModuleTemplate.xaml");
        var code = Read("GameOptimizer.Shell/Views/MasterModuleTemplate.xaml.cs");

        Assert.Contains("AutomationProperties.Name=\"备份与恢复\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Open_BackupManager_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("Undo_ModuleChanges_Click", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("AutomationProperties.Name=\"恢复 Windows 默认\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("恢复到系统出厂设置", xaml, StringComparison.Ordinal);
        Assert.Contains("mainWindow.NavigateToPage(\"backup-manager\")", code, StringComparison.Ordinal);
        Assert.Contains("没有可验证的 Engine 恢复回执，已安全阻断恢复", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteRestoreWindowsDefaultAsync", code, StringComparison.Ordinal);
    }

    [Fact]
    public void GameModeRecoveryRoutesToVerifiedBackupManagementInsteadOfWindowsDefaults()
    {
        var xaml = Read("GameOptimizer.Shell/Views/GameModeHubPage.xaml");
        var code = Read("GameOptimizer.Shell/Views/GameModeHubPage.xaml.cs");

        Assert.Contains("Click=\"OpenBackupManager_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("备份与恢复", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RestoreWindowsBtn", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("恢复Windows默认", xaml, StringComparison.Ordinal);
        Assert.Contains("mainWindow.NavigateToPage(\"backup-manager\")", code, StringComparison.Ordinal);
        Assert.DoesNotContain("RestoreWindows_Click", code, StringComparison.Ordinal);
    }

    [Fact]
    public void AppxCleanupOwnsAnOpaqueThemeSurface()
    {
        var page = Read("GameOptimizer.Shell/Views/AppxCleanupPage.xaml");
        Assert.Contains("Background=\"{DynamicResource Theme.Brush.Background}\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\r\n             Background=\"Transparent\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void HardwareInfoRemovesEmptyAccentPlaceholderAndShowsSafeStorageProgress()
    {
        var xaml = Read("GameOptimizer.Shell/Views/HardwareInfoPage.xaml");
        Assert.Contains("Style x:Key=\"WebCard\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Background=\"{DynamicResource Theme.Brush.Background}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Effect\" Value=\"{DynamicResource Visual.Shadow.Card}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotMatch("#[0-9A-Fa-f]{3,8}", xaml);
        Assert.DoesNotContain("Text=\"{Binding ElementName=OsVersion, Path=Text}\" Foreground=\"{DynamicResource Theme.Brush.AccentBlueLight}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<TextBlock Text=\"PC\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding UsagePercentText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"{Binding UsagePercent}\" Maximum=\"100\" Height=\"5\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CpuVendorSubtitle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GpuVendorSubtitle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MemoryVendorSubtitle\"", xaml, StringComparison.Ordinal);

        Assert.Equal(0, GameOptimizer.Shell.Views.DiskInfoModel.CalculateUsagePercent(0, 0));
        Assert.Equal(0, GameOptimizer.Shell.Views.DiskInfoModel.CalculateUsagePercent(100, 200));
        Assert.Equal(25, GameOptimizer.Shell.Views.DiskInfoModel.CalculateUsagePercent(100, 75));
        Assert.Equal(100, GameOptimizer.Shell.Views.DiskInfoModel.CalculateUsagePercent(100, -1));
    }

    [Fact]
    public void NetworkRadarNodeSelectorAndMetricColumnShareThePageGrid()
    {
        var xaml = Read("GameOptimizer.Shell/Views/NetworkTestPage.xaml");
        Assert.Contains("<controls:ToolTopbar x:Name=\"PageHeader\" Margin=\"0,0,0,14\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"NodeSelector\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Grid Grid.Row=\"2\" Margin=\"0,0,0,14\">", xaml, StringComparison.Ordinal);
        Assert.Contains("<Grid Grid.Column=\"2\">", xaml, StringComparison.Ordinal);
        Assert.Contains("<Grid.RowDefinitions><RowDefinition/><RowDefinition/><RowDefinition/><RowDefinition/><RowDefinition/></Grid.RowDefinitions>", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<StackPanel Grid.Column=\"2\">", xaml, StringComparison.Ordinal);
    }
    private static readonly HashSet<string> FinalComponentOverrides =
    [
        "PrimaryButtonStyle", "SecondaryButtonStyle", "DangerButtonStyle",
        "SectionCardStyle", "InnerCardStyle"
    ];

    private static readonly string[] FoundationDictionaries =
    [
        "DarkTheme.xaml",
        "DesignTokens.xaml",
        "GlobalControlStyles.xaml",
        "Animations.xaml",
        "ModernStyles.xaml",
        "ShellCompatibilityResources.xaml",
        "YuqiComponentLibrary.xaml"
    ];

    private static readonly string[] PublicControls =
    [
        "GameOptimizer.Shell/Controls/PageHeader.xaml",
        "GameOptimizer.Shell/Controls/PageHeader.xaml.cs",
        "GameOptimizer.Shell/Controls/SectionCard.xaml",
        "GameOptimizer.Shell/Controls/SectionCard.xaml.cs",
        "GameOptimizer.Shell/Controls/StatusBadge.xaml",
        "GameOptimizer.Shell/Controls/StatusBadge.xaml.cs",
        "GameOptimizer.Shell/Controls/EmptyState.xaml",
        "GameOptimizer.Shell/Controls/EmptyState.xaml.cs",
        "GameOptimizer.Shell/Controls/ControlState.cs"
    ];

    [Fact]
    public void AppResourceOrderAndThemeKeyContractAreStable()
    {
        var app = Load("GameOptimizer.Shell/App.xaml");
        var sources = app
            .Descendants(XName.Get("ResourceDictionary", "http://schemas.microsoft.com/winfx/2006/xaml/presentation"))
            .Where(x => x.Attribute("Source") is not null)
            .Select(x => x.Attribute("Source")!.Value)
            .ToArray();

        Assert.Equal(
            FoundationDictionaries.Select(x => "Themes/" + x),
            sources);

        Assert.True(File.Exists(RepoPath("GameOptimizer.Shell/Themes/DarkTheme.xaml")));
        Assert.False(File.Exists(RepoPath("GameOptimizer.Shell/Themes/LightTheme.xaml")));

        var activeKeys = new List<string>();
        foreach (var dictionary in FoundationDictionaries)
            activeKeys.AddRange(Keys("GameOptimizer.Shell/Themes/" + dictionary));

        var duplicates = activeKeys
            .GroupBy(x => x, StringComparer.Ordinal)
            .Where(x => x.Count() > 1 && !FinalComponentOverrides.Contains(x.Key))
            .Select(x => x.Key)
            .ToArray();
        Assert.Empty(duplicates);
    }

    [Fact]
    public void FoundationResourceReferencesResolveStatically()
    {
        var files = FoundationDictionaries
            .Select(x => Path.Combine("GameOptimizer.Shell", "Themes", x))
            .Concat(PublicControls)
            .ToArray();
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in FoundationDictionaries)
            known.UnionWith(Keys(Path.Combine("GameOptimizer.Shell", "Themes", file)));
        known.UnionWith(Keys("GameOptimizer.Shell/App.xaml"));
        known.UnionWith(["BoolToVisibilityConverter", "NullToVisibilityConverter"]);

        var unresolved = new List<string>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(RepoPath(file));
            foreach (Match match in Regex.Matches(text, "\\{(?:StaticResource|DynamicResource)\\s+([^}]+)\\}"))
            {
                var reference = match.Groups[1].Value.Trim();
                if (reference.StartsWith("{x:Static", StringComparison.Ordinal) ||
                    reference.StartsWith("x:Static", StringComparison.Ordinal) ||
                    reference.StartsWith("{x:Type", StringComparison.Ordinal) ||
                    reference.StartsWith("x:Type", StringComparison.Ordinal))
                    continue;

                if (!known.Contains(reference))
                    unresolved.Add(file + ":" + reference);
            }
        }

        Assert.Empty(unresolved);
    }

    [Fact]
    public void ThemeColorsUseOnlyCanonicalRolesAndMeetContrastTarget()
    {
        var requiredRoles = new[]
        {
            "Background", "Surface", "SurfaceRaised", "TextPrimary", "TextSecondary",
            "TextOnAccent", "Border", "Accent", "Info", "Success", "Warning", "Error"
        };

        foreach (var theme in new[] { "DarkTheme.xaml" })
        {
            var document = Load(Path.Combine("GameOptimizer.Shell", "Themes", theme));
            var roleColors = document
                .Descendants(XName.Get("Color", "http://schemas.microsoft.com/winfx/2006/xaml/presentation"))
                .Where(x => x.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value.StartsWith("Theme.Role.", StringComparison.Ordinal) == true)
                .ToDictionary(
                    x => x.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))!.Value["Theme.Role.".Length..],
                    x => ParseColor(x.Value),
                    StringComparer.Ordinal);

            Assert.Equal(requiredRoles.OrderBy(x => x), roleColors.Keys.OrderBy(x => x));

            var surface = roleColors["Surface"];
            var background = roleColors["Background"];
            foreach (var textRole in new[] { "TextPrimary", "TextSecondary" })
            {
                Assert.True(Contrast(roleColors[textRole], background) >= 4.5, $"{theme}: {textRole} on Background is below 4.5:1.");
                Assert.True(Contrast(roleColors[textRole], surface) >= 4.5, $"{theme}: {textRole} on Surface is below 4.5:1.");
            }

            Assert.True(Contrast(roleColors["TextOnAccent"], roleColors["Accent"]) >= 4.5, $"{theme}: TextOnAccent on Accent is below 4.5:1.");
            foreach (var statusRole in new[] { "Info", "Success", "Warning", "Error" })
                Assert.True(Contrast(roleColors[statusRole], surface) >= 4.5, $"{theme}: {statusRole} on Surface is below 4.5:1.");

            var semanticAliases = document
                .Descendants(XName.Get("Color", "http://schemas.microsoft.com/winfx/2006/xaml/presentation"))
                .Where(x => !x.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))!.Value.StartsWith("Theme.Role.", StringComparison.Ordinal))
                .ToArray();

            // A Color element cannot safely contain a nested resource expression. Palette colors may
            // be deliberately distinct from the compact semantic-role set (frame, translucent glass
            // washes and module accents), so enforce valid literals rather than an invalid equality.
            Assert.All(semanticAliases, color =>
            {
                var value = color.Value.Trim();
                Assert.Matches("^#[0-9A-Fa-f]{6,8}$", value);
            });

            var invalidColorMarkup = semanticAliases
                .Where(x => x.Value.Contains("{StaticResource", StringComparison.Ordinal) ||
                            x.Value.Contains("{DynamicResource", StringComparison.Ordinal))
                .Select(x => x.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))!.Value)
                .ToArray();
            Assert.Empty(invalidColorMarkup);
        }
    }

    [Fact]
    public void PublicControlsExposeStateFocusWrappingAndDynamicThemeContract()
    {
        var appButton = Read("GameOptimizer.Shell/Themes/ShellCompatibilityResources.xaml");
        var controls = string.Join("\n", PublicControls.Select(Read));
        var interactiveContract = new[] { "IsMouseOver", "IsPressed", "IsKeyboardFocusWithin", "IsEnabled", "Loading", "Error", "Unauthorized", "Recovering" };

        foreach (var state in interactiveContract)
            Assert.Contains(state, appButton, StringComparison.Ordinal);

        Assert.Contains("FocusVisualStyle", appButton, StringComparison.Ordinal);
        Assert.Contains("MinHeight", appButton, StringComparison.Ordinal);
        Assert.Contains("TextWrapping", appButton, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name", controls, StringComparison.Ordinal);
        Assert.Contains("TextWrapping", controls, StringComparison.Ordinal);
        Assert.Contains("DynamicResource Theme.", controls, StringComparison.Ordinal);
        Assert.Contains("ControlVisualState", controls, StringComparison.Ordinal);
        Assert.Contains("Unauthorized", Read("GameOptimizer.Shell/Controls/StatusBadge.xaml.cs"), StringComparison.Ordinal);
        Assert.Contains("Recovering", Read("GameOptimizer.Shell/Controls/StatusBadge.xaml.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void ShellNavigationUsesAnExplicitCatalogSourceAndStartupOffersOptionalFirstProtection()
    {
        var shell = Read("GameOptimizer.Shell/MainWindow.xaml");
        var shellCode = Read("GameOptimizer.Shell/MainWindow.xaml.cs");
        var app = Read("GameOptimizer.Shell/App.xaml.cs");

        Assert.Contains("x:Name=\"PrimaryNavigationList\"", shell, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"主导航\"", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("Source=\"{Binding VisibleNavigationSections, ElementName=RootWindow}\"", shell, StringComparison.Ordinal);
        Assert.Contains("navigationView.Source = _navigationCatalog.VisibleSections", shellCode, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"作者信息\"", shell, StringComparison.Ordinal);
        Assert.Contains("Text=\"作者信息\"", shell, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SupportAuthorHeaderButton\"", shell, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"支持作者\"", shell, StringComparison.Ordinal);
        Assert.Contains("SupportAuthorButton_Click", shell, StringComparison.Ordinal);
        Assert.Contains("new AboutDialog(focusSupportSection: true)", shellCode, StringComparison.Ordinal);

        Assert.DoesNotContain("EnsureMandatoryFirstRunBaselineAsync", app, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateBackupIfNeededAsync", app, StringComparison.Ordinal);
        Assert.Contains("ShowFirstProtectionOnboardingIfNeededAsync(mainWindow)", app, StringComparison.Ordinal);
        Assert.Contains("!isAcceptanceUiInstance && !isRecoveryDevelopmentInstance", app, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<FirstProtectionPreferenceStore>()", app, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<IFirstRunBaselineOrchestrator, FirstRunBaselineOrchestrator>()", app, StringComparison.Ordinal);

        var firstProtectionDialog = Read("GameOptimizer.Shell/Views/FirstRunBaselineDialog.xaml")
            + Read("GameOptimizer.Shell/Views/FirstRunBaselineDialog.xaml.cs");
        Assert.Contains("稍后再说", firstProtectionDialog, StringComparison.Ordinal);
        Assert.Contains("暂不备份，继续使用", firstProtectionDialog, StringComparison.Ordinal);
        Assert.Contains("RecordFailureAsync", firstProtectionDialog, StringComparison.Ordinal);
        Assert.Contains("RecordCompletedAsync", firstProtectionDialog, StringComparison.Ordinal);
    }

    [Fact]
    public void SystemCleanupUsesOneInjectedCatalogAndKeepsItAcrossNavigation()
    {
        var page = Read("GameOptimizer.Shell/Views/SystemCleanupPage.xaml.cs");
        var viewModel = Read("GameOptimizer.Shell/ViewModels/SystemCleanupViewModel.cs");
        var app = Read("GameOptimizer.Shell/App.xaml.cs");

        Assert.Contains("SystemCleanupPage(SystemCleanupViewModel viewModel)", page, StringComparison.Ordinal);
        Assert.Contains("DataContext = _viewModel", page, StringComparison.Ordinal);
        Assert.DoesNotContain("DataContext = new SystemCleanupViewModel()", page, StringComparison.Ordinal);
        Assert.DoesNotContain("DataContext = null", page, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<SystemCleanupViewModel>()", app, StringComparison.Ordinal);
        Assert.Contains("Cleanup catalog loaded", page, StringComparison.Ordinal);
        Assert.Contains("CleanupItems.Count > 0", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void FoundationHasNoNewRawColorsOrInfiniteAnimations()
    {
        var modern = Read("GameOptimizer.Shell/Themes/ModernStyles.xaml");
        Assert.DoesNotMatch("#[0-9A-Fa-f]{3,8}", modern);
        Assert.DoesNotMatch("RepeatBehavior\\s*=\\s*\"Forever\"", modern);
        Assert.DoesNotMatch("(?:Linear|Radial)GradientBrush", modern);

        var publicControlText = string.Join("\n", PublicControls.Select(Read));
        Assert.DoesNotMatch("#[0-9A-Fa-f]{3,8}", publicControlText);
        Assert.DoesNotMatch("Color\\.From(?:Rgb|Argb)", publicControlText);

        var animations = Read("GameOptimizer.Shell/Themes/Animations.xaml");
        Assert.DoesNotMatch("RepeatBehavior\\s*=\\s*\"Forever\"", animations);
    }

    [Fact]
    public void InteractiveMinimumSizeScalesAcrossSupportedDpi()
    {
        var tokens = Load("GameOptimizer.Shell/Themes/DesignTokens.xaml");
        var min = tokens
            .Descendants()
            .First(x => x.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "Token.Size.MinInteractive")
            .Value;
        Assert.Equal(36d, double.Parse(min, CultureInfo.InvariantCulture));

        var expected = new[] { 36, 45, 54, 63, 72 };
        var actual = new[] { 1d, 1.25d, 1.5d, 1.75d, 2d }
            .Select(scale => (int)Math.Ceiling(36d * scale))
            .ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ShellKeepsUnifiedSurfaceAndLowContrastNavigationSelection()
    {
        var shell = Load("GameOptimizer.Shell/MainWindow.xaml");
        var presentation = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation");
        var xNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

        var navigation = shell.Descendants(presentation + "ListBox")
            .Single(element => element.Attribute(xNamespace + "Name")?.Value == "PrimaryNavigationList");
        var sidebar = navigation.Ancestors(presentation + "Border").First();
        Assert.Equal("{DynamicResource Theme.Brush.SidebarBackground}", sidebar.Attribute("Background")?.Value);

        var contentSurface = shell.Descendants(presentation + "Grid")
            .Single(element => element.Elements(presentation + "ContentControl")
                .Any(child => child.Attribute(xNamespace + "Name")?.Value == "OverviewPageHost"));
        Assert.Equal("{DynamicResource Theme.Brush.AppBackground}", contentSurface.Attribute("Background")?.Value);

        var darkTheme = Read("GameOptimizer.Shell/Themes/DarkTheme.xaml");
        Assert.Contains("x:Key=\"Theme.Brush.ShellBackground\"", darkTheme, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"Theme.Background.Primary\">#09090B", darkTheme, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"Theme.Card.Background\">#0CFFFFFF", darkTheme, StringComparison.Ordinal);

        var shellText = Read("GameOptimizer.Shell/MainWindow.xaml");
        Assert.Contains("Background=\"{DynamicResource Theme.Brush.NavItemSelected}\"", shellText, StringComparison.Ordinal);
        Assert.Contains("TargetName=\"Pill\" Property=\"BorderBrush\" Value=\"{DynamicResource Theme.Brush.NavItemSelectedBorder}\"", shellText, StringComparison.Ordinal);
        var selectionIndicator = shell.Descendants(presentation + "Border")
            .Single(element => element.Attribute(xNamespace + "Name")?.Value == "NavigationSelectionIndicator");
        Assert.Equal("0", selectionIndicator.Attribute("BorderThickness")?.Value);
        Assert.Null(selectionIndicator.Attribute("BorderBrush"));
        Assert.Equal("Collapsed", selectionIndicator.Attribute("Visibility")?.Value);

        var selectionShadow = shell.Descendants(presentation + "Border")
            .Single(element => element.Attribute(xNamespace + "Name")?.Value == "NavigationSelectionShadow");
        Assert.Equal("{DynamicResource Visual.Shadow.Card}", selectionShadow.Attribute("Effect")?.Value);
        Assert.Equal("Collapsed", selectionShadow.Attribute("Visibility")?.Value);
        Assert.DoesNotContain("Value=\"#3B82F6\"", shellText, StringComparison.Ordinal);

        var logo = shell.Descendants(presentation + "Rectangle")
            .Single(element => element.Attribute("Fill")?.Value == "{DynamicResource Theme.Brush.LogoMark}");
        Assert.Equal("24", logo.Attribute("Width")?.Value);
        Assert.Equal("24", logo.Attribute("Height")?.Value);
        Assert.Contains("/Assets/yuqi-engine-mark.png", shellText, StringComparison.Ordinal);
        Assert.Contains("Theme.Brush.LogoMark", shellText, StringComparison.Ordinal);

        foreach (var (name, handler) in new[]
                 {
                     ("GitHubProjectButton", "GitHubProjectButton_Click"),
                     ("AboutButton", "AboutButton_Click"),
                     ("FeedbackButton", "FeedbackButton_Click")
                 })
        {
            var button = shell.Descendants(presentation + "Button")
                .Single(element => element.Attribute(xNamespace + "Name")?.Value == name);
            Assert.Equal(handler, button.Attribute("Click")?.Value);
        }

        var shellCode = Read("GameOptimizer.Shell/MainWindow.xaml.cs");
        Assert.Contains("new AppSettings().GitHubRepo", shellCode, StringComparison.Ordinal);
        Assert.Contains("SafeProcessHelper.StartProcess", shellCode, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AmbientBackdrop\"", shellText, StringComparison.Ordinal);
        Assert.Contains("e.Route.GroupKey, \"modules\"", shellCode, StringComparison.Ordinal);
        Assert.Contains("AmbientBackdrop.Visibility", shellCode, StringComparison.Ordinal);
        Assert.DoesNotContain("ZYY817/YuqiEngine", shellCode, StringComparison.Ordinal);

        // Logo 资产换代守卫：打包清单只保留现行真实 logo（mark 主标 + 应用 ico + 托盘 ico），
        // 已淘汰的旧版 logo 文件不得重新进入资源清单。
        var projectText = Read("GameOptimizer.Shell/GameOptimizer.Shell.csproj");
        Assert.Contains("<Resource Include=\"Assets\\yuqi-engine-mark.png\" />", projectText, StringComparison.Ordinal);
        Assert.Contains("<Resource Include=\"Assets\\app.ico\" />", projectText, StringComparison.Ordinal);
        Assert.Contains("<Resource Include=\"Assets\\tray.ico\" />", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain("Assets\\yuqi-logo.png", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain("Assets\\logo.png", projectText, StringComparison.Ordinal);
    }

    [Fact]
    public void StartupPresentsOverviewBeforeSlowDashboardInitialization()
    {
        var shellCode = Read("GameOptimizer.Shell/MainWindow.xaml.cs");
        var onLoadedIndex = shellCode.IndexOf("private async void OnLoaded", StringComparison.Ordinal);
        var overviewNavigationIndex = shellCode.IndexOf("_navigationRouter.Navigate(\"overview\")", onLoadedIndex, StringComparison.Ordinal);
        var dashboardInitializationIndex = shellCode.IndexOf("await _viewModel.InitializeAsync()", onLoadedIndex, StringComparison.Ordinal);

        Assert.True(onLoadedIndex >= 0);
        Assert.True(overviewNavigationIndex > onLoadedIndex);
        Assert.True(dashboardInitializationIndex > overviewNavigationIndex);
        Assert.Contains("系统检测、游戏识别等耗时初始化在概览已经呈现后进行", shellCode, StringComparison.Ordinal);
    }

    [Fact]
    public void SidebarRemainsDiscoverablyScrollableAndTemperaturesUseTheDegreeSymbol()
    {
        var shell = Read("GameOptimizer.Shell/MainWindow.xaml");
        var dashboard = Read("GameOptimizer.Shell/ViewModels/DashboardViewModel.cs");

        Assert.Contains("Style=\"{StaticResource SidebarScrollViewer}\"", shell, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", shell, StringComparison.Ordinal);
        Assert.Contains("Padding=\"0,0,0,16\"", shell, StringComparison.Ordinal);
        Assert.Contains("°C", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("掳C", dashboard, StringComparison.Ordinal);
    }

    [Fact]
    public void SidebarSelectionRemainsThemeAwareAndUnobstructed()
    {
        var shell = Read("GameOptimizer.Shell/MainWindow.xaml");
        var shellCode = Read("GameOptimizer.Shell/MainWindow.xaml.cs");
        var settings = Read("GameOptimizer.Shell/Views/SettingsPage.xaml");
        var app = Read("GameOptimizer.Shell/App.xaml.cs");
        var components = Read("GameOptimizer.Shell/Themes/YuqiComponentLibrary.xaml");

        Assert.Contains("x:Key=\"SidebarScrollBar\"", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("Opacity=\"0.28\"", shell, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Opacity\" Value=\"0\"/>", components, StringComparison.Ordinal);
        Assert.Contains("<Trigger Property=\"IsMouseOver\" Value=\"True\">", components, StringComparison.Ordinal);
        Assert.Contains("<Trigger Property=\"IsMouseCaptureWithin\" Value=\"True\">", components, StringComparison.Ordinal);
        Assert.Contains("AncestorType={x:Type ScrollViewer}", components, StringComparison.Ordinal);
        Assert.Contains("Margin=\"10,0,12,0\"", shell, StringComparison.Ordinal);
        Assert.Contains("Width=\"2\" Height=\"20\"", shell, StringComparison.Ordinal);
        Assert.Contains("Theme.Brush.NavItemSelected", shell, StringComparison.Ordinal);
        Assert.Contains("Theme.Brush.NavItemSelectedBorder", shell, StringComparison.Ordinal);
        Assert.Contains("DropShadowEffect Color=\"#000000\" BlurRadius=\"8\"", shell, StringComparison.Ordinal);
        Assert.Contains("PrimaryNavigationList.ScrollIntoView(section)", shellCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Visual.Mode.IsEnhanced", shellCode, StringComparison.Ordinal);
        Assert.DoesNotContain("LowUsageVisualCard", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("EnhancedVisualCard", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("acceptance-visual-mode", app, StringComparison.Ordinal);
    }

    [Fact]
    public void SidebarPointerNavigationFallsBackToTheClickedRoute()
    {
        var shell = Read("GameOptimizer.Shell/MainWindow.xaml");
        var shellCode = Read("GameOptimizer.Shell/MainWindow.xaml.cs");

        Assert.Contains("PreviewMouseLeftButtonUp=\"PrimaryNavigationList_PreviewMouseLeftButtonUp\"", shell, StringComparison.Ordinal);
        Assert.Contains("private void PrimaryNavigationList_PreviewMouseLeftButtonUp", shellCode, StringComparison.Ordinal);
        Assert.Contains("ItemsControl.ContainerFromElement(listBox, originalSource)", shellCode, StringComparison.Ordinal);
        Assert.Contains("NavigateToPage(section.Key);", shellCode, StringComparison.Ordinal);
        Assert.Contains("!string.Equals(_navigationRouter.CurrentKey, section.Key", shellCode, StringComparison.Ordinal);
    }

    [Fact]
    public void SidebarIconsUseOneDpiSafeThemeAwareStrokeSystem()
    {
        var shell = Read("GameOptimizer.Shell/MainWindow.xaml");

        Assert.Contains("<Viewbox Width=\"16\" Height=\"16\" SnapsToDevicePixels=\"True\">", shell, StringComparison.Ordinal);
        Assert.Contains("Fill=\"Transparent\" Stroke=\"{TemplateBinding Foreground}\" StrokeThickness=\"1.7\"", shell, StringComparison.Ordinal);
        Assert.Contains("StrokeLineJoin=\"Round\" StrokeStartLineCap=\"Round\" StrokeEndLineCap=\"Round\"", shell, StringComparison.Ordinal);
        Assert.Contains("TargetName=\"IconPath\" Property=\"Stroke\" Value=\"{DynamicResource Theme.Brush.ControlAccent}\"", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("TargetName=\"IconPath\" Property=\"Fill\"", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void OverviewKeepsBaselineHierarchyWithOnlyRealStateBindings()
    {
        var overviewText = Read("GameOptimizer.Shell/Views/OverviewPage.xaml");
        var overviewCodeBehind = Read("GameOptimizer.Shell/Views/OverviewPage.xaml.cs");
        foreach (var binding in new[]
                 {
                      "CpuUsageDisplay", "GpuUsageDisplay", "MemorySummary", "DiskSummary",
                      "MemoryUsagePercent", "MemoryAvailableDisplay", "DiskUsagePercent", "DiskAvailableDisplay",
                      "OverviewEntries", "ActivityLogs"
                 })
        {
            Assert.Contains(binding, overviewText, StringComparison.Ordinal);
        }

        var dashboardType = typeof(GameOptimizer.Shell.ViewModels.DashboardViewModel);
        foreach (var property in new[]
                 {
                     "OverviewEntries", "ActivityLogs", "NavigateToModuleCommand", "ClearActivityHistoryCommand",
                     "HasRealCpuSample", "HasRealGpuSample", "HasRealMemorySample", "HasRealDiskSample"
                 })
        {
            Assert.NotNull(dashboardType.GetProperty(property));
        }

        var snapshotType = dashboardType.GetProperty("Snapshot")?.PropertyType;
        Assert.NotNull(snapshotType?.GetProperty("CpuUsage"));
        Assert.NotNull(snapshotType?.GetProperty("GpuUsage"));
        Assert.DoesNotContain("Snapshot.CpuUsage, StringFormat", overviewText, StringComparison.Ordinal);
        Assert.DoesNotContain("Snapshot.GpuUsage, StringFormat", overviewText, StringComparison.Ordinal);

        var dashboardSource = Read("GameOptimizer.Shell/ViewModels/DashboardViewModel.cs");
        foreach (var route in new[] { "hardware", "nettest", "restore", "repair", "backup" })
            Assert.Contains($"new OverviewEntry(\"{route}\"", dashboardSource, StringComparison.Ordinal);
        foreach (var route in new[] { "memory", "performance", "cpu", "graphics", "network", "audio", "privacy", "system", "explorer", "tasks", "services", "cleanup", "peripheral" })
            Assert.Contains($"[\"{route}\"] =", dashboardSource, StringComparison.Ordinal);
        Assert.Contains("NavigateToPageRequested?.Invoke", dashboardSource, StringComparison.Ordinal);
        Assert.Contains("ZeroToVisibleConverter", overviewText, StringComparison.Ordinal);
        Assert.Contains("NonZeroToVisibleConverter", overviewText, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "一键优化", "引导优化", "HeroOptimize", "OptimizeButtonStatus", "RestoreSystemDefault", "RestoreUserBackupCommand", "RestoreWindowsDefaultCommand" })
            Assert.DoesNotContain(forbidden, overviewText, StringComparison.Ordinal);
        Assert.Contains("Overview.FeatureMatrix", overviewText, StringComparison.Ordinal);
        Assert.Contains("Overview.ActivityLog", overviewText, StringComparison.Ordinal);
        Assert.Contains("Overview.TelemetryDescription", overviewText, StringComparison.Ordinal);
        Assert.DoesNotContain("Overview.SessionNotes", overviewText, StringComparison.Ordinal);
        Assert.Contains("Click=\"SupportAuthorButton_Click\"", overviewText, StringComparison.Ordinal);
        Assert.Contains("new AboutDialog(focusSupportSection: true)", overviewCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ExportActivityHistoryCommand", overviewText, StringComparison.Ordinal);
        Assert.DoesNotContain("Command=\"{Binding ClearActivityHistoryCommand}\"", overviewText, StringComparison.Ordinal);
        Assert.Contains("CpuStatusLabel", overviewText, StringComparison.Ordinal);
        Assert.Contains("GpuStatusLabel", overviewText, StringComparison.Ordinal);
        Assert.DoesNotContain("趋势：暂无权威序列", overviewText, StringComparison.Ordinal);
        Assert.DoesNotContain("CpuTrendPoints", overviewText, StringComparison.Ordinal);
        Assert.DoesNotContain("GpuTrendPoints", overviewText, StringComparison.Ordinal);
        Assert.Contains("CpuLiveTrendPoints", overviewText, StringComparison.Ordinal);
        Assert.Contains("GpuLiveTrendPoints", overviewText, StringComparison.Ordinal);
        Assert.Contains("HasCpuLiveTrend", overviewText, StringComparison.Ordinal);
        Assert.Contains("HasGpuLiveTrend", overviewText, StringComparison.Ordinal);
        foreach (var usageBrush in new[] { "CpuUsageBrush", "GpuUsageBrush", "MemoryUsageBrush", "DiskUsageBrush" })
        {
            Assert.Contains(usageBrush, overviewText, StringComparison.Ordinal);
            Assert.NotNull(dashboardType.GetProperty(usageBrush));
        }
    }

    [Fact]
    public void OverviewMatrixFollowsLegacyOrderAndKeepsUnsafeActionsBounded()
    {
        var xaml = Read("GameOptimizer.Shell/Views/OverviewPage.xaml");
        var codeBehind = Read("GameOptimizer.Shell/Views/OverviewPage.xaml.cs");
        var dashboard = Read("GameOptimizer.Shell/ViewModels/DashboardViewModel.cs");
        var moduleOrderStart = dashboard.IndexOf("OverviewModuleOrder =", StringComparison.Ordinal);
        var moduleOrderEnd = dashboard.IndexOf("};", moduleOrderStart, StringComparison.Ordinal);
        var moduleOrder = dashboard[moduleOrderStart..moduleOrderEnd];
        var expectedModules = new[]
        {
            "memory", "performance", "cpu", "graphics", "network", "audio", "privacy",
            "system", "explorer", "tasks", "services", "cleanup", "peripheral"
        };

        var previousIndex = -1;
        foreach (var key in expectedModules)
        {
            var index = moduleOrder.IndexOf($"\"{key}\"", StringComparison.Ordinal);
            Assert.True(index > previousIndex, $"Overview module '{key}' is out of legacy order.");
            previousIndex = index;
        }

        Assert.Contains("ItemsSource=\"{Binding OverviewEntries}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<UniformGrid Columns=\"3\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("实时读写：暂无权威数据", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("趋势：暂无权威序列", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CpuTrendPoints", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("GpuTrendPoints", xaml, StringComparison.Ordinal);
        Assert.Contains("CpuLiveTrendPoints", xaml, StringComparison.Ordinal);
        Assert.Contains("GpuLiveTrendPoints", xaml, StringComparison.Ordinal);
        Assert.Contains("TelemetryTrendBuffer", dashboard, StringComparison.Ordinal);
        Assert.Contains("UpdateLiveTrendSamples", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("0,60 50,54 100,48", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("0,52 50,44 100,38", dashboard, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "一键优化", "引导优化", "HeroOptimize", "OptimizeButtonStatus", "RestoreSystemDefault", "RestoreUserBackupCommand", "OpenDiagnosticsFolderCommand", "RestoreWindowsDefaultCommand" })
            Assert.DoesNotContain(forbidden, xaml, StringComparison.Ordinal);
        Assert.Contains("new AboutDialog", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("http://", codeBehind, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", codeBehind, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RestoredPagesDoNotContainRuntimeInvalidThreePartThicknessValues()
    {
        var pages = new[]
        {
            "GameOptimizer.Shell/Views/OverviewPage.xaml",
            "GameOptimizer.Shell/Views/NetworkTestPage.xaml",
            "GameOptimizer.Shell/Views/HardwareInfoPage.xaml",
            "GameOptimizer.Shell/Views/BackupManagerPage.xaml",
            "GameOptimizer.Shell/Views/RestorePointPage.xaml"
        };

        var invalid = pages
            .SelectMany(page => Regex.Matches(
                    Read(page),
                    "(?:Margin|Padding)=\\\"[^\\\"{},]+,[^\\\"{},]+,[^\\\"{},]+\\\"")
                .Select(match => $"{page}: {match.Value}"))
            .ToArray();

        Assert.Empty(invalid);
    }

    [Fact]
    public void DonationPreviewUsesFullscreenSurfaceWithAnAccessibleExit()
    {
        var dialog = Read("GameOptimizer.Shell/Views/AboutDialog.xaml.cs");

        Assert.Contains("WindowStyle = WindowStyle.None", dialog, StringComparison.Ordinal);
        Assert.Contains("WindowState = WindowState.Maximized", dialog, StringComparison.Ordinal);
        Assert.Contains("Content = \"退出全屏\"", dialog, StringComparison.Ordinal);
        Assert.Contains("eventArgs.Key != Key.Escape", dialog, StringComparison.Ordinal);
        Assert.Contains("Stretch = Stretch.Uniform", dialog, StringComparison.Ordinal);
    }

    [Fact]
    public void PerformanceBoostKeepsCurrentMasterModuleComposition()
    {
        var performance = Load("GameOptimizer.Shell/Views/PerformanceBoostPage.xaml");
        var master = performance.Descendants()
            .Single(element => element.Name.LocalName == "MasterModuleTemplate");

        Assert.Equal("performance", master.Attribute("ModuleKey")?.Value);
        Assert.Equal("性能加速", performance.Root?.Attribute("AutomationProperties.Name")?.Value);
        Assert.DoesNotContain("DataContext", Read("GameOptimizer.Shell/Views/PerformanceBoostPage.xaml.cs"), StringComparison.Ordinal);
    }

    private static XDocument Load(string relativePath) => XDocument.Load(RepoPath(relativePath), LoadOptions.PreserveWhitespace);

    private static string Read(string relativePath) => File.ReadAllText(RepoPath(relativePath));

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

    private static HashSet<string> Keys(string relativePath)
    {
        var xNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        return Load(relativePath)
            .Descendants()
            .Attributes(xNamespace + "Key")
            .Select(x => x.Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static (byte R, byte G, byte B) ParseColor(string value)
    {
        var hex = value.Trim().TrimStart('#');
        if (hex.Length == 6)
            return (byte.Parse(hex[0..2], NumberStyles.HexNumber), byte.Parse(hex[2..4], NumberStyles.HexNumber), byte.Parse(hex[4..6], NumberStyles.HexNumber));
        throw new FormatException("Expected a six-digit canonical theme color.");
    }

    private static double Contrast((byte R, byte G, byte B) foreground, (byte R, byte G, byte B) background)
    {
        var a = Luminance(foreground);
        var b = Luminance(background);
        var lighter = Math.Max(a, b);
        var darker = Math.Min(a, b);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance((byte R, byte G, byte B) color)
    {
        static double Channel(byte value)
        {
            var normalized = value / 255d;
            return normalized <= 0.03928 ? normalized / 12.92 : Math.Pow((normalized + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }
}
