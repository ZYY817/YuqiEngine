using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Infrastructure.Windows.Registry;

/// <summary>
/// Immutable definitions for the M8 policy DWORD migration batch.  Each capability owns one
/// registry value; no definition shares a resource key with another optimization capability.
/// </summary>
public static class OptimizationRegistryDwordCapabilityDefinitions
{
    public static readonly RegistryDwordCapabilityDefinition DisableErrorReporting = MachinePolicy(
        "privacy.disable_error_reporting",
        @"SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting",
        "Disabled",
        1,
        ChangeRisk.LowReversible);

    public static readonly RegistryDwordCapabilityDefinition DisableStepsRecorder = MachinePolicy(
        "privacy.disable_steps_recorder",
        @"SOFTWARE\Policies\Microsoft\Windows\AppCompat",
        "DisableStepsRecorder",
        1,
        ChangeRisk.LowReversible);

    public static readonly RegistryDwordCapabilityDefinition DisableInventoryCollector = MachinePolicy(
        "privacy.disable_inventory_collector",
        @"SOFTWARE\Policies\Microsoft\Windows\AppCompat",
        "DisableInventory",
        1,
        ChangeRisk.LowReversible);

    public static readonly RegistryDwordCapabilityDefinition DisableCloudSpeech = MachinePolicy(
        "privacy.disable_cloud_speech",
        @"SOFTWARE\Policies\Microsoft\InputPersonalization",
        "AllowInputPersonalization",
        0,
        ChangeRisk.LowReversible);

    public static readonly RegistryDwordCapabilityDefinition DisableWebcam = MachinePolicy(
        "privacy.disable_webcam",
        @"SOFTWARE\Policies\Microsoft\Camera",
        "AllowCamera",
        0,
        ChangeRisk.MediumRebootOrExperience);

    public static readonly RegistryDwordCapabilityDefinition DisableOneDriveSync = MachinePolicy(
        "privacy.disable_onedrive_sync",
        @"SOFTWARE\Policies\Microsoft\Windows\OneDrive",
        "DisableFileSyncNGSC",
        1,
        ChangeRisk.LowReversible);

    public static readonly RegistryDwordCapabilityDefinition DisableAppImpactTelemetry = MachinePolicy(
        "privacy.disable_app_impact_telemetry",
        @"SOFTWARE\Policies\Microsoft\Windows\AppCompat",
        "AITEnable",
        0,
        ChangeRisk.LowReversible);

    public static readonly RegistryDwordCapabilityDefinition IncreaseMaxUserPort = MachineTcpParameter(
        "network.increase_max_user_port",
        "MaxUserPort",
        65534,
        ChangeRisk.MediumRebootOrExperience);

    public static readonly RegistryDwordCapabilityDefinition OptimizeTcpTimedWait = MachineTcpParameter(
        "network.optimize_tcp_timed_wait",
        "TcpTimedWaitDelay",
        30,
        ChangeRisk.MediumRebootOrExperience);

    public static readonly RegistryDwordCapabilityDefinition DisableDiagnosticEvents = MachinePolicy(
        "adjustments.disable_diagnostic_events",
        @"SYSTEM\CurrentControlSet\Control\WMI\AutoLogger\AutoLogger-Diagtrack-Listener",
        "Start",
        0,
        ChangeRisk.LowReversible);

    public static readonly RegistryDwordCapabilityDefinition DisableWifiSense = MachinePolicy(
        "privacy.disable_wifi_sense",
        @"SOFTWARE\Microsoft\WcmSvc\wifinetworkmanager\config",
        "AutoConnectAllowedOEM",
        0,
        ChangeRisk.LowReversible);

    public static readonly RegistryDwordCapabilityDefinition OptimizeTtl = MachineTcpParameter(
        "network.optimize_ttl",
        "DefaultTTL",
        64,
        ChangeRisk.MediumRebootOrExperience);

    public static readonly RegistryDwordCapabilityDefinition OptimizeTcpMaxDupAcks = MachineTcpParameter(
        "network.optimize_tcp_max_dup_acks",
        "TcpMaxDupAcks",
        2,
        ChangeRisk.MediumRebootOrExperience);

