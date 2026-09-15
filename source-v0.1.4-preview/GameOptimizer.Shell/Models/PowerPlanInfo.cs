using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using GameOptimizer.Shell.Constants;

namespace GameOptimizer.Shell.Models;

public class PowerPlanInfo
{
    // Windows 内置电源计划 GUID（不可删除）
    private static readonly HashSet<string> BuiltInPlanGuids = new(StringComparer.OrdinalIgnoreCase)
    {
        "381b4222-f694-41f0-9685-ff5bb260df2e", // 平衡
        "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", // 高性能
        "a1841308-3541-4fab-bc81-f71556f20b4a", // 节能
        "e9a42b02-d5df-448d-aa00-03f14749eb61", // 卓越性能
    };

    // 无参构造函数
    public PowerPlanInfo()
    {
        UpdateIconProperties();
    }

    // 带参构造函数（用于PowerPlanEditorService）
    public PowerPlanInfo(Guid guid, string name, bool isActive)
    {
        Guid = guid.ToString("D");
        Name = name;
        IsActive = isActive;
        UpdateIconProperties();
    }

    public string Guid { get; set; } = "";
    
    private string _name = "";
    public string Name 
    { 
        get => _name;
        set
        {
            _name = value;
            UpdateIconProperties();
        }
    }
    
    public string Description { get; set; } = "";
    public bool IsActive { get; set; }
    public Visibility IsActiveVisibility => IsActive ? Visibility.Visible : Visibility.Collapsed;
    public bool IsManaged { get; set; }
    public string PlanKind => IsManaged ? "宇奇受管" : "Windows / OEM（只读）";
    public Visibility ManagedActionsVisibility => IsManaged ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CloneActionVisibility => IsManaged ? Visibility.Collapsed : Visibility.Visible;
    
    // 是否可以删除（内置计划和当前激活的计划不能删除）
    public bool CanDelete => !IsActive && !BuiltInPlanGuids.Contains(Guid);
    public Visibility DeleteButtonVisibility => CanDelete ? Visibility.Visible : Visibility.Collapsed;
    
    // 新增：用于智能选择源计划
    public int QualityScore { get; set; }
    public string Source { get; set; } = ""; // "Standard", "Custom", "ThirdParty"
    
    // 新增：图标相关属性
    public string IconData { get; private set; } = NavigationIconData.Power;
    public Color IconBackgroundStart { get; private set; } = Color.FromArgb(0x15, ThemeColors.Cyan.R, ThemeColors.Cyan.G, ThemeColors.Cyan.B);
    public Color IconBackgroundEnd { get; private set; } = Color.FromArgb(0x08, ThemeColors.Cyan.R, ThemeColors.Cyan.G, ThemeColors.Cyan.B);
    public SolidColorBrush IconColor { get; private set; } = ThemeColors.CyanBrush;
    
    private void UpdateIconProperties()
    {
        var lower = Name.ToLowerInvariant();
        
        if (lower.Contains("high performance") || lower.Contains("高性能") || lower.Contains("ultimate") || lower.Contains("卓越"))
        {
            // 高性能：金色/橙色
            IconData = NavigationIconData.Power;
            IconBackgroundStart = Color.FromArgb(0x15, ThemeColors.Warning.R, ThemeColors.Warning.G, ThemeColors.Warning.B);
            IconBackgroundEnd = Color.FromArgb(0x08, ThemeColors.Warning.R, ThemeColors.Warning.G, ThemeColors.Warning.B);
            IconColor = ThemeColors.WarningBrush;
        }
        else if (lower.Contains("power saver") || lower.Contains("节能") || lower.Contains("省电"))
        {
            // 节能：绿色
            IconData = NavigationIconData.Power;
            IconBackgroundStart = Color.FromArgb(0x15, ThemeColors.Success.R, ThemeColors.Success.G, ThemeColors.Success.B);
            IconBackgroundEnd = Color.FromArgb(0x08, ThemeColors.Success.R, ThemeColors.Success.G, ThemeColors.Success.B);
            IconColor = ThemeColors.SuccessBrush;
        }
        else if (lower.Contains("balanced") || lower.Contains("平衡"))
        {
            // 平衡：紫色
            IconData = NavigationIconData.Power;
            IconBackgroundStart = Color.FromArgb(0x15, ThemeColors.Purple.R, ThemeColors.Purple.G, ThemeColors.Purple.B);
            IconBackgroundEnd = Color.FromArgb(0x08, ThemeColors.Purple.R, ThemeColors.Purple.G, ThemeColors.Purple.B);
            IconColor = ThemeColors.PurpleBrush;
        }
        else
        {
            // 自定义：青色
            IconData = NavigationIconData.Settings;
            IconBackgroundStart = Color.FromArgb(0x15, ThemeColors.Cyan.R, ThemeColors.Cyan.G, ThemeColors.Cyan.B);
            IconBackgroundEnd = Color.FromArgb(0x08, ThemeColors.Cyan.R, ThemeColors.Cyan.G, ThemeColors.Cyan.B);
            IconColor = ThemeColors.CyanBrush;
        }
    }
}
