// File: GameOptimizer.Shell/Views/GamePriorityManagerPage.xaml.cs
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Models;
using GameOptimizer.Shell.Services;
using GameOptimizer.Shell.Constants;

namespace GameOptimizer.Shell.Views;

public partial class GamePriorityManagerPage : UserControl, INotifyPropertyChanged
{
    private const string RuntimePriorityBlockedMessage = "运行时进程优先级尚未接入带显式确认和 durable receipt 的 ProcessTuningOwner，当前已零写入阻断。";
    private static bool IsRuntimePriorityBlocked => true;
    public event PropertyChangedEventHandler? PropertyChanged;

    private ObservableCollection<GamePriorityDisplayModel> _games = new();
    private bool _isOperating;
    
    public ObservableCollection<GamePriorityDisplayModel> Games
    {
        get => _games;
        set
        {
            _games = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Games)));
        }
    }
    
    public GamePriorityManagerPage()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        UpdateLocalizedTexts();
        LoadGames();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, string e)
    {
        _ = Dispatcher.InvokeAsync(UpdateLocalizedTexts);
    }

    private void UpdateLocalizedTexts()
    {
        var loc = LocalizationService.Instance;
        
        PageTitle.Text = loc.Get("GamePriority.PageTitle");
        PageSubtitle.Text = loc.Get("GamePriority.PageSubtitle");
        AddGameBtnText.Text = loc.Get("GamePriority.AddGame");
        TotalGamesLabel.Text = loc.Get("GamePriority.TotalGames");
        EnabledGamesLabel.Text = loc.Get("GamePriority.EnabledGames");
        LastUsedLabel.Text = loc.Get("GamePriority.LastUsed");
        EmptyTitle.Text = loc.Get("GamePriority.EmptyTitle");
        EmptySubtitle.Text = loc.Get("GamePriority.EmptySubtitle");
        
        // Update button texts in list items
        UpdateListItemTexts();
    }
    
    private void UpdateListItemTexts()
    {
        var loc = LocalizationService.Instance;
        
        // Find all buttons in the visual tree and update their content
        if (GamesListContainer.ItemsSource != null)
        {
            foreach (var item in GamesListContainer.Items)
            {
                var container = GamesListContainer.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
                if (container != null)
                {
                    // Update Edit button
                    var editBtn = FindVisualChild<Button>(container, "EditButton");
                    if (editBtn != null) editBtn.Content = loc.Get("GamePriority.Edit");
                    
                    // Update Delete button
                    var deleteBtn = FindVisualChild<Button>(container, "DeleteButton");
                    if (deleteBtn != null) deleteBtn.Content = loc.Get("GamePriority.Delete");
                    
                    // Update Enabled badge
                    var enabledBadgeText = FindVisualChild<TextBlock>(container, "EnabledBadgeText");
                    if (enabledBadgeText != null) enabledBadgeText.Text = loc.Get("GamePriority.Enabled");
                }
            }
        }
    }
    
    private static T? FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T element && element.Name == name)
                return element;
            
            var result = FindVisualChild<T>(child, name);
            if (result != null)
                return result;
        }
        return null;
    }

    private void LoadGames()
    {
        Debug.WriteLine("=== [GamePriorityManagerPage] LoadGames START ===");
        try
        {
            var manager = GamePriorityProfileManager.Instance;
            Debug.WriteLine("[GamePriorityManagerPage] Got manager instance, calling Reload()...");
            
            manager.Reload();
            Debug.WriteLine($"[GamePriorityManagerPage] Reload completed. Profile count: {manager.Profiles.Count}");
            
            Games.Clear();
            Debug.WriteLine("[GamePriorityManagerPage] Games collection cleared");
            
            foreach (var profile in manager.Profiles)
            {
                Debug.WriteLine($"[GamePriorityManagerPage] Adding to UI: {profile.GameName} (ID: {profile.Id})");
                Games.Add(new GamePriorityDisplayModel(profile));
            }
            
            Debug.WriteLine($"[GamePriorityManagerPage] Games collection now has {Games.Count} items");
            
            GamesListContainer.ItemsSource = Games;
            Debug.WriteLine("[GamePriorityManagerPage] ItemsSource set");
            
            UpdateStats();
            Debug.WriteLine("[GamePriorityManagerPage] Stats updated");
            
            UpdateEmptyState();
            Debug.WriteLine($"[GamePriorityManagerPage] Empty state updated. Visibility: {EmptyState.Visibility}");
            
            // Update localized texts after items are loaded
            Dispatcher.BeginInvoke(new Action(() => UpdateListItemTexts()), System.Windows.Threading.DispatcherPriority.Loaded);
            Debug.WriteLine("[GamePriorityManagerPage] UpdateListItemTexts scheduled");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GamePriorityManagerPage] LoadGames ERROR: {ex.Message}");
            Debug.WriteLine($"[GamePriorityManagerPage] Stack trace: {ex.StackTrace}");
            
            ToastService.Instance.Error(
                LocalizationService.Instance.Get("Common.Error"),
                ex.Message);
        }
        Debug.WriteLine("=== [GamePriorityManagerPage] LoadGames END ===");
    }

    private void UpdateStats()
    {
        var totalCount = Games.Count;
        var enabledCount = Games.Count(g => g.IsEnabled);
        var lastUsed = Games.OrderByDescending(g => g.LastUsedAt).FirstOrDefault();
        
        TotalGamesCount.Text = totalCount.ToString();
        EnabledGamesCount.Text = enabledCount.ToString();
        
        if (lastUsed != null && lastUsed.LastUsedAt > DateTime.MinValue)
        {
            var timeSpan = DateTime.Now - lastUsed.LastUsedAt;
            LastUsedText.Text = timeSpan.TotalMinutes < 1 
                ? LocalizationService.Instance.Get("Common.JustNow")
                : timeSpan.TotalHours < 1 
                    ? $"{(int)timeSpan.TotalMinutes} {LocalizationService.Instance.Get("Common.MinutesAgo")}"
                    : timeSpan.TotalDays < 1 
                        ? $"{(int)timeSpan.TotalHours} {LocalizationService.Instance.Get("Common.HoursAgo")}"
                        : lastUsed.LastUsedAt.ToString("yyyy-MM-dd HH:mm");
        }
        else
        {
            LastUsedText.Text = LocalizationService.Instance.Get("Common.Never");
        }
    }

    private void UpdateEmptyState()
    {
        EmptyState.Visibility = Games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddGameButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsRuntimePriorityBlocked)
        {
            ToastService.Instance.Error(LocalizationService.Instance.Get("Common.Error"), RuntimePriorityBlockedMessage);
            return;
        }

        if (_isOperating) return;
        _isOperating = true;
        
        Debug.WriteLine("=== [GamePriorityManagerPage] AddGameButton_Click START ===");
        
        var dialog = new GamePriorityEditDialog() { Owner = Window.GetWindow(this) };
        Debug.WriteLine("[GamePriorityManagerPage] Dialog created, showing...");
        
        var dialogResult = dialog.ShowDialog();
        Debug.WriteLine($"[GamePriorityManagerPage] Dialog closed. Result: {dialogResult}, IsSaved: {dialog.IsSaved}");
        
        if (dialogResult == true && dialog.IsSaved)
        {
            Debug.WriteLine("[GamePriorityManagerPage] Dialog confirmed, adding profile...");
            Debug.WriteLine($"[GamePriorityManagerPage] Profile to add: {dialog.Profile.GameName} (ID: {dialog.Profile.Id})");
            
            try
            {
                GamePriorityProfileManager.Instance.Add(dialog.Profile);
                Debug.WriteLine("[GamePriorityManagerPage] Profile added to manager, calling LoadGames()...");
                
                LoadGames();
                Debug.WriteLine("[GamePriorityManagerPage] LoadGames() completed");
                
                ToastService.Instance.Success(
                    LocalizationService.Instance.Get("Common.Success"),
                    LocalizationService.Instance.Get("GamePriority.AddSuccess"));
                Debug.WriteLine("[GamePriorityManagerPage] Success toast shown");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GamePriorityManagerPage] ERROR: {ex.Message}");
                Debug.WriteLine($"[GamePriorityManagerPage] Stack trace: {ex.StackTrace}");
                
                ToastService.Instance.Error(
                    LocalizationService.Instance.Get("Common.Error"),
                    ex.Message);
            }
        }
        else
        {
            Debug.WriteLine("[GamePriorityManagerPage] Dialog cancelled or not saved");
        }
        
        Debug.WriteLine("=== [GamePriorityManagerPage] AddGameButton_Click END ===");
        _isOperating = false;
    }

    private void EditGameButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsRuntimePriorityBlocked)
        {
            ToastService.Instance.Error(LocalizationService.Instance.Get("Common.Error"), RuntimePriorityBlockedMessage);
            return;
        }

        if (_isOperating) return;
        if (sender is not Button btn || btn.Tag is not string profileId)
            return;

        var manager = GamePriorityProfileManager.Instance;
        var profile = manager.Profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile == null)
            return;

        var dialog = new GamePriorityEditDialog(profile) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true && dialog.IsSaved)
        {
            manager.Update(profile);
            LoadGames();
        }
    }

    private void DeleteGameButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsRuntimePriorityBlocked)
        {
            ToastService.Instance.Error(LocalizationService.Instance.Get("Common.Error"), RuntimePriorityBlockedMessage);
            return;
        }

        if (_isOperating) return;
        if (sender is not Button btn || btn.Tag is not string profileId)
            return;

        var loc = LocalizationService.Instance;
        var confirmed = ConfirmDialog.Show(Window.GetWindow(this),
            loc.Get("Dialog.Confirm"),
            loc.Get("GamePriority.DeleteConfirm"),
            Constants.IconGlyphs.Warning);

        if (confirmed)
        {
            GamePriorityProfileManager.Instance.Remove(profileId);
            LoadGames();
        }
    }

    private void EnableToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (IsRuntimePriorityBlocked)
        {
            ToastService.Instance.Error(LocalizationService.Instance.Get("Common.Error"), RuntimePriorityBlockedMessage);
            return;
        }

        if (sender is not CheckBox checkBox || checkBox.Tag is not string profileId)
            return;

        var manager = GamePriorityProfileManager.Instance;
        var profile = manager.Profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile != null)
        {
            profile.IsEnabled = checkBox.IsChecked == true;
            manager.Update(profile);
        }
    }
}

