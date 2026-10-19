// File: GameOptimizer.Shell/Views/PowerScenarioConfirmDialog.xaml.cs
using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Models;

namespace GameOptimizer.Shell.Views;

public partial class PowerScenarioConfirmDialog : Window
{
    private readonly ScenarioComparisonResult _comparisonResult;
    
    public bool IsConfirmed { get; private set; }
    
    public PowerScenarioConfirmDialog(ScenarioComparisonResult comparisonResult)
    {
        _comparisonResult = comparisonResult ?? throw new ArgumentNullException(nameof(comparisonResult));
        
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }
    
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        UpdateLocalizedTexts();
        LoadComparisonData();
    }
    
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
    }
    
    private void OnLanguageChanged(object? sender, string e)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateLocalizedTexts();
            LoadComparisonData();
        });
    }
    
    private void UpdateLocalizedTexts()
    {
        var loc = LocalizationService.Instance;
        
        TitleText.Text = loc.Get("PowerScenario.Dialog.Title");
        DescriptionLabelText.Text = loc.Get("PowerScenario.Dialog.DescriptionLabel");
        ComparisonTableHeaderText.Text = loc.Get("PowerScenario.Dialog.ComparisonTable");
        HeaderParameterNameText.Text = loc.Get("PowerScenario.Dialog.ParameterName");
        HeaderCurrentValueText.Text = loc.Get("PowerScenario.Dialog.CurrentValue");
        HeaderNewValueText.Text = loc.Get("PowerScenario.Dialog.NewValue");
        NoChangesText.Text = loc.Get("PowerScenario.Dialog.NoChanges");
        
        if (CancelBtn.Template?.FindName("CancelBtnText", CancelBtn) is System.Windows.Controls.TextBlock cancelText)
            cancelText.Text = loc.Get("PowerScenario.Dialog.Cancel");
        
        if (ConfirmBtn.Template?.FindName("ConfirmBtnText", ConfirmBtn) is System.Windows.Controls.TextBlock confirmText)
            confirmText.Text = loc.Get("PowerScenario.Dialog.Confirm");
    }
    
    private void LoadComparisonData()
    {
        var loc = LocalizationService.Instance;
        
        ScenarioNameText.Text = _comparisonResult.ScenarioName;
        ScenarioDescriptionText.Text = _comparisonResult.ScenarioDescription;
        
        var changedComparisons = _comparisonResult.Comparisons.Where(c => c.IsChanged).ToList();
        
        if (changedComparisons.Count == 0)
        {
            ChangesCountText.Text = loc.Get("PowerScenario.Dialog.NoChanges");
            ComparisonsItemsControl.Visibility = Visibility.Collapsed;
            NoChangesPanel.Visibility = Visibility.Visible;
        }
        else
        {
            ChangesCountText.Text = loc.Get("PowerScenario.Dialog.ChangesCount", changedComparisons.Count);
            ComparisonsItemsControl.ItemsSource = changedComparisons;
            ComparisonsItemsControl.Visibility = Visibility.Visible;
            NoChangesPanel.Visibility = Visibility.Collapsed;
        }
    }
    
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }
    
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            IsConfirmed = false;
            DialogResult = false;
            Close();
            e.Handled = true;
        }
    }
    
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        IsConfirmed = false;
        DialogResult = false;
        Close();
    }
    
    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        IsConfirmed = false;
        DialogResult = false;
        Close();
    }
    
    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        IsConfirmed = true;
        DialogResult = true;
        Close();
    }
}
