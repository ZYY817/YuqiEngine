// File: GameOptimizer.Shell/Models/SelectableCoreInfo.cs
using CommunityToolkit.Mvvm.ComponentModel;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Models;

namespace GameOptimizer.Shell.Models;

public partial class SelectableCoreInfo : ObservableObject
{
    private readonly CoreInfo _coreInfo;
    
    [ObservableProperty]
    private bool _isSelected;
    
    public int Index => _coreInfo.Index;
    public int PhysicalIndex => _coreInfo.PhysicalIndex;
    public CoreType Type => _coreInfo.Type;
    public int CcdIndex => _coreInfo.CcdIndex;
    public bool HasVCache => _coreInfo.HasVCache;
    public bool IsEnabled => _coreInfo.IsEnabled;
    public string DisplayName => _coreInfo.DisplayName;
    public string ShortName => _coreInfo.ShortName;
    public string TypeDescription => _coreInfo.TypeDescription;
    
    public string CcdLabel => HasVCache ? $"CCD {CcdIndex} (V-Cache)" : $"CCD {CcdIndex}";
    
    public string CoreLabel => Type switch
    {
        CoreType.PerformanceCore => "P-Core",
        CoreType.EfficiencyCore => "E-Core",
        CoreType.Smt => "SMT",
        _ => $"CCD {CcdIndex}"
    };
    
    public string TypeName
    {
        get
        {
            var loc = LocalizationService.Instance;
            return Type switch
            {
                CoreType.PerformanceCore => loc.Get("CoreAccel.TypePerformance"),
                CoreType.EfficiencyCore => loc.Get("CoreAccel.TypeEfficiency"),
                CoreType.Smt => loc.Get("CoreAccel.TypeSmt"),
                CoreType.Physical => loc.Get("CoreAccel.TypePhysical"),
                _ => loc.Get("CoreAccel.TypeCore")
            };
        }
    }
    
    public SelectableCoreInfo(CoreInfo coreInfo)
    {
        _coreInfo = coreInfo;
    }
}