public class GamePriorityDisplayModel : INotifyPropertyChanged
{
#pragma warning disable CS0067 // Event is required by INotifyPropertyChanged interface but not used in this simple model
    public event PropertyChangedEventHandler? PropertyChanged;
#pragma warning restore CS0067

    private readonly GamePriorityProfile _profile;

    public string Id => _profile.Id;
    public string GameName => _profile.GameName;
    public string ExecutablePath => _profile.ExecutablePath;
    public bool IsEnabled => _profile.IsEnabled;
    public DateTime LastUsedAt => _profile.LastUsedAt;
    
    public string PriorityDisplay
    {
        get
        {
            var loc = LocalizationService.Instance;
            return _profile.Priority switch
            {
                ProcessPriorityClass.Idle => loc.Get("GamePriority.PriorityIdle"),
                ProcessPriorityClass.BelowNormal => loc.Get("GamePriority.PriorityBelowNormal"),
                ProcessPriorityClass.Normal => loc.Get("GamePriority.PriorityNormal"),
                ProcessPriorityClass.AboveNormal => loc.Get("GamePriority.PriorityAboveNormal"),
                ProcessPriorityClass.High => loc.Get("GamePriority.PriorityHigh"),
                ProcessPriorityClass.RealTime => loc.Get("GamePriority.PriorityRealTime"),
                _ => "Unknown"
            };
        }
    }

    public GamePriorityDisplayModel(GamePriorityProfile profile)
    {
        _profile = profile;
    }
}
