using System;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Services;
using GameOptimizer.Shell.Models;

namespace GameOptimizer.Shell.Services;

public sealed class HardwareDetectionService : IHardwareDetectionService
{
    private HardwareInfo? _cachedInfo;
    private readonly IGpuInventoryService _gpuInventory;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private DateTime _lastDetection = DateTime.MinValue;
    private static readonly TimeSpan CacheExpiry = TimeSpan.FromMinutes(5);

    public HardwareDetectionService(IGpuInventoryService gpuInventory)
    {
        _gpuInventory = gpuInventory;
    }

    public async Task<HardwareInfo> GetOrDetectAsync()
    {
        if (_cachedInfo != null && DateTime.Now - _lastDetection < CacheExpiry)
            return _cachedInfo;

        await _lock.WaitAsync();
        try
        {
            if (_cachedInfo != null && DateTime.Now - _lastDetection < CacheExpiry)
                return _cachedInfo;

            _cachedInfo = await Task.Run(() => HardwareInfo.Detect(_gpuInventory));
            _lastDetection = DateTime.Now;
            return _cachedInfo;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void InvalidateCache()
    {
        _cachedInfo = null;
        _lastDetection = DateTime.MinValue;
        _gpuInventory.InvalidateCache();
    }
}
