// File: GameOptimizer.Shell/Views/PowerPlanNameDialog.xaml.cs
using System.Windows;
using System.Windows.Controls;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Constants;

namespace GameOptimizer.Shell.Views;

public partial class PowerPlanNameDialog : Window
{
    public bool Confirmed { get; private set; }
    public string PlanName => PlanNameInput.Text.Trim();

    public PowerPlanNameDialog(string defaultName, string planIcon, string planDisplayName, string planDescription)
    {
        InitializeComponent();
        
        PlanNameInput.Text = defaultName;
        PlanIconText.Text = planIcon;
        PlanNameText.Text = planDisplayName;
        PlanDescText.Text = planDescription;
        
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        PlanNameInput.Focus();
        PlanNameInput.SelectAll();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        UpdateLocalizedTexts();
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
        
        DialogTitle.Text = loc.Get("PowerPlanName.Title");
        DialogSubtitle.Text = loc.Get("PowerPlanName.Subtitle");
        LblPlanName.Text = loc.Get("PowerPlanName.PlanNameLabel");
        TxtWarning.Text = loc.Get("PowerPlanName.WarningNoChange");
        CancelBtn.Content = loc.Get("Common.Cancel");
        
        // ConfirmBtnText 在 ControlTemplate 内部，通过 Template.FindName 访问
        if (ConfirmBtn.Template?.FindName("ConfirmBtnText", ConfirmBtn) is TextBlock confirmText)
        {
            confirmText.Text = loc.Get("PowerPlanName.ConfirmCreate");
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        var planName = PlanNameInput.Text.Trim();
        
        if (string.IsNullOrWhiteSpace(planName))
        {
            var loc = LocalizationService.Instance;
            InfoDialog.Show(this, loc.Get("Dialog.Error"), 
                loc.Get("PowerPlanName.EmptyNameError"), Constants.IconGlyphs.Warning);
            PlanNameInput.Focus();
            return;
        }

        Confirmed = true;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        DialogResult = false;
        Close();
    }
}
