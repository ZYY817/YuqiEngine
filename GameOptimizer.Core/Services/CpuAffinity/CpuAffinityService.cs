using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using Microsoft.Win32;
using GameOptimizer.Core.Models; // P3-030: 使用统一的CpuVendor枚举

namespace GameOptimizer.Core.Services.CpuAffinity
{
    public class CpuAffinityService : ICpuAffinityService
    {
        private CpuInfo? _cachedCpuInfo;

        public CpuInfo GetCpuInfo()
        {
            if (_cachedCpuInfo != null) return _cachedCpuInfo;

            _cachedCpuInfo = new CpuInfo
            {
                LogicalProcessors = Environment.ProcessorCount
            };

            try
            {
                // 使用更短的超时时间，避免 WMI 查询卡住
                var options = new System.Management.EnumerationOptions
                {
                    Timeout = TimeSpan.FromSeconds(3),
                    ReturnImmediately = true
                };
                
                using var searcher = new ManagementObjectSearcher(
                    new ManagementScope(), 
                    new ObjectQuery("SELECT Name, NumberOfCores, Manufacturer FROM Win32_Processor"),
                    options);
                    
                foreach (var obj in searcher.Get())
                {
                    _cachedCpuInfo.Name = obj["Name"]?.ToString() ?? "Unknown CPU";
                    var cpuManufacturer = obj["Manufacturer"]?.ToString() ?? string.Empty;
                    // Why: WMI 可能返回 null，Convert.ToInt32(null)=0 导致后续线程映射错误
                    var cores = Convert.ToInt32(obj["NumberOfCores"] ?? 0);
                    _cachedCpuInfo.PhysicalCores = cores > 0 ? cores : Math.Max(1, _cachedCpuInfo.LogicalProcessors / 2);
                    _cachedCpuInfo.Vendor = DetectCpuVendor(cpuManufacturer, _cachedCpuInfo.Name);
                    break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CpuAffinityService] WMI 查询失败: {ex.Message}");
                _cachedCpuInfo.Name = "Unknown CPU";
                // Why: LogicalProcessors=1 时整数除法结果为 0，后续线程映射和亲和性掩码计算会出错
                _cachedCpuInfo.PhysicalCores = Math.Max(1, _cachedCpuInfo.LogicalProcessors / 2);
            }

            // Ensure vendor is always resolved, including fallback path.
            if (_cachedCpuInfo.Vendor == CpuVendor.Unknown)
                _cachedCpuInfo.Vendor = DetectCpuVendor(string.Empty, _cachedCpuInfo.Name);
            
            // 检测混合架构和3D缓存
            DetectArchitectureFeatures(_cachedCpuInfo);

            // 生成线程列表
            for (int i = 0; i < _cachedCpuInfo.LogicalProcessors; i++)
            {
                var thread = new CpuThread
                {
                    Index = i,
                    PhysicalCoreId = i / 2,
                    CoreType = DetermineCoreType(_cachedCpuInfo, i)
                };
                _cachedCpuInfo.Threads.Add(thread);
            }

            return _cachedCpuInfo;
        }

        public async Task<IReadOnlyList<AffinityDevice>> GetDevicesAsync()
        {
            var devices = new List<AffinityDevice>();

            await Task.Run(() =>
            {
                // 扫描 GPU
                ScanDevices(devices, "Win32_VideoController", DeviceType.Gpu, "显卡");
                
                // 扫描网卡
                ScanDevices(devices, "Win32_NetworkAdapter", DeviceType.NetworkAdapter, "网卡", 
                    obj => obj["NetEnabled"]?.ToString() == "True");
                
                // 扫描 USB 控制器
                ScanDevices(devices, "Win32_USBController", DeviceType.UsbController, "USB控制器");
            });

            return devices;
        }

