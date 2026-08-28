using GameOptimizer.Core.Events;
using GameOptimizer.ModuleContracts.Abstractions;
using Microsoft.Extensions.Logging;

namespace GameOptimizer.Core.Modules;

public sealed class ModuleHost
{
    private readonly IEnumerable<IModule> _modules;
    private readonly IServiceProvider _serviceProvider;
    private readonly IEventBus _eventBus;
    private readonly ILogger<ModuleHost> _logger;
    private readonly List<IModuleWidget> _widgets = new();
    private readonly List<IModuleAction> _actions = new();

    public ModuleHost(IEnumerable<IModule> modules, IServiceProvider serviceProvider, IEventBus eventBus, ILogger<ModuleHost> logger)
    {
        _modules = modules;
        _serviceProvider = serviceProvider;
        _eventBus = eventBus;
        _logger = logger;
    }

    public IReadOnlyList<IModuleWidget> Widgets => _widgets;
    public IReadOnlyList<IModuleAction> Actions => _actions;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        foreach (var module in _modules)
        {
            try
            {
                _logger.LogInformation("Loading module {ModuleName} ({ModuleId})", module.DisplayName, module.Id);
                var context = new ModuleContext(_serviceProvider, _eventBus);
                
                // Why: 防止单个模块 InitializeAsync 挂起阻塞整个启动流程
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));
                
                await module.InitializeAsync(context, timeoutCts.Token).ConfigureAwait(false);
                _widgets.AddRange(module.CreateWidgets(_serviceProvider));
                _actions.AddRange(module.CreateActions(_serviceProvider));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError("[ModuleHost] Module {ModuleName} ({ModuleId}) timed out during initialization (30s)", module.DisplayName, module.Id);
            }
            catch (Exception ex)
            {
                // Why: 单个模块加载失败不应阻断其他模块，记录日志后继续
                _logger.LogError(ex, "[ModuleHost] Failed to load module {ModuleName} ({ModuleId})", module.DisplayName, module.Id);
            }
        }
    }
}
