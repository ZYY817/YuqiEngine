// File: GameOptimizer.Shell/Views/SystemAutoRevertWarningDialog.xaml.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using GameOptimizer.Core.Localization;

namespace GameOptimizer.Shell.Views;

public partial class SystemAutoRevertWarningDialog : Window
{
    private bool _guardEnabled;
    
    public bool GuardEnabled => _guardEnabled;
    
    public SystemAutoRevertWarningDialog()
    {
        InitializeComponent();
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        UpdateLocalizedTexts();
    }
    
    public static bool? Show(Window owner, List<string> revertedTitles)
    {
        var dialog = new SystemAutoRevertWarningDialog();
        dialog.Owner = owner;
        dialog.SetRevertedItems(revertedTitles);
        return dialog.ShowDialog();
    }
    
    private void SetRevertedItems(List<string> titles)
    {
        var loc = LocalizationService.Instance;
        
        MessageText.Text = loc.Get("Esports.AutoRevert.Message", titles.Count);
        
        var displayTitles = titles.Take(15).Select(t => $"• {t}").ToList();
        if (titles.Count > 15)
        {
            displayTitles.Add($"... 及其他 {titles.Count - 15} 项");
        }
        
        RevertedItemsList.ItemsSource = displayTitles;
    }
    
    private void UpdateLocalizedTexts()
    {
        var loc = LocalizationService.Instance;
        
        TitleText.Text = loc.Get("Esports.AutoRevert.Title");
        Reason1Text.Text = loc.Get("Esports.AutoRevert.Reason1");
        Reason2Text.Text = loc.Get("Esports.AutoRevert.Reason2");
        Reason3Text.Text = loc.Get("Esports.AutoRevert.Reason3");
        Reason4Text.Text = loc.Get("Esports.AutoRevert.Reason4");
        RevertedItemsTitle.Text = loc.Get("Esports.AutoRevert.RevertedItems");
        SolutionTitle.Text = loc.Get("Esports.AutoRevert.Solution");
        EnableGuardText.Text = loc.Get("Esports.AutoRevert.EnableGuard");
        EnableGuardDescText.Text = loc.Get("Esports.AutoRevert.EnableGuardDesc");
        EnableGuardButton.Content = loc.Get("Esports.Guard.Enable");
        ManualFixText.Text = loc.Get("Esports.AutoRevert.ManualFix");
        ManualFixDescText.Text = loc.Get("Esports.AutoRevert.ManualFixDesc");
        CloseBtn.Content = loc.Get("Common.Close");
    }
    
    private void OnLanguageChanged(object? sender, string e)
    {
        Dispatcher.Invoke(UpdateLocalizedTexts);
    }
    
    private void EnableGuardButton_Click(object sender, RoutedEventArgs e)
    {
        _guardEnabled = true;
        
        var loc = LocalizationService.Instance;
        MessageBox.Show(
            this,
            loc.Get("Esports.AutoRevert.GuardEnabled"),
            loc.Get("Common.Success"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        
        DialogResult = true;
        Close();
    }
    
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
    
    protected override void OnClosed(EventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        base.OnClosed(e);
    }
}