        public async Task<bool> SetDeviceAffinityAsync(string deviceId, long affinityMask)
        {
            // FIX-R7-#3: 验证 deviceId 防止注册表路径注入
            if (string.IsNullOrEmpty(deviceId) || !IsValidDeviceId(deviceId))
                return false;

            return await Task.Run(() =>
            {
                try
                {
                    // 通过注册表设置设备的 MSI 模式和亲和性
                    // 设备亲和性存储在 HKLM\SYSTEM\CurrentControlSet\Enum\{DeviceId}\Device Parameters\Interrupt Management\Affinity Policy
                    var devicePath = $@"SYSTEM\CurrentControlSet\Enum\{deviceId}\Device Parameters\Interrupt Management\Affinity Policy";
                    
                    using var key = Registry.LocalMachine.CreateSubKey(devicePath, true);
                    if (key == null) return false;

                    // DevicePolicy: 4 = IrqPolicySpecifiedProcessors (使用指定的处理器)
                    key.SetValue("DevicePolicy", 4, RegistryValueKind.DWord);
                    
                    // AssignmentSetOverride: 亲和性掩码（指定哪些处理器处理中断）
                    // 使用 QWORD 支持超过 32 个处理器
                    key.SetValue("AssignmentSetOverride", affinityMask, RegistryValueKind.QWord);

                    return true;
                }
                catch (UnauthorizedAccessException)
                {
                    // 需要管理员权限
                    return false;
                }
                catch (System.Security.SecurityException)
                {
                    return false;
                }
                catch
                {
                    return false;
                }
            });
        }

        public Task<long> GetDeviceAffinityAsync(string deviceId)
        {
            return Task.Run(() =>
            {
                try
                {
                    if (string.IsNullOrEmpty(deviceId) || !IsValidDeviceId(deviceId))
                    {
                        return GetDefaultAffinityMask();
                    }

                    var devicePath = $@"SYSTEM\CurrentControlSet\Enum\{deviceId}\Device Parameters\Interrupt Management\Affinity Policy";
                    using var key = Registry.LocalMachine.OpenSubKey(devicePath);
                    
                    if (key == null)
                    {
                        // 未设置过亲和性，返回默认全核心
                        return GetDefaultAffinityMask();
                    }

                    var value = key.GetValue("AssignmentSetOverride");
                    // Why: DWORD 返回 int，QWORD 返回 long，都要处理
                    if (value is long longValue)
                    {
                        return longValue;
                    }
                    if (value is int intValue)
                    {
                        return intValue;
                    }
                    if (value is byte[] bytes && bytes.Length >= 8)
                    {
                        return BitConverter.ToInt64(bytes, 0);
                    }

                    return GetDefaultAffinityMask();
                }
                catch
                {
                    return GetDefaultAffinityMask();
                }
            });
        }

        private long GetDefaultAffinityMask()
        {
            var cpuInfo = GetCpuInfo();
            return CreateAffinityMask(cpuInfo.LogicalProcessors);
        }

        private static long CreateAffinityMask(int processorCount)
        {
            // C# 对 long 的移位量按 64 取模；64 或更多逻辑处理器必须显式表示为全位掩码。
            if (processorCount >= 64)
                return -1L;

            // 亲和性策略不能写入空掩码。Environment.ProcessorCount 理论上至少为 1，
            // 仍在异常/模拟拓扑下保持一个可用的保守默认值。
            var boundedCount = Math.Max(1, processorCount);
            return (1L << boundedCount) - 1;
        }

        public async Task<bool> ResetDeviceAffinityAsync(string deviceId)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (string.IsNullOrEmpty(deviceId) || !IsValidDeviceId(deviceId))
                        return false;

                    var devicePath = $@"SYSTEM\CurrentControlSet\Enum\{deviceId}\Device Parameters\Interrupt Management\Affinity Policy";
                    
                    // 删除亲和性设置键
                    using var parentKey = Registry.LocalMachine.OpenSubKey(
                        $@"SYSTEM\CurrentControlSet\Enum\{deviceId}\Device Parameters\Interrupt Management", true);
                    
                    if (parentKey != null)
                    {
                        try
                        {
                            parentKey.DeleteSubKeyTree("Affinity Policy", false);
                        }
                        catch { /* 键不存在，忽略 */ }
                    }