    // 备注：本项把 TCP 自动调优级别（TcpAutotuningLevel）写为 0 以禁用接收窗口自动调优，
    // 获得确定性延迟；只改变 TCP/IP 栈参数，不删除系统保护。写入前捕获原值，恢复时按原值回写；
    // 缺失值恢复为删除。重启后由 TCP/IP 栈重新读取。
    public static readonly RegistryDwordCapabilityDefinition DisableTcpAutoTuning = MachineTcpParameter(
        "network.disable_tcp_auto_tuning",
        "TcpAutotuningLevel",
        0,
        ChangeRisk.MediumRebootOrExperience);

    // 备注：本项把 ECN 能力（ECNCapability）写为 0 以关闭显式拥塞通知，避免固件兼容问题；
    // 只改变 TCP/IP 栈参数，不删除系统保护。写入前捕获原值，恢复时按原值回写；缺失值恢复为删除。
    public static readonly RegistryDwordCapabilityDefinition DisableEcnCapability = MachineTcpParameter(
        "network.disable_ecn_capability",
        "ECNCapability",
        0,
        ChangeRisk.MediumRebootOrExperience);

    // 备注：本项把接收段合并（EnableRSC）写为 0 以停止 RSC，使数据包立即到达协议栈；
    // 只改变 TCP/IP 栈参数，不删除系统保护。写入前捕获原值，恢复时按原值回写；缺失值恢复为删除。
    public static readonly RegistryDwordCapabilityDefinition DisableRsc = MachineTcpParameter(
        "network.disable_rsc",
        "EnableRSC",
        0,
        ChangeRisk.MediumRebootOrExperience);

    // 备注：本项把接收端缩放（EnableRSS）写为 1 以启用 RSS，使网卡负载分散到多核；
    // 只改变 TCP/IP 栈参数，不删除系统保护。写入前捕获原值，恢复时按原值回写；缺失值恢复为删除。
    public static readonly RegistryDwordCapabilityDefinition OptimizeRss = MachineTcpParameter(
        "network.optimize_rss",
        "EnableRSS",
        1,
        ChangeRisk.MediumRebootOrExperience);

    // 备注：本项把 RFC1323 时间戳（Tcp1323Opts）写为 1 以启用时间戳支持，保持长距离延迟可见性；
    // 只改变 TCP/IP 栈参数，不删除系统保护。写入前捕获原值，恢复时按原值回写；缺失值恢复为删除。
    public static readonly RegistryDwordCapabilityDefinition EnableTcp1323 = MachineTcpParameter(
        "network.enable_tcp1323",
        "Tcp1323Opts",
        1,
        ChangeRisk.MediumRebootOrExperience);

    public static readonly RegistryDwordCapabilityDefinition OptimizeSystemResponsiveness = Machine(
        "cpu.system_responsiveness",
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
        "SystemResponsiveness",
        10,
        ChangeRisk.LowReversible,
        requiresRestart: false);

    public static readonly RegistryDwordCapabilityDefinition DisableStoreAutoUpdate = MachinePolicy(
        "basic.disable_store_auto_update",
        @"SOFTWARE\Policies\Microsoft\WindowsStore",
        "AutoDownload",
        2,
        ChangeRisk.LowReversible);

    public static readonly RegistryDwordCapabilityDefinition DisableNetworkThrottlingIndex = Machine(
        "adjustments.network_throttling_index",
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
        "NetworkThrottlingIndex",
        unchecked((int)0xFFFFFFFF),
        ChangeRisk.LowReversible,
        requiresRestart: false);

    public static readonly RegistryDwordCapabilityDefinition DisableCortana = MachinePolicy(
        "privacy.disable_cortana",
        @"SOFTWARE\Policies\Microsoft\Windows\Windows Search",
        "AllowCortana",
        0,
        ChangeRisk.LowReversible);

    public static readonly RegistryDwordCapabilityDefinition DisablePrefetch = Machine(
        "system.disable_prefetch",
        @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters",
        "EnablePrefetcher",
        0,
        ChangeRisk.LowReversible,
        requiresRestart: true);

    public static readonly RegistryDwordCapabilityDefinition BoostGamePriority = Machine(
        "system.boost_game_priority",
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games",
        "Priority",
        6,
        ChangeRisk.LowReversible,
        requiresRestart: false);

    public static readonly RegistryDwordCapabilityDefinition EnableTsx = Machine(
        "cpu.enable_tsx",
        @"SYSTEM\CurrentControlSet\Control\Session Manager\Kernel",
        "DisableTsx",
        0,
        ChangeRisk.MediumRebootOrExperience,
        requiresRestart: true);

