using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Models; // P3-030: 使用统一的CpuVendor枚举

namespace GameOptimizer.Core.Services.CpuAffinity
{
    // P3-030: CpuVendor枚举已移至GameOptimizer.Core.Models.CpuTopology

    public enum CpuCoreType
    {
        Unknown,
        Standard,         // 标准核心（非混合架构）
        PerformanceCore,  // P-Core (Intel 12th gen+)
        EfficiencyCore,   // E-Core (Intel)
        CacheCore,        // 3D V-Cache核心 (AMD 7000X3D)
        ComputeCore       // 非3D缓存核心 (AMD 7000X3D)
    }

    public class CpuInfo
    {
        public string Name { get; set; } = string.Empty;
        public int PhysicalCores { get; set; }
        public int LogicalProcessors { get; set; }
        public List<CpuThread> Threads { get; set; } = new();
        public bool HasHybridArchitecture { get; set; }
        public CpuVendor Vendor { get; set; } = CpuVendor.Unknown;
        public bool Has3DCache { get; set; }
        public int CacheCoreCount { get; set; } // 3D缓存核心数量
    }

    public class CpuThread : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        
        public int Index { get; set; }
        public CpuCoreType CoreType { get; set; } = CpuCoreType.Unknown;
        public int PhysicalCoreId { get; set; }
        
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public string CoreTypeDisplay => CoreType switch
        {
            CpuCoreType.Standard => "",
            CpuCoreType.PerformanceCore => LocalizationService.Instance.Get("CpuAffinity.PCore"),
            CpuCoreType.EfficiencyCore => LocalizationService.Instance.Get("CpuAffinity.ECore"),
            CpuCoreType.CacheCore => "3D",
            CpuCoreType.ComputeCore => LocalizationService.Instance.Get("CpuAffinity.ComputeCore"),
            _ => ""
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class AffinityDevice
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DeviceType Type { get; set; }
        public long AffinityMask { get; set; }
        public string RegistryPath { get; set; } = string.Empty;
        
        public string TypeIcon => Type switch
        {
            DeviceType.Gpu => "🎮",
            DeviceType.Mouse => "🖱️",
            DeviceType.Keyboard => "⌨️",
            DeviceType.NetworkAdapter => "🌐",
            DeviceType.UsbController => "🔌",
            DeviceType.AudioDevice => "🔊",
            _ => "📱"
        };
    }

    public enum DeviceType
    {
        Gpu,
        Mouse,
        Keyboard,
        NetworkAdapter,
        UsbController,
        AudioDevice,
        Other
    }

    public class AffinityPreset
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public long AffinityMask { get; set; }
        public bool UsePCoresOnly { get; set; }
        public bool UseECoresOnly { get; set; }
    }

    public enum RecommendationPriority
    {
        Low,
        Medium,
        High
    }

    public class AffinityRecommendation
    {
        public AffinityDevice Device { get; set; } = new();
        public List<int> RecommendedThreads { get; set; } = new();
        public long AffinityMask { get; set; }
        public string Reason { get; set; } = string.Empty;
        public RecommendationPriority Priority { get; set; }
        public bool IsApplied { get; set; }
    }
}
