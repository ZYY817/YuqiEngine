using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Services.SystemCleanup;
using GameOptimizer.Shell.Constants;

namespace GameOptimizer.Shell.Views;

public partial class CleanupConfirmDialog : Window
{
    private static readonly TimeSpan ConfirmationLifetime = TimeSpan.FromMinutes(2);
    private readonly IReadOnlyList<CleanupConfirmationItem> _items;
    private DateTimeOffset? _confirmedAtUtc;
    
    public bool IsConfirmed { get; private set; }

    public CleanupConfirmDialog(
        IEnumerable<CleanupItem> selectedItems,
        IReadOnlyDictionary<string, CleanupPreview> previews)
    {
        InitializeComponent();
        ArgumentNullException.ThrowIfNull(selectedItems);
        ArgumentNullException.ThrowIfNull(previews);
        _items = selectedItems
            .Where(item => item.IsSelected && previews.ContainsKey(item.Id))
            .Select(item => new CleanupConfirmationItem(item, previews[item.Id]))
            .Where(item => item.Preview.Entries.Count > 0)
            .ToList();
        
        ItemsListControl.ItemsSource = _items;
        
        SelectedCountText.Text = _items.Count.ToString();
        FileCountText.Text = _items.Sum(i => i.Preview.Entries.Count).ToString("N0");
        TotalSizeText.Text = FormatSize(_items.Sum(i => i.Preview.TotalBytes));
        
        Loaded += OnLoaded;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
    }
    
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateLocalizedTexts();
    }
    
    private void OnLanguageChanged(object? sender, string e)
    {
        _ = Dispatcher.InvokeAsync(UpdateLocalizedTexts);
    }
    
    private void UpdateLocalizedTexts()
    {
        var loc = LocalizationService.Instance;
        
        Title = loc.Get("CleanupConfirm.Title");
        DialogTitleText.Text = loc.Get("CleanupConfirm.Title");
        WarningTitleText.Text = loc.Get("CleanupConfirm.WarningTitle");
        WarningDescText.Text = loc.Get("CleanupConfirm.WarningDesc");
        LblSelectedItems.Text = loc.Get("CleanupConfirm.SelectedItems");
        LblFileCount.Text = loc.Get("CleanupConfirm.FileCount");
        LblTotalSize.Text = loc.Get("CleanupConfirm.TotalSize");
        LblItemDetails.Text = loc.Get("CleanupConfirm.ItemDetails");
        TipText.Text = IconGlyphs.Lightbulb + " " + loc.Get("CleanupConfirm.Tip");
        
        CancelBtn.ApplyTemplate();
        if (CancelBtn.Template.FindName("CancelBtnText", CancelBtn) is TextBlock cancelText)
            cancelText.Text = loc.Get("Dialog.Cancel");
        
        ConfirmBtn.ApplyTemplate();
        if (ConfirmBtn.Template.FindName("ConfirmBtnText", ConfirmBtn) is TextBlock confirmText)
            confirmText.Text = loc.Get("CleanupConfirm.StartCleanup");
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1) DragMove();
    }

    private void ViewDetails_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        if (element.Tag is not CleanupConfirmationItem item) return;
        
        ShowFileListDialog(item);
    }

    private void ShowFileListDialog(CleanupConfirmationItem item)
    {
        var dialog = new CleanupFileListDialog(item.Item, item.Preview)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        _confirmedAtUtc = DateTimeOffset.UtcNow;
        IsConfirmed = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        IsConfirmed = false;
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        IsConfirmed = false;
        Close();
    }
    
    // P2-003: 键盘导航支持
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _confirmedAtUtc = DateTimeOffset.UtcNow;
            IsConfirmed = true;
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            IsConfirmed = false;
            Close();
            e.Handled = true;
        }
    }
    
    protected override void OnClosed(System.EventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        base.OnClosed(e);
    }

    private static string FormatSize(long bytes)
    {
        var loc = LocalizationService.Instance;
        if (bytes < 0) return loc.Get("CleanupConfirm.Unknown");
        if (bytes == 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int i = 0;
        double size = bytes;
        while (size >= 1024 && i < units.Length - 1)
        {
            size /= 1024;
            i++;
        }
        return $"{size:F2} {units[i]}";
    }

    public static IReadOnlyDictionary<string, CleanupPreviewConfirmation>? Show(
        Window? owner,
        IEnumerable<CleanupItem> selectedItems,
        IReadOnlyDictionary<string, CleanupPreview> previews)
    {
        var dialog = new CleanupConfirmDialog(selectedItems, previews);
        if (owner != null) dialog.Owner = owner;
        dialog.ShowDialog();
        if (!dialog.IsConfirmed || dialog._confirmedAtUtc is null) return null;
        return dialog._items.ToDictionary(
            item => item.Item.Id,
            item => CleanupPreviewConfirmation.Create(item.Preview, dialog._confirmedAtUtc.Value, ConfirmationLifetime),
            StringComparer.Ordinal);
    }
}

public sealed class CleanupConfirmationItem
{
    public CleanupConfirmationItem(CleanupItem item, CleanupPreview preview)
    {
        Item = item;
        Preview = preview;
    }

    public CleanupItem Item { get; }
    public CleanupPreview Preview { get; }
    public string Name => Item.Name;
    public string Description => Item.Description;
    public RiskLevel Risk => Item.Risk;
    public int FileCount => Preview.Entries.Count;
    public string SizeText => FormatSize(Preview.TotalBytes);

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:F2} {units[unit]}";
    }
}