    public static readonly RegistryDwordCapabilityDefinition DisableLicenseTelemetry = MachinePolicy(
        "privacy.disable_license_telemetry",
        @"SOFTWARE\Policies\Microsoft\Windows NT\CurrentVersion\Software Protection Platform",
        "NoGenTicket",
        1,
        ChangeRisk.LowReversible);

    public static readonly RegistryDwordCapabilityDefinition DisableCompatibilityTelemetry = MachinePolicy(
        "privacy.disable_compatibility_telemetry",
        @"SOFTWARE\Policies\Microsoft\Windows\AppCompat",
        "DisableUAR",
        1,
        ChangeRisk.LowReversible);

    public static readonly RegistryDwordCapabilityDefinition DisablePrivacyConsent = MachinePolicy(
        "privacy.disable_privacy_consent",
        @"SOFTWARE\Policies\Microsoft\Windows\OOBE",
        "DisablePrivacyExperience",
        1,
        ChangeRisk.LowReversible);

    // 备注：本项把“还原点创建频率”写为 0 以绕过 Windows 默认 24 小时还原点冷却窗口，
    // 只改变允许创建还原点的频率，不删除或禁用系统保护；原值在写入前被捕获，恢复时按原值回写。
    public static readonly RegistryDwordCapabilityDefinition RestorePointFrequency = Machine(
        "power.restore_point_frequency",
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore",
        "SystemRestorePointCreationFrequency",
        0,
        ChangeRisk.MediumRebootOrExperience,
        requiresRestart: false);

    // 备注：本项把 S0 Modern Standby（现代待机）期间的网络连接策略写为“断开”（AC/DC 均为 0），
    // 以消除待机期间后台联网导致的功耗与潜在网络活动；只影响待机联网策略，不改变待机本身或系统保护。
    // 写入前捕获原值，恢复时按原值回写；缺失值恢复为删除。仅对支持 S0 Modern Standby 的机型有意义。
    public static readonly RegistryDwordCapabilityDefinition DisableModernStandbyNetworkingAc = MachinePolicy(
        "power.s0_network_ac",
        @"SOFTWARE\Policies\Microsoft\Power\PowerSettings\f15576e8-98b7-4186-b944-eafa664402d9",
        "ACSettingIndex",
        0,
        ChangeRisk.MediumRebootOrExperience);

    public static readonly RegistryDwordCapabilityDefinition DisableModernStandbyNetworkingDc = MachinePolicy(
        "power.s0_network_dc",
        @"SOFTWARE\Policies\Microsoft\Power\PowerSettings\f15576e8-98b7-4186-b944-eafa664402d9",
        "DCSettingIndex",
        0,
        ChangeRisk.MediumRebootOrExperience);

    // 备注：本项把 HibernateEnabled 写为 0 以禁用休眠并允许系统回收 hiberfil.sys；
    // 只关闭休眠功能，不删除系统保护或改变其他电源方案。写入前捕获原值，恢复时按原值回写。
    public static readonly RegistryDwordCapabilityDefinition DisableHibernation = Machine(
        "system.disable_hibernation",
        @"SYSTEM\CurrentControlSet\Control\Power",
        "HibernateEnabled",
        0,
        ChangeRisk.MediumRebootOrExperience,
        requiresRestart: false);

    // 备注：本项把 QoS 数据包调度的保留带宽上限（NonBestEffortLimit）写为 0，
    // 释放 Windows 默认保留的 20% 带宽；只改变 QoS 保留策略，不删除或禁用系统保护。
    // 写入前捕获原值，恢复时按原值回写；缺失值恢复为删除。
    public static readonly RegistryDwordCapabilityDefinition DisableQosPacketScheduler = MachinePolicy(
        "network.disable_qos_scheduler",
        @"SOFTWARE\Policies\Microsoft\Windows\Psched",
        "NonBestEffortLimit",
        0,
        ChangeRisk.MediumRebootOrExperience);

    // 备注：本项把 NetBT 的 LMHOSTS 查询（EnableLMHOSTS）写为 0 以禁用基于 LMHOSTS 文件的
    // NetBIOS 名称解析，减少后台名称解析延迟；只影响名称解析方式，不删除系统保护。
    // 写入前捕获原值，恢复时按原值回写；缺失值恢复为删除。
    public static readonly RegistryDwordCapabilityDefinition DisableLmhostsLookup = MachinePolicy(
        "network.disable_lmhosts",
        @"SYSTEM\CurrentControlSet\Services\NetBT\Parameters",
        "EnableLMHOSTS",
        0,
        ChangeRisk.MediumRebootOrExperience);

