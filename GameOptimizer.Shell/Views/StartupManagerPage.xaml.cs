using System;
using System.Windows;
using System.Windows.Controls;
using GameOptimizer.Core.Services.Startup;
using GameOptimizer.Shell.Constants;
using GameOptimizer.Shell.ViewModels;

namespace GameOptimizer.Shell.Views
{
    public partial class StartupManagerPage : UserControl
    {
        public StartupManagerPage(
            IStartupService startupService,
            IStartupEntryOwner startupOwner)
        {
            InitializeComponent();
            try
            {
                var viewModel = new StartupManagerViewModel(
                    startupService ?? throw new ArgumentNullException(nameof(startupService)),
                    startupOwner ?? throw new ArgumentNullException(nameof(startupOwner)),
                    plan =>
                    ConfirmDialog.Show(
                        Window.GetWindow(this),
                        "确认启动项变更",
                        $"目标：YuqiEngine 自有启动项\n动作：{plan.Action}\n风险：{plan.Risk}\n计划：{plan.PlanId:D}\n\n仅此不可变计划可被执行。",
                        IconGlyphs.Question));
                DataContext = viewModel;
                viewModel.Initialize();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StartupManagerPage] Init error: {ex.Message}");
            }
        }
    }
}
