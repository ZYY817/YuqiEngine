// File: GameOptimizer.Shell/Extensions/MemoryModuleExtension.cs
using System;
using System.Windows;
using GameOptimizer.Core.Services.Memory;
using GameOptimizer.Shell.Interfaces;
using GameOptimizer.Shell.ViewModels;
using GameOptimizer.Shell.Views.Controls;

namespace GameOptimizer.Shell.Extensions;

/// <summary>
/// 内存模块扩展 - 为内存优化页面提供专用控件
/// </summary>
/// <remarks>
/// 提供内存监控和清理按钮等内存模块特有功能
/// </remarks>
public sealed class MemoryModuleExtension : IModuleExtension
{
    private readonly IMemoryCleanerService _memoryCleanerService;
    private readonly IMemoryCleaningOwner _memoryCleaningOwner;
    private readonly IProcessCleanerService _processCleanerService;

    public MemoryModuleExtension(
        IMemoryCleanerService memoryCleanerService,
        IMemoryCleaningOwner memoryCleaningOwner,
        IProcessCleanerService processCleanerService)
    {
        _memoryCleanerService = memoryCleanerService ?? throw new ArgumentNullException(nameof(memoryCleanerService));
        _memoryCleaningOwner = memoryCleaningOwner ?? throw new ArgumentNullException(nameof(memoryCleaningOwner));
        _processCleanerService = processCleanerService ?? throw new ArgumentNullException(nameof(processCleanerService));
    }

    public string ModuleKey => "memory";
    
    public UIElement? CreateExtensionContent(DashboardViewModel viewModel)
    {
        if (viewModel == null)
            return null;
        
        try
        {
            // 在UI线程上创建控件
            if (Application.Current?.Dispatcher.CheckAccess() == true)
            {
                return CreatePanel(viewModel);
            }
            
            // 如果不在UI线程，通过Dispatcher创建
            UIElement? result = null;
            Application.Current?.Dispatcher.Invoke(() =>
            {
                result = CreatePanel(viewModel);
            });
            return result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MemoryModuleExtension] Failed to create extension content: {ex.Message}");
            return null;
        }
    }

    private MemoryControlPanel CreatePanel(DashboardViewModel viewModel) => new(
        _memoryCleanerService,
        _memoryCleaningOwner,
        _processCleanerService)
    {
        DataContext = viewModel
    };
}
