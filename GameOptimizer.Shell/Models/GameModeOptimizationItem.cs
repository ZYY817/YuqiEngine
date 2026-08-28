using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using GameOptimizer.Core.Optimization;
using GameOptimizer.Shell.Constants;

namespace GameOptimizer.Shell.Models;

public partial class GameModeOptimizationItem : ObservableObject
{
    public GameModeOptimizationItem(
        string operationId,
        string category,
        OperationRiskLevel riskLevel = OperationRiskLevel.Balanced,
        string? expectedEffect = null,
        string? hardwareHint = null)
    {
        OperationId = operationId;
        Category = category;
        RiskLevel = riskLevel;
        ExpectedEffect = expectedEffect;
        HardwareHint = hardwareHint;
        RefreshLocalizedTexts();
    }

    public string OperationId { get; }
    public string Category { get; }
    
    [ObservableProperty]
    private string title = "";
    
    [ObservableProperty]
    private string description = "";
    
    public void RefreshLocalizedTexts()
    {
        var loc = GameOptimizer.Core.Localization.LocalizationService.Instance;
        var trans = GameOptimizer.Core.Localization.OptimizationTranslations.Instance;
        bool isEnglish = loc.CurrentLanguage == "en-US";
        Title = trans.GetTitle(OperationId);
        Description = trans.GetDescription(OperationId, "", isEnglish);
    }
    public OperationRiskLevel RiskLevel { get; }
    public string? ExpectedEffect { get; }

    [ObservableProperty]
    private string? hardwareHint;

    [ObservableProperty]
    private Brush? hardwareHintBackground;

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private bool isApplied;
    
    [ObservableProperty]
    private bool isCompatible = true;
    
    [ObservableProperty]
    private string? incompatibleReason;

    partial void OnIsSelectedChanging(bool value)
    {
        // 不兼容项禁止选择
        if (value && !IsCompatible)
        {
            throw new InvalidOperationException($"此优化项与当前硬件不兼容: {IncompatibleReason}");
        }
    }

    public string RiskLabel
    {
        get
        {
            var loc = GameOptimizer.Core.Localization.LocalizationService.Instance;
            return RiskLevel switch
            {
                OperationRiskLevel.Safe => loc.Get("Module.RiskSafe"),
                OperationRiskLevel.Balanced => loc.Get("Module.RiskBalanced"),
                OperationRiskLevel.Extreme => loc.Get("Module.RiskExtreme"),
                _ => "Unknown"
            };
        }
    }

    public Brush RiskBackground => RiskLevel switch
    {
        OperationRiskLevel.Safe => ThemeColors.EmeraldBrush,
        OperationRiskLevel.Balanced => ThemeColors.WarningBrush,
        OperationRiskLevel.Extreme => ThemeColors.DangerBrush,
        _ => ThemeColors.MutedPurpleBrush
    };

    public Brush SelectionBorderBrush => IsSelected
        ? new SolidColorBrush(Color.FromRgb(0x4D, 0x66, 0xFF))
        : new SolidColorBrush(Color.FromRgb(0x1E, 0x2A, 0x42));

    public Brush SelectionBackground => IsSelected
        ? new SolidColorBrush(Color.FromRgb(0x1A, 0x25, 0x40))
        : new SolidColorBrush(Color.FromRgb(0x14, 0x1E, 0x30));

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(SelectionBorderBrush));
        OnPropertyChanged(nameof(SelectionBackground));
    }
}