    // 备注：本项把剪贴板历史（EnableClipboardHistory）写为 0 以禁用 Win+V 历史记录，
    // 保护剪贴板隐私；只影响当前用户剪贴板历史，不删除系统保护。写入前捕获原值，恢复时按原值回写。
    public static readonly RegistryDwordCapabilityDefinition DisableClipboardHistory = new(
        "windows.disable_clipboard_history",
        "1.0.0",
        @"SOFTWARE\Microsoft\Clipboard",
        "EnableClipboardHistory",
        0,
        ChangeRisk.MediumRebootOrExperience,
        RequiresExplicitConfirmation: true);

    // 备注：本项把专注助手（NOC_GLOBAL_SETTING_TOASTS_ENABLED）写为 1 以确保游戏通知不被拦截；
    // 只影响当前用户通知设置，不删除系统保护。写入前捕获原值，恢复时按原值回写。
    public static readonly RegistryDwordCapabilityDefinition DisableFocusAssist = new(
        "windows.disable_focus_assist",
        "1.0.0",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Notifications\Settings",
        "NOC_GLOBAL_SETTING_TOASTS_ENABLED",
        1,
        ChangeRisk.MediumRebootOrExperience,
        RequiresExplicitConfirmation: true);

    // 备注：本项把 LargeSystemCache 写为 0 以减少大文件缓存占用，适合 SSD 用户；
    // 只改变系统缓存策略，不删除系统保护。写入前捕获原值，恢复时按原值回写。
    public static readonly RegistryDwordCapabilityDefinition OptimizeSystemCache = Machine(
        "memory.optimize_system_cache",
        @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
        "LargeSystemCache",
        0,
        ChangeRisk.LowReversible,
        requiresRestart: false);

    // 备注：本项把 Win32PrioritySeparation 写为 38（0x26）以让前台游戏获得更高调度权重；
    // 只改变处理器调度策略，不删除系统保护。写入前捕获原值，恢复时按原值回写。
    public static readonly RegistryDwordCapabilityDefinition OptimizeMemoryPriority = Machine(
        "memory.optimize_priority",
        @"SYSTEM\CurrentControlSet\Control\PriorityControl",
        "Win32PrioritySeparation",
        38,
        ChangeRisk.LowReversible,
        requiresRestart: false);

    // 备注：本项把 GameBar 的 AutoGameModeEnabled 写为 0 以关闭 Windows 游戏模式自动优化；
    // 只改变当前用户 GameBar 设置，不删除系统保护。写入前捕获原值，恢复时按原值回写；
    // 缺失值恢复为删除。与 repair 侧 GameModeAutoEnable（target=1）是相反目标、不同 capability id。
    public static readonly RegistryDwordCapabilityDefinition DisableGameModeOptimizations = new(
        "gpu.disable_game_mode_optimizations",
        "1.0.0",
        @"SOFTWARE\Microsoft\GameBar",
        "AutoGameModeEnabled",
        0,
        ChangeRisk.MediumRebootOrExperience,
        RequiresExplicitConfirmation: true,
        Hive: RegistryDwordHive.CurrentUser);

    // 备注：本项把 LargePageMinimum 写为 2097152（2MB）以启用大页面；
    // 仅写注册表值，不授予 SeLockMemoryPrivilege（与旧实现一致）。写入前捕获原值，
    // 恢复时按原值回写；缺失值恢复为删除。该值由内存管理器在启动时读取，需重启后生效。
    public static readonly RegistryDwordCapabilityDefinition EnableLargePages = Machine(
        "memory.enable_large_pages",
        @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
        "LargePageMinimum",
        2097152,
        ChangeRisk.HighSystem,
        requiresRestart: true);

    // 备注：本项把 ClearPageFileAtShutdown 写为 0 以关闭关机时清除页面文件、减少关机时间；
    // 仅写注册表值，不配置 pagefile 大小/位置。写入前捕获原值，恢复时按原值回写；
    // 缺失值恢复为删除。关机时生效，非重启。
    public static readonly RegistryDwordCapabilityDefinition OptimizePagefileClearAtShutdown = Machine(
        "memory.optimize_pagefile",
        @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
        "ClearPageFileAtShutdown",
        0,
        ChangeRisk.HighSystem,
        requiresRestart: false);

