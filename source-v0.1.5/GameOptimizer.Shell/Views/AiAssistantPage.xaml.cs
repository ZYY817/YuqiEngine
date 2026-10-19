// File: GameOptimizer.Shell/Views/AiAssistantPage.xaml.cs
using System.Windows.Controls;
using System.Windows.Input;
using GameOptimizer.Shell.Navigation;
using GameOptimizer.Shell.ViewModels;

namespace GameOptimizer.Shell.Views;

public partial class AiAssistantPage : UserControl
{
    private readonly AiAssistantViewModel _viewModel;

    public AiAssistantPage(AiAssistantViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        _viewModel.ChatEntries.CollectionChanged += (_, _) => ScrollToEnd();
        _viewModel.RequestOpenSettings += OnOpenSettings;
        _viewModel.RequestNavigateToModule += OnNavigateToModule;
    }

    private void OnOpenSettings()
    {
        var window = System.Windows.Window.GetWindow(this);
        if (window is not null)
            AiSettingsDialog.Show(window, _viewModel);
    }

    private void OnNavigateToModule(string moduleKey)
    {
        // moduleKey 可能是路由 key（memory-optimization）或模块 key（system/explorer/tasks/services）
        if (System.Windows.Window.GetWindow(this) is MainWindow window)
        {
            var catalog = (App.Current as App)?.Services?.GetService(typeof(INavigationCatalog)) as INavigationCatalog;
            var route = catalog?.Resolve(moduleKey)
                        ?? catalog?.Routes.FirstOrDefault(r =>
                            string.Equals(r.ModuleKey, moduleKey, StringComparison.OrdinalIgnoreCase));
            if (route is not null)
                window.NavigateToPage(route.Key);
        }
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            _viewModel.SendCommand.Execute(null);
        }
    }

    private void ScrollToEnd()
    {
        if (_viewModel.ChatEntries.Count > 0)
            ChatScroll.ScrollToEnd();
    }
}
