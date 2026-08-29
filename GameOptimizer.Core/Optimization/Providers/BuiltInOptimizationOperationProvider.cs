using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.ServiceProcess;
using GameOptimizer.Core.Constants;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.FileShellSafety;
using GameOptimizer.Core.GraphicsSafety;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Optimization.Operations;
using GameOptimizer.Core.PowerManagement;
using GameOptimizer.Core.Services.Repair;
using GameOptimizer.Core.Security;
using Microsoft.Win32;

namespace GameOptimizer.Core.Optimization.Providers;

[SupportedOSPlatform("windows")]
public sealed class BuiltInOptimizationOperationProvider : IOptimizationOperationProvider
{
    private const int ExpectedModuleCount = 13;
    private const int ExpectedOperationCount = 212;
    private readonly IReadOnlyList<OptimizationModuleDescriptor> _modules;

    private static GameOptimizer.Core.Services.SystemCleanup.ISystemCleanupService? _cleanupService;
    private static GameOptimizer.Core.Services.SystemCleanup.ICleanupQuarantineManager? _cleanupQuarantineManager;
    private static GameOptimizer.Core.Services.SystemCleanup.ICleanupAuthorizationGate? _cleanupAuthorizationGate;
    private static GameOptimizer.Core.Services.Hosts.IHostsBackupOwner? _hostsBackupOwner;

    // Compatibility-only catalog construction for legacy callers that enumerate metadata outside DI.
    // Any attempt to execute the migrated operation through this path fails closed.
    public BuiltInOptimizationOperationProvider()
        : this(UnavailableChangeOrchestrator.Instance, null, null)
    {
    }

    public BuiltInOptimizationOperationProvider(IChangeOrchestrator changeOrchestrator)
        : this(changeOrchestrator, null, null)
    {
    }

    public BuiltInOptimizationOperationProvider(
        IChangeOrchestrator changeOrchestrator,
        IChangeConfirmationAuthority? confirmationAuthority,
        ISecurityProtectionOwner? securityProtectionOwner = null,
        GameOptimizer.Core.Services.NetworkReset.INetworkResetOwner? networkResetOwner = null,
        GameOptimizer.Core.Services.SystemCleanup.ISystemCleanupService? cleanupService = null,
        GameOptimizer.Core.Services.SystemCleanup.ICleanupQuarantineManager? cleanupQuarantineManager = null,
        GameOptimizer.Core.Services.SystemCleanup.ICleanupAuthorizationGate? cleanupAuthorizationGate = null,
        GameOptimizer.Core.Services.Hosts.IHostsBackupOwner? hostsBackupOwner = null)
    {
        _cleanupService = cleanupService;
        _cleanupQuarantineManager = cleanupQuarantineManager;
        _cleanupAuthorizationGate = cleanupAuthorizationGate;
        _hostsBackupOwner = hostsBackupOwner;
        _modules = BuildModules(
            changeOrchestrator,
            confirmationAuthority,
            securityProtectionOwner ?? UnavailableSecurityProtectionOwner.Instance,
            networkResetOwner);
    }

    public IEnumerable<OptimizationModuleDescriptor> GetModules() => _modules;

    private static IReadOnlyList<OptimizationModuleDescriptor> BuildModules(
        IChangeOrchestrator changeOrchestrator,
        IChangeConfirmationAuthority? confirmationAuthority,
        ISecurityProtectionOwner securityProtectionOwner,
        GameOptimizer.Core.Services.NetworkReset.INetworkResetOwner? networkResetOwner)
    {
        // BCD timing changes are typed/orchestrated: each goes through BcdEditChangeCapability
        // (SecureBoot gate + exact-value snapshot restore) with explicit red-flagged confirmation.
        // cpu.force_high_timer_resolution stays permanently fail-closed (process-scoped, no machine-level recovery).
        IOptimizationOperation dynamicTick = Bcd(
            changeOrchestrator,
            confirmationAuthority,
            "cpu.disable_dynamic_tick",
            "禁用 Dynamic Tick",
            "写入 bcdedit /set disabledynamictick yes，避免时钟休眠导致的帧时间波动。",
            "CPU 调优",
            OperationRiskLevel.Extreme,
            "减少时钟休眠导致的帧时间波动",
            "错误配置可能影响启动、计时与虚拟化依赖；需重启生效；已标红，需显式确认后才会写入。");

        IOptimizationOperation disableHpet = Bcd(
            changeOrchestrator,
            confirmationAuthority,
            "cpu.configure_hpet",
            "重新配置 HPET",
            "移除 useplatformclock 绑定，避免强制 HPET 带来的高延迟。",
            "CPU 调优",
            OperationRiskLevel.Extreme,
            "避免未经验证的计时器强制绑定",
            "错误配置可能造成延迟、稳定性或启动问题；需重启生效；已标红，需显式确认后才会写入。");

        IOptimizationOperation usePlatformTick = Bcd(
            changeOrchestrator,
            confirmationAuthority,
            "cpu.use_platform_tick",
            "启用平台计时器",
            "写入 bcdedit /set useplatformtick yes，使用 TSC 替代 HPET，AMD CPU 推荐，可解决随机抽帧。",
            "CPU 调优",
            OperationRiskLevel.Extreme,
            "避免未经验证的全局计时器修改",
            "错误目标可能破坏计时、虚拟化兼容或启动稳定性；需重启生效；已标红，需显式确认后才会写入。");

        IOptimizationOperation timerResolution = PolicyDenied(
            "cpu.force_high_timer_resolution",
            "不允许全局锁定计时器精度",
            "系统计时器请求是进程间聚合状态，无法以单个快照证明全局恢复，当前只允许诊断。",
            "CPU 调优",
            OperationType.SystemApi,
            OperationRiskLevel.Balanced,
            null,
            "可能增加功耗且与其他进程的计时器请求相互影响");

        var modules = new[]
        {
            BuildPerformanceModule(changeOrchestrator, confirmationAuthority, securityProtectionOwner),
            BuildNetworkModule(changeOrchestrator, confirmationAuthority, networkResetOwner),
            BuildPrivacyModule(changeOrchestrator, confirmationAuthority),
            BuildSystemModule(changeOrchestrator, confirmationAuthority),
            BuildCleanupModule(changeOrchestrator, _cleanupService, _cleanupQuarantineManager, _cleanupAuthorizationGate, _hostsBackupOwner),
            BuildExplorerModule(changeOrchestrator, confirmationAuthority),
            BuildCpuModule(changeOrchestrator, confirmationAuthority, dynamicTick, disableHpet, usePlatformTick, timerResolution, securityProtectionOwner),
            BuildGraphicsModule(changeOrchestrator, confirmationAuthority),
            BuildPeripheralModule(changeOrchestrator, confirmationAuthority),
            BuildTasksModule(changeOrchestrator, confirmationAuthority),
            BuildServicesModule(changeOrchestrator, confirmationAuthority),
            BuildMemoryModule(changeOrchestrator, confirmationAuthority),
            BuildAudioModule(changeOrchestrator, confirmationAuthority)
            // Why: gaming/device/accessibility/gpu_advanced/power_advanced/security_mitigation/
            // input_latency/gpu_deep/storage_optimization/network_advanced/windows_features/
            // cpu_advanced/intel_optimization/amd_optimization 的操作已合并到上述13个模块中
        };

        return CanonicalizeAndValidate(modules);
    }

