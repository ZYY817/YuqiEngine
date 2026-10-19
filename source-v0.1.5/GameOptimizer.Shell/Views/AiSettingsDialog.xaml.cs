// File: GameOptimizer.Shell/Views/AiSettingsDialog.xaml.cs
using System.Windows;
using System.Windows.Input;
using GameOptimizer.Shell.ViewModels;

namespace GameOptimizer.Shell.Views;

public partial class AiSettingsDialog : Window
{
    private readonly AiAssistantViewModel _viewModel;

    public AiSettingsDialog(AiAssistantViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        Loaded += (_, _) =>
        {
            ApiKeyBox.Password = _viewModel.ApiKey;
            _viewModel.LoadSkillFile();
        };
    }

    public static void Show(Window owner, AiAssistantViewModel viewModel)
    {
        var dialog = new AiSettingsDialog(viewModel) { Owner = owner };
        dialog.ShowDialog();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e) =>
        _viewModel.ApiKey = ApiKeyBox.Password;

    private void SaveConfig_Click(object sender, RoutedEventArgs e) =>
        _viewModel.SaveConfigCommand.Execute(null);
}