    // 备注：本项把 NtfsDisable8dot3NameCreation 写为 1 以禁用 NTFS 8.3 短文件名生成，
    // 减少目录枚举与磁盘写入；只改变文件系统命名行为，不删除系统保护。写入前捕获原值，
    // 恢复时按原值回写；缺失值恢复为删除。卷级设置需重启后由 NTFS 重新读取。
    public static readonly RegistryDwordCapabilityDefinition DisableNtfs8Dot3 = Machine(
        "storage.disable_8dot3",
        @"SYSTEM\CurrentControlSet\Control\FileSystem",
        "NtfsDisable8dot3NameCreation",
        1,
        ChangeRisk.MediumRebootOrExperience,
        requiresRestart: true);

    // 备注：本项把 IoPageLockLimit 写为 983040（约 960MB）以提升大文件 I/O 性能；
    // 只改变内存管理 I/O 缓冲上限，不删除系统保护。写入前捕获原值，恢复时按原值回写；
    // 缺失值恢复为删除。该值由内存管理器在启动时读取，需重启后生效。
    public static readonly RegistryDwordCapabilityDefinition OptimizeIoPageLockLimit = Machine(
        "storage.optimize_io_page_lock",
        @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
        "IoPageLockLimit",
        983040,
        ChangeRisk.MediumRebootOrExperience,
        requiresRestart: true);

    // 备注：本项把 NtfsDisableEncryption 写为 1 以禁用 NTFS 文件系统加密（EFS）；
    // 只禁止新建 EFS 加密文件，不影响已加密文件或密钥，也不删除任何用户数据。
    // 写入前捕获原值，恢复时按原值回写；缺失值恢复为删除。该值由文件系统在下次加密操作时读取。
    public static readonly RegistryDwordCapabilityDefinition DisableNtfsEncryption = Machine(
        "storage.disable_ntfs_encryption",
        @"SYSTEM\CurrentControlSet\Control\FileSystem",
        "NtfsDisableEncryption",
        1,
        ChangeRisk.MediumRebootOrExperience,
        requiresRestart: false);

    // 备注：本项把机器策略 AllowGameDVR 写为 0 以禁用 Game DVR 录制；
    // 只改变机器级 Game DVR 策略，不删除系统保护。写入前捕获原值，恢复时按原值回写；
    // 缺失值恢复为删除。策略由 GameDVR 服务在策略刷新后读取。
    public static readonly RegistryDwordCapabilityDefinition DisableGameDvrPolicy = MachinePolicy(
        "gaming.dvr.disable",
        @"SOFTWARE\Policies\Microsoft\Windows\GameDVR",
        "AllowGameDVR",
        0,
        ChangeRisk.MediumRebootOrExperience);

    // 备注：本项把音频增强（DisableSystemEffects）写为 1 以关闭 Windows 音频增强处理，减少延迟；
    // 只影响当前用户音频设置，不删除系统保护。写入前捕获原值，恢复时按原值回写。
    public static readonly RegistryDwordCapabilityDefinition DisableAudioEnhancements = new(
        "audio.disable_enhancements",
        "1.0.0",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Audio",
        "DisableSystemEffects",
        1,
        ChangeRisk.LowReversible,
        RequiresExplicitConfirmation: true);

    // 备注：本项把空间音效（EnableSpatialAudio）写为 0 以关闭 Windows Sonic 等空间音效处理；
    // 只影响当前用户音频设置，不删除系统保护。写入前捕获原值，恢复时按原值回写。
    public static readonly RegistryDwordCapabilityDefinition DisableSpatialSound = new(
        "audio.disable_spatial_sound",
        "1.0.0",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Audio",
        "EnableSpatialAudio",
        0,
        ChangeRisk.LowReversible,
        RequiresExplicitConfirmation: true);

    // 备注：本项把通信音量降低（UserDuckingPreference）写为 3 以关闭通话时自动压低其他音量；
    // 只影响当前用户音频设置，不删除系统保护。写入前捕获原值，恢复时按原值回写。
    public static readonly RegistryDwordCapabilityDefinition DisableCommDucking = new(
        "audio.disable_comm_ducking",
        "1.0.0",
        @"SOFTWARE\Microsoft\Multimedia\Audio",
        "UserDuckingPreference",
        3,
        ChangeRisk.LowReversible,
        RequiresExplicitConfirmation: true);

