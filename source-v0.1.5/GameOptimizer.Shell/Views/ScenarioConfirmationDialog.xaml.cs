// File: GameOptimizer.Shell/Views/ScenarioConfirmationDialog.xaml.cs
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Models;
using GameOptimizer.Shell.Services;

namespace GameOptimizer.Shell.Views;

public partial class ScenarioConfirmationDialog : Window
{
    private readonly LocalizationService _localization = LocalizationService.Instance;
    private readonly IPowerScenarioService _scenarioService;
    private readonly ScenarioComparisonResult _comparisonResult;
    private readonly string _scenarioId;
    private readonly HardwareInfo _hardware;
    private bool _isProcessing;

    public bool IsConfirmed { get; private set; }

    public ScenarioConfirmationDialog(
        IPowerScenarioService scenarioService,
        ScenarioComparisonResult comparisonResult,
        string scenarioId,
        HardwareInfo hardware)
    {
        InitializeComponent();
        
        _scenarioService = scenarioService ?? throw new ArgumentNullException(nameof(scenarioService));
        _comparisonResult = comparisonResult ?? throw new ArgumentNullException(nameof(comparisonResult));
        _scenarioId = scenarioId;
        _hardware = hardware;

        ApplyLocalization();
        LoadData();
        
        _localization.LanguageChanged += OnLanguageChanged;
    }

    private void ApplyLocalization()
    {
        Title = _localization.Get("PowerScenario.Dialog.Title");
        TitleText.Text = _localization.Get("PowerScenario.Dialog.Title");
        ProgressText.Text = _localization.Get("PowerScenario.Dialog.Applying");
        
        CancelBtn.Content = _localization.Get("Common.Cancel");
        ConfirmBtn.Content = _localization.Get("PowerScenario.Dialog.Apply");
        
        UpdateDataGridHeaders();
    }

    private void UpdateDataGridHeaders()
    {
        if (ComparisonGrid.Columns.Count >= 4)
        {
            ComparisonGrid.Columns[0].Header = _localization.Get("PowerScenario.Dialog.ParameterName");
            ComparisonGrid.Columns[1].Header = _localization.Get("PowerScenario.Dialog.CurrentValue");
            ComparisonGrid.Columns[2].Header = _localization.Get("PowerScenario.Dialog.Change");
            ComparisonGrid.Columns[3].Header = _localization.Get("PowerScenario.Dialog.NewValue");
        }
    }

    private void OnLanguageChanged(object? sender, string e)
    {
        ApplyLocalization();
        LoadData();
    }

    private void LoadData()
    {
        ScenarioNameText.Text = _comparisonResult.ScenarioName;
        ScenarioDescText.Text = _comparisonResult.ScenarioDescription;
        CurrentPlanText.Text = _comparisonResult.CurrentPowerPlanName;
        NewPlanText.Text = _comparisonResult.NewPowerPlanName;

        if (_comparisonResult.HasConflict)
        {
            ConflictWarning.Visibility = Visibility.Visible;
            ConflictMessageText.Text = _localization.Get("PowerScenario.Warning.PlanExists");
        }

        ComparisonGrid.ItemsSource = _comparisonResult.Comparisons;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !_isProcessing)
        {
            DialogResult = false;
            Close();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_isProcessing) return;
        
        DialogResult = false;
        Close();
    }

    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (_isProcessing) return;

        await ApplyScenarioAsync();
    }

    private async Task ApplyScenarioAsync()
    {
        _isProcessing = true;
        ShowProgress(true);

        try
        {
            var preset = PowerPresetService.GetPresetForCpu(
                _scenarioId,
                _hardware.CpuVendor,
                _hardware.IsIntelHybrid);

            var application = new PowerScenarioApplication
            {
                PowerPlanName = _comparisonResult.NewPowerPlanName,
                Settings = preset.Settings,
                OverwriteExisting = _comparisonResult.HasConflict
            };

            var result = await _scenarioService.ApplyScenarioAsync(application);

            if (result.IsSuccess)
            {
                IsConfirmed = true;
                DialogResult = true;
                Close();
            }
            else
            {
                ShowError(result.Message);
            }
        }
        catch (Exception ex)
        {
            ShowError(_localization.Get("PowerScenario.Error.ApplyFailed", ex.Message));
        }
        finally
        {
            _isProcessing = false;
            ShowProgress(false);
        }
    }

    private void ShowProgress(bool show)
    {
        ProgressPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ButtonPanel.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        
        ConfirmBtn.IsEnabled = !show;
        CancelBtn.IsEnabled = !show;
    }

    private void ShowError(string message)
    {
        try
        {
            var title = _localization.Get("Common.Error");
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch
        {
            MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _localization.LanguageChanged -= OnLanguageChanged;
        base.OnClosed(e);
    }
}
