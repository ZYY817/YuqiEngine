using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Navigation;

namespace GameOptimizer.Shell.Diagnostics;

/// <summary>
/// 开发期界面定位器：从实际命中的视觉元素回溯到 XAML 文件和源码片段。
/// 仅由 Ctrl+Alt+I 临时开启，开启期间会拦截左键，避免诊断点击触发真实优化操作。
/// </summary>
internal sealed class UiSourceInspector : IDisposable
{
    private const int ContextLineCount = 8;
    private readonly Window _owner;
    private readonly Popup _popup;
    private readonly TextBlock _summary;
    private readonly TextBox _source;
    private Cursor? _previousCursor;
    private bool _enabled;

    public UiSourceInspector(Window owner)
    {
        _owner = owner;
        (_popup, _summary, _source) = CreatePopup();
        InputManager.Current.PreProcessInput += InputManager_PreProcessInput;
        _owner.PreviewMouseLeftButtonDown += Owner_PreviewMouseLeftButtonDown;
        _owner.Closed += Owner_Closed;
    }

    private void InputManager_PreProcessInput(object? sender, PreProcessInputEventArgs e)
    {
        if (e.StagingItem.Input is not KeyEventArgs keyEvent
            || keyEvent.RoutedEvent != Keyboard.PreviewKeyDownEvent)
        {
            return;
        }

        if (keyEvent.Key == Key.Escape && _enabled)
        {
            SetEnabled(false);
            keyEvent.Handled = true;
            return;
        }

        // 按住 Alt 时 WPF 会把实体按键包装为 SystemKey；两种报告方式都要识别。
        var pressedKey = keyEvent.Key == Key.System ? keyEvent.SystemKey : keyEvent.Key;
        if (pressedKey == Key.I
            && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
            && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            SetEnabled(!_enabled);
            keyEvent.Handled = true;
        }
    }