    // 备注：本项把音频服务延迟启动（DelayedAutoStart）写为 0 以加快音频服务响应；
    // 只改变服务启动策略，不删除系统保护。写入前捕获原值，恢复时按原值回写。
    public static readonly RegistryDwordCapabilityDefinition DisableAudioServiceRestart = Machine(
        "audio.disable_service_restart",
        @"SYSTEM\CurrentControlSet\Services\Audiosrv",
        "DelayedAutoStart",
        0,
        ChangeRisk.LowReversible,
        requiresRestart: false);

    // 备注：本项把 Games 任务的 GPU Priority 写为 8 以提升原始输入路径的响应；
    // 只改变多媒体任务调度参数，不删除系统保护。写入前捕获原值，恢复时按原值回写。
    public static readonly RegistryDwordCapabilityDefinition OptimizeRawInputBuffer = Machine(
        "input.optimize_raw_input_buffer",
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games",
        "GPU Priority",
        8,
        ChangeRisk.LowReversible,
        requiresRestart: false);

    // 备注：本项把 Audio 多媒体任务的 Priority 写为 6 以提升音频任务调度优先级；
    // 只改变多媒体任务调度参数，不删除系统保护。写入前捕获原值，恢复时按原值回写。
    public static readonly RegistryDwordCapabilityDefinition AudioExclusiveModePriority = Machine(
        "audio.exclusive_mode_priority",
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Audio",
        "Priority",
        6,
        ChangeRisk.LowReversible,
        requiresRestart: false);

    // 备注：本项把 GraphicsDrivers 的 VRROptimizeEnable 写为 0 以关闭可变刷新率（VRR/FreeSync/G-Sync）；
    // 只改变显卡驱动策略，不删除系统保护。写入前捕获原值，恢复时按原值回写。
    public static readonly RegistryDwordCapabilityDefinition DisableVrr = Machine(
        "gpu.disable_vrr",
        @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers",
        "VRROptimizeEnable",
        0,
        ChangeRisk.MediumRebootOrExperience,
        requiresRestart: false);

    // 备注：本项把 SQMClient\\Windows 的 CEIPEnable 写为 0 以退出客户体验改善计划（CEIP）；
    // 只改变遥测开关，不删除系统保护。写入前捕获原值，恢复时按原值回写；缺失值恢复为删除。
    public static readonly RegistryDwordCapabilityDefinition DisableCeipSqmClient = Machine(
        "privacy.disable_ceip.sqm_client",
        @"SOFTWARE\Microsoft\SQMClient\Windows",
        "CEIPEnable",
        0,
        ChangeRisk.LowReversible,
        requiresRestart: false);

    // 备注：本项把 Policies\\SQMClient\\Windows 的 CEIPEnable 写为 0 以从策略层退出 CEIP；
    // 只改变遥测策略，不删除系统保护。写入前捕获原值，恢复时按原值回写；缺失值恢复为删除。
    public static readonly RegistryDwordCapabilityDefinition DisableCeipSqmPolicy = Machine(
        "privacy.disable_ceip.sqm_policy",
        @"SOFTWARE\Policies\Microsoft\SQMClient\Windows",
        "CEIPEnable",
        0,
        ChangeRisk.LowReversible,
        requiresRestart: false);

    // 备注：本项把当前用户 Siuf\\Rules 的 PeriodInNanoSeconds 写为 0 以停止反馈请求计时；
    // 只影响当前用户反馈计时，不删除系统保护。写入前捕获原值，恢复时按原值回写；缺失值恢复为删除。
    public static readonly RegistryDwordCapabilityDefinition DisableFeedbackSiufPeriod = new(
        "privacy.disable_feedback.siuf_period",
        "1.0.0",
        @"SOFTWARE\Microsoft\Siuf\Rules",
        "PeriodInNanoSeconds",
        0,
        ChangeRisk.LowReversible,
        RequiresExplicitConfirmation: true);

    // 备注：本项把 DataCollection 的 DoNotShowFeedbackNotifications 写为 1 以屏蔽反馈通知；
    // 只改变反馈通知策略，不删除系统保护。写入前捕获原值，恢复时按原值回写；缺失值恢复为删除。
    public static readonly RegistryDwordCapabilityDefinition DisableFeedbackNotificationPolicy = Machine(
        "privacy.disable_feedback.notification_policy",
        @"SOFTWARE\Policies\Microsoft\Windows\DataCollection",
        "DoNotShowFeedbackNotifications",
        1,
        ChangeRisk.LowReversible,
        requiresRestart: false);

