// File: GameOptimizer.Core/Services/CpuTopologyService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using GameOptimizer.Core.Models;

namespace GameOptimizer.Core.Services;

public class CpuTopologyService
{
    private const ushort AllProcessorGroups = 0xFFFF;
    private CpuTopology? _cachedTopology;
    
    public CpuTopology GetTopology()
    {
        if (_cachedTopology != null)
            return _cachedTopology;
            
        _cachedTopology = DetectTopology();
        return _cachedTopology;
    }
    
    public void RefreshTopology()
    {
        _cachedTopology = null;
        _cachedTopology = DetectTopology();
    }
    
    private CpuTopology DetectTopology()
    {
        var topology = new CpuTopology
        {
            LogicalCores = Environment.ProcessorCount
        };
        
        try
        {
            DetectCpuInfo(topology);
            DetectAffinityCapability(topology);
            DetectCoreLayout(topology);
            ClassifyArchitecture(topology);
            BuildCcdInfo(topology);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CpuTopologyService] 检测CPU拓扑失败: {ex.Message}");
            BuildFallbackTopology(topology);
        }
        
        return topology;
    }
    
    private void DetectCpuInfo(CpuTopology topology)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
            searcher.Options.Timeout = TimeSpan.FromSeconds(5);
            
            foreach (ManagementObject obj in searcher.Get())
            {
                topology.CpuName = obj["Name"]?.ToString()?.Trim() ?? "Unknown CPU";
                topology.PhysicalCores = Convert.ToInt32(obj["NumberOfCores"] ?? topology.LogicalCores / 2);
                
                var manufacturer = obj["Manufacturer"]?.ToString() ?? string.Empty;
                topology.Vendor = HardwareVendorDetector.DetectCpuVendor(manufacturer, topology.CpuName);
                var identity = (manufacturer + " " + topology.CpuName).ToUpperInvariant();
                topology.IsVirtualized = identity.Contains("VIRTUAL")
                    || identity.Contains("HYPER-V")
                    || identity.Contains("VMWARE")
                    || identity.Contains("QEMU")
                    || identity.Contains("XEN");
                if (topology.IsVirtualized)
                    topology.IsDetailedTopologyReliable = false;
                break;
            }
        }
        catch (System.Management.ManagementException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CpuTopologyService] WMI查询失败: {ex.Message}");
            topology.CpuName = "Unknown CPU";
            // Why: LogicalCores=1 时整数除法结果为 0，后续 coresPerCcd 除法会除零
            topology.PhysicalCores = Math.Max(1, topology.LogicalCores / 2);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CpuTopologyService] CPU检测异常: {ex.Message}");
            topology.CpuName = "Unknown CPU";
            topology.PhysicalCores = Math.Max(1, topology.LogicalCores / 2);
        }
    }
    
    private void DetectCoreLayout(CpuTopology topology)
    {
        topology.Cores.Clear();
        
        for (int i = 0; i < topology.LogicalCores; i++)
        {
            var core = new CoreInfo
            {
                Index = i,
                PhysicalIndex = i < topology.PhysicalCores ? i : i - topology.PhysicalCores,
                Type = i < topology.PhysicalCores ? CoreType.Physical : CoreType.Smt,
                IsEnabled = true
            };
            topology.Cores.Add(core);
        }
        
        // 不再根据 CPU 型号或线程数猜测 P/E/CCD 映射。当前页面仅依赖
        // 可验证的逻辑处理器索引做手动 affinity；细分推荐保持禁用。
    }

    private static void DetectAffinityCapability(CpuTopology topology)
    {
        if (!OperatingSystem.IsWindows())
        {
            topology.CapabilityMessage = "当前平台不支持 Windows 进程亲和性。";
            return;
        }

        try
        {
            topology.ProcessorGroupCount = GetActiveProcessorGroupCount();
            var active = checked((int)GetActiveProcessorCount(AllProcessorGroups));
            topology.LogicalCores = active;
            var capability = EvaluateAffinityCapability(topology.ProcessorGroupCount, active, topology.IsVirtualized);
            topology.IsAffinityMaskSupported = capability.IsSupported;
            topology.CapabilityMessage = capability.Message;
        }
        catch
        {
            topology.IsAffinityMaskSupported = false;
            topology.CapabilityMessage = "无法可靠读取 processor group 拓扑，亲和性已禁用。";
        }
    }

    public static CpuAffinityCapability EvaluateAffinityCapability(int processorGroupCount, int logicalProcessorCount, bool isVirtualized)
    {
        if (processorGroupCount <= 0 || logicalProcessorCount <= 0)
            return new(false, "topology.unknown", "无法可靠读取 processor group 拓扑，亲和性已禁用。");
        if (processorGroupCount != 1)
            return new(false, "topology.processor-group-unsupported", $"检测到 {processorGroupCount} 个 processor group，当前掩码契约无法安全表达。");
        if (logicalProcessorCount > 63)
            return new(false, "topology.mask-width-unsupported", $"检测到 {logicalProcessorCount} 个逻辑处理器，当前正 long 掩码仅安全支持 63 个。");
        return new(true, isVirtualized ? "topology.virtualized-manual-only" : "topology.manual-supported",
            isVirtualized ? "虚拟化 CPU 仅支持手动逻辑处理器亲和性，不提供 P/E/CCD 推荐。" : "支持单 processor group 手动亲和性。");
    }

    [DllImport("kernel32.dll")]
    private static extern ushort GetActiveProcessorGroupCount();

    [DllImport("kernel32.dll")]
    private static extern uint GetActiveProcessorCount(ushort groupNumber);
    
    private void DetectAmdCcdLayout(CpuTopology topology)
    {
        var cpuName = topology.CpuName.ToUpperInvariant();
        
        // 检测X3D处理器
        topology.HasVCache = cpuName.Contains("X3D") || cpuName.Contains("3D");
        
        // 检测CCD数量（基于核心数推断）
        // 9950X3D: 16核 = 2 CCD (CCD0有V-Cache)
        // 9900X3D: 12核 = 2 CCD
        // 7950X3D: 16核 = 2 CCD
        // 7800X3D: 8核 = 1 CCD
        if (topology.PhysicalCores >= 12)
        {
            topology.CcdCount = 2;
        }
        else if (topology.PhysicalCores >= 6)
        {
            topology.CcdCount = 1;
        }
        
        // 分配核心到CCD
        // Why: PhysicalCores 可能为 0（单核 CPU 或 WMI 返回异常值），防除零
        int coresPerCcd = topology.CcdCount > 0 && topology.PhysicalCores > 0
            ? topology.PhysicalCores / topology.CcdCount
            : Math.Max(1, topology.LogicalCores / 2);
        foreach (var core in topology.Cores)
        {
            int physicalIdx = core.Type == CoreType.Smt ? core.PhysicalIndex : core.Index;
            core.CcdIndex = physicalIdx / coresPerCcd;
            
            // X3D处理器：CCD0有V-Cache
            if (topology.HasVCache && core.CcdIndex == 0)
            {
                core.HasVCache = true;
            }
        }
    }
    
    private void DetectIntelHybridLayout(CpuTopology topology)
    {
        var cpuName = topology.CpuName.ToUpperInvariant();
        
        // Why: 12/13/14/15代及未来Intel处理器均采用大小核架构，需逐代匹配
        bool isHybrid = cpuName.Contains("12TH") || cpuName.Contains("13TH") || cpuName.Contains("14TH") || cpuName.Contains("15TH") ||
                        cpuName.Contains("-12") || cpuName.Contains("-13") || cpuName.Contains("-14") || cpuName.Contains("-15") ||
                        cpuName.Contains("I5-12") || cpuName.Contains("I7-12") || cpuName.Contains("I9-12") ||
                        cpuName.Contains("I5-13") || cpuName.Contains("I7-13") || cpuName.Contains("I9-13") ||
                        cpuName.Contains("I5-14") || cpuName.Contains("I7-14") || cpuName.Contains("I9-14") ||
                        cpuName.Contains("I5-15") || cpuName.Contains("I7-15") || cpuName.Contains("I9-15") ||
                        cpuName.Contains("ULTRA 5") || cpuName.Contains("ULTRA 7") || cpuName.Contains("ULTRA 9");
        
        if (!isHybrid)
            return;
            
        topology.HasHybridCores = true;
        
        // 尝试通过Windows API获取P-Core/E-Core信息
        try
        {
            DetectIntelCoreTypes(topology);
        }
        catch
        {
            // 回退：假设前半部分是P-Core，后半部分是E-Core
            EstimateIntelCoreTypes(topology);
        }
    }
    
    private void DetectIntelCoreTypes(CpuTopology topology)
    {
        // 使用GetLogicalProcessorInformationEx获取核心类型
        // 这里简化处理，实际需要P/Invoke
        EstimateIntelCoreTypes(topology);
    }
    
    private void EstimateIntelCoreTypes(CpuTopology topology)
    {
        // 基于逻辑核心数估算P-Core和E-Core数量
        // i9-14900K: 8P + 16E = 24核32线程 (8P*2 + 16E = 32)
        // i7-14700K/KF: 8P + 12E = 20核28线程 (8P*2 + 12E = 28)
        // i5-14600K: 6P + 8E = 14核20线程 (6P*2 + 8E = 20)
        
        int logicalCores = topology.LogicalCores;
        int estimatedPCores;
        int estimatedECores;
        
        // 根据逻辑核心数推断
        if (logicalCores >= 32) { estimatedPCores = 8; estimatedECores = 16; }
        else if (logicalCores >= 28) { estimatedPCores = 8; estimatedECores = 12; }
        else if (logicalCores >= 20) { estimatedPCores = 6; estimatedECores = 8; }
        else if (logicalCores >= 16) { estimatedPCores = 4; estimatedECores = 8; }
        else { estimatedPCores = logicalCores / 2; estimatedECores = 0; }
        
        // 更新物理核心数
        topology.PhysicalCores = estimatedPCores + estimatedECores;
        
        // P-Core有超线程，E-Core没有
        int pCoreThreads = estimatedPCores * 2;
        
        System.Diagnostics.Debug.WriteLine($"[CpuTopologyService] Intel Hybrid: {estimatedPCores}P + {estimatedECores}E = {topology.PhysicalCores}C/{logicalCores}T");
        
        for (int i = 0; i < topology.Cores.Count; i++)
        {
            var core = topology.Cores[i];
            if (i < pCoreThreads)
            {
                core.Type = i < estimatedPCores ? CoreType.PerformanceCore : CoreType.Smt;
                core.PhysicalIndex = i % estimatedPCores;
            }
            else
            {
                core.Type = CoreType.EfficiencyCore;
                core.PhysicalIndex = i - pCoreThreads;
            }
        }
    }
    
    private void ClassifyArchitecture(CpuTopology topology)
    {
        if (topology.Vendor == CpuVendor.AMD)
        {
            if (topology.HasVCache)
                topology.Architecture = CpuArchitecture.AmdX3D;
            else if (topology.CcdCount > 1)
                topology.Architecture = CpuArchitecture.AmdMultiCcd;
            else
                topology.Architecture = CpuArchitecture.Standard;
        }
        else if (topology.Vendor == CpuVendor.Intel)
        {
            topology.Architecture = topology.HasHybridCores 
                ? CpuArchitecture.IntelHybrid 
                : CpuArchitecture.IntelStandard;
        }
        else
        {
            topology.Architecture = CpuArchitecture.Standard;
        }
    }
    
    private void BuildCcdInfo(CpuTopology topology)
    {
        topology.Ccds.Clear();
        
        for (int ccdIdx = 0; ccdIdx < topology.CcdCount; ccdIdx++)
        {
            var ccd = new CcdInfo
            {
                CcdIndex = ccdIdx,
                HasVCache = topology.HasVCache && ccdIdx == 0
            };
            
            foreach (var core in topology.Cores.Where(c => c.CcdIndex == ccdIdx))
            {
                ccd.AllCoreIndices.Add(core.Index);
                if (core.Type == CoreType.Physical || core.Type == CoreType.PerformanceCore)
                {
                    ccd.PhysicalCoreIndices.Add(core.Index);
                }
            }
            
            topology.Ccds.Add(ccd);
        }
    }
    
    private void BuildFallbackTopology(CpuTopology topology)
    {
        topology.PhysicalCores = topology.LogicalCores / 2;
        if (topology.PhysicalCores < 1) topology.PhysicalCores = topology.LogicalCores;
        
        topology.Cores.Clear();
        for (int i = 0; i < topology.LogicalCores; i++)
        {
            topology.Cores.Add(new CoreInfo
            {
                Index = i,
                PhysicalIndex = i < topology.PhysicalCores ? i : i - topology.PhysicalCores,
                Type = i < topology.PhysicalCores ? CoreType.Physical : CoreType.Smt,
                CcdIndex = 0,
                IsEnabled = true
            });
        }
        
        topology.CcdCount = 1;
        topology.Ccds.Add(new CcdInfo
        {
            CcdIndex = 0,
            AllCoreIndices = Enumerable.Range(0, topology.LogicalCores).ToList(),
            PhysicalCoreIndices = Enumerable.Range(0, topology.PhysicalCores).ToList()
        });
    }
    
    public long GetAffinityMask(CpuTopology topology, CoreStrategy strategy, int? specificCcd = null)
    {
        var selectedCores = new List<int>();
        
        switch (strategy)
        {
            case CoreStrategy.VCacheCcdOnly:
                var vCacheCcd = topology.Ccds.FirstOrDefault(c => c.HasVCache);
                if (vCacheCcd != null)
                    selectedCores.AddRange(vCacheCcd.AllCoreIndices);
                else
                    selectedCores.AddRange(topology.Ccds.First().AllCoreIndices);
                break;
                
            case CoreStrategy.SingleCcd:
                var targetCcd = specificCcd.HasValue 
                    ? topology.Ccds.FirstOrDefault(c => c.CcdIndex == specificCcd.Value)
                    : topology.Ccds.FirstOrDefault(c => c.HasVCache) ?? topology.Ccds.First();
                if (targetCcd != null)
                    selectedCores.AddRange(targetCcd.AllCoreIndices);
                break;
                
            case CoreStrategy.PCoreOnly:
                selectedCores.AddRange(topology.Cores
                    .Where(c => c.Type == CoreType.PerformanceCore || c.Type == CoreType.Physical)
                    .Select(c => c.Index));
                break;
                
            case CoreStrategy.PhysicalOnly:
                selectedCores.AddRange(topology.Cores
                    .Where(c => c.Type == CoreType.Physical || c.Type == CoreType.PerformanceCore)
                    .Select(c => c.Index));
                break;
                
            case CoreStrategy.AllCores:
            default:
                selectedCores.AddRange(topology.Cores.Select(c => c.Index));
                break;
        }
        
        return CalculateAffinityMask(selectedCores);
    }
    
    public long CalculateAffinityMask(IEnumerable<int> coreIndices)
    {
        long mask = 0;
        foreach (var idx in coreIndices)
        {
            if (idx >= 0 && idx < 64)
                mask |= 1L << idx;
        }
        return mask;
    }
    
    public CoreStrategy GetRecommendedStrategy(CpuTopology topology)
    {
        return topology.Architecture switch
        {
            CpuArchitecture.AmdX3D => CoreStrategy.VCacheCcdOnly,
            CpuArchitecture.AmdMultiCcd => CoreStrategy.SingleCcd,
            CpuArchitecture.IntelHybrid => CoreStrategy.PCoreOnly,
            _ => CoreStrategy.PhysicalOnly
        };
    }
}