    private void Owner_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_enabled || e.OriginalSource is not DependencyObject hit)
        {
            return;
        }

        // 在定位模式中不得让点击继续传递到按钮、导航或任何会改变系统状态的命令。
        e.Handled = true;
        Present(Resolve(hit));
    }

    private void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (enabled)
        {
            _previousCursor = _owner.Cursor;
            _owner.Cursor = Cursors.Cross;
            _summary.Text = "界面定位器已开启。点击任意位置查看实际 XAML 来源；再次按 Ctrl+Alt+I 或 Esc 退出。";
            _source.Text = "尚未选择控件。定位模式中的点击不会执行任何页面操作。";
            _popup.IsOpen = true;
            return;
        }

        _owner.Cursor = _previousCursor;
        _previousCursor = null;
        _popup.IsOpen = false;
    }

    private void Present(UiSourceLocation location)
    {
        _summary.Text = $"{location.ElementType}  {location.ElementName}\n"
                        + $"文件：{location.DisplayPath}\n"
                        + $"行：{location.LineDescription}\n"
                        + $"DataContext：{location.DataContextType}";
        _source.Text = location.SourceSnippet;
        _popup.IsOpen = true;
    }

    private UiSourceLocation Resolve(DependencyObject hit)
    {
        var elements = GetAncestorElements(hit).ToArray();
        var selected = elements.FirstOrDefault(element => !string.IsNullOrWhiteSpace(element.Name))
                       ?? elements.FirstOrDefault(element => TryResolveRelativePath(element, out _))
                       ?? hit as FrameworkElement;

        if (selected is null)
        {
            return UiSourceLocation.Unavailable("未能从当前命中对象解析出 WPF 元素。");
        }

        var relativePath = TryResolveRelativePath(selected, out var relative) ? relative : null;
        var sourcePath = relativePath is null ? null : TryResolveSourcePath(relativePath);
        // WPF 发布后的 BAML 不公开元素行号；只使用可从原始 XAML 复核的定位方式。
        var line = 0;
        var sourceLines = ReadSourceLines(sourcePath);

        if (line <= 0 && sourceLines is not null)
        {
            line = FindNamedElementLine(sourceLines, selected.Name, selected.GetType().Name);
        }

        var snippet = BuildSnippet(sourceLines, line, selected.Name, selected.GetType().Name);
        var displayPath = relativePath ?? "未能从已编译视图解析 XAML 路径";
        var elementName = string.IsNullOrWhiteSpace(selected.Name) ? "（未命名元素）" : $"x:Name=\"{selected.Name}\"";
        var lineDescription = line > 0
            ? line.ToString()
            : "运行时未保留行号（已按名称/类型查找源码）";

        return new UiSourceLocation(
            selected.GetType().FullName ?? selected.GetType().Name,
            elementName,
            displayPath,
            lineDescription,
            selected.DataContext?.GetType().FullName ?? "（无 DataContext）",
            snippet);
    }

    private static IEnumerable<FrameworkElement> GetAncestorElements(DependencyObject start)
    {
        for (DependencyObject? current = start; current is not null; current = GetParent(current))
        {
            if (current is FrameworkElement element)
            {
                yield return element;
            }
        }
    }

    private static DependencyObject? GetParent(DependencyObject element) =>
        element switch
        {
            Visual or Visual3D => VisualTreeHelper.GetParent(element),
            FrameworkContentElement contentElement => contentElement.Parent,
            _ => LogicalTreeHelper.GetParent(element)
        };

    private static bool TryResolveRelativePath(FrameworkElement element, out string relativePath)
    {
        var uri = BaseUriHelper.GetBaseUri(element);
        var value = uri?.OriginalString;
        const string componentMarker = ";component/";
        var markerIndex = value?.IndexOf(componentMarker, StringComparison.OrdinalIgnoreCase) ?? -1;
        if (markerIndex >= 0)
        {
            relativePath = value![(markerIndex + componentMarker.Length)..].TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            return relativePath.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase);
        }

        relativePath = string.Empty;
        return false;
    }

    private static string? TryResolveSourcePath(string relativePath)
    {
        var explicitRoot = Environment.GetEnvironmentVariable("YUQI_SOURCE_ROOT");
        var roots = new[] { explicitRoot, Directory.GetCurrentDirectory() }
            .Concat(GetAncestorDirectories(AppContext.BaseDirectory));

        foreach (var candidate in roots.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var root = FindProjectRoot(candidate!);
            if (root is null)
            {
                continue;
            }

            var sourcePath = Path.Combine(root, "GameOptimizer.Shell", relativePath);
            if (File.Exists(sourcePath))
            {
                return sourcePath;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetAncestorDirectories(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
        {
            yield return directory.FullName;
        }
    }

    private static string? FindProjectRoot(string path)
    {
        foreach (var candidate in GetAncestorDirectories(path))
        {
            if (File.Exists(Path.Combine(candidate, "GameOptimizer.Shell", "GameOptimizer.Shell.csproj")))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string[]? ReadSourceLines(string? sourcePath)
    {
        if (sourcePath is null)
        {
            return null;
        }

        try
        {
            return File.ReadAllLines(sourcePath);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static int FindNamedElementLine(IReadOnlyList<string> lines, string name, string typeName)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            var nameToken = $"x:Name=\"{name}\"";
            for (var index = 0; index < lines.Count; index++)
            {
                if (lines[index].Contains(nameToken, StringComparison.Ordinal))
                {
                    return index + 1;
                }
            }
        }

        var typeToken = $"<{typeName}";
        for (var index = 0; index < lines.Count; index++)
        {
            if (lines[index].Contains(typeToken, StringComparison.Ordinal))
            {
                return index + 1;
            }
        }

        return 0;
    }

    private static string BuildSnippet(IReadOnlyList<string>? lines, int line, string name, string typeName)
    {
        if (lines is null)
        {
            return "源码文件不可用。开发环境中可设置 YUQI_SOURCE_ROOT 指向仓库根目录后重试。";
        }

        if (line <= 0)
        {
            return $"未能在源文件中唯一定位 {typeName} {name}。";
        }

        var first = Math.Max(0, line - ContextLineCount - 1);
        var last = Math.Min(lines.Count - 1, line + ContextLineCount - 1);
        var builder = new StringBuilder();
        for (var index = first; index <= last; index++)
        {
            var marker = index == line - 1 ? ">" : " ";
            builder.Append(marker).Append((index + 1).ToString().PadLeft(5)).Append("  ").AppendLine(lines[index]);
        }

        return builder.ToString();
    }

    private (Popup Popup, TextBlock Summary, TextBox Source) CreatePopup()
    {
        var summary = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(14, 12, 14, 8)
        };
        var source = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Margin = new Thickness(14, 0, 14, 14),
            MinHeight = 250
        };

        var panel = new Grid { Width = 620, MaxHeight = 560 };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.Children.Add(summary);
        Grid.SetRow(source, 1);
        panel.Children.Add(source);

        var border = new Border
        {
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            Child = panel
        };
        // Popup 不在主窗口的逻辑树里，不能依赖 DynamicResource 向上查找。
        // 直接从宿主解析主题画刷，保证深色主题不会退化为黑字黑底。
        border.Background = ResolveBrush("Theme.Brush.CardBackground", Brushes.Black);
        border.BorderBrush = ResolveBrush("Theme.Brush.ControlAccent", Brushes.DodgerBlue);
        summary.Foreground = ResolveBrush("Theme.Brush.TextPrimary", Brushes.WhiteSmoke);
        source.Foreground = ResolveBrush("Theme.Brush.TextPrimary", Brushes.WhiteSmoke);
        source.Background = ResolveBrush("Theme.Brush.SurfaceAlt", Brushes.Black);

        return (new Popup
        {
            Child = border,
            PlacementTarget = _owner,
            Placement = PlacementMode.Center,
            StaysOpen = true,
            AllowsTransparency = false
        }, summary, source);
    }

    private Brush ResolveBrush(object resourceKey, Brush fallback) =>
        _owner.TryFindResource(resourceKey) as Brush
        ?? Application.Current.TryFindResource(resourceKey) as Brush
        ?? fallback;

    private void Owner_Closed(object? sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        InputManager.Current.PreProcessInput -= InputManager_PreProcessInput;
        _owner.PreviewMouseLeftButtonDown -= Owner_PreviewMouseLeftButtonDown;
        _owner.Closed -= Owner_Closed;
        _popup.IsOpen = false;
    }

    private sealed record UiSourceLocation(
        string ElementType,
        string ElementName,
        string DisplayPath,
        string LineDescription,
        string DataContextType,
        string SourceSnippet)
    {
        public static UiSourceLocation Unavailable(string reason) => new(
            "未解析",
            "（无）",
            "（无）",
            "（无）",
            "（无）",
            reason);
    }
}
