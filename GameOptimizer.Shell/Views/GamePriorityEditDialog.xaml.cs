// File: GameOptimizer.Shell/Views/GamePriorityEditDialog.xaml.cs
using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Constants;
using GameOptimizer.Shell.Models;
using Microsoft.Win32;

namespace GameOptimizer.Shell.Views;

public partial class GamePriorityEditDialog : Window
{
    private readonly GamePriorityProfile _profile;
    private readonly bool _isNewProfile;

    public bool IsSaved { get; private set; }
    public GamePriorityProfile Profile => _profile;
    
    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }
    
    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    public GamePriorityEditDialog(GamePriorityProfile? profile = null)
    {
        InitializeComponent();
        KeyDown += Window_KeyDown;
        
        _isNewProfile = profile == null;
        _profile = profile ?? new GamePriorityProfile();
        
        InitializePriorityCombo();
        LoadProfileData();
        UpdateLocalizedTexts();
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
    }
    
    private void OnLanguageChanged(object? sender, string e)
    {
        _ = Dispatcher.InvokeAsync(() => { InitializePriorityCombo(); UpdateLocalizedTexts(); });
    }
    
    protected override void OnClosed(System.EventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        base.OnClosed(e);
    }

    private void InitializePriorityCombo()
    {
        var loc = LocalizationService.Instance;
        var priorities = new[]
        {
            new { Value = ProcessPriorityClass.Idle, Display = loc.Get("GamePriority.PriorityIdle") },
            new { Value = ProcessPriorityClass.BelowNormal, Display = loc.Get("GamePriority.PriorityBelowNormal") },
            new { Value = ProcessPriorityClass.Normal, Display = loc.Get("GamePriority.PriorityNormal") },
            new { Value = ProcessPriorityClass.AboveNormal, Display = loc.Get("GamePriority.PriorityAboveNormal") },
            new { Value = ProcessPriorityClass.High, Display = loc.Get("GamePriority.PriorityHigh") },
            new { Value = ProcessPriorityClass.RealTime, Display = loc.Get("GamePriority.PriorityRealTime") }
        };
        
        PriorityCombo.ItemsSource = priorities;
        PriorityCombo.DisplayMemberPath = "Display";
        PriorityCombo.SelectedValuePath = "Value";
        PriorityCombo.SelectedValue = _profile.Priority;
    }

    private void LoadProfileData()
    {
        GameNameBox.Text = _profile.GameName;
        ExePathBox.Text = _profile.ExecutablePath;
        PriorityCombo.SelectedValue = _profile.Priority;
        EnabledCheck.IsChecked = _profile.IsEnabled;
    }
    
    private void UpdateLocalizedTexts()
    {
        var loc = LocalizationService.Instance;
        DialogTitleText.Text = _isNewProfile 
            ? loc.Get("GamePriority.AddTitle") 
            : loc.Get("GamePriority.EditTitle");
        LblGameName.Text = loc.Get("GamePriority.GameName");
        LblExePath.Text = loc.Get("GamePriority.ExePath");
        BrowseBtn.Content = loc.Get("Common.Browse");
        LblPriority.Text = loc.Get("GamePriority.Priority");
        LblPriorityDesc.Text = loc.Get("GamePriority.PriorityDesc");
        LblPriorityInfo.Text = loc.Get("GamePriority.PriorityInfo");
        PriorityInfoText.Text = loc.Get("GamePriority.PriorityInfoDetail");
        LblEnabled.Text = loc.Get("GamePriority.Enabled");
        LblEnabledDesc.Text = loc.Get("GamePriority.EnabledDesc");
        CancelBtn.Content = loc.Get("Common.Cancel");
        SaveBtn.Content = loc.Get("Common.Save");
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationService.Instance;
        var dialog = new OpenFileDialog
        {
            Title = loc.Get("GamePriority.SelectExe"),
            Filter = loc.Get("GamePriority.ExeFilter"),
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            ExePathBox.Text = dialog.FileName;
            
            if (string.IsNullOrWhiteSpace(GameNameBox.Text))
            {
                GameNameBox.Text = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
            }
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("=== [GamePriorityEditDialog] SaveButton_Click START ===");
        var loc = LocalizationService.Instance;
        
        if (string.IsNullOrWhiteSpace(GameNameBox.Text))
        {
            Debug.WriteLine("[GamePriorityEditDialog] Validation failed: GameName is empty");
            InfoDialog.Show(this, loc.Get("Dialog.Info"), 
                loc.Get("GamePriority.EnterName"), Constants.IconGlyphs.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(ExePathBox.Text))
        {
            Debug.WriteLine("[GamePriorityEditDialog] Validation failed: ExePath is empty");
            InfoDialog.Show(this, loc.Get("Dialog.Info"), 
                loc.Get("GamePriority.SelectExeFile"), Constants.IconGlyphs.Warning);
            return;
        }

        if (!System.IO.File.Exists(ExePathBox.Text))
        {
            Debug.WriteLine($"[GamePriorityEditDialog] Validation failed: File not found: {ExePathBox.Text}");
            InfoDialog.Show(this, loc.Get("Dialog.Error"), 
                loc.Get("GamePriority.ExeNotFound"), Constants.IconGlyphs.Warning);
            return;
        }

        _profile.GameName = GameNameBox.Text.Trim();
        _profile.ExecutablePath = ExePathBox.Text.Trim();
        _profile.Priority = (ProcessPriorityClass)(PriorityCombo.SelectedValue ?? ProcessPriorityClass.High);
        _profile.IsEnabled = EnabledCheck.IsChecked == true;

        Debug.WriteLine($"[GamePriorityEditDialog] Profile data prepared:");
        Debug.WriteLine($"  - ID: {_profile.Id}");
        Debug.WriteLine($"  - GameName: {_profile.GameName}");
        Debug.WriteLine($"  - ExecutablePath: {_profile.ExecutablePath}");
        Debug.WriteLine($"  - Priority: {_profile.Priority}");
        Debug.WriteLine($"  - IsEnabled: {_profile.IsEnabled}");

        IsSaved = true;
        DialogResult = true;
        Debug.WriteLine($"[GamePriorityEditDialog] IsSaved={IsSaved}, DialogResult={DialogResult}");
        Debug.WriteLine("=== [GamePriorityEditDialog] SaveButton_Click END ===");
    }
}
