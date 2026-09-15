// File: GameOptimizer.Shell/Models/RevertProgressModels.cs
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GameOptimizer.Shell.Models;

public enum RestoreOperationType
{
    RevertOptimizations,
    RestoreWindowsDefaults
}

public class RevertProgressInfo : INotifyPropertyChanged
{
    private string _currentModuleName = string.Empty;
    private int _completedCount;
    private int _totalCount;
    private double _progressPercentage;

    public string CurrentModuleName
    {
        get => _currentModuleName;
        set { _currentModuleName = value; OnPropertyChanged(); }
    }

    public int CompletedCount
    {
        get => _completedCount;
        set { _completedCount = value; OnPropertyChanged(); }
    }

    public int TotalCount
    {
        get => _totalCount;
        set { _totalCount = value; OnPropertyChanged(); }
    }

    public double ProgressPercentage
    {
        get => _progressPercentage;
        set { _progressPercentage = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class RevertResultGroup
{
    public string ModuleName { get; set; } = string.Empty;
    public List<RevertResultItem> Items { get; set; } = new();
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public bool IsExpanded { get; set; } = true;
    public bool IsVisible { get; set; } = true;
    
    public string StatusText => FailedCount > 0 
        ? $"成功 {SuccessCount} 项，失败 {FailedCount} 项" 
        : $"成功 {SuccessCount} 项";
}

public class RevertResultItem
{
    public string Title { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsVisible { get; set; } = true;
}
