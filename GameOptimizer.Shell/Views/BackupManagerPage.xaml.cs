using System;
using System.Windows;
using System.Windows.Controls;
using GameOptimizer.Shell.Services;
using GameOptimizer.Shell.ViewModels;

namespace GameOptimizer.Shell.Views;

public partial class BackupManagerPage : UserControl
{
    private bool _isInitialized;

    public BackupManagerViewModel? ViewModel { get; }

    public bool IsCompositionReady => ViewModel is not null;

    public string CompositionStatusText =>
        "备份页组件未完成装配，无法安全初始化。请重新启动应用；未创建服务、未查找容器，也不会执行任何备份或恢复操作。";

    public BackupManagerPage()
    {
        InitializeComponent();
        AttachLifecycleHandlers();
        PageDiagnosticService.Log("BackupManagerPage", "Constructor called without ViewModel");
    }

    public BackupManagerPage(BackupManagerViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        AttachLifecycleHandlers();
        PageDiagnosticService.Log("BackupManagerPage", "Constructor called with injected ViewModel");
    }

    private void AttachLifecycleHandlers()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!IsCompositionReady)
        {
            PageDiagnosticService.LogLoaded("BackupManagerPage", false, "BackupManagerViewModel was not injected into this page instance");
            return;
        }

        if (_isInitialized)
        {
            ViewModel!.RefreshSnapshots();
            return;
        }

        _isInitialized = true;
        ViewModel!.Initialize();
        PageDiagnosticService.LogLoaded("BackupManagerPage", true, "Injected ViewModel initialized");
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // The ViewModel lifetime belongs to Shell composition; page unload must not dispose it.
    }
}
