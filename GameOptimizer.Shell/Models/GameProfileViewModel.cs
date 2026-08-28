using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Services;

namespace GameOptimizer.Shell.Models;

public class GameProfileViewModel
{
    private readonly GameProfile _profile;
    private readonly bool _isEnglish;

    public GameProfileViewModel(GameProfile profile)
    {
        _profile = profile;
        _isEnglish = LocalizationService.Instance.CurrentLanguage == "en-US";
    }

    public string Id => _profile.Id;
    public string GameName => _profile.GameName;
    public string ExecutablePath => _profile.ExecutablePath;
    public GameType GameType => _profile.GameType;
    public bool AutoOptimizeOnLaunch => _profile.AutoOptimizeOnLaunch;
    public bool RestoreOnExit => _profile.RestoreOnExit;
    public int OptimizationCount => _profile.EnabledOperationIds.Count;

    public string GameTypeDisplay => GameTypeDetector.GetTypeDisplayName(_profile.GameType, _isEnglish);

    public bool HasOptimizations => _profile.EnabledOperationIds.Count > 0;

    public string OptimizedText => _isEnglish ? "Optimized" : "已优化";

    public string OptimizationCountText => _isEnglish 
        ? $"{_profile.EnabledOperationIds.Count} optimizations" 
        : $"{_profile.EnabledOperationIds.Count} 项优化";

    public string OptimizationSummary
    {
        get
        {
            if (_profile.EnabledOperationIds.Count == 0)
                return _isEnglish ? "No optimizations configured" : "未配置优化项";
            
            return GameOptimizationPresets.GetPresetDescription(_profile.GameType, _isEnglish);
        }
    }

    // Why: DataTemplate 内的按钮无法通过 FindName 访问，必须通过 ViewModel 绑定本地化文本
    public string LaunchGameText => LocalizationService.Instance.Get("GameProfiles.LaunchGame");
    public string ConfigureText => LocalizationService.Instance.Get("GameProfiles.Configure");
    public string DeleteText => LocalizationService.Instance.Get("GameProfiles.Delete");

    public GameProfile GetProfile() => _profile;
}
