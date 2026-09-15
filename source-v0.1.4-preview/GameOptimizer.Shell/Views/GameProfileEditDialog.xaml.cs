using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Models;
using GameOptimizer.Shell.Services;
using GameOptimizer.Shell.Constants;

namespace GameOptimizer.Shell.Views;

public partial class GameProfileEditDialog : Window
{
    private readonly GameProfile _profile;
    private readonly List<OptimizationItemViewModel> _optimizationItems;
    private GameType _selectedGameType;

    public bool IsSaved { get; private set; }
    
    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }
    
    // P2-003: 键盘导航支持
    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    public GameProfileEditDialog(GameProfile profile, IEnumerable<OptimizationItemInfo> availableOperations)
    {
        InitializeComponent();
        KeyDown += Window_KeyDown;
        Closed += OnClosed;
        _profile = profile;
        _selectedGameType = profile.GameType;
        var loc = LocalizationService.Instance;
        var isEn = loc.CurrentLanguage == "en-US";
        
        GameNameBox.Text = profile.GameName;
        GamePathText.Text = profile.ExecutablePath;
        AutoOptimizeCheck.IsChecked = profile.AutoOptimizeOnLaunch;
        RestoreOnExitCheck.IsChecked = profile.RestoreOnExit;
        AutoHighPriorityCheck.IsChecked = profile.AutoSetHighPriority;

        InitializeGameTypeCombo(isEn);

        _optimizationItems = availableOperations.Select(op => new OptimizationItemViewModel
        {
            OperationId = op.OperationId,
            Title = op.Title,
            Description = op.Description,
            ModuleName = op.ModuleName,
            RiskLevel = op.RiskLevel,
            RiskText = op.RiskLevel switch { "High" => loc.Get("Risk.High"), "Medium" => loc.Get("Risk.Medium"), _ => loc.Get("Risk.Safe") },
            IsSelected = profile.EnabledOperationIds.Contains(op.OperationId)
        }).ToList();

        foreach (var item in _optimizationItems)
            item.PropertyChanged += OnItemSelectionChanged;

        OptimizationList.ItemsSource = _optimizationItems;
        UpdateLocalizedTexts();
        UpdateSelectedCount();
        UpdateRecommendationPanel(isEn);
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
    }
    
    private void OnLanguageChanged(object? sender, string e)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            var isEn = LocalizationService.Instance.CurrentLanguage == "en-US";
            InitializeGameTypeCombo(isEn);
            UpdateLocalizedTexts();
            UpdateSelectedCount();
            UpdateRecommendationPanel(isEn);
        });
    }

    private void InitializeGameTypeCombo(bool isEn)
    {
        var types = Enum.GetValues<GameType>();
        var items = types.Select(t => new ComboBoxItem
        {
            Content = GameTypeDetector.GetTypeDisplayName(t, isEn),
            Tag = t
        }).ToList();
        
        GameTypeCombo.ItemsSource = items;
        GameTypeCombo.SelectedIndex = (int)_selectedGameType;
    }

    private void GameTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GameTypeCombo.SelectedItem is ComboBoxItem item && item.Tag is GameType type)
        {
            _selectedGameType = type;
            var isEn = LocalizationService.Instance.CurrentLanguage == "en-US";
            UpdateRecommendationPanel(isEn);
        }
    }

    private void UpdateRecommendationPanel(bool isEn)
    {
        var recommended = GameOptimizationPresets.GetRecommendedOperations(_selectedGameType);
        var desc = GameOptimizationPresets.GetPresetDescription(_selectedGameType, isEn);
        RecommendDescText.Text = desc;
        GameTypeDescText.Text = GameTypeDetector.GetTypeDescription(_selectedGameType, isEn);
    }

    private void ApplyRecommendation_Click(object sender, RoutedEventArgs e)
    {
        var recommended = GameOptimizationPresets.GetRecommendedOperations(_selectedGameType);
        var recommendedSet = new HashSet<string>(recommended);
        
        foreach (var item in _optimizationItems)
            item.IsSelected = recommendedSet.Contains(item.OperationId);
        
        UpdateSelectedCount();
        
        var loc = LocalizationService.Instance;
        var isEn = loc.CurrentLanguage == "en-US";
        var typeName = GameTypeDetector.GetTypeDisplayName(_selectedGameType, isEn);
        InfoDialog.Show(this, loc.Get("GameProfiles.RecommendApplied"), 
            loc.Get("GameProfiles.RecommendAppliedMsg", typeName, recommended.Count));
    }
    
    private void UpdateLocalizedTexts()
    {
        var loc = LocalizationService.Instance;
        DialogTitleText.Text = loc.Get("GameProfiles.EditTitle");
        LblGameName.Text = loc.Get("GameProfiles.GameName");
        LblExePath.Text = loc.Get("GameProfiles.ExePath");
        LblGameType.Text = loc.Get("GameProfiles.GameType");
        LblSmartRecommend.Text = loc.Get("GameProfiles.SmartRecommend");
        ApplyRecommendBtn.Content = loc.Get("GameProfiles.ApplyRecommend");
        LblAutoOptimize.Text = loc.Get("GameProfiles.AutoOptimize");
        LblAutoOptimizeDesc.Text = loc.Get("GameProfiles.AutoOptimizeDesc");
        LblRestoreOnExit.Text = loc.Get("GameProfiles.RestoreOnExit");
        LblRestoreOnExitDesc.Text = loc.Get("GameProfiles.RestoreOnExitDesc");
        LblAutoHighPriority.Text = loc.Get("GameProfiles.AutoHighPriority");
        LblAutoHighPriorityDesc.Text = loc.Get("GameProfiles.AutoHighPriorityDesc");
        LblEnabledOpts.Text = loc.Get("GameProfiles.EnabledOpts");
        CancelBtn.Content = loc.Get("Common.Cancel");
        SaveBtn.Content = loc.Get("Common.Save");
    }

    private void OnItemSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OptimizationItemViewModel.IsSelected))
            UpdateSelectedCount();
    }

    private void UpdateSelectedCount()
    {
        var loc = LocalizationService.Instance;
        var count = _optimizationItems.Count(x => x.IsSelected);
        SelectedCountText.Text = loc.Get("GameProfiles.SelectedCount", count);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        foreach (var item in _optimizationItems)
            item.PropertyChanged -= OnItemSelectionChanged;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationService.Instance;
        if (string.IsNullOrWhiteSpace(GameNameBox.Text))
        {
            InfoDialog.Show(this, loc.Get("Dialog.Info"), 
                loc.Get("GameProfiles.EnterName"), Constants.IconGlyphs.Warning);
            return;
        }

        _profile.GameName = GameNameBox.Text.Trim();
        _profile.GameType = _selectedGameType;
        _profile.AutoOptimizeOnLaunch = AutoOptimizeCheck.IsChecked == true;
        _profile.RestoreOnExit = RestoreOnExitCheck.IsChecked == true;
        _profile.AutoSetHighPriority = AutoHighPriorityCheck.IsChecked == true;
        _profile.EnabledOperationIds = _optimizationItems
            .Where(x => x.IsSelected)
            .Select(x => x.OperationId)
            .ToList();

        IsSaved = true;
        Close();
    }
}

public class OptimizationItemViewModel : INotifyPropertyChanged
{
    private bool _isSelected;

    public string OperationId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = "Safe";
    public string RiskText { get; set; } = "Safe";

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

    public event PropertyChangedEventHandler? PropertyChanged;
}

public class OptimizationItemInfo
{
    public string OperationId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = "Safe";
}
