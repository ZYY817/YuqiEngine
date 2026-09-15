using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace GameOptimizer.Shell.Models;

public class OptimizationModuleDefinition : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public OptimizationModuleDefinition(
        string key,
        string title,
        string subtitle,
        string iconData,
        IEnumerable<OptimizationToggleModel>? toggles = null)
    {
        Key = key;
        Title = title;
        Subtitle = subtitle;
        IconData = iconData;
        TitleKey = $"ModulePage.{GetModuleKeyForLocalization(key)}.Title";
        Toggles = new ObservableCollection<OptimizationToggleModel>(
            toggles ?? new List<OptimizationToggleModel>());
        
        // 监听每个Toggle的属性变化
        foreach (var toggle in Toggles)
        {
            toggle.PropertyChanged += OnTogglePropertyChanged;
        }
        
        Toggles.CollectionChanged += (s, e) =>
        {
            if (e.NewItems != null)
            {
                foreach (OptimizationToggleModel toggle in e.NewItems)
                {
                    toggle.PropertyChanged += OnTogglePropertyChanged;
                }
            }
            if (e.OldItems != null)
            {
                foreach (OptimizationToggleModel toggle in e.OldItems)
                {
                    toggle.PropertyChanged -= OnTogglePropertyChanged;
                }
            }
            NotifyCountChanged();
        };
    }
    
    private static string GetModuleKeyForLocalization(string key)
    {
        return key switch
        {
            "audio" => "Audio",
            "services" => "Services",
            "taskscheduler" => "TaskScheduler",
            "systemtuning" => "SystemTuning",
            "privacy" => "Privacy",
            "peripheral" => "Peripheral",
            "network" => "Network",
            "cpu" => "Cpu",
            "graphics" => "Graphics",
            "explorer" => "Explorer",
            "memory" => "Memory",
            "performance" => "Performance",
            _ => key
        };
    }

    private void OnTogglePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OptimizationToggleModel.IsApplied))
        {
            NotifyCountChanged();
        }
    }

    private void NotifyCountChanged()
    {
        OnPropertyChanged(nameof(AppliedCount));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(ProgressWidth));
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public string Key { get; }

    public string Title { get; }
    
    public string TitleKey { get; }

    public string Subtitle { get; }

    public string IconData { get; }

    public ObservableCollection<OptimizationToggleModel> Toggles { get; }

    public bool SupportsExecution { get; set; }

    // 已优化数量
    public int AppliedCount => Toggles.Count(t => t.IsApplied);

    // 模块中实际注册的优化项数量，与对应功能页面共用同一份操作集合。
    public int TotalCount => Toggles.Count;

    // 进度条宽度 (用于绑定)
    public double ProgressWidth => Toggles.Count == 0 ? 0 : (double)AppliedCount / Toggles.Count * 100;
}
