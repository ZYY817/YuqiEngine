using System.Threading.Tasks;

namespace GameOptimizer.Core.Services.AdvancedTuning
{
    public interface IAdvancedTuningService
    {
        int GetSystemResponsiveness();
        Task<TuningResult> SetSystemResponsivenessAsync(int value);
        
        Win32PrioritySeparation GetWin32PrioritySeparation();
        Task<TuningResult> SetWin32PrioritySeparationAsync(Win32PrioritySeparation preset);
        
        int GetSvchostSplitThreshold();
        Task<TuningResult> SetSvchostSplitThresholdAsync(int thresholdKB);
        
        bool Is8Dot3NameDisabled();
        Task<TuningResult> Set8Dot3NameDisabledAsync(bool disabled);
        
        AdvancedTuningState GetCurrentState();
    }

    public enum Win32PrioritySeparation
    {
        Default = 0x02,      // 默认
        Gaming = 0x26,       // 游戏优化 (前台优先)
        Server = 0x18        // 服务器 (后台优先)
    }

    public class AdvancedTuningState
    {
        public int SystemResponsiveness { get; set; }
        public Win32PrioritySeparation PrioritySeparation { get; set; }
        public int SvchostThresholdKB { get; set; }
        public bool Is8Dot3Disabled { get; set; }
    }
}