    // 备注：本项把鼠标数据队列（MouseDataQueueSize）写为 200 以配合高轮询率鼠标减少输入丢失；
    // 只改变 mouclass 服务参数，不删除系统保护。写入前捕获原值，恢复时按原值回写；缺失值恢复为删除。
    public static readonly RegistryDwordCapabilityDefinition OptimizeMouseQueueSize = Machine(
        "input.optimize_usb_polling.mouse_queue",
        @"SYSTEM\CurrentControlSet\Services\mouclass\Parameters",
        "MouseDataQueueSize",
        200,
        ChangeRisk.LowReversible,
        requiresRestart: true);

    // 备注：本项把键盘数据队列（KeyboardDataQueueSize）写为 200 以配合高轮询率输入减少丢失；
    // 只改变 kbdclass 服务参数，不删除系统保护。写入前捕获原值，恢复时按原值回写；缺失值恢复为删除。
    public static readonly RegistryDwordCapabilityDefinition OptimizeKeyboardQueueSize = Machine(
        "input.optimize_usb_polling.keyboard_queue",
        @"SYSTEM\CurrentControlSet\Services\kbdclass\Parameters",
        "KeyboardDataQueueSize",
        200,
        ChangeRisk.LowReversible,
        requiresRestart: true);

    // ── 安全缓解（高风险，标红 + 显式确认，用户自行判断）──
    // 备注：本项把 UAC 的 EnableLUA 写为 0 以关闭用户帐户控制；这是 Windows 官方策略值，非私有设置。
    // 写入前捕获原值，恢复时按原值回写；缺失值恢复为删除。需重启后由内核重新读取。
    public static readonly RegistryDwordCapabilityDefinition DisableUac = Machine(
        "basic.disable_uac",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
        "EnableLUA",
        0,
        ChangeRisk.HighSystem,
        requiresRestart: true);

    // 备注：本项把当前用户 PushNotifications 的 ToastEnabled 写为 0 以关闭安全通知气泡；
    // 只影响当前用户通知设置，不删除系统保护。写入前捕获原值，恢复时按原值回写。
    public static readonly RegistryDwordCapabilityDefinition DisableSecurityNotifications = new(
        "basic.disable_security_notifications",
        "1.0.0",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\PushNotifications",
        "ToastEnabled",
        0,
        ChangeRisk.LowReversible,
        RequiresExplicitConfirmation: true);

    // 备注：本项把 Windows Update 策略 NoAutoUpdate 写为 1 以关闭自动更新；这是官方 AU 策略值。
    // 写入前捕获原值，恢复时按原值回写；缺失值恢复为删除。
    public static readonly RegistryDwordCapabilityDefinition DisableWindowsUpdate = Machine(
        "basic.disable_windows_update",
        @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU",
        "NoAutoUpdate",
        1,
        ChangeRisk.MediumRebootOrExperience,
        requiresRestart: false);

    // 备注：本项把 DeviceGuard 的 EnableVirtualizationBasedSecurity 写为 0 以关闭 VBS；
    // 这是官方 DeviceGuard 策略值。写入前捕获原值，恢复时按原值回写；缺失值恢复为删除。需重启生效。
    public static readonly RegistryDwordCapabilityDefinition DisableVbs = Machine(
        "security.disable_vbs",
        @"SYSTEM\CurrentControlSet\Control\DeviceGuard",
        "EnableVirtualizationBasedSecurity",
        0,
        ChangeRisk.HighSystem,
        requiresRestart: true);

    // 备注：本项把 HypervisorEnforcedCodeIntegrity 的 Enabled 写为 0 以关闭 HVCI（内存完整性）；
    // 这是官方 DeviceGuard 策略值。写入前捕获原值，恢复时按原值回写；缺失值恢复为删除。需重启生效。
    public static readonly RegistryDwordCapabilityDefinition DisableHvci = Machine(
        "security.disable_hvci",
        @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity",
        "Enabled",
        0,
        ChangeRisk.HighSystem,
        requiresRestart: true);