                    return true;
                }
                catch (UnauthorizedAccessException)
                {
                    return false;
                }
                catch (System.Security.SecurityException)
                {
                    return false;
                }
                catch
                {
                    return false;
                }
            });
        }

        public IReadOnlyList<AffinityPreset> GetPresets()
        {
            var cpuInfo = GetCpuInfo();
            var allCoresMask = CreateAffinityMask(cpuInfo.LogicalProcessors);
            var loc = Localization.LocalizationService.Instance;

            var presets = new List<AffinityPreset>
            {
                new() { Name = loc.Get("CpuAffinity.Preset.Default"), Description = loc.Get("CpuAffinity.Preset.Default.Desc"), AffinityMask = allCoresMask },
                new() { Name = loc.Get("CpuAffinity.Preset.SingleCore"), Description = loc.Get("CpuAffinity.Preset.SingleCore.Desc"), AffinityMask = 1 },
                new()
                {
                    Name = loc.Get("CpuAffinity.Preset.HalfCores"),
                    Description = loc.Get("CpuAffinity.Preset.HalfCores.Desc"),
                    AffinityMask = CreateAffinityMask(Math.Max(1, cpuInfo.LogicalProcessors / 2))
                }
            };

            if (cpuInfo.HasHybridArchitecture)
            {
                var pCoreCount = cpuInfo.PhysicalCores / 2;
                presets.Add(new AffinityPreset
                {
                    Name = loc.Get("CpuAffinity.Preset.PCoresOnly"),
                    Description = loc.Get("CpuAffinity.Preset.PCoresOnly.Desc"),
                    AffinityMask = CreateAffinityMask(Math.Min(cpuInfo.LogicalProcessors, Math.Max(1, pCoreCount * 2))),
                    UsePCoresOnly = true
                });
            }

            return presets;
        }

        public AffinityRecommendation GetSmartRecommendation(AffinityDevice device)
        {
            var cpuInfo = GetCpuInfo();
            var recommendation = new AffinityRecommendation { Device = device };

            switch (device.Type)
            {
                case DeviceType.Gpu:
                    recommendation.Priority = RecommendationPriority.High;
                    
                    // AMD 3D缓存CPU：设备中断分配到非3D缓存核心，让游戏独占3D缓存
                    if (cpuInfo.Has3DCache && cpuInfo.HasHybridArchitecture && cpuInfo.Vendor == CpuVendor.AMD)
                    {
                        var cacheThreads = cpuInfo.CacheCoreCount * 2;
                        var computeStart = cacheThreads;
                        var computeCount = Math.Min(4, cpuInfo.LogicalProcessors - computeStart);
                        recommendation.RecommendedThreads = Enumerable.Range(computeStart, computeCount).ToList();
                        recommendation.Reason = "GPU 中断分配到计算核心，让游戏独占 3D 缓存核心";
                    }
                    // Intel混合架构：使用P核心末端
                    else if (cpuInfo.HasHybridArchitecture && cpuInfo.Vendor == CpuVendor.Intel)
                    {
                        var pCoreThreads = cpuInfo.PhysicalCores;
                        var startThread = Math.Max(0, pCoreThreads - 4);
                        recommendation.RecommendedThreads = Enumerable.Range(startThread, Math.Min(4, pCoreThreads - startThread)).ToList();
                        recommendation.Reason = "GPU 中断分配到 P 核心末端，避免与游戏主线程冲突";
                    }
                    // 非混合架构：使用末端核心
                    else
                    {
                        var startThread = Math.Max(0, cpuInfo.LogicalProcessors - 4);
                        recommendation.RecommendedThreads = Enumerable.Range(startThread, cpuInfo.LogicalProcessors - startThread).ToList();
                        recommendation.Reason = "GPU 中断分配到末端核心，减少对主线程的干扰";
                    }
                    break;

                case DeviceType.NetworkAdapter:
                    recommendation.Priority = RecommendationPriority.High;
                    
                    // AMD 3D缓存CPU：网卡也分配到非3D缓存核心
                    if (cpuInfo.Has3DCache && cpuInfo.HasHybridArchitecture && cpuInfo.Vendor == CpuVendor.AMD)
                    {
                        var cacheThreads = cpuInfo.CacheCoreCount * 2;
                        var computeStart = cacheThreads + 4; // 避开GPU使用的核心
                        if (computeStart >= cpuInfo.LogicalProcessors)
                            computeStart = cacheThreads + 2;
                        var computeCount = Math.Min(2, cpuInfo.LogicalProcessors - computeStart);
                        recommendation.RecommendedThreads = Enumerable.Range(computeStart, Math.Max(1, computeCount)).ToList();
                        recommendation.Reason = "网卡中断分配到计算核心，降低延迟且不影响游戏";
                    }
                    // Intel混合架构：使用E核心
                    else if (cpuInfo.HasHybridArchitecture && cpuInfo.Vendor == CpuVendor.Intel)
                    {
                        var eCoreStart = cpuInfo.PhysicalCores;
                        recommendation.RecommendedThreads = Enumerable.Range(eCoreStart, Math.Min(2, cpuInfo.LogicalProcessors - eCoreStart)).ToList();
                        recommendation.Reason = "网卡中断分配到 E 核心，降低延迟且不影响游戏性能";
                    }
                    // 非混合架构：使用核心1-2
                    else
                    {
                        recommendation.RecommendedThreads = new List<int> { 2, 3 };
                        recommendation.Reason = "网卡中断分配到独立核心，降低网络延迟";
                    }
                    break;

                case DeviceType.UsbController:
                    recommendation.Priority = RecommendationPriority.Medium;
                    
                    // AMD 3D缓存CPU：USB也分配到非3D缓存核心
                    if (cpuInfo.Has3DCache && cpuInfo.HasHybridArchitecture && cpuInfo.Vendor == CpuVendor.AMD)
                    {
                        recommendation.RecommendedThreads = new List<int> { cpuInfo.LogicalProcessors - 1 };
                        recommendation.Reason = "USB 控制器分配到计算核心末端，减少外设延迟";
                    }
                    else
                    {
                        recommendation.RecommendedThreads = new List<int> { cpuInfo.LogicalProcessors - 1 };
                        recommendation.Reason = "USB 控制器分配到独立核心，减少外设输入延迟";
                    }
                    break;

                default:
                    recommendation.RecommendedThreads = Enumerable.Range(0, cpuInfo.LogicalProcessors).ToList();
                    recommendation.Reason = "使用默认设置";
                    recommendation.Priority = RecommendationPriority.Low;
                    break;
            }

            recommendation.AffinityMask = CalculateAffinityMask(recommendation.RecommendedThreads);
            return recommendation;
        }

        public IReadOnlyList<AffinityRecommendation> GetAllRecommendations(IEnumerable<AffinityDevice> devices)
        {
            return devices.Select(GetSmartRecommendation).ToList();
        }

        public long CalculateAffinityMask(IEnumerable<int> selectedThreads)
        {
            long mask = 0;
            foreach (var thread in selectedThreads)
            {
                if (thread >= 0 && thread < 64)
                    mask |= 1L << thread;
            }
            return mask;
        }

        public IEnumerable<int> GetThreadsFromMask(long affinityMask)
        {
            for (int i = 0; i < 64; i++)
            {
                if ((affinityMask & (1L << i)) != 0)
                    yield return i;
            }
        }

        private CpuCoreType DetermineCoreType(CpuInfo cpuInfo, int threadIndex)
        {
            // AMD 3D V-Cache CPU
            if (cpuInfo.Has3DCache && cpuInfo.Vendor == CpuVendor.AMD)
            {
                // 7900X3D: 核心0-5(线程0-11)有3D缓存，核心6-11(线程12-23)没有
                // 7950X3D: 核心0-7(线程0-15)有3D缓存，核心8-15(线程16-31)没有
                var cacheThreads = cpuInfo.CacheCoreCount * 2; // 每核心2线程
                return threadIndex < cacheThreads ? CpuCoreType.CacheCore : CpuCoreType.ComputeCore;
            }
            
            // Intel混合架构
            if (cpuInfo.HasHybridArchitecture && cpuInfo.Vendor == CpuVendor.Intel)
            {
                // 混合架构：前半为P核心，后半为E核心
                var pCoreThreads = cpuInfo.PhysicalCores; // P核心通常有超线程
                return threadIndex < pCoreThreads ? CpuCoreType.PerformanceCore : CpuCoreType.EfficiencyCore;
            }
            
            // 非混合架构：所有核心都是标准核心
            return CpuCoreType.Standard;
        }

        private void ScanDevices(List<AffinityDevice> devices, string wmiClass, DeviceType type, 
            string typeLabel, Func<ManagementObject, bool>? filter = null)
        {
            try
            {
                // 使用更短的超时时间
                var options = new System.Management.EnumerationOptions
                {
                    Timeout = TimeSpan.FromSeconds(5),
                    ReturnImmediately = true
                };
                using var searcher = new ManagementObjectSearcher(new ManagementScope(), new ObjectQuery($"SELECT Name, Caption, PNPDeviceID, NetEnabled FROM {wmiClass}"), options);
                foreach (ManagementBaseObject obj in searcher.Get())
                {
                    using (obj) // 确保 WMI 对象被正确释放
                    {
                        try
                        {
                            if (obj is ManagementObject mo && filter != null && !filter(mo)) continue;

                            var name = obj["Name"]?.ToString() ?? obj["Caption"]?.ToString() ?? "Unknown";
                            var pnpDeviceId = obj["PNPDeviceID"]?.ToString();
                            
                            // 必须有有效的 PNPDeviceID 才能设置亲和性
                            if (string.IsNullOrEmpty(pnpDeviceId)) continue;
                            
                            // 验证设备ID格式，过滤非法字符
                            if (!IsValidDeviceId(pnpDeviceId)) continue;

                            devices.Add(new AffinityDevice
                            {
                                Id = pnpDeviceId,
                                Name = name,
                                Description = typeLabel,
                                Type = type
                            });
                        }
                        catch
                        {
                            // 忽略单个设备的错误
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CpuAffinityService] 扫描设备失败 ({wmiClass}): {ex.Message}");
            }
        }

        private static bool IsValidDeviceId(string deviceId)
        {
            // 验证设备ID格式，防止注册表路径注入
            // 有效的 PNPDeviceID 格式如: PCI\VEN_10DE&DEV_2684&SUBSYS_...
            if (string.IsNullOrWhiteSpace(deviceId)) return false;
            if (deviceId.Length > 500) return false; // 防止过长的ID
            
            // 检查是否包含危险字符
            var invalidChars = new[] { "..", "//", "\\\\", "\0", "\r", "\n" };
            foreach (var invalid in invalidChars)
            {
                if (deviceId.Contains(invalid)) return false;
            }
            
            return true;
        }

        private static CpuVendor DetectCpuVendor(string manufacturer, string cpuName)
        {
            return HardwareVendorDetector.DetectCpuVendor(manufacturer, cpuName);
        }

        private static void DetectArchitectureFeatures(CpuInfo cpuInfo)
        {
            var name = cpuInfo.Name;
            var physicalCores = cpuInfo.PhysicalCores;
            var logicalProcessors = cpuInfo.LogicalProcessors;

            // Intel混合架构检测
            if (cpuInfo.Vendor == CpuVendor.Intel)
            {
                var intelHybridPatterns = new[]
                {
                    "12th Gen", "13th Gen", "14th Gen", "15th Gen",
                    "Core Ultra",
                    "i5-12", "i7-12", "i9-12",
                    "i5-13", "i7-13", "i9-13",
                    "i5-14", "i7-14", "i9-14",
                };

                foreach (var pattern in intelHybridPatterns)
                {
                    if (name.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        cpuInfo.HasHybridArchitecture = true;
                        break;
                    }
                }

                // 启发式：逻辑处理器 > 物理核心×2 表示混合架构
                if (!cpuInfo.HasHybridArchitecture && physicalCores > 0 && logicalProcessors > physicalCores * 2)
                {
                    cpuInfo.HasHybridArchitecture = true;
                }
            }

            // AMD 3D V-Cache检测
            if (cpuInfo.Vendor == CpuVendor.AMD)
            {
                // 7800X3D: 8核16线程，全部3D缓存（不需要特殊分配）
                // 7900X3D: 12核24线程，6核有3D缓存
                // 7950X3D: 16核32线程，8核有3D缓存
                // 9800X3D: 8核16线程，全部3D缓存
                // 9900X3D/9950X3D: 未来可能的型号
                
                if (name.Contains("7950X3D", StringComparison.OrdinalIgnoreCase))
                {
                    cpuInfo.Has3DCache = true;
                    cpuInfo.HasHybridArchitecture = true;
                    cpuInfo.CacheCoreCount = 8; // 第一个CCD有3D缓存
                }
                else if (name.Contains("7900X3D", StringComparison.OrdinalIgnoreCase))
                {
                    cpuInfo.Has3DCache = true;
                    cpuInfo.HasHybridArchitecture = true;
                    cpuInfo.CacheCoreCount = 6; // 第一个CCD有3D缓存
                }
                else if (name.Contains("X3D", StringComparison.OrdinalIgnoreCase))
                {
                    // 7800X3D, 9800X3D等单CCD全3D缓存
                    cpuInfo.Has3DCache = true;
                    cpuInfo.CacheCoreCount = physicalCores;
                    // 单CCD不需要特殊分配，不设置HasHybridArchitecture
                }
            }
        }
    }
}
