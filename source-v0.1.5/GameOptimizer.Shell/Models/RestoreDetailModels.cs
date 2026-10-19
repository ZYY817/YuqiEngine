// File: GameOptimizer.Shell/Models/RestoreDetailModels.cs
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GameOptimizer.Shell.Models;

/// <summary>
/// 单个优化项的详细信息
/// </summary>
public class RestoreDetailItem : INotifyPropertyChanged
{
    private bool _isVisible = true;

    public string OperationId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public bool IsApplied { get; set; }
    public bool WillBeRestored { get; set; }

    /// <summary>
    /// 用于搜索过滤时的可见性控制
    /// </summary>
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible != value)
            {
                _isVisible = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// 按模块分组的优化项集合
/// </summary>
public class RestoreDetailGroup : INotifyPropertyChanged
{
    private bool _isExpanded = true;
    private bool _isVisible = true;

    public string ModuleKey { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public int AppliedCount { get; set; }
    public int TotalCount { get; set; }
    public ObservableCollection<RestoreDetailItem> Items { get; set; } = new();

    /// <summary>
    /// 是否展开（用于 Expander 控件）
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 用于搜索过滤时的可见性控制
    /// </summary>
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible != value)
            {
                _isVisible = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 显示的统计信息（如 "12 项已应用"）
    /// </summary>
    public string AppliedCountText => AppliedCount > 0 
        ? $"{AppliedCount} 项已应用" 
        : "未应用";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void UpdateAppliedCountText()
    {
        OnPropertyChanged(nameof(AppliedCountText));
    }
}
