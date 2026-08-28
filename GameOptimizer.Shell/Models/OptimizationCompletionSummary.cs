// File: GameOptimizer.Shell/Models/OptimizationCompletionSummary.cs
using System.Collections.Generic;
using System.ComponentModel;

namespace GameOptimizer.Shell.Models;

public class OptimizationCompletionSummary
{
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public int SkippedHighRiskCount { get; set; } = 3;
    
    public List<OptimizedModuleSummary> OptimizedModules { get; set; } = new();
    public List<HighRiskModuleInfo> HighRiskModules { get; set; } = new();
}

public class OptimizedModuleSummary
{
    public string Icon { get; set; } = "";
    public string Name { get; set; } = "";
    public int Count { get; set; }
    public bool IsSuccess { get; set; }
}

public class HighRiskModuleInfo : INotifyPropertyChanged
{
    private bool _isVisible = true;
    
    public string Type { get; set; } = "";
    public string Icon { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string SuitableFor { get; set; } = "";
    public string NavigationTarget { get; set; } = "";
    
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible != value)
            {
                _isVisible = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible)));
            }
        }
    }
    
    public event PropertyChangedEventHandler? PropertyChanged;
}
