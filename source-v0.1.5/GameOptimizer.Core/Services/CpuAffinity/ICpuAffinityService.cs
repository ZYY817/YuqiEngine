using System.Collections.Generic;
using System.Threading.Tasks;

namespace GameOptimizer.Core.Services.CpuAffinity
{
    public interface ICpuAffinityService
    {
        CpuInfo GetCpuInfo();
        Task<IReadOnlyList<AffinityDevice>> GetDevicesAsync();
        Task<bool> SetDeviceAffinityAsync(string deviceId, long affinityMask);
        Task<long> GetDeviceAffinityAsync(string deviceId);
        Task<bool> ResetDeviceAffinityAsync(string deviceId);
        IReadOnlyList<AffinityPreset> GetPresets();
        long CalculateAffinityMask(IEnumerable<int> selectedThreads);
        IEnumerable<int> GetThreadsFromMask(long affinityMask);
        AffinityRecommendation GetSmartRecommendation(AffinityDevice device);
        IReadOnlyList<AffinityRecommendation> GetAllRecommendations(IEnumerable<AffinityDevice> devices);
    }
}
