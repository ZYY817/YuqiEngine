using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Models;
using GameOptimizer.Core.Services;
using GameOptimizer.ModuleContracts.Abstractions;
using GameOptimizer.ModuleContracts.Enums;
using GameOptimizer.ModuleContracts.Models;
using Microsoft.Extensions.DependencyInjection;
using GameOptimizer.Modules.PerformanceHealth.ViewModels;
using GameOptimizer.Modules.PerformanceHealth.Views;

namespace GameOptimizer.Modules.PerformanceHealth;

public sealed class PerformanceHealthModule : IModule
{
    public string Id => "module.performance-health";
    public string DisplayName => LocalizationService.Instance.Get("Module.PerformanceHealth.Name");
    public Version Version => new(0, 1, 0);
    public ModuleScope Scope => ModuleScope.ShellCard;

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddTransient<PerformanceHealthViewModel>();
    }

    public Task InitializeAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        // P3-008: 模块初始化时可以预热缓存或订阅遥测
        System.Diagnostics.Debug.WriteLine("[PerformanceHealthModule] Initialized");
        return Task.CompletedTask;
    }

    public IEnumerable<IModuleWidget> CreateWidgets(IServiceProvider services) =>
        new[] { new PerformanceHealthWidget() };

    public IEnumerable<IModuleAction> CreateActions(IServiceProvider services) =>
        new[] { new QuickScanAction(services.GetRequiredService<ISystemInsightService>()) };

    private sealed class PerformanceHealthWidget : IModuleWidget
    {
        public string Id => "widget.performance-health.card";

        public WidgetDescriptor Descriptor => new(
            Title: LocalizationService.Instance.Get("Module.PerformanceHealth.Widget.Title"),
            Subtitle: LocalizationService.Instance.Get("Module.PerformanceHealth.Widget.Subtitle"),
            ColumnSpan: 2,
            RowSpan: 1,
            AllowResize: true,
            AllowReorder: true);

        public object BuildContent(IServiceProvider services) =>
            ActivatorUtilities.CreateInstance<PerformanceHealthCard>(services);
    }

    private sealed class QuickScanAction : IModuleAction
    {
        private readonly ISystemInsightService _insightService;

        public QuickScanAction(ISystemInsightService insightService)
        {
            _insightService = insightService;
        }

        public string Id => "action.performance-health.quickscan";

        public ActionDescriptor Descriptor => new(
            Label: LocalizationService.Instance.Get("Module.PerformanceHealth.Action.QuickScan"),
            IconKey: "HealthPulse",
            Category: LocalizationService.Instance.Get("Module.PerformanceHealth.Category"),
            Description: LocalizationService.Instance.Get("Module.PerformanceHealth.Action.QuickScanDesc"));

        public async Task ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
        {
            var snapshot = _insightService.GetCurrentSnapshot();
            var loc = LocalizationService.Instance;
            var recommendation = new OptimizationRecommendation(
                Title: loc.Get("Module.PerformanceHealth.ScoreRefreshed"),
                Description: loc.Get("Module.PerformanceHealth.CpuGpuStatus", snapshot.CpuUsage, snapshot.GpuUsage),
                Impact: loc.Get("Module.PerformanceHealth.LiveFeedback"),
                RequiresRestart: false);

            await context.PublishAsync(recommendation, cancellationToken).ConfigureAwait(false);
        }
    }
}
