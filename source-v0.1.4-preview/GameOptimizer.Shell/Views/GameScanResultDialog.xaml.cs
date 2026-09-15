using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Services;

namespace GameOptimizer.Shell.Views;

public partial class GameScanResultDialog : Window
{
    public List<DetectedGameInfo> SelectedGames { get; } = new();
    public ObservableCollection<GameSelectionItem> Items { get; } = new();
    
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }
    
    // P2-003: 键盘导航支持
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ConfirmButton_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
            e.Handled = true;
        }
    }

    public GameScanResultDialog(IEnumerable<DetectedGameInfo> games)
    {
        InitializeComponent();
        KeyDown += Window_KeyDown;
        Closed += OnClosed;
        
        foreach (var game in games)
        {
            var item = new GameSelectionItem(game);
            item.PropertyChanged += Item_PropertyChanged;
            Items.Add(item);
        }
        
        GameList.ItemsSource = Items;
        UpdateLocalizedTexts();
        UpdateSelectedCount();
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
    }
    
    private void OnLanguageChanged(object? sender, string e)
    {
        _ = Dispatcher.InvokeAsync(() => { UpdateLocalizedTexts(); UpdateSelectedCount(); });
    }
    
    private void OnClosed(object? sender, EventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        foreach (var item in Items)
            item.PropertyChanged -= Item_PropertyChanged;
    }
    
    private void UpdateLocalizedTexts()
    {
        var loc = LocalizationService.Instance;
        DialogTitleText.Text = loc.Get("GameScan.Title");
        SubtitleText.Text = loc.Get("GameScan.Subtitle", Items.Count);
        SelectAllBtn.Content = loc.Get("Common.SelectAll");
        DeselectAllBtn.Content = loc.Get("Common.DeselectAll");
        CancelBtn.Content = loc.Get("Common.Cancel");
        ConfirmBtn.Content = loc.Get("GameScan.AddSelected");
    }

    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GameSelectionItem.IsSelected))
        {
            UpdateSelectedCount();
        }
    }

    private void UpdateSelectedCount()
    {
        var loc = LocalizationService.Instance;
        var count = Items.Count(i => i.IsSelected);
        SelectedCountText.Text = loc.Get("GameScan.SelectedCount", count);
    }

    private void SelectAllButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in Items) item.IsSelected = true;
    }

    private void DeselectAllButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in Items) item.IsSelected = false;
    }

    private void GameItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is GameSelectionItem item)
        {
            item.IsSelected = !item.IsSelected;
        }
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedGames.Clear();
        SelectedGames.AddRange(Items.Where(i => i.IsSelected).Select(i => i.Game));
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

public class GameSelectionItem : INotifyPropertyChanged
{
    private bool _isSelected;
    
    public DetectedGameInfo Game { get; }
    
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    public string SizeText => Game.SizeBytes > 0 
        ? $"{Game.SizeBytes / (1024.0 * 1024 * 1024):F1} GB" 
        : "";

    public GameSelectionItem(DetectedGameInfo game)
    {
        Game = game;
        _isSelected = true; // 默认选中
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