    private static IReadOnlyList<OptimizationModuleDescriptor> CanonicalizeAndValidate(
        IReadOnlyList<OptimizationModuleDescriptor> modules)
    {
        if (modules.Count != ExpectedModuleCount)
        {
            throw new InvalidOperationException(
                $"Built-in optimization catalog must contain exactly {ExpectedModuleCount} modules, but found {modules.Count}.");
        }

        var canonicalModules = modules
            .Select(module => module with
            {
                Operations = module.Operations.Select(CanonicalizeOperation).ToArray()
            })
            .ToArray();
        var operations = canonicalModules.SelectMany(module => module.Operations).ToArray();

        if (operations.Length != ExpectedOperationCount)
        {
            throw new InvalidOperationException(
                $"Built-in optimization catalog must contain exactly {ExpectedOperationCount} operations, but found {operations.Length}.");
        }

        if (canonicalModules.Any(module => string.IsNullOrWhiteSpace(module.Key))
            || operations.Any(operation => string.IsNullOrWhiteSpace(operation.Id)))
        {
            throw new InvalidOperationException("Built-in optimization catalog contains an empty module key or operation ID.");
        }

        var duplicateIds = operations
            .GroupBy(operation => operation.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (duplicateIds.Length > 0)
        {
            throw new InvalidOperationException(
                $"Built-in optimization catalog contains duplicate operation IDs: {string.Join(", ", duplicateIds)}.");
        }

        return canonicalModules;
    }

    private static IOptimizationOperation CanonicalizeOperation(IOptimizationOperation operation) =>
        operation switch
        {
            OrchestratedChangeOperation => operation,
            PolicyDeniedOptimizationOperation => operation,
            FailClosedServiceOptimizationOperation => operation,
            FailClosedSecurityOptimizationOperation => operation,
            FailClosedGraphicsOptimizationOperation => operation,
            FailClosedPowerOptimizationOperation => operation,
            FailClosedFileShellOptimizationOperation => operation,
            _ => new PolicyDeniedOptimizationOperation(
                operation.Id,
                operation.Title,
                operation.Description,
                operation.Category,
                operation.OperationType,
                operation.RiskLevel,
                operation.Benefit,
                operation.Drawback,
                LocalizationService.Instance.Get("Optimization.PolicyDenied.UnownedOperation", operation.GetType().Name))
        };

    private static OptimizationModuleDescriptor BuildPerformanceModule(
        IChangeOrchestrator changeOrchestrator,
        IChangeConfirmationAuthority? confirmationAuthority,
        ISecurityProtectionOwner securityProtectionOwner) =>
        new(
            "performance",
            "性能加速",
            "聚焦 CPU / GPU / 系统级调校",
            new IOptimizationOperation[]
            {
                // 高风险：关闭安全相关功能（标红 + 显式确认，由用户自行判断）
                Service(
                    changeOrchestrator,
                    confirmationAuthority,
                    "basic.disable_security_center",
                    "禁用安全中心服务",
                    "停止并禁用 SecurityHealthService，关闭 Windows 安全中心的状态监控。",
                    "性能加速",
                    "SecurityHealthService",
                    OperationRiskLevel.Extreme,
                    "可能减少安全中心后台监控的资源占用",
                    "失去安全状态监控与告警，可能掩盖真实风险"),
                MachineDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "basic.disable_uac",
                    "禁用 UAC",
                    "将 EnableLUA=0，关闭用户帐户控制，程序运行不再弹出权限确认框。",
                    "性能加速",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
                    "EnableLUA",
                    0,
                    OperationRiskLevel.Extreme,
                    "程序运行不再弹出权限确认框，操作更流畅",
                    "恶意程序可静默获取管理员权限，系统易受攻击；需重启生效",
                    requiresExplicitConfirmation: true),
                Service(
                    changeOrchestrator,
                    confirmationAuthority,
                    "basic.disable_firewall",
                    "禁用 Windows 防火墙",
                    "停止并禁用 MpsSvc 防火墙服务，关闭所有网络流量过滤。",
                    "性能加速",
                    "MpsSvc",
                    OperationRiskLevel.Extreme,
                    "可能减少防火墙过滤的网络处理开销",
                    "主机完全暴露于网络攻击，失去入站/出站流量防护"),
                
                // 中等风险：关闭后台服务
                MachinePolicyDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "basic.disable_store_auto_update",
                    "禁止自动更新商店应用",
                    "通过机器策略将 AutoDownload=2，关闭 Windows 商店后台自动更新。",
                    "性能加速",
                    @"SOFTWARE\Policies\Microsoft\WindowsStore",
                    "AutoDownload",
                    2,
                    OperationRiskLevel.Balanced,
                    "避免游戏时后台下载更新占用带宽和磁盘 I/O",
                    "商店应用不会自动更新，需手动检查更新"),
                new OrchestratedRegistryDwordOperation(
                    changeOrchestrator,
                    "basic.disable_uwp_background",
                    "禁用 UWP 程序后台运行",
                    "阻止通用应用在后台占用资源。",
                    "性能加速",
                    @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications",
                    "GlobalUserDisabled",
                    1,
                    OperationRiskLevel.Balanced,
                    "释放被 UWP 应用占用的内存和 CPU 资源",
                    "邮件、日历等 UWP 应用无法后台同步，可能错过通知",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                // FIX-43: WSearch 索引已由 services 模块统一管理
                MachineDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "basic.disable_windows_update",
                    "关闭 Windows 自动更新",
                    "将 AU 策略 NoAutoUpdate=1，手动控制补丁安装时机。",
                    "性能加速",
                    @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU",
                    "NoAutoUpdate",
                    1,
                    OperationRiskLevel.Balanced,
                    "避免游戏时突然下载更新，防止重启提示干扰",
                    "需手动检查安全更新，系统可能存在未修复的漏洞",
                    requiresExplicitConfirmation: true),
                Service(
                    changeOrchestrator,
                    confirmationAuthority,
                    "perf.wuauserv.disable",
                    "禁用 Windows Update 服务",
                    "停止并禁用 wuauserv，彻底阻止后台更新进程。",
                    "性能加速",
                    "wuauserv",
                    OperationRiskLevel.Balanced,
                    "彻底阻止后台更新进程，释放系统资源",
                    "无法接收任何 Windows 更新，包括安全补丁"),
                
                // 低风险：关闭通知和UI优化
                new OrchestratedRegistryDwordOperation(
                    changeOrchestrator,
                    "basic.disable_security_notifications",
                    "关闭安全通知气泡",
                    "将当前用户 PushNotifications 的 ToastEnabled=0，关闭安全中心通知气泡。",
                    "性能加速",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\PushNotifications",
                    "ToastEnabled",
                    0,
                    OperationRiskLevel.Safe,
                    "游戏时不会被通知弹窗打断，保持沉浸体验",
                    "可能错过重要的系统通知",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                new OrchestratedRegistryDwordOperation(
                    changeOrchestrator,
                    "basic.disable_edge_startup",
                    "禁用 Edge 启动增强",
                    "通过机器策略关闭 Edge Startup Boost。",
                    "性能加速",
                    @"SOFTWARE\Policies\Microsoft\Edge",
                    "StartupBoostEnabled",
                    0,
                    OperationRiskLevel.Balanced,
                    "减少 Edge 后台常驻进程",
                    "Edge 浏览器启动速度可能变慢",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true,
                    scopeKind: TargetScopeKind.Machine,
                    requiresAdministrator: true),
                new OrchestratedRegistryDwordOperation(
                    changeOrchestrator,
                    "basic.disable_map_auto_update",
                    "关闭离线地图自动更新",
                    "通过机器策略阻止离线地图后台自动下载。",
                    "性能加速",
                    @"SOFTWARE\Policies\Microsoft\Windows\Maps",
                    "AutoDownloadAndUpdateMapData",
                    0,
                    OperationRiskLevel.Balanced,
                    "避免后台地图下载占用带宽",
                    "离线地图不会自动更新",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true,
                    scopeKind: TargetScopeKind.Machine,
                    requiresAdministrator: true),
                // FIX-21: 鼠标加速已由 peripheral 模块统一管理
                new OrchestratedRegistryDwordOperation(
                    changeOrchestrator,
                    "basic.disable_game_bar",
                    "关闭 Game Bar 启动面板",
                    "ShowStartupPanel=0，关闭当前用户的 Game Bar 启动提示。",
                    "性能加速",
                    @"SOFTWARE\Microsoft\GameBar",
                    "ShowStartupPanel",
                    0,
                    OperationRiskLevel.Balanced,
                    "避免 Game Bar 启动提示打断游戏",
                    "Game Bar 的首次启动引导将不再显示",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),

                // ── 原 gaming 模块合并 ──
                new OrchestratedRegistryDwordOperation(
                    changeOrchestrator,
                    "gaming.mode.enable",
                    "启用系统游戏模式",
                    "AllowAutoGameMode=1，保持游戏优先。",
                    "性能加速",
                    @"SOFTWARE\Microsoft\GameBar",
                    "AllowAutoGameMode",
                    1,
                    OperationRiskLevel.Safe,
                    "Windows 自动为游戏分配更多系统资源，减少后台干扰",
                    "无明显副作用",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                MachinePolicyDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "gaming.dvr.disable",
                    "禁用 Game DVR 录制",
                    "将机器策略 AllowGameDVR=0，禁止 Win+G 游戏录制与截图，释放 GPU 与内存资源。",
                    "性能加速",
                    @"SOFTWARE\Policies\Microsoft\Windows\GameDVR",
                    "AllowGameDVR",
                    0,
                    OperationRiskLevel.Safe,
                    "释放 Game DVR 占用的 GPU 和内存资源，可能提升帧率",
                    "无法使用 Win+G 录制游戏视频和截图"),

                // ── 原 security_mitigation 模块合并 ──
                // 高风险安全缓解：标红 + 显式确认，由用户自行判断是否关闭（而非永久锁死）。
                MachineDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "security.disable_vbs",
                    "禁用 VBS 虚拟化安全",
                    "将 DeviceGuard 的 EnableVirtualizationBasedSecurity=0，关闭基于虚拟化的安全以换取性能。",
                    "安全缓解",
                    @"SYSTEM\CurrentControlSet\Control\DeviceGuard",
                    "EnableVirtualizationBasedSecurity",
                    0,
                    OperationRiskLevel.Extreme,
                    "可能提升游戏性能，减少虚拟化开销",
                    "大幅降低系统安全性，失去内核级恶意软件防护；需重启生效",
                    requiresExplicitConfirmation: true),
                MachineDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "security.disable_hvci",
                    "禁用内存完整性 (HVCI)",
                    "将 HypervisorEnforcedCodeIntegrity 的 Enabled=0，关闭内存完整性检查。",
                    "安全缓解",
                    @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity",
                    "Enabled",
                    0,
                    OperationRiskLevel.Extreme,
                    "可能提升性能，减少内存完整性检查开销",
                    "降低内核级恶意软件防护能力，易受 Rootkit 攻击；需重启生效",
                    requiresExplicitConfirmation: true),
                MachineDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "security.disable_cfg",
                    "禁用控制流保护 (CFG)",
                    "将内核 MitigationOptions=0，关闭 Control Flow Guard。",
                    "安全缓解",
                    @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel",
                    "MitigationOptions",
                    0,
                    OperationRiskLevel.Extreme,
                    "可能减少 CPU 分支预测开销",
                    "降低漏洞利用防护能力，易受缓冲区溢出攻击；需重启生效",
                    requiresExplicitConfirmation: true),
                MachineDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "security.disable_defender_realtime",
                    "禁用 Defender 实时保护",
                    "将 Defender 策略 DisableRealtimeMonitoring=1，临时关闭实时监控以减少 CPU/磁盘占用。",
                    "安全缓解",
                    @"SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection",
                    "DisableRealtimeMonitoring",
                    1,
                    OperationRiskLevel.Extreme,
                    "可能减少 CPU 占用和磁盘 I/O",
                    "失去实时病毒防护，系统易受恶意软件攻击",
                    requiresExplicitConfirmation: true),
                new OrchestratedRegistryStringOperation(
                    changeOrchestrator,
                    "security.disable_smartscreen",
                    "禁用 SmartScreen",
                    "将 Explorer 的 SmartScreenEnabled=Off，关闭应用启动时的信誉检查。",
                    "安全缓解",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer",
                    "SmartScreenEnabled",
                    "Off",
                    OperationRiskLevel.Balanced,
                    "可能加快程序启动速度，无需等待云端验证",
                    "可能运行未经验证的恶意程序",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true,
                    scopeKind: TargetScopeKind.Machine,
                    requiresAdministrator: true),
                PolicyDenied("security.disable_dep", "不允许禁用 DEP 数据执行保护", "DEP 通过 BCD 启动配置控制，本版本不提供 bcdedit 直写入口；请通过系统安全设置调整。", "安全缓解", OperationType.Registry, OperationRiskLevel.Extreme,
                    "可能提升老游戏兼容性", "大幅降低系统安全性，易受缓冲区溢出攻击"),
                MachineDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "security.disable_sehop",
                    "禁用 SEHOP 异常处理保护",
                    "将内核 DisableExceptionChainValidation=1，关闭结构化异常处理覆盖保护。",
                    "安全缓解",
                    @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel",
                    "DisableExceptionChainValidation",
                    1,
                    OperationRiskLevel.Extreme,
                    "可能提升老游戏兼容性",
                    "降低漏洞利用防护能力，易受 SEH 覆盖攻击；需重启生效",
                    requiresExplicitConfirmation: true)
            });
    private static OptimizationModuleDescriptor BuildNetworkModule(
        IChangeOrchestrator changeOrchestrator,
        IChangeConfirmationAuthority? confirmationAuthority,
        GameOptimizer.Core.Services.NetworkReset.INetworkResetOwner? networkResetOwner) =>
        new(
            "network",
            "网络稳定",
            "降低延迟波动，保持吞吐量稳定可预测。",
            new IOptimizationOperation[]
            {
                // 低风险：优化网络参数，不影响功能
                MachineDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "adjustments.network_throttling_index",
                    "禁用网络节流索引",
                    "将 NetworkThrottlingIndex 设为 0xFFFFFFFF，使多媒体调度永不限制带宽。",
                    "网络稳定",
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
                    "NetworkThrottlingIndex",
                    unchecked((int)0xFFFFFFFF),
                    OperationRiskLevel.Safe,
                    "解除 Windows 对多媒体应用的带宽限制，游戏下载和直播更流畅",
                    "无明显副作用",
                    requiresExplicitConfirmation: true),
                MachineDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "adjustments.disable_diagnostic_events",
                    "禁用诊断自动记录器",
                    "关闭 AutoLogger-DiagTrack 以停止后台跟踪。",
                    "网络稳定",
                    @"SYSTEM\CurrentControlSet\Control\WMI\AutoLogger\AutoLogger-Diagtrack-Listener",
                    "Start",
                    0,
                    OperationRiskLevel.Safe,
                    "减少后台网络流量和磁盘写入，降低系统开销",
                    "无法收集诊断数据用于故障排查",
                    requiresExplicitConfirmation: true),
                MachineDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "privacy.disable_wifi_sense",
                    "禁用 Wi-Fi 感知",
                    "阻止 Windows 自动连接到建议的热点。",
                    "网络稳定",
                    @"SOFTWARE\Microsoft\WcmSvc\wifinetworkmanager\config",
                    "AutoConnectAllowedOEM",
                    0,
                    OperationRiskLevel.Safe,
                    "防止自动连接不安全的公共热点，提升网络安全性",
                    "需手动连接 Wi-Fi 网络",
                    requiresExplicitConfirmation: true),
                new OrchestratedDnsCacheFlushOperation(
                    changeOrchestrator,
                    confirmationAuthority,
                    networkResetOwner),
                // 中等风险：修改网卡高级属性（typed per-interface registry capability）
                new OrchestratedPerInterfaceRegistryOperation(
                    changeOrchestrator,
                    "network.disable_adapter_power_saving",
                    "禁用网卡节能",
                    "关闭 PnP 电源管理策略和厂商 EEE 开关，防止适配器在游戏过程中休眠。",
                    "网络稳定",
                    @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}",
                    "PnPCapabilities,*EEE",
                    "0x18,0",
                    OperationRiskLevel.Balanced,
                    "防止网卡休眠导致的延迟峰值和掉线，游戏网络更稳定",
                    "略微增加功耗，笔记本用户续航可能受影响",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                // 低风险：TCP参数优化（registry-backed，重启后由 TCP/IP 栈读取）
                MachineTcpParameterDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "network.disable_tcp_auto_tuning",
                    "锁定 TCP 自动调优",
                    "将 TcpAutotuningLevel=0，强制禁用接收窗口自动调优以获得确定性延迟。",
                    "网络稳定",
                    "TcpAutotuningLevel",
                    0,
                    OperationRiskLevel.Safe,
                    "禁用接收窗口自动调优，获得确定性延迟",
                    "可能降低高带宽长距离链路的吞吐",
                    requiresExplicitConfirmation: true),
                MachineTcpParameterDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "network.disable_ecn_capability",
                    "禁用 ECN",
                    "将 ECNCapability=0，关闭显式拥塞通知以避免固件兼容性问题。",
                    "网络稳定",
                    "ECNCapability",
                    0,
                    OperationRiskLevel.Safe,
                    "避免固件兼容性问题",
                    "失去显式拥塞通知的拥塞信号",
                    requiresExplicitConfirmation: true),
                MachineTcpParameterDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "network.disable_rsc",
                    "禁用 RSC",
                    "将 EnableRSC=0，停止接收段合并，使数据包立即到达协议栈。",
                    "网络稳定",
                    "EnableRSC",
                    0,
                    OperationRiskLevel.Safe,
                    "数据包立即到达协议栈",
                    "可能降低高吞吐场景的接收效率",
                    requiresExplicitConfirmation: true),
                MachineTcpParameterDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "network.optimize_rss",
                    "启用 RSS",
                    "将 EnableRSS=1，启用接收端缩放，使网卡工作负载分散到多个 CPU 核心。",
                    "网络稳定",
                    "EnableRSS",
                    1,
                    OperationRiskLevel.Safe,
                    "网卡工作负载分散到多核",
                    "某些旧网卡驱动可能不兼容",
                    requiresExplicitConfirmation: true),
                MachineTcpParameterDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "network.enable_tcp1323",
                    "启用 RFC1323 时间戳",
                    "将 Tcp1323Opts=1，保持时间戳支持以维持长距离延迟可见性。",
                    "网络稳定",
                    "Tcp1323Opts",
                    1,
                    OperationRiskLevel.Safe,
                    "保持长距离延迟可见性",
                    "可能略微增加每包开销",
                    requiresExplicitConfirmation: true),
                new OrchestratedRegistryStringOperation(
                    changeOrchestrator,
                    "network.set_congestion_ctcp",
                    "启用复合 TCP",
                    "将 CongestionProvider=ctcp，启用复合 TCP（Compound TCP）以在宽带链路上更快恢复。",
                    "网络稳定",
                    @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters",
                    "CongestionProvider",
                    "ctcp",
                    OperationRiskLevel.Safe,
                    "在宽带链路上更快从拥塞中恢复",
                    "某些旧路由器可能不兼容",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true,
                    scopeKind: TargetScopeKind.Machine,
                    requiresAdministrator: true),
                new OrchestratedPerInterfaceRegistryOperation(
                    changeOrchestrator,
                    "network.disable_flow_control",
                    "禁用流量控制",
                    "强制 *FlowControl=0，使网卡永不因接收拥塞而暂停出站帧。",
                    "网络稳定",
                    @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}",
                    "*FlowControl,FlowControl",
                    "0,0",
                    OperationRiskLevel.Balanced,
                    "消除流量控制导致的发送暂停，降低游戏延迟峰值",
                    "高负载时可能丢包，不建议在网络拥塞环境使用",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                new OrchestratedPerInterfaceRegistryOperation(
                    changeOrchestrator,
                    "network.disable_interrupt_moderation",
                    "禁用中断调制",
                    "关闭中断合并，以最低延迟将数据包传递到 CPU。",
                    "网络稳定",
                    @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}",
                    "*InterruptModeration,*InterruptModerationMode",
                    "0,0",
                    OperationRiskLevel.Balanced,
                    "数据包立即传递到 CPU，可能降低 1-2ms 网络延迟",
                    "增加 CPU 中断负载，高流量时可能影响性能",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                new OrchestratedPerInterfaceRegistryOperation(
                    changeOrchestrator,
                    "network.enable_lso",
                    "启用大数据包发送卸载",
                    "确保网卡对 IPv4/IPv6 执行分段卸载，以保持 CPU 开销最小。",
                    "网络稳定",
                    @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}",
                    "*LsoV1IPv4,*LsoV2IPv4,*LsoV2IPv6",
                    "1,1,1",
                    OperationRiskLevel.Safe,
                    "将大数据包分段工作卸载到网卡，降低 CPU 占用",
                    "无明显副作用",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                new OrchestratedPerInterfaceRegistryOperation(
                    changeOrchestrator,
                    "network.optimize_adapter_buffers",
                    "增大网卡缓冲区",
                    "Raise both receive and transmit buffers to 512 so bursts don’t drop packets under heavy load.",
                    "网络稳定",
                    @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}",
                    "*ReceiveBuffers,*TransmitBuffers",
                    "512,512",
                    OperationRiskLevel.Safe,
                    "增大缓冲区，降低高负载下突发丢包",
                    "无明显副作用",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                new OrchestratedPerInterfaceRegistryOperation(
                    changeOrchestrator,
                    "network.optimize_checksum_offload",
                    "启用校验和卸载",
                    "为 IPv4/IPv6 启用接收/发送校验和卸载，以保持延迟一致。",
                    "网络稳定",
                    @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}",
                    "*IPChecksumOffloadIPv4,*TCPChecksumOffloadIPv4,*UDPChecksumOffloadIPv4,*TCPChecksumOffloadIPv6,*UDPChecksumOffloadIPv6",
                    "3,3,3,3,3",
                    OperationRiskLevel.Safe,
                    "将校验和计算卸载到网卡硬件，降低 CPU 负载",
                    "无明显副作用",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                // 低风险：DNS和TCP参数优化
                new OrchestratedRegistryValueSetOperation(
                    changeOrchestrator,
                    "network.optimize_dns",
                    "优化 DNS 客户端策略",
                    "降低缓存 TTL/负缓存窗口并强制零服务器优先级延迟，使解析器重新查询最快的 DNS 端点。",
                    "网络稳定",
                    @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters",
                    "MaxCacheTtl;MaxNegativeCacheTtl;NegativeSOACacheTime;NetFailureCacheTime;ServerPriorityTimeLimit",
                    "900;30;0;0;0",
                    OperationRiskLevel.Safe,
                    "优化 DNS 缓存策略，更快切换到响应最快的 DNS 服务器",
                    "DNS 查询频率略微增加",
                    OrchestratedRegistryValueSetOperation.CreateOptimizeDnsTargetPayload,
                    confirmationAuthority,
                    requiresExplicitConfirmation: true,
                    scopeKind: TargetScopeKind.Machine,
                    requiresAdministrator: true),
                new OrchestratedPerInterfaceRegistryOperation(
                    changeOrchestrator,
                    "network.disable_nagle",
                    "禁用 Nagle 算法",
                    "在活动适配器上应用 TcpAckFrequency/TCPNoDelay = 1，以获得最低的发送延迟。",
                    "网络稳定",
                    @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces",
                    "TcpAckFrequency,TCPNoDelay",
                    "1,1",
                    OperationRiskLevel.Safe,
                    "禁用数据包合并，降低游戏网络延迟 10-50ms",
                    "小数据包增多，带宽利用率略降",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                MachineTcpParameterDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "network.increase_max_user_port",
                    "提高最大用户端口",
                    "将 MaxUserPort 设置为 65534；该入口需要重启后由 TCP/IP 栈重新读取。",
                    "网络稳定",
                    "MaxUserPort",
                    65534,
                    OperationRiskLevel.Balanced,
                    "扩大可用动态客户端端口上限",
                    "端口池属于机器级共享资源，错误范围可能影响其他连接",
                    requiresExplicitConfirmation: true),
                MachineTcpParameterDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "network.optimize_tcp_timed_wait",
                    "缩短 TIME_WAIT",
                    "将 TcpTimedWaitDelay 设置为 30 秒；该入口需要重启后由 TCP/IP 栈重新读取。",
                    "网络稳定",
                    "TcpTimedWaitDelay",
                    30,
                    OperationRiskLevel.Balanced,
                    "缩短关闭连接保留时间，降低高频短连接的端口占用",
                    "过短的 TIME_WAIT 可能增加连接资源过早复用的风险",
                    requiresExplicitConfirmation: true),
                MachineTcpParameterDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "network.optimize_ttl",
                    "规范化默认 TTL",
                    "将 DefaultTTL 设置为 64 以匹配主流路由器并减少奇怪的跳数。",
                    "网络稳定",
                    "DefaultTTL",
                    64,
                    OperationRiskLevel.Safe,
                    "统一 TTL 值，提升网络兼容性",
                    "无明显副作用",
                    requiresExplicitConfirmation: true),
                MachineTcpParameterDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "network.optimize_tcp_max_dup_acks",
                    "调整 TcpMaxDupAcks",
                    "使用 TcpMaxDupAcks = 2，使重传在有损 Wi-Fi 上更快发生。",
                    "网络稳定",
                    "TcpMaxDupAcks",
                    2,
                    OperationRiskLevel.Safe,
                    "加快丢包检测和重传，改善 Wi-Fi 环境下的游戏体验",
                    "可能略微增加不必要的重传",
                    requiresExplicitConfirmation: true),

                // ── 原 network_advanced 模块合并 ──
                new OrchestratedRegistryValueSetOperation(
                    changeOrchestrator,
                    "network.optimize_afd",
                    "优化 Winsock 参数",
                    "将 AFD 接收/发送窗口设为 65535，提升网络吞吐量。",
                    "网络稳定",
                    @"SYSTEM\CurrentControlSet\Services\AFD\Parameters",
                    "DefaultReceiveWindow;DefaultSendWindow",
                    "65535;65535",
                    OperationRiskLevel.Balanced,
                    "提升网络吞吐量",
                    "可能增加内存占用",
                    OrchestratedRegistryValueSetOperation.CreateOptimizeAfdParametersTargetPayload,
                    confirmationAuthority,
                    requiresExplicitConfirmation: true,
                    scopeKind: TargetScopeKind.Machine,
                    requiresAdministrator: true),
                MachinePolicyDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "network.disable_qos_scheduler",
                    "禁用 QoS 数据包调度",
                    "将 NonBestEffortLimit=0，释放 Windows 默认保留的 20% 带宽。",
                    "网络稳定",
                    @"SOFTWARE\Policies\Microsoft\Windows\Psched",
                    "NonBestEffortLimit",
                    0,
                    OperationRiskLevel.Safe,
                    "释放 QoS 保留的带宽",
                    "可能影响需要 QoS 的应用"),
                new OrchestratedRegistryValueSetOperation(
                    changeOrchestrator,
                    "network.disable_throttling_complete",
                    "禁用网络限流",
                    "将 NetworkThrottlingIndex 设为最大值、SystemResponsiveness 设为 0，完全禁用网络限流。",
                    "网络稳定",
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
                    "NetworkThrottlingIndex;SystemResponsiveness",
                    "4294967295;0",
                    OperationRiskLevel.Balanced,
                    "提升网络传输性能",
                    "可能影响系统响应",
                    OrchestratedRegistryValueSetOperation.CreateDisableNetworkThrottlingTargetPayload,
                    confirmationAuthority,
                    requiresExplicitConfirmation: true,
                    scopeKind: TargetScopeKind.Machine,
                    requiresAdministrator: true),
                MachinePolicyDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "network.disable_delivery_optimization",
                    "禁用传递优化",
                    "通过机器策略将 DODownloadMode=0，停止 Windows 更新 P2P 后台上传。",
                    "网络稳定",
                    @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization",
                    "DODownloadMode",
                    0,
                    OperationRiskLevel.Safe,
                    "停止后台上传，降低游戏期间上行带宽占用与延迟",
                    "Windows 更新不再通过局域网/互联网 P2P 分发，下载可能变慢"),
                MachineDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "network.ipv6_prefer_ipv4",
                    "优先使用 IPv4",
                    "将 DisabledComponents=32（仅禁用 IPv6 隧道，保留原生 IPv6）；重启后由 TCP/IP 栈重新读取。",
                    "网络稳定",
                    @"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters",
                    "DisabledComponents",
                    32,
                    OperationRiskLevel.Safe,
                    "在 IPv6 未正确配置的私有网络中降低解析与连接延迟",
                    "仅关闭 IPv6 隧道，不影响原生 IPv6；重启后生效",
                    requiresExplicitConfirmation: true),
                new OrchestratedPerInterfaceRegistryOperation(
                    changeOrchestrator,
                    "network.disable_netbios",
                    "禁用 NetBIOS over TCP/IP",
                    "在每个 NetBT 接口上将 NetbiosOptions=2，关闭 NetBIOS over TCP/IP 名称解析。",
                    "网络稳定",
                    @"SYSTEM\CurrentControlSet\Services\NetBT\Parameters\Interfaces",
                    "NetbiosOptions",
                    "2",
                    OperationRiskLevel.Balanced,
                    "减少后台名称解析与广播流量",
                    "依赖 NetBIOS 的旧式名称解析与共享发现将不可用",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                MachinePolicyDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "network.disable_lmhosts",
                    "禁用 LMHOSTS 查询",
                    "将 EnableLMHOSTS=0，禁用基于 LMHOSTS 文件的 NetBIOS 名称解析。",
                    "网络稳定",
                    @"SYSTEM\CurrentControlSet\Services\NetBT\Parameters",
                    "EnableLMHOSTS",
                    0,
                    OperationRiskLevel.Safe,
                    "减少后台名称解析延迟",
                    "依赖 LMHOSTS 的旧式名称解析将不可用")
            });

    private static OptimizationModuleDescriptor BuildPrivacyModule(
        IChangeOrchestrator changeOrchestrator,
        IChangeConfirmationAuthority? confirmationAuthority)
    {
        const string privacyCategory = "隐私防护";

        return new(
            "privacy",
            privacyCategory,
            "锁定遥测、权限与云同步，避免后台泄露个人数据。",
            new IOptimizationOperation[]
            {
                new OrchestratedRegistryValueSetOperation(
                    changeOrchestrator,
                    "privacy.disable_advertising_id",
                    "禁用广告 ID",
                    "重置 AdvertisingInfo 并将 Enabled 设为 0，避免跨应用跟踪。",
                    privacyCategory,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo",
                    "Enabled;Id",
                    "0;00000000-0000-0000-0000-000000000000",
                    OperationRiskLevel.Balanced,
                    "阻止跨应用广告追踪，保护隐私",
                    "广告推荐可能不够精准，且会重置当前广告标识",
                    OrchestratedRegistryValueSetOperation.CreateAdvertisingIdTargetPayload,
                    confirmationAuthority),
                MachinePolicyDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "privacy.disable_cortana",
                    "关闭 Cortana",
                    "AllowCortana=0，彻底禁用语音助理与云端收集。",
                    privacyCategory,
                    @"SOFTWARE\Policies\Microsoft\Windows\Windows Search",
                    "AllowCortana",
                    0,
                    OperationRiskLevel.Safe,
                    "减少后台资源占用，阻止语音数据上传",
                    "无法使用 Cortana 语音助手功能"),
                new DisableCeipCompositeOperation(changeOrchestrator, confirmationAuthority),
                MachinePolicyDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "privacy.disable_license_telemetry",
                    "阻止许可证遥测",
                    "NoGenTicket=1，阻止软件许可状态被周期性上报。",
                    privacyCategory,
                    @"SOFTWARE\Policies\Microsoft\Windows NT\CurrentVersion\Software Protection Platform",
                    "NoGenTicket",
                    1,
                    OperationRiskLevel.Safe,
                    "阻止许可证状态被上报，保护隐私",
                    "无明显副作用"),
                MachinePolicyDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "privacy.disable_compatibility_telemetry",
                    "禁用兼容性遥测",
                    "DisableUAR=1，阻止兼容性评估数据回传。",
                    privacyCategory,
                    @"SOFTWARE\Policies\Microsoft\Windows\AppCompat",
                    "DisableUAR",
                    1,
                    OperationRiskLevel.Safe,
                    "减少后台 CPU 和磁盘占用",
                    "无明显副作用"),
                MachinePolicyDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "privacy.disable_privacy_consent",
                    "跳过隐私同意屏",
                    "DisablePrivacyExperience=1，首次登录不再出现隐私提示。",
                    privacyCategory,
                    @"SOFTWARE\Policies\Microsoft\Windows\OOBE",
                    "DisablePrivacyExperience",
                    1,
                    OperationRiskLevel.Safe,
                    "新用户登录时跳过隐私设置向导",
                    "无法在首次登录时配置隐私选项"),
                // 中等风险：禁用服务
                new MultiServiceCompositeOperation(
                    changeOrchestrator,
                    confirmationAuthority,
                    "privacy.disable_upnp_sharing",
                    "关闭 UPnP 共享",
                    "禁用 SSDPSRV 与 UPnP Device Host，阻止局域网自动发现和共享。",
                    privacyCategory,
                    OperationRiskLevel.Balanced,
                    "阻止设备被局域网自动发现，提升安全性",
                    "智能家居设备和媒体服务器可能无法自动发现",
                    ["SSDPSRV", "upnphost"]),
                new MultiServiceCompositeOperation(
                    changeOrchestrator,
                    confirmationAuthority,
                    "privacy.disable_sensor_services",
                    "禁用传感器服务",
                    "停用 SensorDataService/SensrSvc，应用无法获取环境/运动数据。",
                    privacyCategory,
                    OperationRiskLevel.Safe,
                    "阻止应用获取位置和运动数据",
                    "依赖传感器的应用可能无法正常工作",
                    ["SensorDataService", "SensrSvc"]),
                new OrchestratedRegistryStringOperation(
                    changeOrchestrator,
                    "privacy.deny_app_radio_access",
                    "拒绝无线电访问",
                    "将 CapabilityAccessManager\\radios 置为 Deny。阻止应用控制 Wi-Fi/蓝牙开关，保护无线连接安全。部分网络管理应用可能受影响。",
                    privacyCategory,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\radios",
                    "Value",
                    "Deny",
                    OperationRiskLevel.Balanced,
                    "阻止应用控制 Wi-Fi/蓝牙开关",
                    "部分网络管理应用可能受影响",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                new OrchestratedRegistryStringOperation(
                    changeOrchestrator,
                    "privacy.deny_app_access_to_messaging",
                    "拒绝短信/聊天",
                    "chat capability = Deny。阻止应用读取短信和聊天记录，保护通信隐私。短信同步应用将无法工作。",
                    privacyCategory,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\chat",
                    "Value",
                    "Deny",
                    OperationRiskLevel.Balanced,
                    "阻止应用读取短信和聊天记录",
                    "短信同步应用将无法工作",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                new OrchestratedRegistryStringOperation(
                    changeOrchestrator,
                    "privacy.deny_app_access_to_email",
                    "拒绝邮箱访问",
                    "email capability = Deny。阻止应用访问邮件内容，保护邮件隐私。第三方邮件客户端可能受影响。",
                    privacyCategory,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\email",
                    "Value",
                    "Deny",
                    OperationRiskLevel.Balanced,
                    "阻止应用访问邮件内容",
                    "第三方邮件客户端可能受影响",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                new OrchestratedRegistryStringOperation(
                    changeOrchestrator,
                    "privacy.deny_app_access_to_calendar",
                    "拒绝日历访问",
                    "appointments capability = Deny。阻止应用读取日程安排，保护行程隐私。日历同步应用将无法工作。",
                    privacyCategory,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\appointments",
                    "Value",
                    "Deny",
                    OperationRiskLevel.Balanced,
                    "阻止应用读取日程安排",
                    "日历同步应用将无法工作",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                new OrchestratedRegistryStringOperation(
                    changeOrchestrator,
                    "privacy.deny_app_access_to_contacts",
                    "拒绝联系人访问",
                    "contacts capability = Deny。阻止应用读取联系人列表，保护社交隐私。通讯录同步应用将无法工作。",
                    privacyCategory,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\contacts",
                    "Value",
                    "Deny",
                    OperationRiskLevel.Balanced,
                    "阻止应用读取联系人列表",
                    "通讯录同步应用将无法工作",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                new OrchestratedRegistryStringOperation(
                    changeOrchestrator,
                    "privacy.deny_app_access_to_camera",
                    "拒绝相机访问",
                    "webcam capability = Deny。阻止应用使用摄像头，防止偷拍。视频通话和拍照应用将无法使用摄像头。",
                    privacyCategory,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam",
                    "Value",
                    "Deny",
                    OperationRiskLevel.Balanced,
                    "阻止应用使用摄像头",
                    "视频通话和拍照应用将无法使用摄像头",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                new OrchestratedRegistryStringOperation(
                    changeOrchestrator,
                    "privacy.deny_app_access_to_phone_calls",
                    "拒绝拨号",
                    "phoneCall capability = Deny。阻止应用发起电话呼叫，防止恶意扣费。VoIP 应用可能受影响。",
                    privacyCategory,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\phoneCall",
                    "Value",
                    "Deny",
                    OperationRiskLevel.Balanced,
                    "阻止应用发起电话呼叫",
                    "VoIP 应用可能受影响",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                new OrchestratedRegistryStringOperation(
                    changeOrchestrator,
                    "privacy.deny_app_access_to_call_history",
                    "拒绝通话记录",
                    "phoneCallHistory capability = Deny。阻止应用读取通话历史，保护通信隐私。通话记录同步应用将无法工作。",
                    privacyCategory,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\phoneCallHistory",
                    "Value",
                    "Deny",
                    OperationRiskLevel.Balanced,
                    "阻止应用读取通话历史",
                    "通话记录同步应用将无法工作",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                new OrchestratedRegistryStringOperation(
                    changeOrchestrator,
                    "privacy.deny_app_access_to_trusted_devices",
                    "拒绝受信任设备",
                    "trustedDevices capability = Deny。阻止应用访问已配对设备列表，保护设备信息。设备管理应用可能受影响。",
                    privacyCategory,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\trustedDevices",
                    "Value",
                    "Deny",
                    OperationRiskLevel.Balanced,
                    "阻止应用访问已配对设备列表",
                    "设备管理应用可能受影响",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                new OrchestratedRegistryStringOperation(
                    changeOrchestrator,
                    "privacy.deny_app_access_to_motion",
                    "拒绝运动传感器",
                    "activity capability = Deny。阻止应用获取运动和健身数据，保护健康隐私。健身追踪应用将无法工作。",
                    privacyCategory,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\activity",
                    "Value",
                    "Deny",
                    OperationRiskLevel.Balanced,
                    "阻止应用获取运动和健身数据",
                    "健身追踪应用将无法工作",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                new OrchestratedRegistryStringOperation(
                    changeOrchestrator,
                    "privacy.deny_app_access_to_tasks",
                    "拒绝任务访问",
                    "userDataTasks capability = Deny。阻止应用读取待办事项，保护工作隐私。任务同步应用将无法工作。",
                    privacyCategory,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\userDataTasks",
                    "Value",
                    "Deny",
                    OperationRiskLevel.Balanced,
                    "阻止应用读取待办事项",
                    "任务同步应用将无法工作",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                new OrchestratedRegistryStringOperation(
                    changeOrchestrator,
                    "privacy.deny_app_access_to_notifications",
                    "拒绝系统通知访问",
                    "userNotificationListener capability = Deny。阻止应用读取系统通知内容，保护消息隐私。通知同步应用将无法工作。",
                    privacyCategory,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\userNotificationListener",
                    "Value",
                    "Deny",
                    OperationRiskLevel.Balanced,
                    "阻止应用读取系统通知内容",
                    "通知同步应用将无法工作",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                new OrchestratedRegistryStringOperation(
                    changeOrchestrator,
                    "privacy.deny_app_access_to_account_info",
                    "拒绝账户信息",
                    "userAccountInformation capability = Deny。阻止应用获取用户名和头像，保护身份隐私。个性化应用可能显示默认头像。",
                    privacyCategory,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\userAccountInformation",
                    "Value",
                    "Deny",
                    OperationRiskLevel.Balanced,
                    "阻止应用获取用户名和头像",
                    "个性化应用可能显示默认头像",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                new OrchestratedRegistryStringOperation(
                    changeOrchestrator,
                    "privacy.deny_app_access_to_diagnostic_info",
                    "拒绝诊断数据",
                    "appDiagnostics capability = Deny。阻止应用收集其他应用的诊断信息，保护使用习惯隐私。诊断工具可能无法正常工作。",
                    privacyCategory,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\appDiagnostics",
                    "Value",
                    "Deny",
                    OperationRiskLevel.Balanced,
                    "阻止应用收集其他应用的诊断信息",
                    "诊断工具可能无法正常工作",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                new OrchestratedRegistryValueSetOperation(
                    changeOrchestrator,
                    "privacy.disable_app_sync",
                    "禁用设置/应用同步",
                    "DisableSettingSync 与 DisableSettingSyncUserOverride 均为 1，强制本地化配置。",
                    privacyCategory,
                    @"SOFTWARE\Policies\Microsoft\Windows\SettingSync",
                    "DisableSettingSync;DisableSettingSyncUserOverride",
                    "1;1",
                    OperationRiskLevel.Safe,
                    "阻止设置和应用数据上传到云端",
                    "更换设备时需重新配置所有设置",
                    OrchestratedRegistryValueSetOperation.CreateDisableAppSyncTargetPayload,
                    confirmationAuthority,
                    requiresExplicitConfirmation: true,
                    scopeKind: TargetScopeKind.Machine,
                    requiresAdministrator: true),
                new OrchestratedRegistryDwordOperation(
                    changeOrchestrator,
                    "privacy.hide_most_used_apps",
                    "隐藏最常使用的应用",
                    "Start_TrackProgs=0，开始菜单将不再累计或展示“最常使用”的列表。",
                    privacyCategory,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                    "Start_TrackProgs",
                    0,
                    OperationRiskLevel.Balanced,
                    "保护应用使用习惯隐私",
                    "开始菜单不再显示常用应用快捷方式",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                new DisableFeedbackCompositeOperation(changeOrchestrator, confirmationAuthority),
                new OrchestratedRegistryDwordOperation(
                    changeOrchestrator,
                    RepairChangeCapabilityIds.FeedbackReset,
                    "降低反馈请求频率",
                    "将当前用户 SIUF 反馈计数归零；与修复中心复用同一唯一 Owner。",
                    privacyCategory,
                    @"SOFTWARE\Microsoft\Siuf\Rules",
                    "NumberOfSIUFInPeriod",
                    0,
                    OperationRiskLevel.Safe,
                    "减少 Windows 反馈请求弹窗",
                    "反馈触发频率可能降低",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                new OrchestratedRegistryDwordOperation(
                    changeOrchestrator,
                    "privacy.disable_website_access_language_list",
                    "阻止网站读取语言列表",
                    "HttpAcceptLanguageOptOut=1，网站无法枚举系统语言排序。",
                    privacyCategory,
                    @"Control Panel\International\User Profile",
                    "HttpAcceptLanguageOptOut",
                    1,
                    OperationRiskLevel.Balanced,
                    "防止网站通过语言偏好追踪用户",
                    "网站可能无法自动显示正确语言",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                new OrchestratedRegistryValueSetOperation(
                    changeOrchestrator,
                    "privacy.disable_cloud_content",
                    "关闭云推荐内容",
                    "禁用 Spotlight/消费者体验等云推送。",
                    privacyCategory,
                    @"SOFTWARE\Policies\Microsoft\Windows\CloudContent",
                    "DisableWindowsConsumerFeatures;DisableWindowsSpotlightFeatures;DisableSoftLanding",
                    "1;1;1",
                    OperationRiskLevel.Safe,
                    "消除锁屏广告和推荐应用",
                    "无法看到 Windows 聚焦壁纸",
                    OrchestratedRegistryValueSetOperation.CreateDisableCloudContentTargetPayload,
                    confirmationAuthority,
                    requiresExplicitConfirmation: true,
                    scopeKind: TargetScopeKind.Machine,
                    requiresAdministrator: true),
                // Why: privacy.disable_activity_feed（候选 privacy.activity_history）与 windows.disable_timeline
                // 写完全相同的 3 个 HKLM Policies\System DWORD（EnableActivityFeed/PublishUserActivities/UploadUserActivities=0），
                // 属重复候选；当前无 HKLM 多值 typed Owner 可安全闭环，故不另立第二 Owner，保持由 windows.disable_timeline 单一入口承载。
                // 去重记录见 docs/RESEARCH/12 §2.2。
                new OrchestratedRegistryDwordOperation(
                    changeOrchestrator,
                    "privacy.tailored_experiences",
                    "关闭定向体验",
                    "TailoredExperiencesWithDiagnosticDataEnabled=0，停止根据诊断数据定制建议内容。",
                    privacyCategory,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Privacy",
                    "TailoredExperiencesWithDiagnosticDataEnabled",
                    0,
                    OperationRiskLevel.Safe,
                    "阻止根据遥测定制推荐与个性化内容，保护隐私",
                    "无法获得个性化提示与建议，需重新打开时手动恢复",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                MachinePolicyDword(
                    changeOrchestrator, confirmationAuthority, "privacy.disable_error_reporting", "禁用错误报告",
                    "Disabled=1，阻止 Windows Error Reporting 上传 dump。", privacyCategory,
                    @"SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting", "Disabled", 1,
                    OperationRiskLevel.Safe, "阻止崩溃信息被上传，保护隐私", "微软无法收集错误信息修复 Bug"),
                MachinePolicyDword(
                    changeOrchestrator, confirmationAuthority, "privacy.disable_steps_recorder", "禁用 Steps Recorder",
                    "DisableStepsRecorder=1，关闭问题步骤记录器。", privacyCategory,
                    @"SOFTWARE\Policies\Microsoft\Windows\AppCompat", "DisableStepsRecorder", 1,
                    OperationRiskLevel.Safe, "防止屏幕操作被录制", "无法使用步骤记录器排查问题"),
                MachinePolicyDword(
                    changeOrchestrator, confirmationAuthority, "privacy.disable_inventory_collector", "停用 Inventory Collector",
                    "DisableInventory=1，阻止硬件/应用清单被回传。", privacyCategory,
                    @"SOFTWARE\Policies\Microsoft\Windows\AppCompat", "DisableInventory", 1,
                    OperationRiskLevel.Safe, "阻止硬件和软件清单被收集", "无明显副作用"),
                new OrchestratedRegistryValueSetOperation(
                    changeOrchestrator,
                    "privacy.disable_handwriting_collection",
                    "禁止文本/手写收集",
                    "RestrictImplicitInk/TextCollection 设为 1。",
                    privacyCategory,
                    @"SOFTWARE\Microsoft\InputPersonalization",
                    "RestrictImplicitInkCollection;RestrictImplicitTextCollection",
                    "1;1",
                    OperationRiskLevel.Balanced,
                    "阻止输入内容被收集用于改进输入法",
                    "输入法个性化学习效果可能下降",
                    OrchestratedRegistryValueSetOperation.CreateHandwritingCollectionTargetPayload,
                    confirmationAuthority),
                MachinePolicyDword(
                    changeOrchestrator, confirmationAuthority, "privacy.disable_cloud_speech", "关闭云端语音识别",
                    "AllowInputPersonalization=0，禁用语音/墨迹上传。", privacyCategory,
                    @"SOFTWARE\Policies\Microsoft\InputPersonalization", "AllowInputPersonalization", 0,
                    OperationRiskLevel.Safe, "阻止语音和手写数据上传到云端", "语音识别准确度可能下降"),
                // 中等风险：禁用摄像头可能影响视频通话
                MachinePolicyDword(
                    changeOrchestrator, confirmationAuthority, "privacy.disable_webcam", "禁用系统级摄像头",
                    "AllowCamera=0，任何 UWP 都无法启用相机。", privacyCategory,
                    @"SOFTWARE\Policies\Microsoft\Camera", "AllowCamera", 0,
                    OperationRiskLevel.Balanced, "彻底阻止 UWP 应用访问摄像头，防止偷拍", "视频通话、拍照等应用将无法使用摄像头"),
                MachinePolicyDword(
                    changeOrchestrator, confirmationAuthority, "privacy.disable_onedrive_sync", "停用 OneDrive 同步",
                    "DisableFileSyncNGSC=1，防止后台自动同步。", privacyCategory,
                    @"SOFTWARE\Policies\Microsoft\Windows\OneDrive", "DisableFileSyncNGSC", 1,
                    OperationRiskLevel.Safe, "阻止文件自动上传到云端，节省带宽", "文件不会自动备份到 OneDrive"),
                MachinePolicyDword(
                    changeOrchestrator, confirmationAuthority, "privacy.disable_app_impact_telemetry", "关闭应用影响遥测",
                    "AITEnable=0，停止 Application Telemetry 引擎。", privacyCategory,
                    @"SOFTWARE\Policies\Microsoft\Windows\AppCompat", "AITEnable", 0,
                    OperationRiskLevel.Safe, "减少后台遥测进程的 CPU 占用", "无明显副作用")
            });
    }

    private static OptimizationModuleDescriptor BuildSystemModule(
        IChangeOrchestrator changeOrchestrator,
        IChangeConfirmationAuthority? confirmationAuthority) =>
        new(
            "system",
            "系统核心调校",
            "面向电源/调度/内核的基础开关",
            new IOptimizationOperation[]
            {
                // 低风险：关闭休眠释放空间
                MachineDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "system.disable_hibernation",
                    "关闭休眠并回收 hiberfil.sys",
                    "HibernateEnabled=0，禁用休眠以释放系统盘空间并避免电源方案冲突。",
                    "系统核心调校",
                    @"SYSTEM\CurrentControlSet\Control\Power",
                    "HibernateEnabled",
                    0,
                    OperationRiskLevel.Safe,
                    "释放与内存等量的磁盘空间（通常 8-32GB），加快关机速度",
                    "无法使用休眠功能，笔记本合盖后只能睡眠或关机",
                    requiresExplicitConfirmation: true),
                // Why: DisablePagingExecutive 由 memory 模块 DisableKernelPagingOperation 统一管理，避免重复写入
                // Why: Win32PrioritySeparation 由 memory 模块 OptimizeMemoryPriorityOperation 统一管理，避免重复写入
                MachineDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "system.boost_game_priority",
                    "提高游戏任务优先级",
                    "Games 任务的 Priority=6，使游戏 CPU 调度权重更高。",
                    "系统核心调校",
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games",
                    "Priority",
                    6,
                    OperationRiskLevel.Safe,
                    "游戏进程获得更高调度优先级，减少卡顿",
                    "无明显副作用",
                    requiresExplicitConfirmation: true),
                new OrchestratedRegistryStringOperation(
                    changeOrchestrator,
                    "system.prioritize_games",
                    "设置游戏调度类别",
                    "Scheduling Category=High，保证任务始终运行在高优先级队列。",
                    "系统核心调校",
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games",
                    "Scheduling Category",
                    "High",
                    OperationRiskLevel.Safe,
                    "游戏任务始终运行在高优先级队列",
                    "无明显副作用",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true,
                    scopeKind: TargetScopeKind.Machine,
                    requiresAdministrator: true),
                // Why: LargeSystemCache 由 memory 模块 OptimizeSystemCacheOperation 统一管理，避免重复写入
                MachineDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "system.disable_prefetch",
                    "禁用 Prefetch",
                    "EnablePrefetcher=0，避免 SSD 场景下的冗余预读。",
                    "系统核心调校",
                    @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters",
                    "EnablePrefetcher",
                    0,
                    OperationRiskLevel.Safe,
                    "减少 SSD 不必要的预读写入，延长 SSD 寿命",
                    "HDD 用户程序启动可能变慢，SSD 用户无影响",
                    requiresExplicitConfirmation: true),

                // 原 power_advanced 目录元数据保留；全部迁入 typed power:setting:* 能力（红标显式确认）。
                PowerSetting(changeOrchestrator, confirmationAuthority, "power.disable_usb_suspend", "禁用 USB 选择性暂停", "防止 USB 设备休眠导致鼠标键盘延迟。", "系统核心调校", OperationRiskLevel.Safe, "可能减少外设唤醒延迟", "USB 设备不休眠会增加功耗；已标红，需显式确认后才会写入。"),
                PowerSetting(changeOrchestrator, confirmationAuthority, "power.disable_aspm", "禁用 PCIe 节能 (ASPM)", "关闭 PCIe Link State Power Management。", "系统核心调校", OperationRiskLevel.Balanced, "可能降低显卡和 NVMe 延迟", "增加功耗，PCIe 设备不休眠；已标红，需显式确认后才会写入。"),
                PowerSetting(changeOrchestrator, confirmationAuthority, "power.processor_min_state_100", "处理器最小状态 100%", "请求处理器始终保持高性能状态。", "系统核心调校", OperationRiskLevel.Balanced, "可能减少降频带来的延迟", "大幅增加功耗和发热；已标红，需显式确认后才会写入。"),
                PowerSetting(changeOrchestrator, confirmationAuthority, "power.perf_increase_threshold", "处理器性能提升阈值", "请求更快触发处理器性能提升。", "系统核心调校", OperationRiskLevel.Balanced, "可能提高负载响应速度", "会增加频率切换和功耗；已标红，需显式确认后才会写入。"),
                PowerSetting(changeOrchestrator, confirmationAuthority, "power.idle_demote_threshold", "处理器闲置降级阈值", "请求延后处理器性能降级。", "系统核心调校", OperationRiskLevel.Balanced, "可能减少短时负载下的降频", "增加功耗并降低节能能力；已标红，需显式确认后才会写入。"),
                PowerSetting(changeOrchestrator, confirmationAuthority, "power.perf_time_check", "处理器性能时间检查间隔", "请求改变处理器性能检查周期。", "系统核心调校", OperationRiskLevel.Balanced, "可能减少频率切换", "处理器响应负载变化可能变慢；已标红，需显式确认后才会写入。"),
                PowerSetting(changeOrchestrator, confirmationAuthority, "power.class1_efficiency_threshold", "第一类处理器效率阈值", "请求调整混合架构性能核效率阈值。", "系统核心调校", OperationRiskLevel.Balanced, "可能使性能核更快响应", "仅部分处理器支持且会增加功耗；已标红，需显式确认后才会写入。"),
                PowerSetting(changeOrchestrator, confirmationAuthority, "power.class1_perf_level", "第一类处理器性能级别", "请求调整混合架构性能核性能级别。", "系统核心调校", OperationRiskLevel.Balanced, "可能提高性能核频率", "仅部分处理器支持且会增加发热；已标红，需显式确认后才会写入。"),
                PowerSetting(changeOrchestrator, confirmationAuthority, "power.disable_throttle", "禁止 CPU 节流", "请求关闭处理器电源节流策略。", "系统核心调校", OperationRiskLevel.Extreme, "可能减少策略性降频", "可能突破散热和功耗预期；已标红，需显式确认后才会写入。"),
                PowerSetting(changeOrchestrator, confirmationAuthority, "power.hetero_thread_scheduling", "异类线程调度策略", "请求改变混合架构处理器的电源调度策略。", "系统核心调校", OperationRiskLevel.Extreme, "可能优先性能核处理游戏线程", "硬件、Windows build 与 OEM 策略差异可能导致性能回退；已标红，需显式确认后才会写入。"),

                // ── 原 storage_optimization 模块合并 ──
                MachineDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "system.long_paths",
                    "启用长路径支持",
                    "LongPathsEnabled=1，允许超过 260 字符的文件路径。",
                    "系统核心调校",
                    @"SYSTEM\CurrentControlSet\Control\FileSystem",
                    "LongPathsEnabled",
                    1,
                    OperationRiskLevel.Safe,
                    "移除 260 字符路径限制，避免深层目录与长文件名报错",
                    "部分旧应用仍可能不识别长路径；机器级设置需管理员",
                    requiresExplicitConfirmation: true),
                MachineDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "system.disable_wpbt",
                    "禁用 WPBT 启动执行",
                    "DisableWpbtExecution=1，阻止 OEM 平台二进制表在启动时注入程序。",
                    "系统核心调校",
                    @"SYSTEM\CurrentControlSet\Control\Session Manager",
                    "DisableWpbtExecution",
                    1,
                    OperationRiskLevel.Balanced,
                    "阻止厂商在启动阶段强制安装或注入软件，降低安全风险",
                    "个别厂商防盗/驱动恢复脚本可能失效；机器级设置需管理员",
                    requiresExplicitConfirmation: true),
                // 备注：本项把还原点创建频率写为 0，仅绕过 Windows 默认 24 小时冷却窗口，
                // 不删除或禁用系统保护；写入前捕获原值，恢复时按原值回写。需管理员权限。
                MachineDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "power.restore_point_frequency",
                    "允许频繁创建还原点",
                    "SystemRestorePointCreationFrequency=0，绕过还原点 24 小时冷却窗口。",
                    "系统核心调校",
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore",
                    "SystemRestorePointCreationFrequency",
                    0,
                    OperationRiskLevel.Balanced,
                    "可连续创建多个系统还原点，便于快速回滚",
                    "绕过系统默认冷却策略，频繁创建还原点会占用磁盘空间",
                    requiresExplicitConfirmation: true),
                // 备注：本项把 S0 Modern Standby（现代待机）期间的网络连接策略写为“断开”（AC/DC 均为 0），
                // 只影响待机联网策略，不改变待机本身或系统保护；写入前捕获原值，恢复时按原值回写。需管理员权限。
                MachineDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "power.s0_network_ac",
                    "待机时断开网络连接（AC）",
                    "S0 现代待机期间 AC 电源下断开网络连接，减少待机功耗与后台联网。",
                    "系统核心调校",
                    @"SOFTWARE\Policies\Microsoft\Power\PowerSettings\f15576e8-98b7-4186-b944-eafa664402d9",
                    "ACSettingIndex",
                    0,
                    OperationRiskLevel.Balanced,
                    "待机期间断开网络，减少功耗与后台网络活动",
                    "待机唤醒后需重新联网；仅对支持 S0 现代待机的机型有意义",
                    requiresExplicitConfirmation: true),
                MachineDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "power.s0_network_dc",
                    "待机时断开网络连接（DC）",
                    "S0 现代待机期间电池供电下断开网络连接，减少待机功耗与后台联网。",
                    "系统核心调校",
                    @"SOFTWARE\Policies\Microsoft\Power\PowerSettings\f15576e8-98b7-4186-b944-eafa664402d9",
                    "DCSettingIndex",
                    0,
                    OperationRiskLevel.Balanced,
                    "待机期间断开网络，减少功耗与后台网络活动",
                    "待机唤醒后需重新联网；仅对支持 S0 现代待机的机型有意义",
                    requiresExplicitConfirmation: true),
                MachineDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "storage.disable_8dot3",
                    "禁用 NTFS 短文件名",
                    "将 NtfsDisable8dot3NameCreation=1，禁用 8.3 短文件名生成，提升 NTFS 文件系统性能，减少磁盘写入。",
                    "存储优化",
                    @"SYSTEM\CurrentControlSet\Control\FileSystem",
                    "NtfsDisable8dot3NameCreation",
                    1,
                    OperationRiskLevel.Balanced,
                    "可能提升文件系统性能，减少磁盘写入",
                    "极少数老程序可能无法识别长文件名；卷级设置需重启后生效",
                    requiresExplicitConfirmation: true),
                MachineDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "storage.optimize_io_page_lock",
                    "优化 I/O 页面锁定",
                    "将 IoPageLockLimit=983040，提升大文件读写性能，适合 16GB+ 内存用户。",
                    "存储优化",
                    @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
                    "IoPageLockLimit",
                    983040,
                    OperationRiskLevel.Balanced,
                    "可能提升大文件读写性能",
                    "占用更多内存用于 I/O 缓冲；需重启后生效",
                    requiresExplicitConfirmation: true),
                new ClearStandbyListOperation(),
                MachineDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "storage.disable_ntfs_encryption",
                    "禁用 NTFS 加密",
                    "将 NtfsDisableEncryption=1，禁用 NTFS 文件系统加密（EFS），减少 CPU 开销。⚠️ 已加密文件不受影响，但无法新建加密文件。",
                    "存储优化",
                    @"SYSTEM\CurrentControlSet\Control\FileSystem",
                    "NtfsDisableEncryption",
                    1,
                    OperationRiskLevel.Balanced,
                    "可能减少 CPU 开销",
                    "无法新建 EFS 加密文件",
                    requiresExplicitConfirmation: true)
            });

    private static OptimizationModuleDescriptor BuildCleanupModule(
        IChangeOrchestrator changeOrchestrator,
        GameOptimizer.Core.Services.SystemCleanup.ISystemCleanupService? cleanupService,
        GameOptimizer.Core.Services.SystemCleanup.ICleanupQuarantineManager? quarantineManager,
        GameOptimizer.Core.Services.SystemCleanup.ICleanupAuthorizationGate? cleanupAuthorizationGate,
        GameOptimizer.Core.Services.Hosts.IHostsBackupOwner? hostsBackupOwner)
    {
        var owner = UnavailableFileShellSafetyOwner.Instance;
        var operations = new List<IOptimizationOperation>
        {
            // 已迁入 typed OrchestratedFileCleanupOperation：
            // 预览→授权门→确认→逐文件 SHA-256 校验隔离，恢复走中央隔离区。
            Cleanup(changeOrchestrator, "cleanup.gpu_cache", "清理 GPU 着色器缓存", "清理 NVIDIA/AMD/D3DSCache 着色器缓存目录。", "系统清理", OperationRiskLevel.Safe, "gpu_cache", "释放磁盘空间，修复着色器异常", "下次游戏启动需重新编译着色器，首次加载变慢"),
            Cleanup(changeOrchestrator, "cleanup.prefetch_files", "清理 Prefetch 文件", "清理 Windows Prefetch 跟踪文件。", "系统清理", OperationRiskLevel.Balanced, "prefetch", "减少 Prefetch 干扰", "应用启动速度可能短暂下降"),
            Cleanup(changeOrchestrator, "cleanup.system_temp", "清理系统临时文件", "清理 Windows\\Temp 目录。", "系统清理", OperationRiskLevel.Safe, "system_temp", "释放磁盘空间", "正在使用的临时文件会被跳过"),
            Cleanup(changeOrchestrator, "cleanup.user_temp", "清理用户临时文件", "清理用户 %TEMP% 目录。", "系统清理", OperationRiskLevel.Safe, "user_temp", "释放磁盘空间", "正在使用的临时文件会被跳过"),
            Cleanup(changeOrchestrator, "cleanup.thumbnail_cache", "清理缩略图缓存", "清理 Explorer thumbcache/iconcache 数据库。", "系统清理", OperationRiskLevel.Safe, "thumbnail_cache", "重建损坏的缩略图缓存", "文件夹缩略图需重新生成"),
            Cleanup(changeOrchestrator, "cleanup.windows_update_cache", "清理更新下载缓存", "清理 SoftwareDistribution\\Download 缓存。", "系统清理", OperationRiskLevel.Balanced, "update_cache", "释放磁盘空间，修复更新卡滞", "已下载的更新包需重新下载"),

            // A6：hosts 原子备份/恢复（哈希校验、临时文件原子替换，禁止内容编辑与联网拉取）。
            new OrchestratedHostsBackupOperation(
                "cleanup.hosts_backup",
                "备份/恢复 hosts 文件",
                "对系统 hosts 文件创建 SHA-256 校验的原子备份副本；恢复时逐字节还原到备份点。",
                "系统清理",
                OperationRiskLevel.Balanced,
                "修改网络配置前留有可靠回退点",
                "恢复会覆盖当前 hosts 中未被备份记录的更改；需管理员权限；已标红，需显式确认后才会写入。",
                hostsBackupOwner),

            // 永久 fail-closed：无恢复语义的不可逆销毁不进入优化目录。
            BlockedFileCleanup(owner, "cleanup.windows_logs", "系统日志清理旧入口不可用", "诊断日志不会由旧目录永久删除。"),
            BlockedFileCleanup(owner, "cleanup.recycle_bin", "回收站永久清空被阻断", "本产品不通过优化目录永久删除回收站内容。")
        };

        return new(
            "cleanup",
            "系统清理",
            "可恢复清理项委托中央系统清理服务的预览、确认与隔离清单；不可逆项保持阻断。",
            operations.ToArray());
    }

    private static OrchestratedFileCleanupOperation Cleanup(
        IChangeOrchestrator changeOrchestrator,
        string id,
        string title,
        string description,
        string category,
        OperationRiskLevel riskLevel,
        string cleanupItemId,
        string? benefit,
        string? drawback) =>
        new(
            changeOrchestrator,
            id,
            title,
            description,
            category,
            riskLevel,
            cleanupItemId,
            benefit,
            drawback,
            _cleanupService,
            _cleanupQuarantineManager,
            _cleanupAuthorizationGate);

    private static FailClosedFileShellOptimizationOperation BlockedFileCleanup(
        IFileShellSafetyOwner owner,
        string id,
        string title,
        string description) =>
        new(owner, FileShellMutationKind.QuarantineFiles, id, title, description, "系统清理",
            "未连接具备 canonical allowlist、reparse 防护、确认、baseline、quarantine manifest 与恢复回读的 Owner。");

    private static OptimizationModuleDescriptor BuildExplorerModule(
        IChangeOrchestrator changeOrchestrator,
        IChangeConfirmationAuthority? confirmationAuthority) =>
        new(
            "explorer",
            "资源管理器界面",
            "改善资源管理器可读性并减少干扰提示。",
            new IOptimizationOperation[]
            {
                // M1 纵切：旧入口保留，但真实读/写/恢复统一委托安全协调器。
                new OrchestratedShowFileExtensionsOperation(changeOrchestrator, confirmationAuthority),
                new OrchestratedRegistryDwordOperation(
                    changeOrchestrator,
                    "explorer.hidden_files",
                    "显示隐藏文件",
                    "Hidden=1，在资源管理器中显示带隐藏属性的文件与文件夹。",
                    "资源管理器界面",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                    "Hidden",
                    1,
                    OperationRiskLevel.Safe,
                    "可查看被隐藏的系统与用户文件，便于排查与整理",
                    "隐藏系统文件可见后存在误删风险",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                new OrchestratedRegistryDwordOperation(
                    changeOrchestrator,
                    "registry.disable_thumbnail_delay",
                    "移除缩略图悬停延迟",
                    "ExtendedUIHoverTime=1，使任务栏预览立即出现。",
                    "资源管理器界面",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                    "ExtendedUIHoverTime",
                    1,
                    OperationRiskLevel.Safe,
                    "任务栏预览立即显示，操作更流畅",
                    "无明显副作用",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                new OrchestratedRegistryDwordOperation(
                    changeOrchestrator,
                    "registry.disable_startup_sound",
                    "禁用启动声音",
                    "DisableStartupSound=1，在共享空间中保持启动静音。",
                    "资源管理器界面",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
                    "DisableStartupSound",
                    1,
                    OperationRiskLevel.Balanced,
                    "开机时不播放声音，适合公共场所或夜间使用",
                    "启动声音将不再播放",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true,
                    scopeKind: TargetScopeKind.Machine,
                    requiresAdministrator: true),
                new OrchestratedRegistryDwordOperation(
                    changeOrchestrator,
                    "registry.disable_low_disk_check",
                    "禁用低磁盘空间警报",
                    "NoLowDiskSpaceChecks=1，关闭低磁盘空间提醒；这可能掩盖磁盘即将耗尽的风险。",
                    "资源管理器界面",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer",
                    "NoLowDiskSpaceChecks",
                    1,
                    OperationRiskLevel.Balanced,
                    "减少重复的低磁盘空间提醒",
                    "可能掩盖磁盘空间耗尽，导致更新、保存或应用运行失败",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                new OrchestratedRegistryDwordOperation(
                    changeOrchestrator,
                    "edge.disable_game_assist",
                    "禁用 Edge 游戏助手",
                    "关闭 Edge 游戏助手覆盖层，避免游戏时 GPU 资源竞争和帧率下降。",
                    "资源管理器界面",
                    @"Software\Microsoft\Edge\GameAssist",
                    "Enabled",
                    0,
                    OperationRiskLevel.Safe,
                    "避免游戏时 Edge 覆盖层占用 GPU 资源，提升帧率",
                    "无法使用 Edge 游戏助手功能",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                new OrchestratedRegistryDwordOperation(
                    changeOrchestrator,
                    "edge.disable_game_assist_policy",
                    "禁用 Edge 游戏助手（策略）",
                    "通过机器策略禁用 Edge 游戏助手，防止自动重新启用。",
                    "资源管理器界面",
                    @"SOFTWARE\Policies\Microsoft\Edge",
                    "GameAssistEnabled",
                    0,
                    OperationRiskLevel.Balanced,
                    "防止 Edge 更新后自动重新启用游戏助手",
                    "所有用户都将无法使用 Edge 游戏助手",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true,
                    scopeKind: TargetScopeKind.Machine,
                    requiresAdministrator: true),

                // ── 原 windows_features 模块合并 ──
                new OrchestratedRegistryValueSetOperation(
                    changeOrchestrator,
                    "windows.disable_tips",
                    "禁用 Windows 提示",
                    "关闭 ContentDeliveryManager 的软推荐和提示内容。",
                    "资源管理器界面",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
                    "SoftLandingEnabled;SubscribedContent-338389Enabled",
                    "0;0",
                    OperationRiskLevel.Balanced,
                    "减少系统提示和推荐内容干扰",
                    "将不再显示部分 Windows 使用建议",
                    OrchestratedRegistryValueSetOperation.CreateWindowsTipsTargetPayload,
                    confirmationAuthority),
                new OrchestratedRegistryValueSetOperation(
                    changeOrchestrator,
                    "windows.disable_suggested_apps",
                    "禁用建议应用",
                    "关闭预装、静默安装和 OEM 建议应用入口。",
                    "资源管理器界面",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
                    "OemPreInstalledAppsEnabled;PreInstalledAppsEnabled;PreInstalledAppsEverEnabled;SilentInstalledAppsEnabled",
                    "0;0;0;0",
                    OperationRiskLevel.Balanced,
                    "避免系统静默推荐或安装应用",
                    "将无法获得 Microsoft 或 OEM 的建议应用",
                    OrchestratedRegistryValueSetOperation.CreateSuggestedAppsTargetPayload,
                    confirmationAuthority),
                new OrchestratedRegistryDwordOperation(
                    changeOrchestrator,
                    "windows.disable_clipboard_history",
                    "禁用剪贴板历史",
                    "关闭 Windows 剪贴板历史记录功能。",
                    "资源管理器界面",
                    @"SOFTWARE\Microsoft\Clipboard",
                    "EnableClipboardHistory",
                    0,
                    OperationRiskLevel.Safe,
                    "保护剪贴板隐私",
                    "无法使用 Win+V 历史记录",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                new OrchestratedRegistryDwordOperation(
                    changeOrchestrator,
                    "windows.disable_focus_assist",
                    "禁用专注助手",
                    "关闭 Windows 专注助手，确保游戏通知不被拦截。",
                    "资源管理器界面",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Notifications\Settings",
                    "NOC_GLOBAL_SETTING_TOASTS_ENABLED",
                    1,
                    OperationRiskLevel.Safe,
                    "游戏通知不被拦截",
                    "可能收到更多通知",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                new OrchestratedRegistryValueSetOperation(
                    changeOrchestrator,
                    "windows.disable_timeline",
                    "禁用时间线",
                    "关闭活动历史记录的启用、发布与上传，阻止跨设备同步。",
                    "资源管理器界面",
                    @"SOFTWARE\Policies\Microsoft\Windows\System",
                    "EnableActivityFeed;PublishUserActivities;UploadUserActivities",
                    "0;0;0",
                    OperationRiskLevel.Safe,
                    "保护活动历史隐私，阻止跨设备同步",
                    "无法跨设备同步活动历史",
                    OrchestratedRegistryValueSetOperation.CreateDisableTimelineTargetPayload,
                    confirmationAuthority,
                    requiresExplicitConfirmation: true,
                    scopeKind: TargetScopeKind.Machine,
                    requiresAdministrator: true),
                new OrchestratedRegistryDwordOperation(
                    changeOrchestrator,
                    "explorer.end_task",
                    "任务栏右键结束任务",
                    "TaskbarEndTask=1，在任务栏右键菜单显示“结束任务”。",
                    "资源管理器界面",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings",
                    "TaskbarEndTask",
                    1,
                    OperationRiskLevel.Safe,
                    "直接从任务栏结束未响应程序，无需打开任务管理器",
                    "仅 Windows 11 有效；Windows 10 上该值不生效",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true)
            });

    private static OptimizationModuleDescriptor BuildCpuModule(
        IChangeOrchestrator changeOrchestrator,
        IChangeConfirmationAuthority? confirmationAuthority,
        IOptimizationOperation dynamicTick,
        IOptimizationOperation disableHpet,
        IOptimizationOperation usePlatformTick,
        IOptimizationOperation timerResolution,
        ISecurityProtectionOwner securityProtectionOwner) =>
        new(
            "cpu",
            "CPU 调优",
            "与核心调度/时钟相关的底层优化",
            new IOptimizationOperation[]
            {
                // 中等风险：修改CPU调度策略
                PowerSetting(changeOrchestrator, confirmationAuthority, "cpu.disable_core_parking", "让所有 CPU 核心保持可用", "游戏需要更多核心时，不必等待核心从省电状态唤醒。", "CPU 调优", OperationRiskLevel.Balanced, "可能减少核心唤醒等待", "会增加功耗和发热，笔记本续航可能下降；确认后才会修改。"),
                PowerSetting(changeOrchestrator, confirmationAuthority, "cpu.processor_boost_mode", "让 CPU 更积极地加速", "负载升高时更快提高频率。", "CPU 调优", OperationRiskLevel.Balanced, "可能提高突发性能", "会增加温度和风扇噪声；确认后才会修改。"),
                PowerSetting(changeOrchestrator, confirmationAuthority, "cpu.disable_boost_mode", "限制 CPU 自动加速", "降低发热和噪声，但也会降低最高性能。", "CPU 调优", OperationRiskLevel.Balanced, "可能降低温度与功耗", "会降低游戏的最高性能；确认后才会修改。"),
                // 低风险：优化响应参数
                MachineDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "cpu.system_responsiveness",
                    "优化 SystemResponsiveness",
                    "将 SystemResponsiveness 设为 10，降低 MMCSS 为后台保留的 CPU。",
                    "CPU 调优",
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
                    "SystemResponsiveness",
                    10,
                    OperationRiskLevel.Safe,
                    "减少系统为后台任务保留的 CPU 资源，游戏获得更多 CPU 时间",
                    "后台音频/视频处理可能偶尔卡顿",
                    requiresExplicitConfirmation: true),
                // 中等风险：启用TSX可能影响稳定性
                MachineDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "cpu.enable_tsx",
                    "启用 TSX",
                    "DisableTsx=0，再次启用 Intel TSX 指令集（需硬件支持）。",
                    "CPU 调优",
                    @"SYSTEM\CurrentControlSet\Control\Session Manager\Kernel",
                    "DisableTsx",
                    0,
                    OperationRiskLevel.Balanced,
                    "启用 Intel 事务性同步扩展，可能提升多线程性能",
                    "仅 Intel CPU 支持，部分 CPU 因安全漏洞已禁用此功能",
                    requiresExplicitConfirmation: true),
                // 高风险：修改BCD设置
                dynamicTick,
                disableHpet,
                usePlatformTick,
                // 中等风险：修改计时器精度
                timerResolution,
                // 高风险：关闭安全缓解措施（标红 + 显式确认）
                new OrchestratedRegistryValueSetOperation(
                    changeOrchestrator,
                    "cpu.disable_spectre_meltdown",
                    "禁用 Spectre/Meltdown 缓解",
                    "将内核 Memory Management 的 FeatureSettingsOverride/FeatureSettingsOverrideMask=3，关闭 CPU 漏洞缓解。",
                    "CPU 调优",
                    @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
                    "FeatureSettingsOverride;FeatureSettingsOverrideMask",
                    "3;3",
                    OperationRiskLevel.Extreme,
                    "可能提升 CPU 性能，减少漏洞缓解开销",
                    "使系统暴露于已知侧信道攻击；需重启生效",
                    OrchestratedRegistryValueSetOperation.CreateDisableSpectreMeltdownTargetPayload,
                    confirmationAuthority,
                    scopeKind: TargetScopeKind.Machine,
                    requiresAdministrator: true),

                // ── 原 cpu_advanced 模块合并 ──
                PowerSetting(changeOrchestrator, confirmationAuthority, "cpu.disable_pstates", "禁用 CPU P-States", "请求固定处理器最小/最大性能状态。", "CPU 高级调优", OperationRiskLevel.Extreme, "可能减少频率变化", "显著增加功耗和发热，且 OEM 策略可能不支持；已标红，需显式确认后才会写入。"),
                Bcd(changeOrchestrator, confirmationAuthority, "cpu.disable_tsc_sync", "禁用 TSC 同步", "写入 bcdedit /set tscsyncpolicy Legacy，某些多核场景下可提升性能。", "CPU 高级调优", OperationRiskLevel.Extreme, "多核场景下减少 TSC 同步开销", "可能导致时间戳不一致、虚拟化兼容或启动稳定性问题；需重启生效；已标红，需显式确认后才会写入。"),

                // ── 原 intel_optimization 模块合并 ──
                PowerSetting(changeOrchestrator, confirmationAuthority, "intel.disable_cstates", "禁用 CPU C-States", "请求禁用处理器空闲状态。", "Intel 调优", OperationRiskLevel.Extreme, "可能减少空闲状态退出延迟", "仅适用于部分 Intel 平台，并显著增加功耗和温度；已标红，需显式确认后才会写入。"),

                // ── 原 amd_optimization 模块合并 ──
                // typed per-interface 厂商过滤注册表能力：仅对 ProviderName 含 AMD/ATI 的 GPU
                // 显示类子键写 EnableUlps=0 + PP_SclkDeepSleepDisable=1（标红 + 显式确认）。
                new OrchestratedPerInterfaceRegistryOperation(
                    changeOrchestrator,
                    "amd.radeon_anti_lag",
                    "AMD Radeon 低延迟",
                    "关闭 ULPS 节能与深度睡眠，降低 GPU 响应延迟；仅作用于检测到的 AMD/ATI 显卡。",
                    "CPU 调优",
                    @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}",
                    "EnableUlps,PP_SclkDeepSleepDisable",
                    "0,1",
                    OperationRiskLevel.Balanced,
                    "可能降低 GPU 响应延迟，输入更跟手",
                    "增加功耗和发热，待机时 GPU 不休眠",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true)
            });

    private static OptimizationModuleDescriptor BuildGraphicsModule(
        IChangeOrchestrator changeOrchestrator,
        IChangeConfirmationAuthority? confirmationAuthority) =>
        new(
            "graphics",
            "图形与帧率",
            "针对 GPU 调度与全屏体验的关键开关。",
            // 低风险：GPU优化设置
            new IOptimizationOperation[]
            {
                new OrchestratedRegistryDwordOperation(
                    changeOrchestrator,
                    "gpu.enable_hags",
                    "开启硬件加速 GPU 调度",
                    "HwSchMode=2，启用 Hardware Accelerated GPU Scheduling（硬件加速GPU调度），可降低渲染延迟。",
                    "图形与帧率",
                    @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers",
                    "HwSchMode",
                    2,
                    OperationRiskLevel.Safe,
                    "GPU 直接管理显存调度，可能降低输入延迟 1-3ms",
                    "需要 Windows 10 2004+ 和支持的显卡驱动，老旧硬件可能不兼容",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true,
                    scopeKind: TargetScopeKind.Machine,
                    requiresAdministrator: true),
                new OrchestratedRegistryStringOperation(
                    changeOrchestrator,
                    "gpu.force_high_performance_gpu",
                    "全局优先独显",
                    "将 DirectXUserGlobalSettings=GpuPreference=2;，让系统默认使用高性能独立显卡，避免游戏误用核显。",
                    "图形与帧率",
                    @"SOFTWARE\Microsoft\DirectX\UserGpuPreferences",
                    "DirectXUserGlobalSettings",
                    "GpuPreference=2;",
                    OperationRiskLevel.Balanced,
                    "游戏默认使用高性能独显，避免误用核显",
                    "多 GPU/混合显卡下可能增加功耗并覆盖用户逐应用选择",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                new OrchestratedRegistryStringOperation(
                    changeOrchestrator,
                    "gpu.set_sfio_priority",
                    "提升 GPU SFIO 优先级",
                    "将 Games 多媒体任务的 SFIO Priority=High，使游戏流媒体 I/O 走高优通道，减少帧时间波动。",
                    "图形与帧率",
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games",
                    "SFIO Priority",
                    "High",
                    OperationRiskLevel.Balanced,
                    "游戏流式 I/O 走高优通道，减少帧时间波动",
                    "可能影响音视频、后台任务和系统调度稳定性",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true,
                    scopeKind: TargetScopeKind.Machine,
                    requiresAdministrator: true),
                new OrchestratedRegistryValueSetOperation(
                    changeOrchestrator,
                    "fps.disable_fullscreen_optimizations",
                    "禁用全屏优化",
                    "强制 GameConfigStore 中 FSE 行为为传统独占模式，避免 Win10 全屏优化带来的输入延迟。",
                    "图形与帧率",
                    @"System\GameConfigStore",
                    "GameDVR_FSEBehavior;GameDVR_FSEBehaviorMode",
                    "2;2",
                    OperationRiskLevel.Balanced,
                    "部分游戏可能降低输入延迟",
                    "部分新游戏、HDR、覆盖层或无边框窗口兼容性可能下降",
                    OrchestratedRegistryValueSetOperation.CreateFullscreenOptimizationsTargetPayload,
                    confirmationAuthority),

                // ── 原 gpu_advanced 模块合并 ──
                new OrchestratedRegistryDwordOperation(
                    changeOrchestrator,
                    "gpu.disable_game_mode_optimizations",
                    "禁用游戏模式优化",
                    "将 GameBar 的 AutoGameModeEnabled=0，关闭 Windows 游戏模式的自动优化，避免与手动优化冲突。",
                    "图形与帧率",
                    @"SOFTWARE\Microsoft\GameBar",
                    "AutoGameModeEnabled",
                    0,
                    OperationRiskLevel.Balanced,
                    "避免 Windows 自动优化与手动设置冲突，获得更可预测的性能",
                    "关闭游戏模式可能降低部分游戏的前台资源调度效果",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),

                // ── 原 gpu_deep 模块合并 ──
                new OrchestratedRegistryValueSetOperation(
                    changeOrchestrator,
                    "gpu.disable_dwm_animations",
                    "禁用 DWM 动画",
                    "将 DWM 的 EnableAeroPeek/AlwaysHibernateThumbnails=0，关闭桌面窗口管理器动画，减少 GPU 渲染开销。",
                    "图形与帧率",
                    @"SOFTWARE\Microsoft\Windows\DWM",
                    "EnableAeroPeek;AlwaysHibernateThumbnails",
                    "0;0",
                    OperationRiskLevel.Safe,
                    "可能减少 GPU 渲染开销，窗口切换更快",
                    "失去 Aero Peek 预览和窗口动画效果",
                    OrchestratedRegistryValueSetOperation.CreateDisableDwmAnimationsTargetPayload,
                    confirmationAuthority),
                new DisableGameDvrCompleteOperation(changeOrchestrator, confirmationAuthority),
                Service(changeOrchestrator, confirmationAuthority, "gpu.disable_xbox_game_monitoring", "禁用 Xbox 游戏监控", "关闭 Xbox Game Monitoring 服务（xbgm），减少后台资源占用。", "图形与帧率", "xbgm", OperationRiskLevel.Safe, "减少后台资源占用", "Xbox 游戏统计和成就追踪可能受影响"),
                MachineDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "gpu.disable_vrr",
                    "禁用可变刷新率",
                    "将 GraphicsDrivers 的 VRROptimizeEnable=0，关闭 Windows 可变刷新率（VRR/FreeSync/G-Sync）。",
                    "图形与帧率",
                    @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers",
                    "VRROptimizeEnable",
                    0,
                    OperationRiskLevel.Balanced,
                    "解决某些游戏的 VRR 兼容性问题",
                    "失去 FreeSync/G-Sync 防撕裂效果",
                    requiresExplicitConfirmation: true)
            });

    private static OptimizationModuleDescriptor BuildPeripheralModule(
        IChangeOrchestrator changeOrchestrator,
        IChangeConfirmationAuthority? confirmationAuthority)
    {
        return new(
            "peripheral",
            "外设调优",
            "键鼠响应 · USB 供电 · 输入延迟优化",
            // FIX-45: keyboard.delay/speed 已由 keyboard_response 统一管理，此处删除重复
            new IOptimizationOperation[]
            {
                PowerSetting(changeOrchestrator, confirmationAuthority, "peripheral.usb.suspend", "禁用 USB 选择性挂起", "请求关闭 USB selective suspend。", "外设调优", OperationRiskLevel.Safe, "可能减少外设休眠导致的掉线和延迟", "会增加 USB 设备功耗；已标红，需显式确认后才会写入。"),
                new OrchestratedRegistryDwordOperation(
                    changeOrchestrator,
                    "peripheral.anti_mistouch",
                    "禁用 Win 键误触",
                    "NoWinKeys=1，避免游戏过程中误触 Windows 键弹出开始菜单。",
                    "外设调优",
                    @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer",
                    "NoWinKeys",
                    1,
                    OperationRiskLevel.Balanced,
                    "游戏时不会因误触 Win 键而切出游戏",
                    "会禁用 Win+V、Win+Shift+S、Win+R 等系统快捷键，办公或日常场景不建议开启",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                new OrchestratedRegistryValueSetOperation(
                    changeOrchestrator,
                    "peripheral.mouse_precision",
                    "提升鼠标精度",
                    "关闭指针加速并统一 PointerPrecision，保持 1:1 轨迹。",
                    "外设调优",
                    @"Control Panel\Mouse",
                    "MouseSpeed;MouseThreshold1;MouseThreshold2",
                    "来自外设设置的冻结目标",
                    OperationRiskLevel.Balanced,
                    "鼠标移动与物理位移按用户选定参数响应",
                    "会改变鼠标肌肉记忆，日常办公可能需要重新适应",
                    OrchestratedRegistryValueSetOperation.CreateMousePrecisionTargetPayload,
                    confirmationAuthority),
                // FIX-15: usb_suspend 兼容版与 usb.suspend 重复，已删除
                new OrchestratedRegistryValueSetOperation(
                    changeOrchestrator,
                    "peripheral.keyboard_response",
                    "优化键盘响应",
                    "KeyboardDelay=0、KeyboardSpeed=31，获得最快输入反馈。",
                    "外设调优",
                    @"Control Panel\Keyboard",
                    "KeyboardDelay;KeyboardSpeed",
                    "来自外设设置的冻结目标",
                    OperationRiskLevel.Balanced,
                    "按键响应使用用户选定的重复延迟和速度",
                    "过快设置可能增加误重复输入",
                    OrchestratedRegistryValueSetOperation.CreateKeyboardResponseTargetPayload,
                    confirmationAuthority),

                // ── 原 device 模块合并 ──
                // 已迁入 typed DeviceStateChangeCapability：WMI 稳定身份探测 + pnputil 切换 +
                // ConfigManagerErrorCode 回读 + 精确原状态恢复，全部走显式标红确认。
                Device(changeOrchestrator, confirmationAuthority, "device.disable_system_timer", "禁用 System Timer", "禁用系统计时器设备，AMD CPU 推荐，可降低 DPC 延迟。Intel CPU 需谨慎。【优点】可能降低 DPC 延迟，减少输入延迟。【缺点】Intel CPU 可能出现时间不准或系统不稳定。", "外设调优", OperationRiskLevel.Extreme, "可能降低 DPC 延迟，减少输入延迟", "Intel CPU 可能出现时间不准或系统不稳定；已标红，需显式确认后才会写入。"),
                Device(changeOrchestrator, confirmationAuthority, "device.disable_umbus", "禁用 UMBus Root Bus Enumerator", "禁用 UMBus 根总线枚举器，减少系统开销和中断。【优点】减少系统中断，可能降低延迟。【缺点】某些 USB 设备可能无法正常工作。", "外设调优", OperationRiskLevel.Extreme, "减少系统中断，可能降低延迟", "某些 USB 设备可能无法正常工作；已标红，需显式确认后才会写入。"),
                Device(changeOrchestrator, confirmationAuthority, "device.disable_composite_bus", "禁用 Composite Bus Enumerator", "禁用复合总线枚举器，减少 USB 相关中断，降低输入延迟。【优点】减少 USB 中断开销。【缺点】复合 USB 设备（如带音频的摄像头）可能无法使用。", "外设调优", OperationRiskLevel.Extreme, "减少 USB 中断开销", "复合 USB 设备（如带音频的摄像头）可能无法使用；已标红，需显式确认后才会写入。"),
                Device(changeOrchestrator, confirmationAuthority, "device.disable_hpet_device", "禁用 HPET 设备", "在设备管理器级别禁用高精度事件计时器。AMD CPU 推荐，Intel CPU 不建议。【优点】AMD 平台可能显著降低延迟。【缺点】⚠️ 高风险：Intel 平台可能导致系统不稳定或蓝屏。", "外设调优", OperationRiskLevel.Extreme, "AMD 平台可能显著降低延迟", "⚠️ 高风险：Intel 平台可能导致系统不稳定或蓝屏；已标红，需显式确认后才会写入。"),

                // ── 原 accessibility 模块合并 ──
                new OrchestratedAccessibilityOperation(
                    changeOrchestrator,
                    "accessibility.disable_sticky_keys",
                    "禁用粘滞键",
                    "禁用粘滞键热键，避免连按 Shift 时弹出系统提示。",
                    "避免游戏中因连按 Shift 触发系统提示。",
                    "依赖粘滞键辅助功能的用户将无法通过热键启用它。",
                    confirmationAuthority),
                new OrchestratedAccessibilityOperation(
                    changeOrchestrator,
                    "accessibility.disable_filter_keys",
                    "禁用筛选键",
                    "禁用筛选键热键，避免长按右 Shift 时弹出系统提示。",
                    "避免长按右 Shift 触发系统提示。",
                    "依赖筛选键辅助功能的用户将无法通过热键启用它。",
                    confirmationAuthority),
                new OrchestratedAccessibilityOperation(
                    changeOrchestrator,
                    "accessibility.disable_toggle_keys",
                    "禁用切换键",
                    "禁用切换键热键，避免按 Num Lock 等键时出现提示音。",
                    "避免按 Num Lock 等键时出现提示音。",
                    "依赖切换键辅助功能的用户将无法通过热键启用它。",
                    confirmationAuthority),

                // ── 原 input_latency 模块合并 ──
                new OrchestratedRegistryStringOperation(
                    changeOrchestrator,
                    "input.disable_pointer_trails",
                    "禁用鼠标指针轨迹",
                    "MouseTrails=0，关闭鼠标指针轨迹。",
                    "外设调优",
                    @"Control Panel\Mouse",
                    "MouseTrails",
                    "0",
                    OperationRiskLevel.Balanced,
                    "减少指针轨迹的额外绘制",
                    "依赖指针轨迹辅助定位的用户会失去该视觉反馈",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                new OrchestratedRegistryStringOperation(
                    changeOrchestrator,
                    "input.disable_snap_to",
                    "禁用自动跳转到默认按钮",
                    "SnapToDefaultButton=0，关闭指针自动跳转。",
                    "外设调优",
                    @"Control Panel\Mouse",
                    "SnapToDefaultButton",
                    "0",
                    OperationRiskLevel.Balanced,
                    "避免指针位置被系统自动改变",
                    "习惯自动跳转到默认按钮的用户需要手动移动指针",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                new OrchestratedRegistryValueSetOperation(
                    changeOrchestrator,
                    "input.disable_touch_feedback",
                    "禁用触控反馈",
                    "ContactVisualization/GestureVisualization=0，关闭触控视觉反馈。",
                    "外设调优",
                    @"Control Panel\Cursors",
                    "ContactVisualization;GestureVisualization",
                    "0;0",
                    OperationRiskLevel.Balanced,
                    "减少触控反馈动画带来的额外绘制",
                    "触控输入将不再显示接触点和手势视觉反馈",
                    OrchestratedRegistryValueSetOperation.CreateTouchFeedbackTargetPayload,
                    confirmationAuthority),
                new OrchestratedRegistryDwordOperation(
                    changeOrchestrator,
                    "input.disable_pen_feedback",
                    "禁用手写笔反馈",
                    "PenVisualization=0，关闭手写笔输入时的视觉反馈。",
                    "外设调优",
                    @"Software\Microsoft\TabletTip\1.7",
                    "PenVisualization",
                    0,
                    OperationRiskLevel.Balanced,
                    "减少手写笔反馈动画带来的额外绘制",
                    "手写笔输入将不再显示视觉反馈；仅建议在不依赖该反馈时启用",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                new OptimizeUsbPollingCompositeOperation(changeOrchestrator, confirmationAuthority),
                new OrchestratedRegistryDwordOperation(
                    changeOrchestrator,
                    "input.optimize_raw_input_buffer",
                    "优化原始输入缓冲",
                    "将 Games 任务的 GPU Priority=8，提升原始输入路径的响应。",
                    "外设调优",
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games",
                    "GPU Priority",
                    8,
                    OperationRiskLevel.Balanced,
                    "可能提升原始输入路径的响应",
                    "可能与自定义多媒体任务设置冲突",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true,
                    scopeKind: TargetScopeKind.Machine,
                    requiresAdministrator: true)
            });
    }

    private static OptimizationModuleDescriptor BuildTasksModule(
        IChangeOrchestrator changeOrchestrator,
        IChangeConfirmationAuthority? confirmationAuthority) =>
        new(
            "tasks",
            "任务调度",
            "计划任务批量裁剪",
            new IOptimizationOperation[]
            // 低风险：禁用计划任务，不影响系统核心功能
            {
                ScheduledTask(changeOrchestrator, confirmationAuthority, "tasks.disable_app_experience", "禁用 Application Experience", "关闭 Microsoft Compatibility Appraiser，阻止兼容性评估占用。", "任务调度", @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser", OperationRiskLevel.Safe),
                ScheduledTask(changeOrchestrator, confirmationAuthority, "tasks.disable_auto_proxy_detection", "禁用自动代理检测", "关闭 Autochk\\Proxy 计划任务，避免多余网络探测。", "任务调度", @"\Microsoft\Windows\Autochk\Proxy", OperationRiskLevel.Safe),
                ScheduledTask(changeOrchestrator, confirmationAuthority, "tasks.disable_ceip", "禁用 CEIP 计划任务", "停止 Customer Experience Improvement Program\\Consolidator。", "任务调度", @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator", OperationRiskLevel.Safe),
                ScheduledTask(changeOrchestrator, confirmationAuthority, "tasks.disable_cleanup_tasks", "禁用组件维护任务", "关闭 Servicing\\StartComponentCleanup，避免维护任务在游戏时段占用磁盘。", "任务调度", @"\Microsoft\Windows\Servicing\StartComponentCleanup", OperationRiskLevel.Safe, benefit: "减少游戏时段磁盘维护干扰", drawback: "组件存储清理与安全维护需手动重检"),
                ScheduledTask(changeOrchestrator, confirmationAuthority, "tasks.disable_defragmentation", "禁用磁盘碎片整理", "阻止 ScheduledDefrag 在高峰期运行。", "任务调度", @"\Microsoft\Windows\Defrag\ScheduledDefrag", OperationRiskLevel.Safe),
                ScheduledTask(changeOrchestrator, confirmationAuthority, "tasks.disable_disk_diagnostic", "禁用磁盘诊断", "关闭 DiskDiagnosticDataCollector，降低磁盘扫描干扰。", "任务调度", @"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector", OperationRiskLevel.Safe),
                ScheduledTask(changeOrchestrator, confirmationAuthority, "tasks.disable_ms_store_update_tasks", "禁用商店自动更新", "关闭 WindowsUpdate\\Automatic App Update。", "任务调度", @"\Microsoft\Windows\WindowsUpdate\Automatic App Update", OperationRiskLevel.Safe),
                ScheduledTask(changeOrchestrator, confirmationAuthority, "tasks.disable_ms_sync_tasks", "禁用 Sync Center", "停用 SyncCenterScheduledSync，避免后台同步。", "任务调度", @"\Microsoft\Windows\SyncCenter\SyncCenterScheduledSync", OperationRiskLevel.Safe),
                ScheduledTask(changeOrchestrator, confirmationAuthority, "tasks.disable_notification_tasks", "禁用备份通知", "关闭 WindowsBackup\\ConfigNotification，消除备份提醒。", "任务调度", @"\Microsoft\Windows\WindowsBackup\ConfigNotification", OperationRiskLevel.Safe),
                ScheduledTask(changeOrchestrator, confirmationAuthority, "tasks.disable_policy_updates", "禁用更新策略任务", "关闭 WindowsUpdate\\Scheduled Start，避免策略刷新在游戏时段抢占资源。", "任务调度", @"\Microsoft\Windows\WindowsUpdate\Scheduled Start", OperationRiskLevel.Safe, benefit: "减少游戏时段更新策略干扰", drawback: "Windows Update 策略刷新需手动重检"),
                ScheduledTask(changeOrchestrator, confirmationAuthority, "tasks.disable_remote_management", "禁用远程协助计划", "关闭 RemoteAssistanceTask，防止远程协助后台。", "任务调度", @"\Microsoft\Windows\RemoteAssistance\RemoteAssistanceTask", OperationRiskLevel.Safe),
                ScheduledTask(changeOrchestrator, confirmationAuthority, "tasks.disable_store_features", "禁用 Store Feature Installers", "阻断 CloudExperienceHost\\CreateObjectTask。", "任务调度", @"\Microsoft\Windows\CloudExperienceHost\CreateObjectTask", OperationRiskLevel.Safe),
                ScheduledTask(changeOrchestrator, confirmationAuthority, "tasks.disable_sync_features", "禁用 SettingSync", "关闭 SettingSync\\BackgroundUploadTask，避免同步占用。", "任务调度", @"\Microsoft\Windows\SettingSync\BackgroundUploadTask", OperationRiskLevel.Safe),
                ScheduledTask(changeOrchestrator, confirmationAuthority, "tasks.disable_windows_update", "禁用 Windows Update 扫描", "关闭 UpdateOrchestrator\\Schedule Scan，避免更新扫描在游戏时段占用资源。", "任务调度", @"\Microsoft\Windows\UpdateOrchestrator\Schedule Scan", OperationRiskLevel.Balanced, benefit: "减少游戏时段更新扫描干扰", drawback: "安全更新可发现性需手动重检"),
                ScheduledTask(changeOrchestrator, confirmationAuthority, "tasks.disable_xbox_features", "禁用 Xbox 功能任务", "停止 XblGameSaveTask，减少云同步触发。", "任务调度", @"\Microsoft\XblGameSave\XblGameSaveTask", OperationRiskLevel.Safe),
                ScheduledTask(changeOrchestrator, confirmationAuthority, "tasks.disable_xbox_tasks", "禁用 Xbox 相关登陆任务", "关闭 XblGameSaveTaskLogon，防止开机自启。", "任务调度", @"\Microsoft\XblGameSave\XblGameSaveTaskLogon", OperationRiskLevel.Safe)
            });

    private static OptimizationModuleDescriptor BuildServicesModule(
        IChangeOrchestrator changeOrchestrator,
        IChangeConfirmationAuthority? confirmationAuthority) =>
        new(
            "services",
            "系统服务",
            "裁剪后台常驻服务",
            new IOptimizationOperation[]
            {
                // 低风险：禁用不常用服务
                Service(changeOrchestrator, confirmationAuthority, "service.disable_spooler", "停止 Print Spooler", "在无打印需求时禁用 Spooler，避免快捷键错误触发。", "系统服务", "Spooler", OperationRiskLevel.Balanced,
                    benefit: "释放后台资源，消除打印机相关安全漏洞风险",
                    drawback: "无法使用打印机，需要打印时需手动启用"),
                Service(changeOrchestrator, confirmationAuthority, "service.disable_wsearch", "停用 Windows Search", "WSearch 服务停用后释放磁盘与内存资源。", "系统服务", "WSearch", OperationRiskLevel.Safe,
                    benefit: "大幅减少磁盘 I/O 和内存占用，HDD 用户效果明显",
                    drawback: "文件搜索速度变慢，无法使用 Windows 搜索快速查找"),
                Service(changeOrchestrator, confirmationAuthority, "service.disable_telemetry", "禁用核心遥测服务 (DiagTrack)", "关闭 Connected User Experiences and Telemetry。", "系统服务", "DiagTrack", OperationRiskLevel.Safe,
                    benefit: "阻止 Windows 收集和上传使用数据，保护隐私",
                    drawback: "微软无法收集诊断数据用于改进 Windows"),
                Service(changeOrchestrator, confirmationAuthority, "services.disable_diagtrack", "额外关闭共享遥测服务", "禁用 dmwappushservice 推送遥测服务，阻断残余上传。", "系统服务", "dmwappushservice", OperationRiskLevel.Safe,
                    benefit: "阻断残余遥测上传",
                    drawback: "部分设备管理推送可能失效"),
                Service(changeOrchestrator, confirmationAuthority, "services.disable_fax", "禁用 Fax 服务", "桌面场景极少使用传真，可关闭以减轻占用。", "系统服务", "Fax", OperationRiskLevel.Safe),
                Service(changeOrchestrator, confirmationAuthority, "services.disable_maps_broker", "停用 MapsBroker", "阻止离线地图托管服务常驻后台。", "系统服务", "MapsBroker", OperationRiskLevel.Safe),
                Service(changeOrchestrator, confirmationAuthority, "services.disable_remote_registry", "禁用 Remote Registry", "避免被远程修改注册表，提升安全性。", "系统服务", "RemoteRegistry", OperationRiskLevel.Safe),
                // FIX-33/46: SysMain 已由 memory 模块统一管理
                Service(changeOrchestrator, confirmationAuthority, "services.disable_wallet_service", "关闭 WalletService", "在非 UWP 支付场景下禁用 WalletService，减少启动项。", "系统服务", "WalletService", OperationRiskLevel.Safe),
                new MultiServiceCompositeOperation(
                    changeOrchestrator,
                    confirmationAuthority,
                    "services.disable_xbox_services",
                    "停用 Xbox 关联服务",
                    "禁用 XboxGipSvc/XblAuthManager/XblGameSave/XboxNetApiSvc/GamingServices。",
                    "系统服务",
                    OperationRiskLevel.Safe,
                    "减少后台 Xbox 服务占用",
                    "Xbox 相关功能与游戏存档同步可能不可用",
                    ["XboxGipSvc", "XblAuthManager", "XblGameSave", "XboxNetApiSvc", "GamingServices", "GamingServicesNet"]),
                // 中等风险：禁用蓝牙可能影响蓝牙设备
                Service(changeOrchestrator, confirmationAuthority, "service.bluetooth.disable", "禁用蓝牙后台服务", "在无蓝牙需求时关闭 bthserv。", "系统服务", "bthserv", OperationRiskLevel.Balanced,
                    benefit: "减少后台资源占用",
                    drawback: "蓝牙设备将无法连接"),
                Service(changeOrchestrator, confirmationAuthority, "service.wer.disable", "禁用 Windows 错误报告", "关闭 WerSvc 避免后台上传。", "系统服务", "WerSvc", OperationRiskLevel.Safe),
                // FIX-48: SensorDataService 已由 privacy 模块统一管理
                // FIX-14/16: Game DVR 和 Game Mode 已由 gaming 模块统一管理，此处删除重复
                PowerScheme(
                    changeOrchestrator,
                    confirmationAuthority,
                    "services.enable_high_performance_mode",
                    "激活高性能电源计划",
                    "仅在传统交流供电桌面、无 OEM/组织策略限制且 Windows 实际枚举到内置高性能计划时切换；其它环境仅返回系统设置引导或不可用状态。",
                    "系统服务",
                    OperationRiskLevel.Balanced,
                    "在受支持平台上可能提高持续负载响应",
                    "会增加整机功耗、温度和风扇噪声；笔记本、Modern Standby、受限或未知平台不会自动应用。")
            });

    // Why: gaming 操作已合并到 BuildPerformanceModule

    private static FailClosedSecurityOptimizationOperation BlockedSecurity(
        ISecurityProtectionOwner owner,
        SecurityProtectionArea area,
        string id,
        string title,
        string description,
        string category,
        string drawback) =>
        new(owner, area, id, title, description, category, drawback);

    private static RegistryToggleOperation RegistryDword(
        string id,
        string title,
        string description,
        string category,
        RegistryHive hive,
        string path,
        string name,
        int value,
        OperationRiskLevel riskLevel = OperationRiskLevel.Balanced,
        string? benefit = null,
        string? drawback = null,
        bool affectsUserFeeling = false) =>
        new(id, title, description, category, hive, path, name, value, RegistryValueKind.DWord) 
        { 
            RiskLevel = riskLevel, 
            Benefit = benefit, 
            Drawback = drawback,
            AffectsUserFeeling = affectsUserFeeling
        };

    private static OrchestratedRegistryDwordOperation MachinePolicyDword(
        IChangeOrchestrator changeOrchestrator,
        IChangeConfirmationAuthority? confirmationAuthority,
        string id,
        string title,
        string description,
        string category,
        string registryPath,
        string registryValueName,
        int targetValue,
        OperationRiskLevel riskLevel,
        string benefit,
        string drawback) =>
        MachineDword(
            changeOrchestrator,
            confirmationAuthority,
            id,
            title,
            description,
            category,
            registryPath,
            registryValueName,
            targetValue,
            riskLevel,
            benefit,
            drawback,
            requiresExplicitConfirmation: true);

    private static OrchestratedRegistryDwordOperation MachineTcpParameterDword(
        IChangeOrchestrator changeOrchestrator,
        IChangeConfirmationAuthority? confirmationAuthority,
        string id,
        string title,
        string description,
        string category,
        string registryValueName,
        int targetValue,
        OperationRiskLevel riskLevel,
        string benefit,
        string drawback,
        bool requiresExplicitConfirmation) =>
        MachineDword(
            changeOrchestrator,
            confirmationAuthority,
            id,
            title,
            description,
            category,
            @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters",
            registryValueName,
            targetValue,
            riskLevel,
            benefit,
            drawback,
            requiresExplicitConfirmation);

    private static OrchestratedRegistryDwordOperation MachineDword(
        IChangeOrchestrator changeOrchestrator,
        IChangeConfirmationAuthority? confirmationAuthority,
        string id,
        string title,
        string description,
        string category,
        string registryPath,
        string registryValueName,
        int targetValue,
        OperationRiskLevel riskLevel,
        string benefit,
        string drawback,
        bool requiresExplicitConfirmation) =>
        new(
            changeOrchestrator,
            id,
            title,
            description,
            category,
            registryPath,
            registryValueName,
            targetValue,
            riskLevel,
            benefit,
            drawback,
            confirmationAuthority,
            requiresExplicitConfirmation,
            scopeKind: TargetScopeKind.Machine,
            requiresAdministrator: true);

    private sealed class UnavailableChangeOrchestrator : IChangeOrchestrator
    {
        public static UnavailableChangeOrchestrator Instance { get; } = new();

        public Task<StateObservation?> ObserveAsync(string capabilityId, TargetScope scope, CancellationToken cancellationToken) =>
            Task.FromResult<StateObservation?>(null);

        public Task<PreparationResult> PrepareAsync(ChangeRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new PreparationResult(
                ChangePreparationStatus.Blocked,
                null,
                "composition.missing",
                "该迁移能力必须通过应用依赖注入容器执行。",
                null));

        public Task<BatchReceipt> ExecuteAsync(PreparedChangePlan plan, ConfirmationToken confirmation, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("迁移能力未连接到应用依赖注入容器。");

        public Task<BatchReceipt> RestoreAsync(RestoreRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("迁移能力未连接到应用依赖注入容器。");
    }

    private static RegistryToggleOperation RegistryString(
        string id,
        string title,
        string description,
        string category,
        RegistryHive hive,
        string path,
        string name,
        string value,
        OperationRiskLevel riskLevel = OperationRiskLevel.Balanced,
        bool affectsUserFeeling = false) =>
        new(id, title, description, category, hive, path, name, value, RegistryValueKind.String) 
        { 
            RiskLevel = riskLevel,
            AffectsUserFeeling = affectsUserFeeling
        };

    private static RegistryToggleOperation DenyCapability(
        string id,
        string title,
        string description,
        string category,
        string capability,
        RegistryHive hive = RegistryHive.CurrentUser,
        OperationRiskLevel riskLevel = OperationRiskLevel.Safe) =>
        RegistryString(
            id,
            title,
            description,
            category,
            hive,
            $@"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\{capability}",
            "Value",
            "Deny",
            riskLevel);

    private static OrchestratedServiceOptimizationOperation Service(
        IChangeOrchestrator changeOrchestrator,
        IChangeConfirmationAuthority? confirmationAuthority,
        string id,
        string title,
        string description,
        string category,
        string serviceName,
        OperationRiskLevel riskLevel = OperationRiskLevel.Balanced,
        string? benefit = null,
        string? drawback = null) =>
        new(changeOrchestrator, id, title, description, category, serviceName, riskLevel, benefit, drawback, confirmationAuthority);

    private static FailClosedServiceOptimizationOperation BlockedService(
        string id,
        string title,
        string description,
        string category,
        OperationRiskLevel riskLevel,
        string? benefit,
        string? drawback,
        string reason) =>
        new(id, title, description, category, riskLevel, benefit, drawback, reason);

    private static OrchestratedPowerSettingOperation PowerSetting(
        IChangeOrchestrator changeOrchestrator,
        IChangeConfirmationAuthority? confirmationAuthority,
        string id,
        string title,
        string description,
        string category,
        OperationRiskLevel riskLevel,
        string? benefit,
        string? drawback) =>
        new(changeOrchestrator, id, title, description, category, riskLevel, benefit, drawback, confirmationAuthority);

    private static OrchestratedPowerSchemeOperation PowerScheme(
        IChangeOrchestrator changeOrchestrator,
        IChangeConfirmationAuthority? confirmationAuthority,
        string id,
        string title,
        string description,
        string category,
        OperationRiskLevel riskLevel,
        string? benefit,
        string? drawback) =>
        new(changeOrchestrator, id, title, description, category, riskLevel, benefit, drawback, confirmationAuthority);

    private static PolicyDeniedOptimizationOperation PolicyDenied(
        string id,
        string title,
        string description,
        string category,
        OperationType operationType,
        OperationRiskLevel riskLevel,
        string? benefit,
        string? drawback) =>
        new(id, title, description, category, operationType, riskLevel, benefit, drawback,
            "当前版本没有可验证的执行与恢复链；本操作暂不执行，不会写入系统。");

    private static OrchestratedDeviceStateOperation Device(
        IChangeOrchestrator changeOrchestrator,
        IChangeConfirmationAuthority? confirmationAuthority,
        string id,
        string title,
        string description,
        string category,
        OperationRiskLevel riskLevel,
        string? benefit,
        string? drawback) =>
        new(changeOrchestrator, id, title, description, category, riskLevel, benefit, drawback, confirmationAuthority);

    private static OrchestratedBcdSettingOperation Bcd(
        IChangeOrchestrator changeOrchestrator,
        IChangeConfirmationAuthority? confirmationAuthority,
        string id,
        string title,
        string description,
        string category,
        OperationRiskLevel riskLevel,
        string? benefit,
        string? drawback) =>
        new(changeOrchestrator, id, title, description, category, riskLevel, benefit, drawback, confirmationAuthority);

    private static OrchestratedScheduledTaskOptimizationOperation ScheduledTask(
        IChangeOrchestrator changeOrchestrator,
        IChangeConfirmationAuthority? confirmationAuthority,
        string id,
        string title,
        string description,
        string category,
        string taskPath,
        OperationRiskLevel riskLevel = OperationRiskLevel.Balanced,
        string? benefit = null,
        string? drawback = null) =>
        new(
            changeOrchestrator,
            id,
            title,
            description,
            category,
            taskPath,
            riskLevel,
            benefit,
            drawback,
            confirmationAuthority);

    // Why: device 操作已合并到 BuildPeripheralModule

    // Why: accessibility 操作已合并到 BuildPeripheralModule
    // Why: gpu_advanced 操作已合并到 BuildGraphicsModule

    private static OptimizationModuleDescriptor BuildMemoryModule(
        IChangeOrchestrator changeOrchestrator,
        IChangeConfirmationAuthority? confirmationAuthority) =>
        new(
            "memory",
            "内存优化",
            "优化内存管理策略，减少后台内存占用",
            // FIX-47: DisableKernelPaging 由 system 模块统一管理
            new IOptimizationOperation[]
            {
                new OrchestratedMmAgentOperation(
                    changeOrchestrator,
                    "memory.disable_compression",
                    "关闭内存压缩",
                    "通过 MMAgent 关闭内存压缩，减少 CPU 解压开销，适合大内存用户。",
                    "内存优化",
                    OperationRiskLevel.Balanced,
                    "降低 CPU 占用，减少解压延迟",
                    "可能增加页面文件压力并降低稳定性；需重启生效；已标红，需显式确认后才会写入。",
                    confirmationAuthority),
                Service(changeOrchestrator, confirmationAuthority, "memory.disable_superfetch", "禁用 SuperFetch", "关闭 SysMain 服务的预取功能，减少后台磁盘活动，适合 SSD 用户。", "内存优化", "SysMain", OperationRiskLevel.Balanced, "减少后台磁盘活动，降低 SSD 磨损", "可能降低 HDD 上的应用启动速度"),
                MachineDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "memory.enable_large_pages",
                    "启用大页面",
                    "将 LargePageMinimum=2097152（2MB），启用大页面支持；仅写注册表值，不授予锁定内存权限。",
                    "内存优化",
                    @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
                    "LargePageMinimum",
                    2097152,
                    OperationRiskLevel.Extreme,
                    "大页面可降低 TLB 未命中，提升大内存应用吞吐",
                    "可能扩大攻击面、浪费内存或导致应用不兼容；需重启生效",
                    requiresExplicitConfirmation: true),
                MachineDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "memory.optimize_system_cache",
                    "优化系统缓存",
                    "将 LargeSystemCache=0，减少大文件缓存占用，适合 SSD 用户。",
                    "内存优化",
                    @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
                    "LargeSystemCache",
                    0,
                    OperationRiskLevel.Safe,
                    "减少缓存占用，降低 SSD 写入放大",
                    "可能降低文件缓存命中率",
                    requiresExplicitConfirmation: true),
                MachineDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "memory.optimize_pagefile",
                    "关闭关机清除页面文件",
                    "将 ClearPageFileAtShutdown=0，关闭关机时清除页面文件以缩短关机时间；仅写注册表值，不配置页面文件大小。",
                    "内存优化",
                    @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
                    "ClearPageFileAtShutdown",
                    0,
                    OperationRiskLevel.Extreme,
                    "缩短关机时间",
                    "关机时不清除页面文件，可能残留敏感数据",
                    requiresExplicitConfirmation: true),
                MachineDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "memory.optimize_priority",
                    "优化内存优先级",
                    "将 Win32PrioritySeparation=38（0x26），让前台游戏获得更高调度权重。",
                    "内存优化",
                    @"SYSTEM\CurrentControlSet\Control\PriorityControl",
                    "Win32PrioritySeparation",
                    38,
                    OperationRiskLevel.Safe,
                    "前台进程响应更快",
                    "后台进程响应变慢",
                    requiresExplicitConfirmation: true)
            });

    private static OptimizationModuleDescriptor BuildAudioModule(
        IChangeOrchestrator changeOrchestrator,
        IChangeConfirmationAuthority? confirmationAuthority) =>
        new(
            "audio",
            "音频优化",
            "降低音频处理延迟，优化游戏音效体验",
            new IOptimizationOperation[]
            {
                new OrchestratedRegistryDwordOperation(
                    changeOrchestrator,
                    "audio.disable_enhancements",
                    "禁用音频增强",
                    "将 DisableSystemEffects=1，关闭 Windows 音频增强效果，减少音频处理延迟。",
                    "音频优化",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Audio",
                    "DisableSystemEffects",
                    1,
                    OperationRiskLevel.Safe,
                    "可能减少音频处理延迟，声音更真实",
                    "失去虚拟环绕声等音效增强",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                MachineDword(
                    changeOrchestrator,
                    confirmationAuthority,
                    "audio.exclusive_mode_priority",
                    "音频独占模式优先",
                    "将 Audio 多媒体任务 Priority=6，提升音频任务调度优先级，获得更低延迟的音频输出。",
                    "音频优化",
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Audio",
                    "Priority",
                    6,
                    OperationRiskLevel.Safe,
                    "可能获得更低延迟的音频输出",
                    "独占模式下其他应用无法播放声音",
                    requiresExplicitConfirmation: true),
                new OrchestratedRegistryDwordOperation(
                    changeOrchestrator,
                    "audio.disable_spatial_sound",
                    "禁用空间音效",
                    "将 EnableSpatialAudio=0，关闭 Windows Sonic 等空间音效处理，减少音频延迟。",
                    "音频优化",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Audio",
                    "EnableSpatialAudio",
                    0,
                    OperationRiskLevel.Safe,
                    "减少音频处理延迟",
                    "失去 3D 环绕声效果",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true),
                new OrchestratedRegistryStringOperation(
                    changeOrchestrator,
                    "audio.optimize_buffer",
                    "优化音频缓冲区",
                    "将 Games 任务的 Latency Sensitive=True，降低音频缓冲延迟约 10-20ms。",
                    "音频优化",
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games",
                    "Latency Sensitive",
                    "True",
                    OperationRiskLevel.Balanced,
                    "降低音频延迟约 10-20ms",
                    "低端声卡可能出现爆音",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true,
                    scopeKind: TargetScopeKind.Machine,
                    requiresAdministrator: true),
                new OrchestratedRegistryDwordOperation(
                    changeOrchestrator,
                    "audio.disable_service_restart",
                    "禁用音频服务自动重启",
                    "将 Audiosrv 的 DelayedAutoStart=0，加快音频服务响应，避免游戏音频中断。",
                    "音频优化",
                    @"SYSTEM\CurrentControlSet\Services\Audiosrv",
                    "DelayedAutoStart",
                    0,
                    OperationRiskLevel.Safe,
                    "避免游戏中音频突然中断",
                    "音频服务崩溃后需手动重启",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true,
                    scopeKind: TargetScopeKind.Machine,
                    requiresAdministrator: true),
                new OrchestratedRegistryValueSetOperation(
                    changeOrchestrator,
                    "audio.optimize_sample_rate",
                    "优化音频采样率",
                    "设置 Audio 多媒体任务 Priority=6、Affinity=0、Scheduling Category/SFIO Priority=High，降低音频处理延迟。",
                    "音频优化",
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Audio",
                    "Priority;Affinity;Scheduling Category;SFIO Priority",
                    "6;0;High;High",
                    OperationRiskLevel.Safe,
                    "获得更好的音频质量与更低延迟",
                    "略微增加 CPU 开销",
                    OrchestratedRegistryValueSetOperation.CreateOptimizeSampleRateTargetPayload,
                    confirmationAuthority,
                    scopeKind: TargetScopeKind.Machine,
                    requiresAdministrator: true),
                new OrchestratedRegistryDwordOperation(
                    changeOrchestrator,
                    "audio.disable_comm_ducking",
                    "禁用通信音量降低",
                    "将 UserDuckingPreference=3，关闭通话时自动压低其他音量的功能。",
                    "音频优化",
                    @"SOFTWARE\Microsoft\Multimedia\Audio",
                    "UserDuckingPreference",
                    3,
                    OperationRiskLevel.Safe,
                    "游戏音量不会被语音通话影响",
                    "通话时可能听不清对方",
                    confirmationAuthority,
                    requiresExplicitConfirmation: true)
            });

    // Why: power_advanced 操作已合并到 BuildSystemModule
    // Why: security_mitigation 操作已合并到 BuildPerformanceModule
    // Why: input_latency 操作已合并到 BuildPeripheralModule
    // Why: gpu_deep 操作已合并到 BuildGraphicsModule
    // Why: storage_optimization 操作已合并到 BuildSystemModule
    // Why: network_advanced 操作已合并到 BuildNetworkModule
    // Why: windows_features 操作已合并到 BuildExplorerModule
    // Why: cpu_advanced/intel_optimization/amd_optimization 操作已合并到 BuildCpuModule
}
