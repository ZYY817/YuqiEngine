using System.Threading.Tasks;
using GameOptimizer.Shell.Models;

namespace GameOptimizer.Shell.Services;

public interface IHardwareDetectionService
{
    Task<HardwareInfo> GetOrDetectAsync();
    void InvalidateCache();
}