    // 备注：本项把内核 MitigationOptions 写为 0 以关闭 CFG；这是官方内核缓解位掩码。
    // 写入前捕获原值，恢复时按原值回写；缺失值恢复为删除。需重启生效。
    public static readonly RegistryDwordCapabilityDefinition DisableCfg = Machine(
        "security.disable_cfg",
        @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel",
        "MitigationOptions",
        0,
        ChangeRisk.HighSystem,
        requiresRestart: true);

    // 备注：本项把 Defender 实时保护策略 DisableRealtimeMonitoring 写为 1 以临时关闭实时监控；
    // 这是官方 Defender 策略值（受 Tamper Protection 保护时会被系统拒绝）。写入前捕获原值，
    // 恢复时按原值回写；缺失值恢复为删除。
    public static readonly RegistryDwordCapabilityDefinition DisableDefenderRealtime = Machine(
        "security.disable_defender_realtime",
        @"SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection",
        "DisableRealtimeMonitoring",
        1,
        ChangeRisk.HighSystem,
        requiresRestart: false);

    // 备注：本项把内核 DisableExceptionChainValidation 写为 1 以关闭 SEHOP；这是官方内核缓解值。
    // 写入前捕获原值，恢复时按原值回写；缺失值恢复为删除。需重启生效。
    public static readonly RegistryDwordCapabilityDefinition DisableSehop = Machine(
        "security.disable_sehop",
        @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel",
        "DisableExceptionChainValidation",
        1,
        ChangeRisk.HighSystem,
        requiresRestart: true);

    public static IReadOnlyList<RegistryDwordCapabilityDefinition> NextBatch { get; } =
    [
        IncreaseMaxUserPort,
        OptimizeTcpTimedWait
    ];

    public static IReadOnlyList<RegistryDwordCapabilityDefinition> CurrentBatch { get; } =
    [
        DisableErrorReporting,
        DisableStepsRecorder,
        DisableInventoryCollector,
        DisableCloudSpeech,
        DisableWebcam,
        DisableOneDriveSync,
        DisableAppImpactTelemetry,
        IncreaseMaxUserPort,
        OptimizeTcpTimedWait,
        DisableDiagnosticEvents,
        DisableWifiSense,
        OptimizeTtl,
        OptimizeTcpMaxDupAcks,
        DisableTcpAutoTuning,
        DisableEcnCapability,
        DisableRsc,
        OptimizeRss,
        EnableTcp1323,
        OptimizeSystemResponsiveness,
        DisableStoreAutoUpdate,
        DisableNetworkThrottlingIndex,
        DisableCortana,
        DisablePrefetch,
        BoostGamePriority,
        EnableTsx,
        DisableLicenseTelemetry,
        DisableCompatibilityTelemetry,
        DisablePrivacyConsent,
        RestorePointFrequency,
        DisableModernStandbyNetworkingAc,
        DisableModernStandbyNetworkingDc,
        DisableHibernation,
        DisableQosPacketScheduler,
        DisableLmhostsLookup,
        OptimizeSystemCache,
        OptimizeMemoryPriority,
        EnableLargePages,
        OptimizePagefileClearAtShutdown,
        DisableNtfs8Dot3,
        OptimizeIoPageLockLimit,
        DisableNtfsEncryption,
        DisableGameDvrPolicy,
        DisableAudioServiceRestart,
        OptimizeRawInputBuffer,
        AudioExclusiveModePriority,
        DisableVrr
    ];

    private static RegistryDwordCapabilityDefinition MachinePolicy(
        string capabilityId,
        string subKeyPath,
        string valueName,
        int targetValue,
        ChangeRisk risk) =>
        Machine(capabilityId, subKeyPath, valueName, targetValue, risk, requiresRestart: false);

    private static RegistryDwordCapabilityDefinition MachineTcpParameter(
        string capabilityId,
        string valueName,
        int targetValue,
        ChangeRisk risk) =>
        Machine(
            capabilityId,
            @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters",
            valueName,
            targetValue,
            risk,
            requiresRestart: true);

    private static RegistryDwordCapabilityDefinition Machine(
        string capabilityId,
        string subKeyPath,
        string valueName,
        int targetValue,
        ChangeRisk risk,
        bool requiresRestart) =>
        new(
            capabilityId,
            "1.0.0",
            subKeyPath,
            valueName,
            targetValue,
            risk,
            RequiresExplicitConfirmation: true,
            Hive: RegistryDwordHive.LocalMachine,
            RequiresRestart: requiresRestart);
}
