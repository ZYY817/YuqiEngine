using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GameOptimizer.Shell.Constants;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Services.SystemCleanup;

namespace GameOptimizer.Shell.Views;

public partial class CleanupFileListDialog : Window
{
    private readonly CleanupItem _item;
    private readonly List<FileListItem> _files = new();
    private readonly string _primaryPath;
    private readonly CleanupPreview? _preview;

    public CleanupFileListDialog(CleanupItem item) : this(item, null)
    {
    }

    public CleanupFileListDialog(CleanupItem item, CleanupPreview? preview)
    {
        InitializeComponent();
        _item = item;
        _preview = preview;
        _primaryPath = item.TargetPaths.FirstOrDefault() ?? "";
        
        var loc = LocalizationService.Instance;
        DialogTitle.Text = loc.Get("CleanupFileList.Title", item.Name);
        ItemNameText.Text = item.Name;
        ItemDescText.Text = item.Description;
        FileSizeText.Text = item.SizeText;
        FileCountInfoText.Text = loc.Get("CleanupFileList.FileCount", item.FileCount);
        CleanPathText.Text = _primaryPath;
        
        SetRiskBadge(item.Risk);
        
        Loaded += OnLoaded;
        KeyDown += Window_KeyDown;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
    }
    
    // P2-003: 键盘导航支持
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape || e.Key == Key.Enter)
        {
            Close();
            e.Handled = true;
        }
    }
    
    private void OnLanguageChanged(object? sender, string e)
    {
        _ = Dispatcher.InvokeAsync(UpdateLocalizedTexts);
    }
    
    private void UpdateLocalizedTexts()
    {
        var loc = LocalizationService.Instance;
        DialogTitle.Text = loc.Get("CleanupFileList.Title", _item.Name);
        FileCountInfoText.Text = loc.Get("CleanupFileList.FileCount", _item.FileCount);
        LblCleanPath.Text = "📍 " + loc.Get("CleanupFileList.CleanPath");
        OpenFolderText.Text = "📁 " + loc.Get("CleanupFileList.Open");
        LblFileName.Text = loc.Get("CleanupFileList.FileName");
        LblSize.Text = loc.Get("CleanupFileList.Size");
        LblModified.Text = loc.Get("CleanupFileList.ModifiedTime");
        EmptyText.Text = loc.Get("CleanupFileList.Empty");
        LoadingText.Text = loc.Get("CleanupFileList.Loading");
        TipText.Text = IconGlyphs.Lightbulb + " " + loc.Get("CleanupFileList.Tip");
        
        SetRiskBadge(_item.Risk);
        
        CloseBtn.ApplyTemplate();
        if (CloseBtn.Template.FindName("CloseBtnText", CloseBtn) is TextBlock closeText)
            closeText.Text = loc.Get("Dialog.Close");
    }

    private void SetRiskBadge(RiskLevel risk)
    {
        var loc = LocalizationService.Instance;
        switch (risk)
        {
            case RiskLevel.Safe:
                RiskBadge.Background = ThemeColors.SuccessAlpha20Brush;
                RiskText.Text = loc.Get("CleanupFileList.RiskSafe");
                RiskText.Foreground = ThemeColors.SuccessBrush;
                break;
            case RiskLevel.Low:
                RiskBadge.Background = ThemeColors.InfoLightAlpha20Brush;
                RiskText.Text = loc.Get("CleanupFileList.RiskLow");
                RiskText.Foreground = ThemeColors.InfoLightBrush;
                break;
            case RiskLevel.Medium:
                RiskBadge.Background = ThemeColors.WarningAlpha20Brush;
                RiskText.Text = loc.Get("CleanupFileList.RiskMedium");
                RiskText.Foreground = ThemeColors.WarningBrush;
                break;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateLocalizedTexts();
        await LoadFilesAsync();
    }

    private async Task LoadFilesAsync()
    {
        LoadingState.Visibility = Visibility.Visible;
        EmptyState.Visibility = Visibility.Collapsed;
        FileListControl.ItemsSource = null;

        try
        {
            if (_preview is not null)
            {
                _files.AddRange(_preview.Entries.Take(500).Select(entry => new FileListItem
                {
                    Name = Path.GetFileName(entry.CanonicalPath),
                    FullPath = entry.CanonicalPath,
                    Size = entry.SizeBytes,
                    SizeText = FormatSize(entry.SizeBytes),
                    Modified = entry.LastWriteTimeUtc.ToLocalTime(),
                    ModifiedText = entry.LastWriteTimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                }));
                RenderFiles();
                return;
            }

            await Task.Run(() =>
            {
                foreach (var targetPath in _item.TargetPaths)
                {
                    var path = Environment.ExpandEnvironmentVariables(targetPath);
                    if (!Directory.Exists(path)) continue;

                    var searchOption = _item.IncludeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                    var patterns = _item.Patterns.Length > 0 ? _item.Patterns : new[] { "*" };
                    
                    foreach (var pattern in patterns)
                    {
                        try
                        {
                            var files = Directory.EnumerateFiles(path, pattern, searchOption)
                                .Take(500 - _files.Count)
                                .Select(f =>
                                {
                                    try
                                    {
                                        var info = new FileInfo(f);
                                        return new FileListItem
                                        {
                                            Name = info.Name,
                                            FullPath = f,
                                            Size = info.Length,
                                            SizeText = FormatSize(info.Length),
                                            Modified = info.LastWriteTime,
                                            ModifiedText = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
                                        };
                                    }
                                    catch
                                    {
                                        return null;
                                    }
                                })
                                .Where(f => f != null);

                            _files.AddRange(files!);
                            
                            if (_files.Count >= 500) break;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[CleanupFileListDialog] Error enumerating files: {ex.Message}");
                        }
                    }
                    
                    if (_files.Count >= 500) break;
                }
                
                _files.Sort((a, b) => b.Size.CompareTo(a.Size));
            });

            RenderFiles();
        }
        catch (Exception ex)
        {
            _ = Dispatcher.InvokeAsync(() =>
            {
                LoadingState.Visibility = Visibility.Collapsed;
                EmptyState.Visibility = Visibility.Visible;
                Debug.WriteLine($"[CleanupFileListDialog] LoadFiles error: {ex.Message}");
            });
        }
    }

    private void RenderFiles()
    {
        LoadingState.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = _files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FileListControl.ItemsSource = _files.Count == 0 ? null : _files;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1) DragMove();
    }

    private void OpenFolder_Click(object sender, MouseButtonEventArgs e)
    {
        var loc = LocalizationService.Instance;
        try
        {
            var path = Environment.ExpandEnvironmentVariables(_primaryPath);
            if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = path,
                    UseShellExecute = true
                });
            }
            else
            {
                InfoDialog.Show(this, loc.Get("Dialog.Info"), loc.Get("CleanupFileList.DirNotExist"), IconGlyphs.Lightbulb);
            }
        }
        catch (Exception ex)
        {
            InfoDialog.Show(this, loc.Get("Dialog.Error"), loc.Get("CleanupFileList.CannotOpen", ex.Message), IconGlyphs.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    
    protected override void OnClosed(EventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        base.OnClosed(e);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 0) return "?";
        if (bytes == 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int i = 0;
        double size = bytes;
        while (size >= 1024 && i < units.Length - 1)
        {
            size /= 1024;
            i++;
        }
        return $"{size:F1} {units[i]}";
    }
}

public class FileListItem
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public long Size { get; set; }
    public string SizeText { get; set; } = string.Empty;
    public DateTime Modified { get; set; }
    public string ModifiedText { get; set; } = string.Empty;
}
