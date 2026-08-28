namespace GameOptimizer.Core.Localization;

public class OptimizationTranslations
{
    public static OptimizationTranslations Instance { get; } = new();
    
    private static readonly Dictionary<string, (string TitleZh, string TitleEn, string DescZh, string DescEn)> _translations = new()
    {
        // ========== 音频优化 ==========
        ["audio.disable_enhancements"] = (
            "禁用音频增强", "Disable Audio Enhancements",
            "关闭音效处理，让声音更真实、延迟更低", "Disable audio effects for lower latency and cleaner sound"),
        ["audio.exclusive_mode"] = (
            "音频独占模式优先", "Audio Exclusive Mode Priority",
            "让游戏独享声卡，获得最低延迟", "Allow games exclusive access to audio device for lowest latency"),
        ["audio.exclusive_mode_priority"] = (
            "音频独占模式优先", "Audio Exclusive Mode Priority",
            "让游戏独享声卡，获得最低延迟", "Allow games exclusive access to audio device for lowest latency"),
        ["audio.disable_spatial"] = (
            "禁用空间音效", "Disable Spatial Audio",
            "关闭 3D 环绕声处理，减少音频延迟", "Disable 3D surround sound processing to reduce audio latency"),
        ["audio.disable_spatial_sound"] = (
            "禁用空间音效", "Disable Spatial Audio",
            "关闭 3D 环绕声处理，减少音频延迟", "Disable 3D surround sound processing to reduce audio latency"),
        ["audio.optimize_buffer"] = (
            "优化音频缓冲区", "Optimize Audio Buffer",
            "减小音频缓冲，降低延迟", "Reduce audio buffer size for lower latency"),
        ["audio.disable_service_restart"] = (
            "禁用音频服务自动重启", "Disable Audio Service Auto-Restart",
            "优化音频服务启动方式，避免游戏中声音中断", "Optimize audio service startup to prevent sound interruption"),
        ["audio.optimize_sample_rate"] = (
            "优化音频采样率", "Optimize Audio Sample Rate",
            "提升音频处理优先级，获得更好的音质", "Increase audio processing priority for better quality"),
        ["audio.disable_ducking"] = (
            "禁用通信音量衰减", "Disable Communication Ducking",
            "关闭语音通话时自动降低游戏音量的功能", "Disable automatic volume reduction during voice calls"),
        ["audio.disable_comm_ducking"] = (
            "禁用通信音量衰减", "Disable Communication Ducking",
            "关闭语音通话时自动降低游戏音量的功能", "Disable automatic volume reduction during voice calls"),
            
        // ========== CPU 优化 ==========
        ["cpu.disable_pstates"] = (
            "锁定 CPU 最高频率", "Lock CPU Maximum Frequency",
            "禁用动态调频，保持 CPU 满频运行", "Disable dynamic frequency scaling, keep CPU at max frequency"),
        ["cpu.disable_tsc_sync"] = (
            "禁用 TSC 同步", "Disable TSC Sync",
            "禁用 TSC 同步，某些多核场景下可提升性能", "Disable TSC sync for potential multi-core performance gains"),
        ["cpu.adjust_csrss_priority"] = (
            "调整 CSRSS 优先级", "Adjust CSRSS Priority",
            "降低系统进程优先级，为游戏让出更多 CPU 时间", "Lower system process priority to give games more CPU time"),
        ["cpu.disable_core_parking"] = (
            "禁用核心停靠", "Disable Core Parking",
            "保持所有 CPU 核心活跃，减少唤醒延迟", "Keep all CPU cores active to reduce wake-up latency"),
        ["cpu.timer_resolution"] = (
            "优化系统计时器", "Optimize System Timer",
            "提高系统计时器精度，减少输入延迟", "Increase system timer resolution for lower input latency"),
        ["cpu.enable_tsx"] = (
            "启用 TSX", "Enable TSX",
            "启用 Intel TSX 指令集（需硬件支持）", "Enable Intel TSX instruction set (requires hardware support)"),
            
        // ========== GPU 优化 ==========
        ["gpu.disable_dwm_animations"] = (
            "禁用 DWM 动画", "Disable DWM Animations",
            "关闭桌面动画效果，减少 GPU 渲染开销", "Disable desktop animations to reduce GPU overhead"),
        ["gpu.disable_game_dvr_complete"] = (
            "彻底禁用 Game DVR", "Completely Disable Game DVR",
            "完全关闭游戏录制功能，释放后台资源", "Completely disable game recording to free background resources"),
        ["gpu.disable_xbox_game_monitoring"] = (
            "禁用 Xbox 游戏监控", "Disable Xbox Game Monitoring",
            "关闭 Xbox 游戏监控服务，减少后台占用", "Disable Xbox game monitoring service to reduce background usage"),
        ["gpu.disable_vrr"] = (
            "禁用可变刷新率", "Disable Variable Refresh Rate",
            "关闭 VRR/FreeSync/G-Sync，某些游戏更稳定", "Disable VRR for more stable performance in some games"),
        ["gpu.clear_shader_cache"] = (
            "清理 Shader 缓存", "Clear Shader Cache",
            "清理着色器缓存，解决某些游戏卡顿问题", "Clear shader cache to fix stuttering in some games"),
        ["gpu.amd_anti_lag"] = (
            "AMD 低延迟优化", "AMD Low Latency Optimization",
            "优化 AMD 显卡设置，降低输入延迟", "Optimize AMD GPU settings for lower input latency"),
        ["gpu.nvidia_low_latency"] = (
            "NVIDIA 低延迟优化", "NVIDIA Low Latency Optimization",
            "优化 NVIDIA 显卡设置，降低输入延迟", "Optimize NVIDIA GPU settings for lower input latency"),
        ["gpu.disable_hags"] = (
            "禁用硬件加速 GPU 调度", "Disable Hardware-Accelerated GPU Scheduling",
            "关闭 HAGS，某些游戏可能更稳定", "Disable HAGS for more stable performance in some games"),
        ["gpu.enable_hags"] = (
            "启用硬件加速 GPU 调度", "Enable Hardware-Accelerated GPU Scheduling",
            "开启 HAGS，可能提升帧率和降低延迟", "Enable HAGS for potential FPS gains and lower latency"),
            
        // ========== 输入延迟 ==========
        ["input.disable_pointer_trails"] = (
            "禁用鼠标轨迹", "Disable Pointer Trails",
            "关闭鼠标指针轨迹效果，减少渲染开销", "Disable mouse pointer trails to reduce rendering overhead"),
        ["input.disable_snap_to"] = (
            "禁用鼠标自动对齐", "Disable Snap To",
            "关闭鼠标自动移动到按钮的功能", "Disable automatic mouse movement to dialog buttons"),
        ["input.disable_touch_feedback"] = (
            "禁用触摸反馈", "Disable Touch Feedback",
            "关闭触摸屏点击时的视觉反馈", "Disable visual feedback for touch screen taps"),
        ["input.disable_pen_feedback"] = (
            "禁用手写笔反馈", "Disable Pen Feedback",
            "关闭手写笔的视觉反馈效果", "Disable visual feedback for pen input"),
        ["input.optimize_usb_polling"] = (
            "优化 USB 数据队列", "Optimize USB Data Queue",
            "增加鼠标键盘数据队列，减少输入丢失", "Increase mouse/keyboard data queue to reduce input loss"),
        ["input.optimize_raw_input_buffer"] = (
            "优化 Raw Input 缓冲区", "Optimize Raw Input Buffer",
            "优化输入缓冲区，减少高轮询率鼠标延迟", "Optimize input buffer for high polling rate mice"),
        ["input.disable_mouse_acceleration"] = (
            "禁用鼠标加速", "Disable Mouse Acceleration",
            "关闭鼠标加速，获得 1:1 精准移动", "Disable mouse acceleration for 1:1 precise movement"),
        ["input.optimize_keyboard_response"] = (
            "优化键盘响应", "Optimize Keyboard Response",
            "减少键盘重复延迟，提升响应速度", "Reduce keyboard repeat delay for faster response"),
            
        // ========== 网络优化 ==========
        ["network.optimize_afd"] = (
            "优化 Winsock 参数", "Optimize Winsock Parameters",
            "优化网络发送/接收窗口，提升吞吐量", "Optimize network send/receive windows for better throughput"),
        ["network.disable_qos_scheduler"] = (
            "禁用 QoS 带宽限制", "Disable QoS Bandwidth Limit",
            "移除 QoS 带宽保留限制，释放全部带宽", "Remove QoS bandwidth reservation to release full bandwidth"),
        ["network.disable_throttling_complete"] = (
            "完全禁用网络节流", "Completely Disable Network Throttling",
            "彻底禁用网络节流，获得最大网络性能", "Completely disable network throttling for maximum performance"),
        ["network.disable_delivery_optimization"] = (
            "禁用传递优化", "Disable Delivery Optimization",
            "关闭 Windows 更新 P2P 分发，避免后台上传", "Disable Windows Update P2P to prevent background uploads"),
        ["network.disable_netbios"] = (
            "禁用 NetBIOS", "Disable NetBIOS",
            "关闭 NetBIOS 协议，减少网络广播流量", "Disable NetBIOS protocol to reduce network broadcast traffic"),
        ["network.disable_lmhosts"] = (
            "禁用 LMHOSTS 查找", "Disable LMHOSTS Lookup",
            "关闭 LMHOSTS 文件查找，加快网络连接", "Disable LMHOSTS lookup for faster network connections"),
        ["network.optimize_tcp"] = (
            "优化 TCP 参数", "Optimize TCP Parameters",
            "优化 TCP 连接参数，降低网络延迟", "Optimize TCP connection parameters for lower latency"),
        ["network.disable_nagle"] = (
            "禁用 Nagle 算法", "Disable Nagle Algorithm",
            "关闭 TCP 延迟发送，减少网络延迟", "Disable TCP delayed sending for lower latency"),
            
        // ========== 安全缓解 ==========
        ["security.disable_vbs"] = (
            "禁用 VBS 虚拟化安全", "Disable VBS",
            "关闭基于虚拟化的安全，减少虚拟化开销；需要重启，系统安全性会降低", "Disable virtualization-based security to reduce virtualization overhead; restart required and system security is reduced"),
        ["security.disable_hvci"] = (
            "禁用内存完整性 (HVCI)", "Disable Memory Integrity (HVCI)",
            "关闭内存完整性检查，减少检查开销；需要重启，内核防护能力会降低", "Disable memory integrity checks to reduce overhead; restart required and kernel protection is reduced"),
        ["security.disable_cfg"] = (
            "禁用控制流保护 (CFG)", "Disable Control Flow Guard (CFG)",
            "关闭控制流保护，减少部分分支检查开销；需要重启，漏洞利用防护会降低", "Disable Control Flow Guard to reduce some branch-check overhead; restart required and exploit protection is reduced"),
        ["security.disable_defender_realtime"] = (
            "禁用 Defender 实时保护", "Disable Defender Real-time Protection",
            "关闭实时恶意软件监控，减少 CPU 与磁盘占用；系统将失去实时病毒防护", "Disable real-time malware monitoring to reduce CPU and disk usage; real-time virus protection is lost"),
        ["security.disable_smartscreen"] = (
            "禁用 SmartScreen", "Disable SmartScreen",
            "关闭应用与下载信誉检查，减少启动时的云端验证等待；未知程序防护会降低", "Disable application and download reputation checks to reduce cloud-verification wait; protection against unknown programs is reduced"),
        ["security.disable_dep"] = (
            "DEP 保护不可关闭", "DEP Cannot Be Disabled",
            "产品安全策略禁止降低数据执行保护", "Product security policy forbids weakening Data Execution Prevention"),
        ["security.disable_sehop"] = (
            "禁用 SEHOP 异常处理保护", "Disable SEHOP",
            "关闭结构化异常处理覆盖保护，减少异常链校验；需要重启，漏洞利用防护会降低", "Disable structured exception-handling overwrite protection to reduce exception-chain checks; restart required and exploit protection is reduced"),
        ["security.disable_spectre_meltdown"] = (
            "Spectre/Meltdown 缓解不可关闭", "Spectre/Meltdown Mitigations Cannot Be Disabled",
            "产品安全策略禁止关闭 CPU 漏洞缓解", "Product security policy forbids disabling CPU vulnerability mitigations"),
            
        // ========== 存储优化 ==========
        ["storage.disable_8dot3"] = (
            "禁用 NTFS 短文件名", "Disable NTFS 8.3 Names",
            "禁用短文件名生成，提升文件系统性能", "Disable short filename generation for better filesystem performance"),
        ["storage.optimize_io_page_lock"] = (
            "优化 I/O 页面锁定", "Optimize I/O Page Lock",
            "增加页面锁定限制，提升大文件读写性能", "Increase page lock limit for better large file I/O performance"),
        ["storage.clear_standby_list"] = (
            "清理待机内存", "Clear Standby Memory",
            "清理待机内存列表，释放被缓存占用的内存", "Clear standby memory list to free cached memory"),
        ["storage.disable_ntfs_encryption"] = (
            "禁用 NTFS 加密", "Disable NTFS Encryption",
            "禁用文件系统加密功能，减少 CPU 开销", "Disable filesystem encryption to reduce CPU overhead"),
        ["storage.disable_prefetch"] = (
            "禁用预读取", "Disable Prefetch",
            "关闭 Windows 预读取功能，减少磁盘 I/O", "Disable Windows prefetch to reduce disk I/O"),
        ["storage.disable_superfetch"] = (
            "禁用 SuperFetch", "Disable SuperFetch",
            "关闭 SuperFetch 服务，减少后台磁盘活动", "Disable SuperFetch service to reduce background disk activity"),
            
        // ========== Windows 功能 ==========
        ["windows.disable_tips"] = (
            "禁用 Windows 提示", "Disable Windows Tips",
            "关闭 Windows 使用提示和建议通知", "Disable Windows tips and suggestion notifications"),
        ["windows.disable_suggested_apps"] = (
            "禁用建议应用", "Disable Suggested Apps",
            "关闭开始菜单和设置中的建议应用", "Disable suggested apps in Start menu and Settings"),
        ["windows.disable_clipboard_history"] = (
            "禁用剪贴板历史", "Disable Clipboard History",
            "关闭剪贴板历史记录功能，减少内存占用", "Disable clipboard history to reduce memory usage"),
        ["windows.disable_focus_assist"] = (
            "禁用专注助手", "Disable Focus Assist",
            "关闭专注助手功能，避免通知被静音", "Disable Focus Assist to prevent notification muting"),
        ["windows.disable_timeline"] = (
            "禁用时间线", "Disable Timeline",
            "关闭活动历史功能，减少后台资源占用", "Disable activity history to reduce background resource usage"),
        ["windows.disable_cortana"] = (
            "禁用 Cortana", "Disable Cortana",
            "关闭 Cortana 语音助手，减少后台占用", "Disable Cortana voice assistant to reduce background usage"),
        ["windows.disable_search_indexing"] = (
            "禁用搜索索引", "Disable Search Indexing",
            "关闭 Windows 搜索索引，减少磁盘 I/O", "Disable Windows Search indexing to reduce disk I/O"),
            
        // ========== Intel 专属优化 ==========
        ["intel.disable_cstates"] = (
            "禁用 CPU C-States", "Disable CPU C-States",
            "禁用 CPU 空闲状态，保持 CPU 始终活跃", "Disable CPU idle states to keep CPU always active"),
            
        // ========== AMD 专属优化 ==========
        ["amd.disable_cool_quiet"] = (
            "禁用 Cool'n'Quiet", "Disable Cool'n'Quiet",
            "关闭 AMD 节能功能，保持 CPU 高频运行", "Disable AMD power saving to keep CPU at high frequency"),
        ["amd.optimize_pbo"] = (
            "优化 PBO 设置", "Optimize PBO Settings",
            "优化 Precision Boost Overdrive 设置", "Optimize Precision Boost Overdrive settings"),
            
        // ========== 隐私保护 ==========
        ["privacy.disable_telemetry"] = (
            "禁用遥测数据收集", "Disable Telemetry",
            "关闭 Windows 遥测数据收集，保护隐私", "Disable Windows telemetry data collection for privacy"),
        ["privacy.disable_advertising_id"] = (
            "禁用广告 ID", "Disable Advertising ID",
            "关闭广告标识符，减少个性化广告追踪", "Disable advertising ID to reduce personalized ad tracking"),
        ["privacy.disable_location"] = (
            "禁用位置服务", "Disable Location Services",
            "关闭位置服务，保护位置隐私", "Disable location services for location privacy"),
        ["privacy.disable_activity_history"] = (
            "禁用活动历史", "Disable Activity History",
            "关闭活动历史记录，保护使用隐私", "Disable activity history for usage privacy"),
        ["privacy.disable_diagnostic_data"] = (
            "禁用诊断数据", "Disable Diagnostic Data",
            "将诊断数据设为最低级别，减少数据收集", "Set diagnostic data to minimum level to reduce data collection"),
        ["privacy.disable_cortana"] = (
            "关闭 Cortana", "Disable Cortana",
            "禁用语音助理与云端收集", "Disable voice assistant and cloud collection"),
        ["privacy.disable_license_telemetry"] = (
            "阻止许可证遥测", "Block License Telemetry",
            "阻止软件许可状态被周期性上报", "Prevent license status from being reported"),
        ["privacy.disable_ceip"] = (
            "退出 CEIP", "Opt Out of CEIP",
            "关闭客户体验改善计划", "Disable Customer Experience Improvement Program"),
        ["privacy.disable_compatibility_telemetry"] = (
            "禁用兼容性遥测", "Disable Compatibility Telemetry",
            "阻止兼容性评估数据回传", "Prevent compatibility assessment data from being sent"),
        ["privacy.disable_privacy_consent"] = (
            "跳过隐私同意屏", "Skip Privacy Consent Screen",
            "首次登录不再出现隐私提示", "Skip privacy prompts on first login"),
        ["privacy.disable_upnp_sharing"] = (
            "关闭 UPnP 共享", "Disable UPnP Sharing",
            "阻止局域网自动发现和共享", "Prevent automatic LAN discovery and sharing"),
        ["privacy.disable_sensor_services"] = (
            "禁用传感器服务", "Disable Sensor Services",
            "应用无法获取环境/运动数据", "Apps cannot access environment/motion data"),
        ["privacy.deny_app_radio_access"] = (
            "拒绝无线电访问", "Deny Radio Access",
            "阻止应用控制 Wi-Fi/蓝牙开关", "Prevent apps from controlling Wi-Fi/Bluetooth"),
        ["privacy.deny_app_access_to_messaging"] = (
            "拒绝短信/聊天", "Deny Messaging Access",
            "阻止应用读取短信和聊天记录", "Prevent apps from reading messages and chat history"),
        ["privacy.deny_app_access_to_email"] = (
            "拒绝邮箱访问", "Deny Email Access",
            "阻止应用访问邮件内容", "Prevent apps from accessing email content"),
        ["privacy.deny_app_access_to_calendar"] = (
            "拒绝日历访问", "Deny Calendar Access",
            "阻止应用读取日程安排", "Prevent apps from reading calendar"),
        ["privacy.deny_app_access_to_contacts"] = (
            "拒绝联系人访问", "Deny Contacts Access",
            "阻止应用读取联系人列表", "Prevent apps from reading contacts"),
        ["privacy.deny_app_access_to_camera"] = (
            "拒绝相机访问", "Deny Camera Access",
            "阻止应用使用摄像头", "Prevent apps from using camera"),
        ["privacy.deny_app_access_to_phone_calls"] = (
            "拒绝拨号", "Deny Phone Calls",
            "阻止应用发起电话呼叫", "Prevent apps from making phone calls"),
        ["privacy.deny_app_access_to_call_history"] = (
            "拒绝通话记录", "Deny Call History Access",
            "阻止应用读取通话历史", "Prevent apps from reading call history"),
        ["privacy.deny_app_access_to_trusted_devices"] = (
            "拒绝受信任设备", "Deny Trusted Devices Access",
            "阻止应用访问已配对设备列表", "Prevent apps from accessing paired devices"),
        ["privacy.deny_app_access_to_motion"] = (
            "拒绝运动传感器", "Deny Motion Sensor Access",
            "阻止应用获取运动和健身数据", "Prevent apps from accessing motion data"),
        ["privacy.deny_app_access_to_tasks"] = (
            "拒绝任务访问", "Deny Tasks Access",
            "阻止应用读取待办事项", "Prevent apps from reading tasks"),
        ["privacy.deny_app_access_to_notifications"] = (
            "拒绝系统通知访问", "Deny Notifications Access",
            "阻止应用读取系统通知内容", "Prevent apps from reading notifications"),
        ["privacy.deny_app_access_to_account_info"] = (
            "拒绝账户信息", "Deny Account Info Access",
            "阻止应用获取用户名和头像", "Prevent apps from accessing account info"),
        ["privacy.deny_app_access_to_diagnostic_info"] = (
            "拒绝诊断数据", "Deny Diagnostic Info Access",
            "阻止应用收集其他应用的诊断信息", "Prevent apps from collecting diagnostic info"),
        ["privacy.disable_app_sync"] = (
            "禁用设置/应用同步", "Disable Settings/App Sync",
            "强制本地化配置，不同步到云端", "Force local configuration, no cloud sync"),
        ["privacy.hide_most_used_apps"] = (
            "隐藏最常使用的应用", "Hide Most Used Apps",
            "开始菜单不再显示最常使用列表", "Start menu won't show most used apps"),
        ["privacy.disable_feedback"] = (
            "屏蔽反馈提示", "Disable Feedback Prompts",
            "禁用反馈通知", "Disable feedback notifications"),
        ["privacy.disable_website_access_language_list"] = (
            "阻止网站读取语言列表", "Block Website Language List Access",
            "浏览器无法枚举系统语言排序", "Browser cannot enumerate system languages"),
        ["privacy.disable_cloud_content"] = (
            "关闭云推荐内容", "Disable Cloud Content",
            "禁用 Spotlight/消费者体验等云推送", "Disable Spotlight and consumer experience cloud content"),
        ["privacy.disable_activity_feed"] = (
            "关闭活动历史", "Disable Activity Feed",
            "将活动提要和上传全部禁用", "Disable activity feed and upload"),
        ["privacy.tailored_experiences"] = (
            "关闭定向体验", "Disable Tailored Experiences",
            "停止根据诊断数据定制建议内容", "Stop tailoring suggestions from diagnostic data"),
        ["privacy.disable_error_reporting"] = (
            "禁用错误报告", "Disable Error Reporting",
            "阻止 Windows Error Reporting 上传", "Prevent Windows Error Reporting uploads"),
        ["privacy.disable_steps_recorder"] = (
            "禁用 Steps Recorder", "Disable Steps Recorder",
            "关闭问题步骤记录器", "Disable problem steps recorder"),
        ["privacy.disable_inventory_collector"] = (
            "停用 Inventory Collector", "Disable Inventory Collector",
            "阻止硬件/应用清单被回传", "Prevent hardware/app inventory from being sent"),
        ["privacy.disable_handwriting_collection"] = (
            "禁止文本/手写收集", "Disable Text/Handwriting Collection",
            "阻止输入内容被收集", "Prevent input content from being collected"),
        ["privacy.disable_cloud_speech"] = (
            "关闭云端语音识别", "Disable Cloud Speech Recognition",
            "禁用语音/墨迹上传", "Disable voice/ink upload"),
        ["privacy.disable_webcam"] = (
            "禁用系统级摄像头", "Disable System Camera",
            "任何 UWP 都无法启用相机", "No UWP app can use camera"),
        ["privacy.disable_onedrive_sync"] = (
            "停用 OneDrive 同步", "Disable OneDrive Sync",
            "防止后台自动同步", "Prevent background auto-sync"),
        ["privacy.disable_app_impact_telemetry"] = (
            "关闭应用影响遥测", "Disable App Impact Telemetry",
            "停止 Application Telemetry 引擎", "Stop Application Telemetry engine"),
            
        // ========== 系统服务 ==========
        ["service.disable_print_spooler"] = (
            "禁用打印服务", "Disable Print Spooler",
            "关闭打印后台服务，减少资源占用", "Disable print spooler service to reduce resource usage"),
        ["service.disable_fax"] = (
            "禁用传真服务", "Disable Fax Service",
            "关闭传真服务，减少后台占用", "Disable fax service to reduce background usage"),
        ["service.disable_remote_registry"] = (
            "禁用远程注册表", "Disable Remote Registry",
            "关闭远程注册表服务，提升安全性", "Disable remote registry service for better security"),
        ["service.disable_xbox_services"] = (
            "禁用 Xbox 服务", "Disable Xbox Services",
            "关闭 Xbox 相关服务，减少后台占用", "Disable Xbox services to reduce background usage"),
            
        // ========== 计划任务 ==========
        ["task.disable_compatibility_appraiser"] = (
            "禁用兼容性评估", "Disable Compatibility Appraiser",
            "关闭兼容性评估任务，减少后台扫描", "Disable compatibility appraiser to reduce background scanning"),
        ["task.disable_customer_experience"] = (
            "禁用客户体验改善计划", "Disable Customer Experience Improvement",
            "关闭 CEIP 数据收集任务", "Disable CEIP data collection tasks"),
        ["task.disable_disk_diagnostics"] = (
            "禁用磁盘诊断", "Disable Disk Diagnostics",
            "关闭磁盘诊断任务，减少磁盘 I/O", "Disable disk diagnostics to reduce disk I/O"),
            
        // ========== 外设优化 ==========
        ["peripheral.optimize_mouse_polling"] = (
            "优化鼠标轮询率", "Optimize Mouse Polling Rate",
            "优化 USB 鼠标轮询率设置", "Optimize USB mouse polling rate settings"),
        ["peripheral.disable_usb_power_saving"] = (
            "禁用 USB 节能", "Disable USB Power Saving",
            "关闭 USB 设备节能，保持设备始终活跃", "Disable USB power saving to keep devices always active"),
        ["peripheral.optimize_keyboard_filter"] = (
            "优化键盘过滤", "Optimize Keyboard Filter",
            "优化键盘过滤驱动设置", "Optimize keyboard filter driver settings"),
            
        // ========== 内存优化 ==========
        ["memory.optimize_paging"] = (
            "优化分页文件", "Optimize Paging File",
            "优化虚拟内存分页文件设置", "Optimize virtual memory paging file settings"),
        ["memory.disable_memory_compression"] = (
            "禁用内存压缩", "Disable Memory Compression",
            "关闭内存压缩功能，减少 CPU 开销", "Disable memory compression to reduce CPU overhead"),
        ["memory.optimize_large_pages"] = (
            "启用大页面支持", "Enable Large Pages",
            "启用大页面内存支持，提升内存访问效率", "Enable large page memory support for better memory access"),
            
        // ========== 桌面体验 ==========
        ["explorer.disable_animations"] = (
            "禁用窗口动画", "Disable Window Animations",
            "关闭窗口动画效果，提升响应速度", "Disable window animations for faster response"),
        ["explorer.disable_thumbnails"] = (
            "禁用缩略图缓存", "Disable Thumbnail Cache",
            "关闭缩略图缓存，减少磁盘占用", "Disable thumbnail cache to reduce disk usage"),
        ["explorer.optimize_visual_effects"] = (
            "优化视觉效果", "Optimize Visual Effects",
            "调整视觉效果为最佳性能", "Adjust visual effects for best performance"),
            
        // ========== 电竞模式 ==========
        ["esports.enable_hags"] = (
            "硬件加速 GPU 调度 (HAGS)", "Hardware-Accelerated GPU Scheduling (HAGS)",
            "启用 HAGS 可降低延迟并提升帧率", "Enable HAGS for lower latency and better FPS"),
        ["esports.prefer_dgpu"] = (
            "全局优先独显", "Prefer Discrete GPU",
            "强制所有应用使用独立显卡", "Force all applications to use discrete GPU"),
        ["esports.gpu_sfio_priority"] = (
            "GPU SFIO 优先级", "GPU SFIO Priority",
            "提升 GPU 调度优先级，减少帧延迟", "Increase GPU scheduling priority for lower frame latency"),
        ["esports.disable_fullscreen_optimization"] = (
            "禁用全屏优化", "Disable Fullscreen Optimization",
            "关闭 Windows 全屏优化，减少输入延迟", "Disable Windows fullscreen optimization for lower input lag"),
        ["esports.enable_game_mode"] = (
            "启用 Windows 游戏模式", "Enable Windows Game Mode",
            "开启游戏模式，优化系统资源分配", "Enable Game Mode for optimized system resource allocation"),
        ["esports.disable_game_dvr"] = (
            "禁用 Game DVR 录制", "Disable Game DVR Recording",
            "关闭后台录制功能，释放系统资源", "Disable background recording to free system resources"),
        ["esports.windowed_optimization"] = (
            "窗口化游戏优化", "Windowed Game Optimization",
            "优化窗口化游戏的渲染性能", "Optimize rendering performance for windowed games"),
        ["esports.disable_background_apps"] = (
            "禁用后台应用", "Disable Background Apps",
            "关闭后台应用运行，释放系统资源", "Disable background apps to free system resources"),
        ["esports.disable_visual_effects"] = (
            "关闭视觉效果", "Disable Visual Effects",
            "关闭 Windows 视觉效果，提升性能", "Disable Windows visual effects for better performance"),
        ["esports.foreground_priority"] = (
            "优化前台优先级", "Optimize Foreground Priority",
            "提升前台应用的 CPU 优先级", "Increase CPU priority for foreground applications"),
            
        // ========== 性能加速 ==========
        ["performance.disable_security_center"] = (
            "禁用安全中心服务", "Disable Security Center Service",
            "停止 SecurityHealthService，减少安全中心后台监控占用；不会再提供安全状态告警", "Stop SecurityHealthService to reduce Security Center background monitoring; security status alerts will no longer be provided"),
        ["performance.disable_uac"] = (
            "禁用 UAC", "Disable UAC",
            "关闭用户帐户控制，程序运行时不再弹出权限确认；需要重启且恶意程序可静默提权", "Disable User Account Control so programs no longer prompt for consent; restart required and malware may elevate silently"),
        ["performance.disable_firewall"] = (
            "禁用 Windows 防火墙", "Disable Windows Firewall",
            "停止 MpsSvc，关闭网络流量过滤；设备将失去入站与出站防护", "Stop MpsSvc to disable network traffic filtering; inbound and outbound protection is lost"),
        ["performance.disable_store_auto_update"] = (
            "禁止自动更新商店应用", "Disable Store App Auto-Update",
            "关闭商店应用自动更新，减少后台流量", "Disable Store app auto-update to reduce background traffic"),
        ["performance.disable_uwp_background"] = (
            "禁用 UWP 程序后台运行", "Disable UWP Background Apps",
            "禁止 UWP 应用在后台运行", "Prevent UWP apps from running in background"),
        ["performance.disable_indexing"] = (
            "禁用 Windows 索引", "Disable Windows Indexing",
            "关闭搜索索引服务，减少磁盘 I/O", "Disable search indexing service to reduce disk I/O"),
            
        // ========== 内存高级优化 ==========
        ["memory.disable_compression"] = (
            "禁用内存压缩", "Disable Memory Compression",
            "关闭内存压缩功能，减少 CPU 开销", "Disable memory compression to reduce CPU overhead"),
        ["memory.disable_superfetch"] = (
            "禁用 SuperFetch", "Disable SuperFetch",
            "关闭 SuperFetch 服务，减少后台磁盘活动", "Disable SuperFetch service to reduce background disk activity"),
        ["memory.disable_kernel_paging"] = (
            "禁用内核分页", "Disable Kernel Paging",
            "禁止内核分页到磁盘，提升响应速度", "Disable kernel paging to disk for faster response"),
        ["memory.enable_large_pages"] = (
            "启用大页面支持", "Enable Large Pages Support",
            "启用大页面内存支持，提升内存访问效率", "Enable large page memory support for better memory access"),
        ["memory.optimize_system_cache"] = (
            "优化系统缓存大小", "Optimize System Cache Size",
            "调整系统缓存大小，平衡内存使用", "Adjust system cache size to balance memory usage"),
        ["memory.optimize_pagefile"] = (
            "优化页面文件设置", "Optimize Pagefile Settings",
            "优化虚拟内存页面文件配置", "Optimize virtual memory pagefile configuration"),
        ["memory.optimize_priority"] = (
            "优化内存优先级", "Optimize Memory Priority",
            "调整前台应用的内存优先级，确保游戏获得更多内存资源", "Adjust foreground app memory priority to ensure games get more memory resources"),
            
        // ========== 性能加速模块 (basic.*) ==========
        ["basic.disable_security_center"] = (
            "禁用安全中心服务", "Disable Security Center Service",
            "停止 SecurityHealthService，减少安全中心后台监控占用；不会再提供安全状态告警", "Stop SecurityHealthService to reduce Security Center background monitoring; security status alerts will no longer be provided"),
        ["basic.disable_uac"] = (
            "禁用 UAC", "Disable UAC",
            "关闭用户帐户控制，程序运行时不再弹出权限确认；需要重启且恶意程序可静默提权", "Disable User Account Control so programs no longer prompt for consent; restart required and malware may elevate silently"),
        ["basic.disable_firewall"] = (
            "禁用 Windows 防火墙", "Disable Windows Firewall",
            "停止 MpsSvc，关闭网络流量过滤；设备将失去入站与出站防护", "Stop MpsSvc to disable network traffic filtering; inbound and outbound protection is lost"),
        ["basic.disable_store_auto_update"] = (
            "禁止自动更新商店应用", "Disable Store App Auto-Update",
            "关闭 Windows 商店后台自动更新", "Disable Windows Store background auto-update"),
        ["basic.disable_uwp_background"] = (
            "禁用 UWP 程序后台运行", "Disable UWP Background Apps",
            "阻止通用应用在后台占用资源", "Prevent universal apps from using resources in background"),
        ["basic.disable_indexing"] = (
            "禁用 Windows 索引", "Disable Windows Indexing",
            "关闭 WSearch 索引服务，减少 IO", "Disable WSearch indexing service to reduce IO"),
        ["basic.disable_windows_update"] = (
            "关闭 Windows 自动更新程序", "Disable Windows Auto-Update",
            "手动控制补丁", "Manual patch control"),
        ["perf.wuauserv.disable"] = (
            "禁用 Windows Update 服务", "Disable Windows Update Service",
            "防止系统在游戏过程中触发后台更新", "Prevent background updates during gaming"),
        ["basic.disable_security_notifications"] = (
            "关闭安全通知气泡", "Disable Security Notification Toasts",
            "关闭当前用户的安全通知气泡，减少游戏时的弹窗打扰；可能错过重要提醒", "Disable security notification toasts for the current user to reduce interruptions during gaming; important alerts may be missed"),
        ["basic.disable_edge_startup"] = (
            "禁用 Edge 启动和后台运行", "Disable Edge Startup",
            "关闭 Edge StartupBoost，避免后台常居", "Disable Edge StartupBoost"),
        ["basic.disable_map_auto_update"] = (
            "关闭自动更新地图", "Disable Map Auto-Update",
            "阻止离线地图后台下载", "Prevent offline map background download"),
        ["basic.disable_mouse_acceleration"] = (
            "关闭鼠标加速", "Disable Mouse Acceleration",
            "禁用增强型指针精度", "Disable enhanced pointer precision"),
        ["basic.disable_game_bar"] = (
            "关闭 Game Bar", "Disable Game Bar",
            "禁用游戏栏弹窗，减少前台开锁", "Disable game bar popup"),
            
        // ========== CPU 模块 ==========
        ["system.coreparking.min"] = (
            "禁用 Core Parking（最小核心）", "Disable Core Parking (Min)",
            "将最小活动核心维持在 100%", "Keep minimum active cores at 100%"),
        ["system.coreparking.max"] = (
            "禁用 Core Parking（最大核心）", "Disable Core Parking (Max)",
            "最大活动核心保持 100%", "Keep maximum active cores at 100%"),
        ["cpu.processor_boost_mode"] = (
            "开启 Processor Boost Mode", "Enable Processor Boost Mode",
            "维持最高加速状态", "Maintain maximum boost state"),
        ["cpu.disable_boost_mode"] = (
            "禁用 Processor Boost", "Disable Processor Boost",
            "彻底关闭 Boost", "Completely disable Boost"),
        ["cpu.disable_dynamic_tick"] = (
            "禁用 Dynamic Tick", "Disable Dynamic Tick",
            "避免时钟休眠导致的帧时间波动", "Avoid frame time fluctuation from clock sleep"),
        ["cpu.configure_hpet"] = (
            "重新配置 HPET", "Reconfigure HPET",
            "移除 useplatformclock 绑定", "Remove useplatformclock binding"),
        ["cpu.use_platform_tick"] = (
            "启用平台计时器", "Enable Platform Tick",
            "使用 TSC 替代 HPET，AMD CPU 推荐", "Use TSC instead of HPET, recommended for AMD"),
        ["cpu.force_high_timer_resolution"] = (
            "锁定 0.5ms 计时器", "Lock 0.5ms Timer",
            "降低输入与音频延迟", "Reduce input and audio latency"),
        ["cpu.disable_spectre_meltdown"] = (
            "关闭 Spectre/Meltdown 缓解", "Disable Spectre/Meltdown Mitigations",
            "关闭所有 Spectre/Meltdown 缓解以换取性能", "Disable all mitigations for performance"),
            
        // ========== 网络模块 ==========
        ["adjustments.network_throttling_index"] = (
            "禁用网络节流索引", "Disable Network Throttling Index",
            "使多媒体调度永不限制带宽", "Never limit bandwidth for multimedia scheduling"),
        ["adjustments.disable_diagnostic_events"] = (
            "禁用诊断自动记录器", "Disable Diagnostic Auto-Logger",
            "关闭 AutoLogger-DiagTrack 以停止后台跟踪", "Disable AutoLogger-DiagTrack"),
        ["privacy.disable_wifi_sense"] = (
            "禁用 Wi-Fi 感知", "Disable Wi-Fi Sense",
            "阻止 Windows 自动连接到建议的热点", "Prevent auto-connect to suggested hotspots"),
        ["network.clear_dns_cache"] = (
            "刷新 DNS 解析器缓存", "Flush DNS Cache",
            "清除过期或错误的 DNS 缓存", "Clear expired or incorrect DNS cache"),
        ["network.disable_adapter_power_saving"] = (
            "禁用网卡节能", "Disable Network Adapter Power Saving",
            "防止适配器在游戏过程中休眠", "Prevent adapter from sleeping during gaming"),
        ["network.disable_tcp_auto_tuning"] = (
            "锁定 TCP 自动调优", "Lock TCP Auto-Tuning",
            "强制禁用接收窗口自动调优", "Force disable receive window auto-tuning"),
        ["network.disable_ecn_capability"] = (
            "禁用 ECN", "Disable ECN",
            "关闭显式拥塞通知", "Disable Explicit Congestion Notification"),
        ["network.disable_rsc"] = (
            "禁用 RSC", "Disable RSC",
            "停止接收段合并", "Stop Receive Segment Coalescing"),
        ["network.optimize_rss"] = (
            "启用 RSS", "Enable RSS",
            "使网卡工作负载分散到多个 CPU 核心", "Distribute NIC workload across CPU cores"),
        ["network.enable_tcp1323"] = (
            "启用 RFC1323 时间戳", "Enable RFC1323 Timestamps",
            "保持时间戳支持以维持长距离延迟可见性", "Maintain timestamp support for latency visibility"),
        ["network.set_congestion_ctcp"] = (
            "启用复合 TCP", "Enable Compound TCP",
            "将拥塞提供程序切换到 CTCP", "Switch congestion provider to CTCP"),
        ["network.disable_flow_control"] = (
            "禁用流量控制", "Disable Flow Control",
            "使网卡永不因接收拥塞而暂停出站帧", "Never pause outbound frames due to receive congestion"),
        ["network.disable_interrupt_moderation"] = (
            "禁用中断调制", "Disable Interrupt Moderation",
            "关闭中断合并，以最低延迟将数据包传递到 CPU", "Disable interrupt coalescing for lowest latency"),
        ["network.enable_lso"] = (
            "启用大数据包发送卸载", "Enable Large Send Offload",
            "确保网卡对 IPv4/IPv6 执行分段卸载", "Ensure NIC performs segmentation offload"),
        ["network.optimize_adapter_buffers"] = (
            "增加适配器缓冲区", "Increase Adapter Buffers",
            "提高接收和发送缓冲区以防止丢包", "Increase buffers to prevent packet loss"),
        ["network.optimize_checksum_offload"] = (
            "启用校验和卸载", "Enable Checksum Offload",
            "为 IPv4/IPv6 启用接收/发送校验和卸载", "Enable checksum offload for IPv4/IPv6"),
        ["network.optimize_dns"] = (
            "优化 DNS 客户端策略", "Optimize DNS Client Policy",
            "降低缓存 TTL/负缓存窗口", "Lower cache TTL/negative cache window"),
        ["network.disable_nagle"] = (
            "禁用 Nagle 算法", "Disable Nagle Algorithm",
            "以获得最低的发送延迟", "For lowest send latency"),
        ["network.increase_max_user_port"] = (
            "提高最大用户端口", "Increase Max User Port",
            "将临时端口范围扩展到 65534", "Extend ephemeral port range to 65534"),
        ["network.optimize_tcp_timed_wait"] = (
            "缩短 TIME_WAIT", "Shorten TIME_WAIT",
            "使套接字更快回收", "Faster socket recycling"),
        ["network.optimize_ttl"] = (
            "规范化默认 TTL", "Normalize Default TTL",
            "将 DefaultTTL 设置为 64", "Set DefaultTTL to 64"),
        ["network.optimize_tcp_max_dup_acks"] = (
            "调整 TcpMaxDupAcks", "Adjust TcpMaxDupAcks",
            "使重传在有损 Wi-Fi 上更快发生", "Faster retransmission on lossy Wi-Fi"),
        ["network.disable_delivery_optimization"] = (
            "禁用传递优化", "Disable Delivery Optimization",
            "停止 Windows 更新 P2P 后台上传", "Stop Windows Update P2P background upload"),
        ["network.ipv6_prefer_ipv4"] = (
            "优先使用 IPv4", "Prefer IPv4",
            "关闭 IPv6 隧道以降低私有网络延迟", "Disable IPv6 tunnels to reduce latency on private networks"),
            
        // ========== 电竞模式专用 ==========
        ["gpu.force_high_performance_gpu"] = (
            "全局优先独显", "Prefer Discrete GPU",
            "强制所有应用使用高性能独立显卡，避免游戏误用集显导致卡顿", "Force all apps to use discrete GPU to avoid stuttering from integrated graphics"),
        ["gpu.set_sfio_priority"] = (
            "GPU SFIO 优先级", "GPU SFIO Priority",
            "提升游戏的 GPU 流媒体 I/O 优先级，让显卡优先处理游戏画面渲染请求", "Increase GPU streaming I/O priority for game rendering"),
        ["fps.disable_fullscreen_optimizations"] = (
            "禁用全屏优化", "Disable Fullscreen Optimizations",
            "关闭 Windows 的全屏优化功能，使用传统独占全屏模式，降低输入延迟", "Disable Windows fullscreen optimization for lower input latency"),
        ["gaming.mode.enable"] = (
            "启用 Windows 游戏模式", "Enable Windows Game Mode",
            "开启后系统会优先为游戏分配 CPU/GPU 资源，减少后台更新和通知干扰", "Prioritize CPU/GPU resources for games, reduce background interruptions"),
        ["gaming.dvr.disable"] = (
            "禁用 Game DVR 录制", "Disable Game DVR Recording",
            "关闭 Xbox Game Bar 的后台录制功能，释放 GPU 编码资源", "Disable Xbox Game Bar background recording to free GPU resources"),
        ["privacy.disable_background_apps"] = (
            "禁用后台应用", "Disable Background Apps",
            "阻止 UWP 应用在后台运行，释放内存和 CPU 资源", "Prevent UWP apps from running in background"),
        ["basic.disable_visual_effects"] = (
            "关闭视觉效果", "Disable Visual Effects",
            "禁用 Windows 动画、透明效果和阴影，减少 GPU 占用", "Disable Windows animations and transparency for lower GPU usage"),
        ["system.boost_foreground_priority"] = (
            "优化前台优先级", "Boost Foreground Priority",
            "提升前台应用的 CPU 调度优先级，确保游戏获得更多 CPU 时间片", "Increase foreground app CPU priority for games"),
        ["peripheral.mouse_precision"] = (
            "提升鼠标精度", "Improve Mouse Precision",
            "优化鼠标 DPI 缩放和指针精度设置，消除系统对鼠标移动的额外处理", "Optimize mouse DPI scaling and pointer precision"),
        ["peripheral.mouse_polling"] = (
            "优化鼠标轮询", "Optimize Mouse Polling",
            "提升系统对鼠标轮询率的响应速度，配合高轮询率鼠标效果更佳", "Improve system response to high polling rate mice"),
        ["peripheral.keyboard.delay"] = (
            "优化键盘延迟", "Optimize Keyboard Delay",
            "将键盘首次按键延迟设为最低，按下按键后立即响应", "Set keyboard initial delay to minimum"),
        ["peripheral.keyboard.speed"] = (
            "提升键盘速度", "Increase Keyboard Speed",
            "将键盘重复率设为最高，长按按键时字符输入更快", "Set keyboard repeat rate to maximum"),
        ["peripheral.keyboard_response"] = (
            "优化键盘响应", "Optimize Keyboard Response",
            "综合优化键盘响应速度，包括按键缓冲和重复间隔", "Optimize keyboard response including buffer and repeat interval"),
        ["cpu.system_responsiveness"] = (
            "优化系统响应", "Optimize System Responsiveness",
            "调整 SystemResponsiveness 参数为 0，让系统优先响应前台应用", "Set SystemResponsiveness to 0 for foreground priority"),
        ["device.disable_system_timer"] = (
            "禁用 System Timer", "Disable System Timer",
            "禁用系统计时器设备，减少中断延迟。AMD 推荐，Intel 需谨慎", "Disable system timer device to reduce interrupt latency"),
        ["device.disable_umbus"] = (
            "禁用 UMBus 总线", "Disable UMBus",
            "禁用 UMBus 根总线枚举器，减少不必要的设备枚举开销", "Disable UMBus root bus enumerator"),
        ["device.disable_composite_bus"] = (
            "禁用复合总线", "Disable Composite Bus",
            "禁用复合总线枚举器，减少 USB 设备枚举延迟", "Disable composite bus enumerator for lower USB latency"),
        ["amd.radeon_anti_lag"] = (
            "AMD Radeon 低延迟模式", "AMD Radeon Low Latency",
            "禁用 ULPS 节能，降低 AMD 显卡唤醒延迟", "Disable ULPS power saving for lower AMD GPU wake latency"),
        ["amd.precision_boost"] = (
            "AMD Precision Boost 优化", "AMD Precision Boost Optimization",
            "优化 Precision Boost Overdrive 设置，提升 Ryzen 处理器睿频表现", "Optimize PBO settings for better Ryzen boost performance"),
        ["amd.disable_cool_n_quiet"] = (
            "禁用 Cool'n'Quiet", "Disable Cool'n'Quiet",
            "禁用 AMD 节能技术，CPU 保持最高频率运行。会增加功耗发热", "Disable AMD power saving, keep CPU at max frequency"),
            
        // ========== 系统核心调校 (System Tuning) ==========
        ["system.disable_hibernation"] = (
            "关闭休眠并回收 hiberfil.sys", "Disable Hibernation",
            "禁用休眠以释放系统盘空间并避免电源方案冲突", "Disable hibernation to free disk space"),
        ["system.disable_kernel_paging"] = (
            "禁止内核代码分页", "Disable Kernel Paging",
            "确保内核与驱动常驻内存降低调度延迟", "Keep kernel and drivers in memory for lower latency"),
        ["system.boost_game_priority"] = (
            "提高游戏任务优先级", "Boost Game Priority",
            "使游戏 CPU 调度权重更高", "Increase game CPU scheduling priority"),
        ["system.prioritize_games"] = (
            "设置游戏调度类别", "Set Game Scheduling Category",
            "保证任务始终运行在高优先级队列", "Ensure tasks run in high priority queue"),
        ["system.disable_large_system_cache"] = (
            "禁用 Large System Cache", "Disable Large System Cache",
            "将更多内存留给前台游戏", "Reserve more memory for foreground games"),
        ["system.disable_prefetch"] = (
            "禁用 Prefetch", "Disable Prefetch",
            "避免 SSD 场景下的冗余预读", "Avoid redundant prefetch on SSD"),
        ["power.restore_point_frequency"] = (
            "允许频繁创建还原点", "Allow Frequent Restore Points",
            "绕过还原点 24 小时冷却窗口", "Bypass the 24-hour restore point cooldown"),
        ["power.s0_network_ac"] = (
            "待机时断开网络连接（AC）", "Disconnect Network in Standby (AC)",
            "S0 现代待机期间断开网络，减少待机功耗", "Disconnect network during S0 Modern Standby to save power"),
        ["power.s0_network_dc"] = (
            "待机时断开网络连接（DC）", "Disconnect Network in Standby (DC)",
            "S0 现代待机期间断开网络，减少待机功耗", "Disconnect network during S0 Modern Standby to save power"),
        ["system.long_paths"] = (
            "启用长路径支持", "Enable Long Paths",
            "允许超过 260 字符的文件路径", "Allow file paths longer than 260 characters"),
        ["system.disable_wpbt"] = (
            "禁用 WPBT 启动执行", "Disable WPBT Execution",
            "阻止 OEM 启动阶段注入程序", "Block OEM program injection at boot"),
            
        // ========== 系统调校 (Tuning) ==========
        ["adjustments.update_storage_reserve"] = (
            "关闭更新保留空间", "Disable Update Storage Reserve",
            "禁用 Windows Update 预留磁盘容量", "Disable Windows Update reserved storage"),
        ["adjustments.disable_last_access_update"] = (
            "禁用 Last Access 更新时间戳", "Disable Last Access Timestamp",
            "降低 NTFS 每次访问写入的 IO", "Reduce NTFS IO on file access"),
        ["tuning.largecache"] = (
            "更新缓存保留值", "Update Cache Reserve",
            "避免服务器缓存策略影响游戏延迟", "Avoid server cache policy affecting game latency"),
        ["tuning.prefetcher"] = (
            "禁用 Prefetcher", "Disable Prefetcher",
            "减少机械盘预取带来的 IO 抢占", "Reduce IO contention from HDD prefetch"),
        ["tuning.superfetch"] = (
            "禁用 SuperFetch 注册表", "Disable SuperFetch Registry",
            "关闭 EnableSuperfetch，配合同步停用服务", "Disable SuperFetch with service"),
        ["tuning.recentdocs.disable"] = (
            "禁用最近访问记录", "Disable Recent Documents",
            "阻止资源占用和历史缓存", "Prevent resource usage and history cache"),
        ["tuning.recentapps.disable"] = (
            "禁用最近应用记录", "Disable Recent Apps",
            "避免开始菜单历史下拉", "Avoid Start menu history dropdown"),
        ["tuning.powerthrottling"] = (
            "禁用 Power Throttling", "Disable Power Throttling",
            "确保前台进程不被自动限频", "Ensure foreground processes are not throttled"),
        ["tuning.powerplan.high"] = (
            "启用高性能电源模式", "Enable High Performance Power Plan",
            "切换到内置高性能方案，提升供电策略", "Switch to high performance power plan"),
            
        // ========== 资源管理器界面 (Desktop/Explorer) ==========
        ["registry.show_file_extensions"] = (
            "显示文件扩展名", "Show File Extensions",
            "使所有文件始终显示其扩展名", "Always show file extensions"),
        ["explorer.hidden_files"] = (
            "显示隐藏文件", "Show Hidden Files",
            "在资源管理器中显示隐藏文件与文件夹", "Show hidden files and folders in Explorer"),
        ["explorer.end_task"] = (
            "任务栏右键结束任务", "End Task on Taskbar",
            "在任务栏右键菜单显示“结束任务”", "Show \"End task\" in the taskbar context menu"),
        ["registry.disable_thumbnail_delay"] = (
            "移除缩略图悬停延迟", "Remove Thumbnail Hover Delay",
            "使任务栏预览立即出现", "Make taskbar preview appear immediately"),
        ["registry.disable_startup_sound"] = (
            "禁用启动声音", "Disable Startup Sound",
            "在共享空间中保持启动静音", "Keep startup silent in shared spaces"),
        ["registry.disable_low_disk_check"] = (
            "禁用低磁盘空间警报", "Disable Low Disk Space Alert",
            "防止重复的气球提示", "Prevent repeated balloon notifications"),
        ["edge.disable_game_assist"] = (
            "禁用 Edge 游戏助手", "Disable Edge Game Assist",
            "关闭 Edge 游戏助手覆盖层", "Disable Edge Game Assist overlay"),
        ["edge.disable_game_assist_policy"] = (
            "禁用 Edge 游戏助手（策略）", "Disable Edge Game Assist (Policy)",
            "通过组策略彻底禁用 Edge 游戏助手", "Disable Edge Game Assist via policy"),
            
        // ========== 任务调度 (Task Scheduler) ==========
        ["tasks.disable_app_experience"] = (
            "禁用 Application Experience", "Disable Application Experience",
            "关闭 Microsoft Compatibility Appraiser", "Disable Microsoft Compatibility Appraiser"),
        ["tasks.disable_auto_proxy_detection"] = (
            "禁用自动代理检测", "Disable Auto Proxy Detection",
            "关闭 Autochk\\Proxy 计划任务", "Disable Autochk\\Proxy scheduled task"),
        ["tasks.disable_ceip"] = (
            "禁用 CEIP 计划任务", "Disable CEIP Task",
            "停止 Customer Experience Improvement Program", "Stop Customer Experience Improvement Program"),
        ["tasks.disable_cleanup_tasks"] = (
            "禁用系统清理任务", "Disable System Cleanup Tasks",
            "关闭 StartComponentCleanup，防止自动 DISM 清理", "Disable StartComponentCleanup"),
        ["tasks.disable_defragmentation"] = (
            "禁用磁盘碎片整理", "Disable Disk Defragmentation",
            "阻止 ScheduledDefrag 在高峰期运行", "Prevent ScheduledDefrag from running"),
        ["tasks.disable_disk_diagnostic"] = (
            "禁用磁盘诊断", "Disable Disk Diagnostic",
            "关闭 DiskDiagnosticDataCollector", "Disable DiskDiagnosticDataCollector"),
        ["tasks.disable_ms_store_update_tasks"] = (
            "禁用商店自动更新", "Disable Store Auto-Update",
            "关闭 WindowsUpdate\\Automatic App Update", "Disable Automatic App Update"),
        ["tasks.disable_ms_sync_tasks"] = (
            "禁用 Sync Center", "Disable Sync Center",
            "停用 SyncCenterScheduledSync", "Disable SyncCenterScheduledSync"),
        ["tasks.disable_notification_tasks"] = (
            "禁用备份通知", "Disable Backup Notifications",
            "关闭 WindowsBackup\\ConfigNotification", "Disable backup notifications"),
        ["tasks.disable_policy_updates"] = (
            "禁用策略刷新任务", "Disable Policy Refresh Task",
            "停止 WindowsUpdate\\Scheduled Start", "Stop WindowsUpdate\\Scheduled Start"),
        ["tasks.disable_remote_management"] = (
            "禁用远程协助计划", "Disable Remote Assistance Task",
            "关闭 RemoteAssistanceTask", "Disable RemoteAssistanceTask"),
        ["tasks.disable_store_features"] = (
            "禁用 Store Feature Installers", "Disable Store Feature Installers",
            "阻断 CloudExperienceHost\\CreateObjectTask", "Disable CloudExperienceHost\\CreateObjectTask"),
        ["tasks.disable_sync_features"] = (
            "禁用 SettingSync", "Disable SettingSync",
            "关闭 SettingSync\\BackgroundUploadTask", "Disable SettingSync\\BackgroundUploadTask"),
        ["tasks.disable_windows_update"] = (
            "禁用 Windows Update 扫描", "Disable Windows Update Scan",
            "关闭 UpdateOrchestrator\\Schedule Scan", "Disable UpdateOrchestrator\\Schedule Scan"),
        ["tasks.disable_xbox_features"] = (
            "禁用 Xbox 功能任务", "Disable Xbox Feature Tasks",
            "停止 XblGameSaveTask", "Stop XblGameSaveTask"),
        ["tasks.disable_xbox_tasks"] = (
            "禁用 Xbox 相关登陆任务", "Disable Xbox Login Tasks",
            "关闭 XblGameSaveTaskLogon", "Disable XblGameSaveTaskLogon"),
            
        // ========== 系统服务 (Services) ==========
        ["service.disable_spooler"] = (
            "停止 Print Spooler", "Stop Print Spooler",
            "在无打印需求时禁用 Spooler", "Disable Spooler when not printing"),
        ["service.disable_wsearch"] = (
            "停用 Windows Search", "Disable Windows Search",
            "WSearch 服务停用后释放磁盘与内存资源", "Free disk and memory resources"),
        ["service.disable_telemetry"] = (
            "禁用核心遥测服务 (DiagTrack)", "Disable Core Telemetry (DiagTrack)",
            "关闭 Connected User Experiences and Telemetry", "Disable telemetry service"),
        ["services.disable_diagtrack"] = (
            "额外关闭共享遥测服务", "Disable Additional Telemetry Services",
            "禁用 dmwappushservice 等推送遥测相关服务", "Disable dmwappushservice and related services"),
        ["services.disable_fax"] = (
            "禁用 Fax 服务", "Disable Fax Service",
            "桌面场景极少使用传真，可关闭以减轻占用", "Disable rarely used fax service"),
        ["services.disable_maps_broker"] = (
            "停用 MapsBroker", "Disable MapsBroker",
            "阻止离线地图托管服务常驻后台", "Prevent offline maps service from running"),
        ["services.disable_remote_registry"] = (
            "禁用 Remote Registry", "Disable Remote Registry",
            "避免被远程修改注册表，提升安全性", "Prevent remote registry modification"),
        ["services.disable_sysmain_unique"] = (
            "停止 SysMain/SuperFetch", "Stop SysMain/SuperFetch",
            "关闭 SysMain，防止磁盘预读拖慢 SSD", "Disable SysMain to prevent SSD slowdown"),
        ["services.disable_wallet_service"] = (
            "关闭 WalletService", "Disable WalletService",
            "在非 UWP 支付场景下禁用 WalletService", "Disable WalletService for non-UWP scenarios"),
        ["services.disable_xbox_services"] = (
            "停用 Xbox 关联服务", "Disable Xbox Services",
            "禁用 XboxGipSvc/XblAuthManager/XblGameSave 等服务", "Disable Xbox-related services"),
        ["service.bluetooth.disable"] = (
            "禁用蓝牙后台服务", "Disable Bluetooth Service",
            "在无蓝牙需求时关闭 bthserv", "Disable bthserv when not using Bluetooth"),
        ["service.wer.disable"] = (
            "禁用 Windows 错误报告", "Disable Windows Error Reporting",
            "关闭 WerSvc 避免后台上传", "Disable WerSvc to prevent uploads"),
        ["service.sensordata.disable"] = (
            "禁用步数记录器", "Disable Step Counter",
            "关闭 SensorDataService，禁止计步与传感搜集", "Disable SensorDataService"),
        ["services.disable_game_dvr"] = (
            "禁用 Game DVR 录制", "Disable Game DVR Recording",
            "AllowGameDVR=0，阻止系统后台录屏", "Prevent background game recording"),
        ["services.enable_game_mode"] = (
            "启用 Game Mode", "Enable Game Mode",
            "AllowAutoGameMode=1，保持游戏优先资源调度", "Keep game priority resource scheduling"),
        ["services.enable_high_performance_mode"] = (
            "激活高性能电源计划", "Activate High Performance Power Plan",
            "切换到系统内置高性能/卓越性能方案", "Switch to high performance power plan"),
            
        // ========== 系统清理 (Cleanup) ==========
        ["cleanup.gpu_cache"] = (
            "GPU 缓存旧入口不可用", "Legacy GPU Cache Entry Unavailable",
            "请使用具备可恢复隔离能力的系统清理入口", "Use the system cleanup flow with recoverable quarantine"),
        ["cleanup.prefetch_files"] = (
            "Prefetch 清理旧入口不可用", "Legacy Prefetch Entry Unavailable",
            "旧入口不会直接删除 Windows Prefetch", "The legacy entry will not delete Windows Prefetch"),
        ["cleanup.system_temp"] = (
            "系统临时文件旧入口不可用", "Legacy System Temp Entry Unavailable",
            "必须先生成预览、隔离清单和恢复证明", "Preview, quarantine manifest, and restore proof are required"),
        ["cleanup.user_temp"] = (
            "用户临时文件旧入口不可用", "Legacy User Temp Entry Unavailable",
            "必须先生成预览、隔离清单和恢复证明", "Preview, quarantine manifest, and restore proof are required"),
        ["cleanup.thumbnail_cache"] = (
            "缩略图缓存旧入口不可用", "Legacy Thumbnail Cache Entry Unavailable",
            "旧入口不会删除 Explorer 缩略图缓存", "The legacy entry will not delete Explorer thumbnail cache"),
        ["cleanup.windows_logs"] = (
            "系统日志清理旧入口不可用", "Legacy System Log Entry Unavailable",
            "诊断日志不会由旧入口永久删除", "Diagnostic logs will not be permanently deleted by this entry"),
        ["cleanup.windows_update_cache"] = (
            "更新缓存旧入口不可用", "Legacy Update Cache Entry Unavailable",
            "旧入口不会直接删除更新缓存", "The legacy entry will not delete update cache files"),
        ["cleanup.recycle_bin"] = (
            "回收站永久清空被阻断", "Permanent Recycle Bin Emptying Blocked",
            "优化目录不会永久删除回收站内容", "The optimization catalog will not permanently delete recycle bin contents"),
            
        // ========== 外设调优 (Peripherals) ==========
        ["peripheral.anti_mistouch"] = (
            "禁用 Win 键误触", "Disable Win Key Mistouch",
            "避免游戏过程中误触 Windows 键弹出开始菜单", "Prevent accidental Win key press during gaming"),
        ["peripheral.usb.suspend"] = (
            "禁用 USB 选择性挂起", "Disable USB Selective Suspend",
            "避免供电不足导致的外设掉线", "Prevent peripheral disconnection from power saving"),
        ["peripheral.usb_suspend"] = (
            "禁用 USB 选择性挂起（兼容版）", "Disable USB Selective Suspend (Compatible)",
            "同步禁用 AC/DC USB selective suspend", "Disable USB suspend for AC/DC"),
            
        // ========== 设备优化 (Device) ==========
        ["device.disable_system_timer"] = (
            "禁用 System Timer", "Disable System Timer",
            "禁用系统计时器设备，AMD CPU 推荐", "Disable system timer device, recommended for AMD"),
        ["device.disable_umbus"] = (
            "禁用 UMBus Root Bus Enumerator", "Disable UMBus Root Bus Enumerator",
            "禁用 UMBus 根总线枚举器，减少系统开销", "Disable UMBus root bus enumerator"),
        ["device.disable_composite_bus"] = (
            "禁用 Composite Bus Enumerator", "Disable Composite Bus Enumerator",
            "禁用复合总线枚举器，减少 USB 相关中断", "Disable composite bus enumerator"),
        ["device.disable_hpet_device"] = (
            "禁用 HPET 设备", "Disable HPET Device",
            "在设备管理器级别禁用高精度事件计时器", "Disable HPET in device manager"),
            
        // ========== 游戏模式 (Gaming) ==========
        ["gaming.mode.enable"] = (
            "启用系统游戏模式", "Enable System Game Mode",
            "AllowAutoGameMode=1，保持游戏优先", "Keep game priority"),
        ["gaming.dvr.disable"] = (
            "禁用 Game DVR", "Disable Game DVR",
            "通过策略禁止游戏录制，释放资源", "Disable game recording via policy"),
            
        // ========== GPU 高级优化 ==========
        ["gpu.disable_game_mode_optimizations"] = (
            "禁用游戏模式优化", "Disable Game Mode Optimizations",
            "关闭 Windows 游戏模式的自动优化", "Disable Windows Game Mode auto-optimization"),
        ["gpu.enable_hags"] = (
            "开启硬件加速 GPU 调度", "Enable Hardware-Accelerated GPU Scheduling",
            "启用 HAGS，可降低渲染延迟", "Enable HAGS for lower rendering latency"),
            
        // ========== 电源计划优化 (Power Plan) ==========
        ["power.disable_usb_suspend"] = (
            "禁用 USB 选择性暂停", "Disable USB Selective Suspend",
            "防止 USB 设备休眠导致鼠标键盘延迟，游戏必备优化", "Prevent USB device sleep causing mouse/keyboard lag"),
        ["power.disable_aspm"] = (
            "禁用 PCIe 节能 (ASPM)", "Disable PCIe Power Saving (ASPM)",
            "关闭 PCIe Link State Power Management，降低显卡和 NVMe 延迟", "Disable PCIe Link State Power Management for lower GPU and NVMe latency"),
        ["power.processor_min_state_100"] = (
            "处理器最小状态 100%", "Processor Minimum State 100%",
            "CPU 始终保持最高频率运行，不降频，游戏性能最大化", "CPU always runs at max frequency, no downclocking, maximum gaming performance"),
        ["power.perf_increase_threshold"] = (
            "处理器性能提升阈值", "Processor Performance Increase Threshold",
            "设为 1%，CPU 使用率稍有提升就立即触发睿频，响应更快", "Set to 1%, CPU boosts immediately on slight load increase"),
        ["power.idle_demote_threshold"] = (
            "处理器闲置降级阈值", "Processor Idle Demote Threshold",
            "设为 100%，防止 CPU 因短暂空闲而降频，保持高性能状态", "Set to 100%, prevent CPU from downclocking on brief idle"),
        ["power.hetero_thread_scheduling"] = (
            "异类线程调度策略", "Heterogeneous Thread Scheduling",
            "Intel 大小核调度优化：大核优先处理游戏线程，小核处理后台任务。AMD 用户选择「所有处理器」", "Intel hybrid core scheduling: P-Cores prioritize game threads, E-Cores handle background tasks"),
        ["power.perf_time_check"] = (
            "处理器性能时间检查间隔", "Processor Performance Time Check Interval",
            "设为最大值，减少 CPU 频率切换次数，保持稳定高频", "Set to max, reduce CPU frequency switching for stable high frequency"),
        ["power.class1_efficiency_threshold"] = (
            "第一类处理器效率阈值", "Class 1 Processor Efficiency Threshold",
            "设为 1%，大核（P-Core）更快响应负载变化，提升游戏帧率稳定性", "Set to 1%, P-Cores respond faster to load changes for stable FPS"),
        ["power.class1_perf_level"] = (
            "第一类处理器性能级别", "Class 1 Processor Performance Level",
            "设为 100%，大核（P-Core）始终保持最高性能，不降频", "Set to 100%, P-Cores always at max performance"),
        ["power.disable_throttle"] = (
            "禁止 CPU 节流", "Disable CPU Throttling",
            "关闭允许节流状态，防止 CPU 因温度或功耗限制而降频", "Disable throttle state, prevent CPU from downclocking due to temp/power limits"),
            
        // ========== 辅助功能模块操作 ==========
        ["accessibility.disable_sticky_keys"] = (
            "禁用粘滞键", "Disable Sticky Keys",
            "禁用粘滞键功能，避免游戏中连按 Shift 弹出提示", "Disable Sticky Keys to prevent prompts when pressing Shift repeatedly"),
        ["accessibility.disable_filter_keys"] = (
            "禁用筛选键", "Disable Filter Keys",
            "禁用筛选键功能，避免长按右 Shift 弹出提示", "Disable Filter Keys to prevent prompts when holding right Shift"),
        ["accessibility.disable_toggle_keys"] = (
            "禁用切换键", "Disable Toggle Keys",
            "禁用切换键功能，避免按 Num Lock 等键时发出提示音", "Disable Toggle Keys to prevent beeps when pressing Num Lock"),
    };
    
    private static readonly Dictionary<string, (string Zh, string En)> _categoryTranslations = new()
    {
        ["音频优化"] = ("音频优化", "Audio Optimization"),
        ["CPU 高级优化"] = ("CPU 高级优化", "Advanced CPU Optimization"),
        ["GPU 深度优化"] = ("GPU 深度优化", "Advanced GPU Optimization"),
        ["输入延迟"] = ("输入延迟", "Input Latency"),
        ["网络深度优化"] = ("网络深度优化", "Advanced Network Optimization"),
        ["安全缓解"] = ("安全缓解", "Security Mitigations"),
        ["存储优化"] = ("存储优化", "Storage Optimization"),
        ["Windows 功能"] = ("Windows 功能", "Windows Features"),
        ["Intel 专属优化"] = ("Intel 专属优化", "Intel-Specific Optimization"),
        ["AMD 专属优化"] = ("AMD 专属优化", "AMD-Specific Optimization"),
        ["隐私保护"] = ("隐私保护", "Privacy Protection"),
        ["隐私防护"] = ("隐私防护", "Privacy Protection"),
        ["系统服务"] = ("系统服务", "System Services"),
        ["计划任务"] = ("计划任务", "Scheduled Tasks"),
        ["任务调度"] = ("任务调度", "Task Scheduler"),
        ["外设优化"] = ("外设优化", "Peripheral Optimization"),
        ["外设调优"] = ("外设调优", "Peripheral Tuning"),
        ["内存优化"] = ("内存优化", "Memory Optimization"),
        ["桌面体验"] = ("桌面体验", "Desktop Experience"),
        ["资源管理器界面"] = ("资源管理器界面", "Explorer Interface"),
        ["系统调校"] = ("系统调校", "System Tuning"),
        ["系统核心调校"] = ("系统核心调校", "Core System Tuning"),
        ["系统清理"] = ("系统清理", "System Cleanup"),
        ["网络优化"] = ("网络优化", "Network Optimization"),
        ["网络稳定"] = ("网络稳定", "Network Stability"),
        ["图形渲染"] = ("图形渲染", "Graphics Rendering"),
        ["图形与帧率"] = ("图形与帧率", "Graphics & FPS"),
        ["处理器优化"] = ("处理器优化", "Processor Optimization"),
        ["CPU 调优"] = ("CPU 调优", "CPU Tuning"),
        ["其他"] = ("其他", "Other"),
        ["电竞模式"] = ("电竞模式", "Esports Mode"),
        ["性能加速"] = ("性能加速", "Performance Boost"),
        ["游戏优化"] = ("游戏优化", "Game Optimization"),
        ["游戏模式"] = ("游戏模式", "Game Mode"),
        ["设备优化"] = ("设备优化", "Device Optimization"),
        ["辅助功能"] = ("辅助功能", "Accessibility"),
        ["GPU 高级优化"] = ("GPU 高级优化", "Advanced GPU Optimization"),
    };

    public string GetTitle(string operationId, string fallback, bool isEnglish)
    {
        if (_translations.TryGetValue(operationId, out var t))
            return isEnglish ? t.TitleEn : t.TitleZh;
        return fallback;
    }
    
    public string GetTitle(string operationId)
    {
        var isEnglish = LocalizationService.Instance.CurrentLanguage != "zh-CN";
        if (_translations.TryGetValue(operationId, out var t))
            return isEnglish ? t.TitleEn : t.TitleZh;
        return operationId;
    }

    public string GetDescription(string operationId, string fallback, bool isEnglish)
    {
        if (_translations.TryGetValue(operationId, out var t))
            return isEnglish ? t.DescEn : t.DescZh;
        return fallback;
    }

    public string GetCategory(string category, bool isEnglish)
    {
        if (_categoryTranslations.TryGetValue(category, out var t))
            return isEnglish ? t.En : t.Zh;
        return category;
    }
    
    public string GetModuleName(string moduleName)
    {
        var isEnglish = LocalizationService.Instance.CurrentLanguage != "zh-CN";
        if (_categoryTranslations.TryGetValue(moduleName, out var t))
            return isEnglish ? t.En : t.Zh;
        return moduleName;
    }
    
    // Static wrappers for backward compatibility
    public static string GetTitleStatic(string operationId, string fallback, bool isEnglish) => Instance.GetTitle(operationId, fallback, isEnglish);
    public static string GetTitleStatic(string operationId) => Instance.GetTitle(operationId);
    public static string GetDescriptionStatic(string operationId, string fallback, bool isEnglish) => Instance.GetDescription(operationId, fallback, isEnglish);
    public static string GetCategoryStatic(string category, bool isEnglish) => Instance.GetCategory(category, isEnglish);
    public static string GetModuleNameStatic(string moduleName) => Instance.GetModuleName(moduleName);
    
    /// <summary>
    /// 获取操作的"为什么这样做"专业说明，无匹配时返回 null
    /// </summary>
    public string? GetWhy(string operationId, bool isEnglish)
    {
        if (_whyTranslations.TryGetValue(operationId, out var w))
            return isEnglish ? w.En : w.Zh;
        return null;
    }
    
    private static readonly Dictionary<string, (string Zh, string En)> _whyTranslations = new()
    {
        // ========== 音频优化 ==========
        ["audio.disable_enhancements"] = (
            "Windows 音频增强会对原始音频信号进行额外的 DSP 处理（均衡器、虚拟环绕等），每次处理都会引入数毫秒的延迟。禁用后音频直通声卡，延迟可降低 5-15ms，FPS 游戏中能更快听到脚步声和枪声方位。",
            "Windows audio enhancements apply DSP processing (equalizer, virtual surround, etc.) to the raw audio signal, adding several milliseconds of latency per pass. Disabling them allows audio to pass directly to the sound card, reducing latency by 5-15ms — critical for hearing footsteps and gunshot directions in FPS games."),
        ["audio.exclusive_mode"] = (
            "独占模式允许游戏绕过 Windows 音频混合器（AudioSes），直接控制声卡硬件。这消除了混合器引入的缓冲延迟（通常 10-30ms），让游戏获得最低的音频输出延迟。",
            "Exclusive mode lets games bypass the Windows audio mixer (AudioSes) and directly control the sound card hardware. This eliminates the mixer's buffer latency (typically 10-30ms), giving games the lowest possible audio output latency."),
        ["audio.exclusive_mode_priority"] = (
            "独占模式允许游戏绕过 Windows 音频混合器（AudioSes），直接控制声卡硬件。这消除了混合器引入的缓冲延迟（通常 10-30ms），让游戏获得最低的音频输出延迟。",
            "Exclusive mode lets games bypass the Windows audio mixer (AudioSes) and directly control the sound card hardware. This eliminates the mixer's buffer latency (typically 10-30ms), giving games the lowest possible audio output latency."),
        ["audio.disable_spatial"] = (
            "空间音效（Windows Sonic / Dolby Atmos）需要实时计算 3D 声场定位，消耗额外的 CPU 和 DSP 资源。对于已内置 HRTF 音频引擎的游戏，系统级空间音效反而会产生双重处理，导致声音定位不准且增加延迟。",
            "Spatial audio (Windows Sonic / Dolby Atmos) requires real-time 3D sound field calculations, consuming extra CPU and DSP resources. For games with built-in HRTF audio engines, system-level spatial audio causes double processing, leading to inaccurate sound positioning and increased latency."),
        ["audio.disable_spatial_sound"] = (
            "空间音效（Windows Sonic / Dolby Atmos）需要实时计算 3D 声场定位，消耗额外的 CPU 和 DSP 资源。对于已内置 HRTF 音频引擎的游戏，系统级空间音效反而会产生双重处理，导致声音定位不准且增加延迟。",
            "Spatial audio (Windows Sonic / Dolby Atmos) requires real-time 3D sound field calculations, consuming extra CPU and DSP resources. For games with built-in HRTF audio engines, system-level spatial audio causes double processing, leading to inaccurate sound positioning and increased latency."),
        ["audio.optimize_buffer"] = (
            "音频缓冲区越大，系统等待填满缓冲区的时间越长，延迟越高。减小缓冲区大小可以让音频数据更快送达声卡，降低从游戏事件到实际听到声音之间的延迟，代价是对 CPU 调度精度要求更高。",
            "Larger audio buffers mean the system waits longer to fill them before sending to the sound card, increasing latency. Reducing buffer size delivers audio data faster, lowering the delay between game events and actual sound output, at the cost of requiring more precise CPU scheduling."),
        ["audio.disable_service_restart"] = (
            "Windows 音频服务（Audiosrv）默认配置为崩溃后自动重启，重启过程中会导致 1-3 秒的声音中断。优化启动方式可以避免游戏中突然出现的声音断裂，同时减少服务监控的后台开销。",
            "The Windows Audio service (Audiosrv) is configured to auto-restart on crash, causing 1-3 seconds of sound interruption during restart. Optimizing the startup behavior prevents sudden audio cuts during gaming while reducing background monitoring overhead."),
        ["audio.optimize_sample_rate"] = (
            "提升音频处理线程的优先级，确保音频数据能及时被处理而不被其他低优先级任务抢占 CPU 时间。这在高负载场景下尤为重要，可以防止游戏高帧率运行时出现音频卡顿或爆音。",
            "Increasing the audio processing thread priority ensures audio data is processed promptly without being preempted by lower-priority tasks. This is especially important under high load, preventing audio stuttering or crackling when games run at high frame rates."),
        ["audio.disable_ducking"] = (
            "Windows 默认在检测到语音通话时自动降低其他应用的音量（最多降 80%）。这在游戏中使用 Discord/TeamSpeak 时会导致游戏音效突然变小，影响听声辨位。禁用后游戏音量保持恒定。",
            "Windows automatically reduces other app volumes (up to 80%) when voice calls are detected. This causes game audio to suddenly drop when using Discord/TeamSpeak, affecting sound-based positioning. Disabling this keeps game volume constant."),
        ["audio.disable_comm_ducking"] = (
            "Windows 默认在检测到语音通话时自动降低其他应用的音量（最多降 80%）。这在游戏中使用 Discord/TeamSpeak 时会导致游戏音效突然变小，影响听声辨位。禁用后游戏音量保持恒定。",
            "Windows automatically reduces other app volumes (up to 80%) when voice calls are detected. This causes game audio to suddenly drop when using Discord/TeamSpeak, affecting sound-based positioning. Disabling this keeps game volume constant."),
        // ========== CPU 优化 ==========
        ["cpu.disable_pstates"] = (
            "CPU P-States（性能状态）允许处理器根据负载动态调整频率和电压。虽然节能，但频率切换需要数微秒到数毫秒的过渡时间，在游戏中可能导致微卡顿。锁定最高频率消除了这种切换延迟。",
            "CPU P-States allow the processor to dynamically adjust frequency and voltage based on load. While energy-efficient, frequency transitions take microseconds to milliseconds, potentially causing micro-stutters in games. Locking maximum frequency eliminates this switching latency."),
        ["cpu.disable_tsc_sync"] = (
            "TSC（时间戳计数器）同步确保多核间的时间一致性，但同步过程本身会引入核间通信开销。在某些多核场景下禁用同步可以减少这种开销，但需注意可能影响依赖精确计时的应用。",
            "TSC (Time Stamp Counter) synchronization ensures time consistency across cores, but the sync process itself introduces inter-core communication overhead. Disabling it in certain multi-core scenarios can reduce this overhead, though it may affect applications relying on precise timing."),
        ["cpu.adjust_csrss_priority"] = (
            "CSRSS（客户端/服务器运行时子系统）是 Windows 核心进程，负责控制台窗口和线程管理。降低其优先级可以让游戏线程获得更多的 CPU 时间片，减少因系统进程抢占导致的帧时间波动。",
            "CSRSS (Client/Server Runtime Subsystem) is a core Windows process handling console windows and thread management. Lowering its priority gives game threads more CPU time slices, reducing frame time fluctuations caused by system process preemption."),
        ["cpu.disable_core_parking"] = (
            "核心停靠（Core Parking）会将空闲的 CPU 核心置于深度睡眠状态以节能。当游戏突然需要更多核心时，唤醒过程需要数百微秒到数毫秒，导致帧时间突增。保持所有核心活跃消除了这种唤醒延迟。",
            "Core Parking puts idle CPU cores into deep sleep to save power. When games suddenly need more cores, the wake-up process takes hundreds of microseconds to milliseconds, causing frame time spikes. Keeping all cores active eliminates this wake-up latency."),
        ["cpu.timer_resolution"] = (
            "Windows 默认系统计时器精度为 15.6ms，这意味着线程调度和睡眠唤醒的最小粒度是 15.6ms。提高到 0.5-1ms 后，游戏的输入轮询、物理计算和帧调度都能更精确地执行，显著降低输入延迟。",
            "The default Windows system timer resolution is 15.6ms, meaning thread scheduling and sleep wake-up granularity is 15.6ms minimum. Increasing to 0.5-1ms allows game input polling, physics calculations, and frame scheduling to execute more precisely, significantly reducing input latency."),
        ["cpu.enable_tsx"] = (
            "Intel TSX（事务性同步扩展）提供硬件级别的事务内存支持，可以减少多线程程序中锁竞争的开销。对于使用细粒度锁的游戏引擎，TSX 可以提升多线程性能，但需要 CPU 硬件支持。",
            "Intel TSX (Transactional Synchronization Extensions) provides hardware-level transactional memory support, reducing lock contention overhead in multi-threaded programs. For game engines using fine-grained locks, TSX can improve multi-threaded performance, but requires CPU hardware support."),
        ["cpu.processor_boost_mode"] = (
            "Processor Boost Mode 控制 CPU 睿频行为。开启后处理器会在负载增加时自动提升到最高睿频频率，确保游戏获得最大的单核性能。这对于依赖单线程性能的游戏尤为重要。",
            "Processor Boost Mode controls CPU turbo boost behavior. When enabled, the processor automatically boosts to maximum turbo frequency under load, ensuring games get maximum single-core performance. This is especially important for games that rely on single-threaded performance."),
        ["cpu.disable_boost_mode"] = (
            "在某些散热受限的场景下，持续睿频会导致 CPU 过热降频（Thermal Throttling），反而造成性能波动。禁用 Boost 后 CPU 运行在基础频率，虽然峰值性能降低，但帧时间更稳定。",
            "In thermally constrained scenarios, sustained turbo boost can cause CPU thermal throttling, actually causing performance fluctuations. Disabling boost keeps the CPU at base frequency — lower peak performance but more stable frame times."),
        ["cpu.disable_dynamic_tick"] = (
            "Dynamic Tick 允许系统在空闲时跳过时钟中断以节能。但这种不规则的时钟行为会导致帧时间测量不准确，在某些游戏中表现为微卡顿。禁用后时钟中断保持固定间隔，帧调度更稳定。",
            "Dynamic Tick allows the system to skip clock interrupts during idle to save power. But this irregular clock behavior causes inaccurate frame time measurements, manifesting as micro-stutters in some games. Disabling it keeps clock interrupts at fixed intervals for more stable frame scheduling."),
        ["cpu.configure_hpet"] = (
            "HPET（高精度事件计时器）在某些系统上会引入额外的计时延迟。移除 useplatformclock 绑定后，系统可以选择更快的 TSC 作为计时源，减少每次计时查询的开销。",
            "HPET (High Precision Event Timer) can introduce additional timing latency on some systems. Removing the useplatformclock binding allows the system to use the faster TSC as the timing source, reducing overhead per timing query."),
        ["cpu.use_platform_tick"] = (
            "TSC（时间戳计数器）是现代 CPU 内置的高精度计时器，读取速度远快于 HPET。AMD Ryzen 处理器的 TSC 实现特别稳定，使用 TSC 替代 HPET 可以降低计时开销，减少帧时间波动。",
            "TSC (Time Stamp Counter) is a high-precision timer built into modern CPUs, much faster to read than HPET. AMD Ryzen processors have particularly stable TSC implementations. Using TSC instead of HPET reduces timing overhead and frame time fluctuations."),
        ["cpu.force_high_timer_resolution"] = (
            "将系统计时器锁定在 0.5ms 精度，确保所有依赖 Sleep/WaitForSingleObject 的线程调度都以最高精度执行。这对降低输入延迟和音频延迟都有直接帮助，是竞技游戏的基础优化。",
            "Locking the system timer at 0.5ms precision ensures all thread scheduling relying on Sleep/WaitForSingleObject executes at maximum precision. This directly helps reduce both input and audio latency — a fundamental optimization for competitive gaming."),
        ["cpu.disable_spectre_meltdown"] = (
            "Spectre/Meltdown 缓解措施通过在每次系统调用和上下文切换时刷新分支预测缓存和 TLB，防止侧信道攻击。这些额外操作会增加 5-15% 的 CPU 开销。禁用后可回收这部分性能，但会降低安全防护等级。",
            "Spectre/Meltdown mitigations flush branch prediction caches and TLB on every system call and context switch to prevent side-channel attacks. These extra operations add 5-15% CPU overhead. Disabling them recovers this performance but reduces security protection."),
        ["cpu.system_responsiveness"] = (
            "SystemResponsiveness 参数控制 Windows 为后台任务保留的 CPU 资源比例。默认值 20 表示保留 20% 给后台。设为 0 后系统将几乎所有 CPU 资源优先分配给前台游戏，显著提升游戏帧率稳定性。",
            "The SystemResponsiveness parameter controls the percentage of CPU resources Windows reserves for background tasks. The default value of 20 reserves 20% for background. Setting it to 0 allocates nearly all CPU resources to the foreground game, significantly improving frame rate stability."),
        // ========== GPU 优化 ==========
        ["gpu.disable_dwm_animations"] = (
            "DWM（桌面窗口管理器）动画效果需要 GPU 持续渲染桌面合成动画，占用显存带宽和 GPU 渲染管线。禁用后 GPU 可以将更多资源分配给游戏渲染，减少帧时间波动。",
            "DWM (Desktop Window Manager) animations require the GPU to continuously render desktop composition animations, consuming VRAM bandwidth and GPU render pipeline. Disabling them frees more GPU resources for game rendering, reducing frame time fluctuations."),
        ["gpu.disable_game_dvr_complete"] = (
            "Game DVR 在后台持续录制游戏画面，占用 GPU 编码器（NVENC/VCE）和显存。即使不保存录像，编码器仍在工作。彻底禁用后可释放 3-5% 的 GPU 性能和数百 MB 显存。",
            "Game DVR continuously records gameplay in the background, using the GPU encoder (NVENC/VCE) and VRAM. Even without saving recordings, the encoder is still working. Completely disabling it frees 3-5% GPU performance and hundreds of MB of VRAM."),
        ["gpu.disable_xbox_game_monitoring"] = (
            "Xbox 游戏监控服务持续追踪游戏进程的 GPU 使用情况和帧率数据，用于 Xbox Game Bar 的性能覆盖层。这种持续监控会引入额外的 CPU 和 GPU 开销，禁用后可减少后台资源占用。",
            "Xbox Game Monitoring continuously tracks game process GPU usage and frame rate data for the Xbox Game Bar performance overlay. This constant monitoring introduces extra CPU and GPU overhead. Disabling it reduces background resource consumption."),
        ["gpu.disable_vrr"] = (
            "可变刷新率（VRR/FreeSync/G-Sync）虽然能消除画面撕裂，但在某些游戏中会引入额外的帧缓冲延迟。对于追求最低输入延迟的竞技玩家，禁用 VRR 配合帧率上限可能获得更低的端到端延迟。",
            "Variable Refresh Rate (VRR/FreeSync/G-Sync) eliminates screen tearing but can introduce additional frame buffer latency in some games. For competitive players seeking minimum input latency, disabling VRR with a frame rate cap may achieve lower end-to-end latency."),
        ["gpu.clear_shader_cache"] = (
            "GPU 着色器缓存存储编译后的着色器程序，避免每次启动游戏重新编译。但损坏或过期的缓存可能导致着色器编译错误，表现为游戏卡顿或画面异常。清理后游戏会重新编译并缓存最新的着色器。",
            "GPU shader cache stores compiled shader programs to avoid recompilation on every game launch. But corrupted or outdated cache can cause shader compilation errors, manifesting as game stuttering or visual artifacts. Clearing it forces games to recompile and cache fresh shaders."),
        ["gpu.amd_anti_lag"] = (
            "AMD Anti-Lag 通过控制 CPU 提交渲染命令的时机来减少渲染队列深度，从而降低输入到显示的延迟。优化 AMD 显卡的相关注册表设置可以确保 Anti-Lag 功能正常工作，获得最低的输入延迟。",
            "AMD Anti-Lag reduces render queue depth by controlling when the CPU submits render commands, lowering input-to-display latency. Optimizing AMD GPU registry settings ensures Anti-Lag functions properly for minimum input latency."),
        ["gpu.nvidia_low_latency"] = (
            "NVIDIA 低延迟模式通过限制预渲染帧队列深度（从默认 3 帧降到 1 帧），减少 GPU 渲染管线中的帧缓冲延迟。这意味着你看到的画面更接近当前的游戏状态，输入响应更即时。",
            "NVIDIA Low Latency mode reduces frame buffer latency in the GPU render pipeline by limiting the pre-rendered frame queue depth (from default 3 frames to 1). This means the displayed image is closer to the current game state, making input response more immediate."),
        ["gpu.disable_hags"] = (
            "HAGS（硬件加速 GPU 调度）将 GPU 内存管理从内核模式驱动转移到 GPU 硬件。在某些旧驱动或特定游戏中，HAGS 可能导致帧时间不稳定或兼容性问题。禁用后使用传统的软件调度，更稳定。",
            "HAGS (Hardware-Accelerated GPU Scheduling) moves GPU memory management from kernel-mode driver to GPU hardware. With some older drivers or specific games, HAGS can cause unstable frame times or compatibility issues. Disabling it uses traditional software scheduling for more stability."),
        ["gpu.enable_hags"] = (
            "HAGS 让 GPU 硬件直接管理显存分配和任务调度，减少了 CPU 内核模式驱动的介入。这可以降低渲染延迟并提升帧率，尤其在 GPU 负载较高的场景下效果明显。需要 Windows 10 2004+ 和较新的显卡驱动。",
            "HAGS lets GPU hardware directly manage VRAM allocation and task scheduling, reducing CPU kernel-mode driver involvement. This can lower rendering latency and improve FPS, especially under high GPU load. Requires Windows 10 2004+ and recent GPU drivers."),
        ["gpu.force_high_performance_gpu"] = (
            "笔记本和部分台式机同时拥有集成显卡和独立显卡。Windows 可能错误地将游戏分配给集显运行，导致严重卡顿。强制全局使用独显确保所有游戏都在高性能 GPU 上运行。",
            "Laptops and some desktops have both integrated and discrete GPUs. Windows may incorrectly assign games to the integrated GPU, causing severe stuttering. Forcing global discrete GPU usage ensures all games run on the high-performance GPU."),
        ["gpu.set_sfio_priority"] = (
            "GPU SFIO（Streaming File I/O）优先级控制 GPU 处理渲染请求的调度权重。提升优先级后，游戏的渲染命令会被 GPU 优先处理，减少因其他应用（如浏览器硬件加速）抢占 GPU 资源导致的帧延迟。",
            "GPU SFIO (Streaming File I/O) priority controls the scheduling weight for GPU render requests. Higher priority means game render commands are processed first by the GPU, reducing frame delays caused by other apps (like browser hardware acceleration) competing for GPU resources."),
        ["gpu.disable_game_mode_optimizations"] = (
            "Windows 游戏模式的自动优化有时会与游戏自身的优化机制冲突，导致帧率不稳定。禁用系统级自动优化，让游戏引擎完全控制资源分配，可以获得更可预测的性能表现。",
            "Windows Game Mode auto-optimizations can sometimes conflict with games' own optimization mechanisms, causing unstable frame rates. Disabling system-level auto-optimization lets the game engine fully control resource allocation for more predictable performance."),
        // ========== 输入延迟 ==========
        ["input.disable_pointer_trails"] = (
            "鼠标轨迹效果需要 GPU 在每一帧额外渲染多个半透明的鼠标指针图像。虽然单次开销很小，但在高刷新率（144Hz+）下累积的渲染开销不可忽视，且会干扰鼠标位置的视觉判断。",
            "Mouse pointer trails require the GPU to render multiple semi-transparent cursor images each frame. While individual overhead is small, it accumulates significantly at high refresh rates (144Hz+) and interferes with visual cursor position judgment."),
        ["input.disable_snap_to"] = (
            "鼠标自动对齐功能会在对话框弹出时将鼠标指针强制移动到默认按钮位置。这种非用户主动的鼠标移动会干扰肌肉记忆，在游戏中可能导致意外的瞄准偏移。",
            "Snap To automatically moves the mouse pointer to the default button when dialogs appear. This non-user-initiated mouse movement disrupts muscle memory and can cause unexpected aim shifts in games."),
        ["input.disable_touch_feedback"] = (
            "触摸反馈在每次触摸屏幕时显示视觉动画效果，这些动画由 DWM 渲染，会占用 GPU 资源。对于使用触摸屏的游戏设备，禁用后可以减少不必要的渲染开销和输入处理延迟。",
            "Touch feedback displays visual animations on every screen touch, rendered by DWM and consuming GPU resources. For gaming devices with touch screens, disabling it reduces unnecessary rendering overhead and input processing latency."),
        ["input.disable_pen_feedback"] = (
            "手写笔视觉反馈效果与触摸反馈类似，会在笔触时显示墨迹动画。这些额外的渲染操作会增加输入到显示的延迟。对于不使用手写笔的游戏场景，禁用可以减少系统开销。",
            "Pen visual feedback, similar to touch feedback, displays ink animations on pen strokes. These extra rendering operations increase input-to-display latency. For gaming scenarios not using a stylus, disabling it reduces system overhead."),
        ["input.optimize_usb_polling"] = (
            "USB 数据队列（DataQueueSizeMaximum）控制鼠标和键盘输入事件的缓冲区大小。增大队列可以防止在高轮询率（1000Hz+）鼠标快速移动时因缓冲区溢出导致的输入数据丢失。",
            "The USB data queue (DataQueueSizeMaximum) controls the buffer size for mouse and keyboard input events. Increasing the queue prevents input data loss from buffer overflow during rapid mouse movements with high polling rate (1000Hz+) mice."),
        ["input.optimize_raw_input_buffer"] = (
            "Raw Input 是 Windows 提供的低级输入 API，绕过了消息队列的额外处理。优化其缓冲区大小可以确保高轮询率鼠标的每一个采样点都被准确捕获，避免因缓冲不足导致的鼠标移动不平滑。",
            "Raw Input is a low-level Windows input API that bypasses message queue processing. Optimizing its buffer size ensures every sample point from high polling rate mice is accurately captured, preventing choppy mouse movement from insufficient buffering."),
        ["input.disable_mouse_acceleration"] = (
            "鼠标加速（Enhanced Pointer Precision）会根据鼠标移动速度动态调整指针位移比例——快速移动时指针走得更远。这破坏了物理移动和屏幕移动的线性关系，使得 FPS 游戏中的瞄准无法建立稳定的肌肉记忆。",
            "Mouse acceleration (Enhanced Pointer Precision) dynamically adjusts pointer displacement ratio based on mouse speed — faster movement means more pointer travel. This breaks the linear relationship between physical and screen movement, making it impossible to build stable muscle memory for aiming in FPS games."),
        ["input.optimize_keyboard_response"] = (
            "键盘重复延迟和重复速率控制按住按键时字符输入的响应速度。减少重复延迟意味着按键后更快开始连续输入，提升重复速率意味着连续输入更快。这对需要快速连续按键的游戏操作有直接帮助。",
            "Keyboard repeat delay and repeat rate control how quickly characters are input when holding a key. Reducing repeat delay means faster continuous input after key press, and increasing repeat rate means faster continuous input. This directly helps game operations requiring rapid key presses."),
        // ========== 网络优化 ==========
        ["network.optimize_afd"] = (
            "AFD（Ancillary Function Driver）是 Winsock 的内核驱动，控制网络发送/接收窗口大小。优化这些参数可以提升 TCP 吞吐量，减少网络数据传输的等待时间，对在线游戏的数据包收发效率有直接影响。",
            "AFD (Ancillary Function Driver) is the Winsock kernel driver controlling network send/receive window sizes. Optimizing these parameters improves TCP throughput and reduces network data transfer wait times, directly affecting packet send/receive efficiency in online games."),
        ["network.disable_qos_scheduler"] = (
            "Windows QoS 数据包调度程序默认保留 20% 的网络带宽用于系统服务。移除这个限制后，游戏可以使用 100% 的可用带宽，在网络拥塞时获得更好的数据传输性能。",
            "The Windows QoS Packet Scheduler reserves 20% of network bandwidth for system services by default. Removing this limit lets games use 100% of available bandwidth, achieving better data transfer performance during network congestion."),
        ["network.disable_throttling_complete"] = (
            "Windows 网络节流机制会在检测到多媒体应用运行时限制非多媒体网络流量的发送速率。这种节流在游戏场景下可能误判，导致游戏数据包被延迟发送。彻底禁用确保所有网络流量以最大速率传输。",
            "Windows network throttling limits non-multimedia network traffic send rates when multimedia applications are detected. This throttling can misidentify games, causing game packets to be delayed. Complete disabling ensures all network traffic transmits at maximum rate."),
        ["network.disable_delivery_optimization"] = (
            "传递优化（Delivery Optimization）使用 P2P 技术在局域网和互联网上分发 Windows 更新。这会在后台持续上传数据，占用上行带宽和 CPU 资源。对于在线游戏，上行带宽被占用会直接增加网络延迟。",
            "Delivery Optimization uses P2P technology to distribute Windows updates across LAN and internet. This continuously uploads data in the background, consuming upload bandwidth and CPU resources. For online games, occupied upload bandwidth directly increases network latency."),
        ["network.disable_netbios"] = (
            "NetBIOS 是一个遗留的网络协议，会定期发送广播数据包进行名称解析。这些广播流量会占用网络带宽并增加网络栈的处理开销。现代网络环境中 DNS 已完全替代 NetBIOS 的功能。",
            "NetBIOS is a legacy network protocol that periodically sends broadcast packets for name resolution. These broadcasts consume network bandwidth and increase network stack processing overhead. In modern networks, DNS has completely replaced NetBIOS functionality."),
        ["network.disable_lmhosts"] = (
            "LMHOSTS 文件查找是 NetBIOS 名称解析的一部分，系统会在每次网络连接时检查该文件。禁用后可以跳过这个不必要的查找步骤，加快网络连接建立速度。",
            "LMHOSTS file lookup is part of NetBIOS name resolution, checked by the system on every network connection. Disabling it skips this unnecessary lookup step, speeding up network connection establishment."),
        ["network.optimize_tcp"] = (
            "TCP 参数优化包括调整窗口大小、确认延迟、重传超时等核心参数。合理的 TCP 参数可以减少数据包往返时间（RTT），降低游戏中的网络延迟，提升在线对战的响应速度。",
            "TCP parameter optimization includes adjusting window size, acknowledgment delay, retransmission timeout, and other core parameters. Proper TCP parameters reduce packet round-trip time (RTT), lowering network latency in games and improving online match responsiveness."),
        ["network.disable_nagle"] = (
            "Nagle 算法会将多个小数据包合并成一个大包再发送，以提高网络利用率。但这种合并引入了最多 200ms 的发送延迟。对于游戏来说，每个操作指令都需要立即发送，Nagle 算法的延迟是不可接受的。",
            "Nagle's algorithm combines multiple small packets into one large packet before sending, improving network utilization. But this merging introduces up to 200ms of send delay. For games, every action command needs immediate transmission — Nagle's delay is unacceptable."),
        ["network.clear_dns_cache"] = (
            "DNS 缓存存储域名到 IP 地址的映射。过期或错误的缓存条目可能导致游戏连接到错误的服务器或连接失败。刷新缓存后系统会重新查询 DNS 服务器获取最新的 IP 地址。",
            "DNS cache stores domain-to-IP address mappings. Expired or incorrect cache entries can cause games to connect to wrong servers or fail to connect. Flushing the cache forces the system to re-query DNS servers for the latest IP addresses."),
        ["network.disable_adapter_power_saving"] = (
            "网卡节能模式会在检测到低网络活动时降低网卡功耗，甚至让网卡进入休眠状态。从休眠唤醒需要数百毫秒，在游戏中表现为突然的网络延迟飙升（lag spike）。禁用后网卡始终保持全速运行。",
            "Network adapter power saving reduces NIC power consumption during low network activity, even putting it to sleep. Waking from sleep takes hundreds of milliseconds, manifesting as sudden lag spikes in games. Disabling it keeps the NIC running at full speed."),
        ["network.disable_tcp_auto_tuning"] = (
            "TCP 自动调优会动态调整接收窗口大小以适应网络条件。但在某些路由器和防火墙环境下，动态窗口大小变化可能导致连接不稳定。锁定窗口大小可以获得更可预测的网络性能。",
            "TCP auto-tuning dynamically adjusts receive window size to adapt to network conditions. But with some routers and firewalls, dynamic window size changes can cause connection instability. Locking window size provides more predictable network performance."),
        ["network.disable_ecn_capability"] = (
            "ECN（显式拥塞通知）允许路由器在拥塞时标记数据包而非丢弃。但许多旧路由器不支持 ECN，可能错误地丢弃带 ECN 标记的数据包。禁用后使用传统的丢包检测机制，兼容性更好。",
            "ECN (Explicit Congestion Notification) allows routers to mark packets during congestion instead of dropping them. But many older routers don't support ECN and may incorrectly drop ECN-marked packets. Disabling it uses traditional packet loss detection for better compatibility."),
        ["network.disable_rsc"] = (
            "RSC（接收段合并）将多个小的 TCP 段合并成一个大段再传递给上层协议栈。虽然减少了 CPU 中断次数，但合并过程引入了额外延迟。对于延迟敏感的游戏，禁用 RSC 可以让每个数据包更快到达应用层。",
            "RSC (Receive Segment Coalescing) merges multiple small TCP segments into one large segment before passing to the upper protocol stack. While reducing CPU interrupts, the merging process introduces extra latency. For latency-sensitive games, disabling RSC lets each packet reach the application layer faster."),
        ["network.optimize_rss"] = (
            "RSS（接收端缩放）将网卡接收到的数据包分散到多个 CPU 核心处理，避免单核成为网络处理瓶颈。启用后可以提升网络吞吐量，减少高流量场景下的数据包处理延迟。",
            "RSS (Receive Side Scaling) distributes received packets across multiple CPU cores, preventing a single core from becoming a network processing bottleneck. Enabling it improves network throughput and reduces packet processing latency under high traffic."),
        ["network.enable_tcp1323"] = (
            "RFC 1323 时间戳选项为每个 TCP 段添加精确的时间信息，帮助 TCP 栈更准确地计算往返时间（RTT）和检测重复数据包。这对于长距离连接（如跨区域游戏服务器）的延迟测量尤为重要。",
            "RFC 1323 timestamps add precise timing information to each TCP segment, helping the TCP stack more accurately calculate round-trip time (RTT) and detect duplicate packets. This is especially important for latency measurement on long-distance connections like cross-region game servers."),
        ["network.set_congestion_ctcp"] = (
            "CTCP（复合 TCP）是微软开发的拥塞控制算法，比默认的 CUBIC 算法更积极地利用可用带宽。在高带宽、高延迟的网络环境下，CTCP 可以更快地达到最大吞吐量。",
            "CTCP (Compound TCP) is a congestion control algorithm developed by Microsoft that more aggressively utilizes available bandwidth than the default CUBIC algorithm. In high-bandwidth, high-latency network environments, CTCP reaches maximum throughput faster."),
        ["network.disable_flow_control"] = (
            "流量控制（Flow Control）允许接收方在缓冲区满时通知发送方暂停发送。但在游戏场景下，暂停发送意味着游戏数据包被延迟，导致网络延迟飙升。禁用后数据包始终以最大速率发送。",
            "Flow Control allows the receiver to tell the sender to pause when its buffer is full. But in gaming, pausing means game packets are delayed, causing latency spikes. Disabling it ensures packets are always sent at maximum rate."),
        ["network.disable_interrupt_moderation"] = (
            "中断调制（Interrupt Moderation）将多个网络中断合并成一个，减少 CPU 中断频率。但合并过程引入了微秒级的延迟。禁用后每个数据包到达时立即触发中断，以最低延迟传递给 CPU 处理。",
            "Interrupt Moderation combines multiple network interrupts into one, reducing CPU interrupt frequency. But the combining process introduces microsecond-level latency. Disabling it triggers an interrupt immediately when each packet arrives, delivering to the CPU with minimum latency."),
        ["network.enable_lso"] = (
            "LSO（大数据包发送卸载）让网卡硬件负责将大数据块分割成标准大小的 TCP 段，而非由 CPU 执行。这减少了 CPU 在网络发送上的开销，让更多 CPU 资源可用于游戏逻辑处理。",
            "LSO (Large Send Offload) lets NIC hardware handle splitting large data blocks into standard TCP segments instead of the CPU. This reduces CPU overhead for network sending, freeing more CPU resources for game logic processing."),
        ["network.optimize_adapter_buffers"] = (
            "增加网卡的接收和发送缓冲区可以在网络流量突发时提供更大的缓冲空间，防止因缓冲区溢出导致的丢包。丢包会触发 TCP 重传，每次重传至少增加一个 RTT 的延迟。",
            "Increasing NIC receive and send buffers provides more buffer space during traffic bursts, preventing packet loss from buffer overflow. Packet loss triggers TCP retransmission, adding at least one RTT of latency per retransmission."),
        ["network.optimize_checksum_offload"] = (
            "校验和卸载让网卡硬件计算 TCP/IP 校验和，而非由 CPU 软件计算。这减少了每个数据包的 CPU 处理时间，在高流量场景下可以显著降低 CPU 网络处理开销。",
            "Checksum offload lets NIC hardware calculate TCP/IP checksums instead of CPU software. This reduces CPU processing time per packet, significantly lowering CPU network processing overhead under high traffic."),
        ["network.optimize_dns"] = (
            "降低 DNS 缓存 TTL 和负缓存窗口可以让系统更快地获取最新的 DNS 解析结果。当游戏服务器 IP 发生变更时，较短的缓存时间确保你能更快连接到正确的服务器。",
            "Lowering DNS cache TTL and negative cache window lets the system obtain the latest DNS resolution results faster. When game server IPs change, shorter cache times ensure you connect to the correct server more quickly."),
        ["network.increase_max_user_port"] = (
            "默认的临时端口范围（49152-65535）在高并发连接场景下可能不够用。扩展到 65534 后可以支持更多的并发网络连接，避免因端口耗尽导致的连接失败。",
            "The default ephemeral port range (49152-65535) may be insufficient for high concurrent connections. Extending to 65534 supports more concurrent network connections, preventing connection failures from port exhaustion."),
        ["network.optimize_tcp_timed_wait"] = (
            "TCP TIME_WAIT 状态默认持续 240 秒，期间端口无法被复用。缩短这个时间可以让端口更快回收，在频繁建立和断开连接的游戏场景下避免端口耗尽。",
            "TCP TIME_WAIT state lasts 240 seconds by default, during which the port cannot be reused. Shortening this time allows faster port recycling, preventing port exhaustion in gaming scenarios with frequent connection establishment and teardown."),
        ["network.optimize_ttl"] = (
            "TTL（生存时间）控制数据包在网络中经过的最大路由器跳数。设为 64 是互联网标准值，确保数据包能到达目标服务器同时防止路由环路导致的无限转发。",
            "TTL (Time To Live) controls the maximum router hops a packet traverses. Setting it to 64 is the internet standard, ensuring packets reach the destination server while preventing infinite forwarding from routing loops."),
        ["network.optimize_tcp_max_dup_acks"] = (
            "TcpMaxDupAcks 控制触发快速重传所需的重复 ACK 数量。在有损的 Wi-Fi 网络上，降低这个值可以让 TCP 更快检测到丢包并触发重传，减少因丢包导致的延迟。",
            "TcpMaxDupAcks controls the number of duplicate ACKs needed to trigger fast retransmission. On lossy Wi-Fi networks, lowering this value lets TCP detect packet loss faster and trigger retransmission, reducing latency from packet loss."),
        ["network.disable_delivery_optimization"] = (
            "传递优化让 Windows 更新通过局域网或互联网以 P2P 方式向其他电脑分发更新。游戏期间这会在后台占用上行带宽。禁用后停止后台上传，降低延迟。",
            "Delivery Optimization lets Windows Update distribute updates to other PCs via LAN or internet P2P. During gaming this consumes upload bandwidth in the background. Disabling stops background upload to reduce latency."),
        ["network.ipv6_prefer_ipv4"] = (
            "在 IPv6 未正确配置的私有网络中，IPv6 隧道和回退协商可能引入额外的解析与连接延迟。将 DisabledComponents=32 仅关闭 IPv6 隧道，保留原生 IPv6，可降低这类延迟。",
            "On private networks where IPv6 is misconfigured, IPv6 tunnel and fallback negotiation can add resolution and connection latency. Setting DisabledComponents=32 disables only IPv6 tunnels while keeping native IPv6, reducing such latency."),
        ["adjustments.network_throttling_index"] = (
            "NetworkThrottlingIndex 控制 Windows 多媒体调度服务对网络流量的限制。设为 0xFFFFFFFF（最大值）后，系统不再限制任何网络流量的发送速率，确保游戏数据包以最大速率传输。",
            "NetworkThrottlingIndex controls Windows multimedia scheduling service's network traffic limiting. Setting to 0xFFFFFFFF (maximum) stops the system from limiting any network traffic send rate, ensuring game packets transmit at maximum rate."),
        // ========== 安全缓解 ==========
        ["security.disable_vbs"] = (
            "VBS 会通过虚拟化隔离关键系统组件。关闭后可减少虚拟化开销，但会降低内核隔离保护并需要重启。",
            "VBS isolates critical system components through virtualization. Disabling may reduce virtualization overhead but lowers kernel isolation and requires a restart."),
        ["security.disable_hvci"] = (
            "HVCI 会验证内核代码完整性。关闭后可减少检查开销，但 Rootkit 等内核攻击的防护能力会降低并需要重启。",
            "HVCI validates kernel code integrity. Disabling may reduce check overhead but lowers protection against kernel attacks such as rootkits and requires a restart."),
        ["security.disable_cfg"] = (
            "CFG 会校验间接调用目标，降低控制流劫持风险。关闭后可减少少量分支检查，但漏洞利用防护会降低并需要重启。",
            "CFG validates indirect call targets to reduce control-flow hijacking. Disabling may reduce minor branch-check overhead but lowers exploit protection and requires a restart."),
        ["security.disable_defender_realtime"] = (
            "Defender 实时保护会扫描文件和进程。关闭后可减少 CPU 与磁盘 I/O，但系统失去实时恶意软件检测。",
            "Defender real-time protection scans files and processes. Disabling may reduce CPU and disk I/O but removes real-time malware detection."),
        ["security.disable_smartscreen"] = (
            "SmartScreen 会对应用和下载执行信誉检查。关闭后可减少启动时的云端验证等待，但未知程序防护会降低。",
            "SmartScreen checks application and download reputation. Disabling may reduce cloud-verification wait at startup but lowers protection against unknown programs."),
        ["security.disable_dep"] = (
            "DEP 阻止在数据区域执行恶意代码。兼容性问题应由应用级修复处理，不能通过全局关闭 DEP 规避。",
            "DEP prevents malicious code execution from data regions. Compatibility issues require app-scoped remediation, not globally disabling DEP."),
        ["security.disable_sehop"] = (
            "SEHOP 会验证结构化异常处理链完整性。关闭后可减少异常链校验，但漏洞利用防护会降低并需要重启。",
            "SEHOP validates structured exception-handler chain integrity. Disabling may reduce exception-chain checks but lowers exploit protection and requires a restart."),
        ["security.disable_spectre_meltdown"] = (
            "Spectre/Meltdown 缓解保护系统免受侧信道攻击。Yuqi Engine 不会为了性能关闭它们。",
            "Spectre/Meltdown mitigations protect against side-channel attacks. Yuqi Engine will not disable them for performance."),
        // ========== 存储优化 ==========
        ["storage.disable_8dot3"] = (
            "NTFS 短文件名（8.3 格式）是为兼容 DOS 程序保留的遗留功能。每次创建文件时系统都要额外生成一个短文件名，增加了文件系统的写入开销。禁用后文件创建速度提升，目录枚举也更快。",
            "NTFS short filenames (8.3 format) are a legacy feature for DOS program compatibility. The system generates an extra short filename on every file creation, adding filesystem write overhead. Disabling speeds up file creation and directory enumeration."),
        ["storage.optimize_io_page_lock"] = (
            "I/O 页面锁定限制控制系统允许锁定在物理内存中的 I/O 缓冲区大小。增加这个限制可以让大文件读写操作使用更大的缓冲区，减少磁盘 I/O 次数，提升游戏资源加载速度。",
            "I/O page lock limit controls the size of I/O buffers the system allows to be locked in physical memory. Increasing this limit lets large file operations use bigger buffers, reducing disk I/O operations and improving game resource loading speed."),
        ["storage.clear_standby_list"] = (
            "Windows 将不再活跃使用的内存页面放入待机列表作为缓存。当待机列表占用过多内存时，游戏可能因为可用内存不足而触发频繁的页面换入换出（Page Fault），导致卡顿。清理后释放这些内存给游戏使用。",
            "Windows places inactive memory pages into the standby list as cache. When the standby list consumes too much memory, games may trigger frequent page faults due to insufficient available memory, causing stuttering. Clearing frees this memory for game use."),
        ["storage.disable_ntfs_encryption"] = (
            "NTFS 加密（EFS）在每次文件读写时执行加密/解密操作，消耗 CPU 资源。对于游戏文件来说，加密是不必要的开销。禁用后文件 I/O 直接读写，减少 CPU 在存储操作上的开销。",
            "NTFS encryption (EFS) performs encrypt/decrypt operations on every file read/write, consuming CPU resources. For game files, encryption is unnecessary overhead. Disabling allows direct file I/O, reducing CPU overhead on storage operations."),
        ["storage.disable_prefetch"] = (
            "Windows Prefetch 预读取功能会在系统启动和应用启动时预加载文件到内存。在 SSD 上这种预读取反而增加了不必要的磁盘 I/O，因为 SSD 的随机读取速度已经足够快，不需要预读优化。",
            "Windows Prefetch preloads files into memory during system and application startup. On SSDs, this prefetching actually adds unnecessary disk I/O since SSD random read speeds are already fast enough without prefetch optimization."),
        ["storage.disable_superfetch"] = (
            "SuperFetch（SysMain）分析应用使用模式并预加载常用数据到内存。但它的预测算法会持续进行后台磁盘读取，在游戏运行时与游戏争抢磁盘 I/O 带宽，导致游戏资源加载变慢。",
            "SuperFetch (SysMain) analyzes application usage patterns and preloads frequently used data into memory. But its prediction algorithm continuously performs background disk reads, competing with games for disk I/O bandwidth during gameplay, slowing game resource loading."),
        // ========== Windows 功能 ==========
        ["windows.disable_tips"] = (
            "Windows 提示功能会定期扫描系统状态并弹出建议通知，这些扫描和通知渲染会占用 CPU 和 GPU 资源。在游戏全屏模式下，通知弹窗还可能导致游戏失去焦点或触发窗口化切换。",
            "Windows Tips periodically scans system state and displays suggestion notifications, consuming CPU and GPU resources. In fullscreen gaming, notification popups can cause games to lose focus or trigger windowed mode switching."),
        ["windows.disable_suggested_apps"] = (
            "建议应用功能会在后台下载和安装微软推荐的应用，占用网络带宽和磁盘 I/O。这些后台活动在游戏运行时会与游戏争抢系统资源，导致帧率波动。",
            "Suggested Apps downloads and installs Microsoft-recommended apps in the background, consuming network bandwidth and disk I/O. These background activities compete with games for system resources during gameplay, causing frame rate fluctuations."),
        ["windows.disable_clipboard_history"] = (
            "剪贴板历史功能会在每次复制操作时将内容存储到内存中的历史记录。虽然单次开销很小，但持续累积会占用数十 MB 内存。禁用后减少内存占用，同时避免敏感信息被意外保留。",
            "Clipboard history stores content in memory on every copy operation. While individual overhead is small, continuous accumulation can consume tens of MB of memory. Disabling reduces memory usage and prevents sensitive information from being inadvertently retained."),
        ["windows.disable_focus_assist"] = (
            "专注助手会自动静音通知，但其后台监控逻辑会持续检测应用状态。禁用后通知正常显示，同时减少后台监控的 CPU 开销。游戏中的通知管理建议使用游戏模式替代。",
            "Focus Assist automatically mutes notifications, but its background monitoring logic continuously checks application state. Disabling allows normal notifications while reducing background monitoring CPU overhead. Use Game Mode instead for in-game notification management."),
        ["windows.disable_timeline"] = (
            "时间线（活动历史）功能会记录你打开的每个应用和文档，并同步到微软云端。这种持续的记录和同步操作会占用 CPU、内存和网络资源。禁用后减少后台资源占用。",
            "Timeline (Activity History) records every app and document you open, syncing to Microsoft cloud. This continuous recording and syncing consumes CPU, memory, and network resources. Disabling reduces background resource usage."),
        ["windows.disable_cortana"] = (
            "Cortana 语音助手在后台持续监听语音唤醒词，并维护搜索索引。这些后台活动占用 CPU 和内存资源。禁用后释放这些资源，同时减少不必要的网络通信。",
            "Cortana voice assistant continuously listens for wake words in the background and maintains search indexes. These background activities consume CPU and memory. Disabling frees these resources and reduces unnecessary network communication."),
        ["windows.disable_search_indexing"] = (
            "Windows 搜索索引服务（WSearch）持续扫描文件系统变化并更新索引数据库。这种持续的磁盘 I/O 活动会与游戏争抢 SSD/HDD 带宽，导致游戏资源加载变慢和帧时间波动。",
            "Windows Search Indexing service (WSearch) continuously scans filesystem changes and updates the index database. This constant disk I/O activity competes with games for SSD/HDD bandwidth, slowing game resource loading and causing frame time fluctuations."),
        // ========== Intel/AMD 专属 ==========
        ["intel.disable_cstates"] = (
            "CPU C-States 是处理器的空闲节能状态，从 C1（轻度休眠）到 C10（深度休眠）。进入和退出深度 C-State 需要数十微秒到数毫秒，在游戏中表现为周期性的帧时间突增。禁用后 CPU 始终保持活跃状态。",
            "CPU C-States are processor idle power-saving states, from C1 (light sleep) to C10 (deep sleep). Entering and exiting deep C-States takes tens of microseconds to milliseconds, manifesting as periodic frame time spikes in games. Disabling keeps the CPU always active."),
        ["amd.disable_cool_quiet"] = (
            "Cool'n'Quiet 是 AMD 的动态频率和电压调节技术。它会根据负载降低 CPU 频率和电压以节能，但频率切换过程中的延迟会导致游戏帧时间不稳定。禁用后 CPU 保持高频运行。",
            "Cool'n'Quiet is AMD's dynamic frequency and voltage scaling technology. It reduces CPU frequency and voltage based on load to save power, but the latency during frequency transitions causes unstable frame times in games. Disabling keeps the CPU at high frequency."),
        ["amd.optimize_pbo"] = (
            "PBO（Precision Boost Overdrive）允许 AMD Ryzen 处理器在散热和供电允许的范围内超越默认睿频限制。优化 PBO 设置可以让 CPU 在游戏中达到更高的单核频率，直接提升帧率。",
            "PBO (Precision Boost Overdrive) allows AMD Ryzen processors to exceed default boost limits within thermal and power delivery constraints. Optimizing PBO settings lets the CPU reach higher single-core frequencies in games, directly improving frame rates."),
        ["amd.radeon_anti_lag"] = (
            "ULPS（Ultra Low Power State）是 AMD 显卡的深度节能模式，会在低负载时大幅降低 GPU 频率。从 ULPS 唤醒需要数百毫秒，在游戏中表现为画面冻结。禁用 ULPS 后 GPU 保持就绪状态，响应更快。",
            "ULPS (Ultra Low Power State) is AMD GPU's deep power-saving mode that drastically reduces GPU frequency under low load. Waking from ULPS takes hundreds of milliseconds, manifesting as screen freezes in games. Disabling ULPS keeps the GPU ready for faster response."),
        ["amd.precision_boost"] = (
            "Precision Boost Overdrive 通过放宽功耗和电流限制，让 Ryzen 处理器在更多场景下维持高频运行。优化后的 PBO 设置可以在不影响稳定性的前提下，提升 5-10% 的游戏性能。",
            "Precision Boost Overdrive relaxes power and current limits, allowing Ryzen processors to maintain high frequencies in more scenarios. Optimized PBO settings can improve gaming performance by 5-10% without affecting stability."),
        ["amd.disable_cool_n_quiet"] = (
            "Cool'n'Quiet 是 AMD 的节能技术，会在低负载时降低 CPU 频率和电压。虽然节能，但频率切换延迟会导致游戏中的微卡顿。禁用后 CPU 始终以最高频率运行，功耗和发热会增加。",
            "Cool'n'Quiet is AMD's power-saving technology that reduces CPU frequency and voltage under low load. While energy-efficient, frequency switching latency causes micro-stutters in games. Disabling keeps the CPU at maximum frequency, increasing power consumption and heat."),
        // ========== 隐私保护 ==========
        ["privacy.disable_telemetry"] = (
            "Windows 遥测服务（DiagTrack）持续收集系统使用数据并上传到微软服务器。这个后台进程会占用 CPU、内存和网络带宽。禁用后减少后台资源占用，同时保护用户隐私。",
            "Windows telemetry service (DiagTrack) continuously collects system usage data and uploads to Microsoft servers. This background process consumes CPU, memory, and network bandwidth. Disabling reduces background resource usage while protecting user privacy."),
        ["privacy.disable_advertising_id"] = (
            "广告 ID 允许应用跨应用追踪用户行为以投放个性化广告。禁用后应用无法通过广告 ID 关联用户行为，减少后台数据收集活动。",
            "Advertising ID allows apps to track user behavior across apps for personalized ads. Disabling prevents apps from correlating user behavior via advertising ID, reducing background data collection."),
        ["privacy.disable_location"] = (
            "位置服务允许应用获取设备的地理位置信息。后台位置查询会消耗 CPU 和网络资源。对于桌面游戏场景，位置服务通常不必要，禁用后减少后台开销。",
            "Location services allow apps to access device geographic location. Background location queries consume CPU and network resources. For desktop gaming, location services are usually unnecessary — disabling reduces background overhead."),
        ["privacy.disable_activity_history"] = (
            "活动历史记录用户的应用使用和文件访问行为，并可能同步到云端。这种持续的记录和同步操作占用系统资源。禁用后减少后台 I/O 和网络活动。",
            "Activity history records app usage and file access behavior, potentially syncing to the cloud. This continuous recording and syncing consumes system resources. Disabling reduces background I/O and network activity."),
        ["privacy.disable_diagnostic_data"] = (
            "诊断数据收集会在后台持续监控系统状态并生成报告。将级别设为最低可以大幅减少数据收集量，降低 CPU 和磁盘 I/O 开销。",
            "Diagnostic data collection continuously monitors system state and generates reports in the background. Setting to minimum level significantly reduces data collection volume, lowering CPU and disk I/O overhead."),
        ["privacy.disable_cortana"] = (
            "Cortana 在后台维护语音识别模型和搜索索引，持续占用内存和 CPU。禁用后释放这些资源，同时阻止语音数据被上传到云端。",
            "Cortana maintains voice recognition models and search indexes in the background, continuously consuming memory and CPU. Disabling frees these resources and prevents voice data from being uploaded to the cloud."),
        ["privacy.disable_license_telemetry"] = (
            "许可证遥测会定期向微软报告软件许可状态。这些周期性的网络请求虽然数据量小，但会唤醒网络栈和 CPU，影响系统的深度空闲状态。",
            "License telemetry periodically reports software license status to Microsoft. While data volume is small, these periodic network requests wake the network stack and CPU, affecting deep idle states."),
        ["privacy.disable_ceip"] = (
            "客户体验改善计划（CEIP）收集软件使用统计数据。退出后系统不再收集和上传这些数据，减少后台网络和 CPU 活动。",
            "Customer Experience Improvement Program (CEIP) collects software usage statistics. Opting out stops the system from collecting and uploading this data, reducing background network and CPU activity."),
        ["privacy.disable_compatibility_telemetry"] = (
            "兼容性遥测（CompatTelRunner.exe）会定期扫描系统中安装的所有程序并评估兼容性。这个扫描过程非常消耗 CPU 和磁盘 I/O，可能持续数分钟。禁用后消除这种周期性的性能冲击。",
            "Compatibility telemetry (CompatTelRunner.exe) periodically scans all installed programs and evaluates compatibility. This scan is very CPU and disk I/O intensive, potentially lasting several minutes. Disabling eliminates this periodic performance impact."),
        ["privacy.disable_wifi_sense"] = (
            "Wi-Fi 感知会自动连接到建议的开放热点并与联系人共享 Wi-Fi 密码。这种自动连接行为可能导致网络切换，在游戏中造成连接中断。禁用后网络连接更可控。",
            "Wi-Fi Sense automatically connects to suggested open hotspots and shares Wi-Fi passwords with contacts. This auto-connection behavior can cause network switching, interrupting game connections. Disabling makes network connections more controllable."),
        ["privacy.disable_error_reporting"] = (
            "Windows 错误报告（WER）在应用崩溃时收集内存转储并上传。这个过程会占用大量磁盘 I/O 和网络带宽。禁用后崩溃不再触发上传，减少系统开销。",
            "Windows Error Reporting (WER) collects memory dumps on application crashes and uploads them. This process consumes significant disk I/O and network bandwidth. Disabling prevents crash uploads, reducing system overhead."),
        ["privacy.disable_onedrive_sync"] = (
            "OneDrive 后台同步会持续监控文件变化并上传到云端，占用网络上行带宽和磁盘 I/O。在游戏时上行带宽被占用会直接增加网络延迟。禁用后释放这些资源。",
            "OneDrive background sync continuously monitors file changes and uploads to the cloud, consuming upload bandwidth and disk I/O. Upload bandwidth consumption during gaming directly increases network latency. Disabling frees these resources."),
        ["privacy.disable_cloud_content"] = (
            "云推荐内容（Spotlight、消费者体验等）会在后台下载广告和推荐内容，占用网络带宽和磁盘空间。禁用后减少不必要的后台下载活动。",
            "Cloud content (Spotlight, consumer experiences, etc.) downloads ads and recommended content in the background, consuming network bandwidth and disk space. Disabling reduces unnecessary background download activity."),
        ["privacy.disable_activity_feed"] = (
            "活动提要记录并上传用户的应用使用历史。禁用后停止这种持续的数据收集和上传，减少后台 CPU 和网络开销。",
            "Activity feed records and uploads user app usage history. Disabling stops this continuous data collection and upload, reducing background CPU and network overhead."),
        ["privacy.tailored_experiences"] = (
            "定向体验会根据诊断数据定制推荐内容和个性化建议。关闭后停止这种个性化，减少遥测数据被用于内容定制的隐私风险。",
            "Tailored experiences customizes recommendations and suggestions based on diagnostic data. Disabling stops this personalization, reducing the privacy risk of telemetry being used for content tailoring."),
        ["privacy.disable_app_impact_telemetry"] = (
            "应用影响遥测引擎监控应用对系统性能的影响。这种持续监控本身就会消耗 CPU 资源。禁用后减少监控开销，让更多资源可用于游戏。",
            "App impact telemetry engine monitors application impact on system performance. This continuous monitoring itself consumes CPU resources. Disabling reduces monitoring overhead, freeing more resources for gaming."),
        ["privacy.disable_steps_recorder"] = (
            "问题步骤记录器可以在后台录制用户操作。虽然通常不会自动启动，但禁用可以确保它不会被意外触发，避免不必要的屏幕录制开销。",
            "Problem Steps Recorder can record user actions in the background. While it usually doesn't auto-start, disabling ensures it cannot be accidentally triggered, avoiding unnecessary screen recording overhead."),
        ["privacy.disable_inventory_collector"] = (
            "Inventory Collector 收集硬件和软件清单信息并上传。这种扫描会遍历所有已安装的程序和硬件设备，消耗 CPU 和磁盘 I/O。禁用后阻止这种数据收集。",
            "Inventory Collector gathers hardware and software inventory information for upload. This scan traverses all installed programs and hardware devices, consuming CPU and disk I/O. Disabling prevents this data collection."),
        ["privacy.disable_handwriting_collection"] = (
            "文本和手写收集功能会记录用户的输入内容用于改善输入法。禁用后阻止输入内容被收集，保护输入隐私。",
            "Text and handwriting collection records user input content to improve input methods. Disabling prevents input content from being collected, protecting input privacy."),
        ["privacy.disable_cloud_speech"] = (
            "云端语音识别会将语音和墨迹数据上传到微软服务器进行处理。禁用后语音识别仅在本地执行（功能可能受限），但消除了数据上传的网络开销。",
            "Cloud speech recognition uploads voice and ink data to Microsoft servers for processing. Disabling limits speech recognition to local processing (functionality may be reduced) but eliminates data upload network overhead."),
        // ========== 隐私保护（应用权限） ==========
        ["privacy.disable_privacy_consent"] = (
            "跳过首次登录时的隐私同意屏幕，减少系统初始化时间。这不会影响实际的隐私设置，只是跳过了提示界面。",
            "Skip the privacy consent screen on first login, reducing system initialization time. This doesn't affect actual privacy settings, just skips the prompt interface."),
        ["privacy.disable_upnp_sharing"] = (
            "UPnP 自动发现会在局域网中广播设备信息，可能暴露设备给同网络的其他用户。禁用后减少网络广播流量，同时提升网络安全性。",
            "UPnP auto-discovery broadcasts device information on the LAN, potentially exposing devices to other users on the network. Disabling reduces network broadcast traffic while improving network security."),
        ["privacy.disable_sensor_services"] = (
            "传感器服务允许应用访问环境光、加速度计等传感器数据。桌面游戏场景通常不需要这些数据，禁用后减少后台服务开销。",
            "Sensor services allow apps to access ambient light, accelerometer, and other sensor data. Desktop gaming typically doesn't need this data — disabling reduces background service overhead."),
        ["privacy.deny_app_radio_access"] = (
            "阻止应用控制 Wi-Fi 和蓝牙开关，防止应用意外切换网络连接状态。这在游戏中尤为重要，避免网络连接被意外中断。",
            "Prevents apps from controlling Wi-Fi and Bluetooth switches, stopping apps from accidentally toggling network connection state. Especially important during gaming to avoid unexpected network interruptions."),
        ["privacy.deny_app_access_to_messaging"] = (
            "阻止应用读取短信和聊天记录，保护通信隐私。这些权限在桌面游戏场景中完全不必要。",
            "Prevents apps from reading messages and chat history, protecting communication privacy. These permissions are completely unnecessary for desktop gaming."),
        ["privacy.deny_app_access_to_email"] = (
            "阻止应用访问邮件内容，保护邮件隐私。游戏和大多数桌面应用不需要邮件访问权限。",
            "Prevents apps from accessing email content, protecting email privacy. Games and most desktop apps don't need email access."),
        ["privacy.deny_app_access_to_calendar"] = (
            "阻止应用读取日程安排，保护日程隐私。这个权限在游戏场景中完全不必要。",
            "Prevents apps from reading calendar, protecting schedule privacy. This permission is completely unnecessary for gaming."),
        ["privacy.deny_app_access_to_contacts"] = (
            "阻止应用读取联系人列表，保护社交隐私。游戏不需要访问系统联系人。",
            "Prevents apps from reading contacts, protecting social privacy. Games don't need access to system contacts."),
        ["privacy.deny_app_access_to_camera"] = (
            "阻止应用使用摄像头，保护视觉隐私。除非游戏需要摄像头功能（如 AR 游戏），否则应禁用此权限。",
            "Prevents apps from using the camera, protecting visual privacy. Unless games need camera functionality (like AR games), this permission should be disabled."),
        ["privacy.deny_app_access_to_phone_calls"] = (
            "阻止应用发起电话呼叫。桌面游戏场景中完全不需要此权限，禁用可防止恶意应用滥用。",
            "Prevents apps from making phone calls. Completely unnecessary for desktop gaming — disabling prevents malicious app abuse."),
        ["privacy.deny_app_access_to_call_history"] = (
            "阻止应用读取通话历史记录，保护通信隐私。",
            "Prevents apps from reading call history, protecting communication privacy."),
        ["privacy.deny_app_access_to_trusted_devices"] = (
            "阻止应用访问已配对的蓝牙和 USB 设备列表。这可以防止应用枚举你的外设信息。",
            "Prevents apps from accessing paired Bluetooth and USB device lists. This stops apps from enumerating your peripheral information."),
        ["privacy.deny_app_access_to_motion"] = (
            "阻止应用获取运动和健身传感器数据。桌面游戏通常不需要这些数据，禁用减少后台数据收集。",
            "Prevents apps from accessing motion and fitness sensor data. Desktop games typically don't need this data — disabling reduces background data collection."),
        ["privacy.deny_app_access_to_tasks"] = (
            "阻止应用读取待办事项列表，保护任务管理隐私。",
            "Prevents apps from reading task lists, protecting task management privacy."),
        ["privacy.deny_app_access_to_notifications"] = (
            "阻止应用读取系统通知内容。通知可能包含敏感信息（如验证码），禁用此权限可防止信息泄露。",
            "Prevents apps from reading system notification content. Notifications may contain sensitive information (like verification codes) — disabling prevents information leakage."),
        ["privacy.deny_app_access_to_account_info"] = (
            "阻止应用获取用户名和头像等账户信息，减少个人信息暴露。",
            "Prevents apps from accessing username, avatar, and other account information, reducing personal information exposure."),
        ["privacy.deny_app_access_to_diagnostic_info"] = (
            "阻止应用收集其他应用的诊断信息。这可以防止应用监控你使用其他软件的行为。",
            "Prevents apps from collecting diagnostic information about other apps. This stops apps from monitoring your usage of other software."),
        ["privacy.disable_app_sync"] = (
            "禁用设置和应用同步后，系统配置仅保存在本地，不会上传到微软云端。这减少了后台同步的网络和 CPU 开销。",
            "Disabling settings and app sync keeps system configuration local only, not uploaded to Microsoft cloud. This reduces background sync network and CPU overhead."),
        ["privacy.hide_most_used_apps"] = (
            "隐藏最常使用的应用列表可以保护使用习惯隐私，同时减少开始菜单的数据收集和排序计算开销。",
            "Hiding the most used apps list protects usage habit privacy while reducing Start menu data collection and sorting computation overhead."),
        ["privacy.disable_feedback"] = (
            "反馈提示会定期弹出请求用户评价 Windows 体验。这些弹窗在游戏中可能导致焦点丢失。禁用后不再出现反馈请求。",
            "Feedback prompts periodically pop up requesting users to rate their Windows experience. These popups can cause focus loss during gaming. Disabling prevents feedback requests."),
        ["privacy.disable_website_access_language_list"] = (
            "阻止网站读取系统语言列表可以减少浏览器指纹信息，提升匿名性。语言列表是浏览器指纹追踪的常用数据点之一。",
            "Blocking website access to the system language list reduces browser fingerprint information, improving anonymity. Language lists are a common data point for browser fingerprint tracking."),
        ["privacy.disable_webcam"] = (
            "在系统级别禁用摄像头后，任何 UWP 应用都无法启用相机。这是最彻底的摄像头隐私保护措施。",
            "Disabling the camera at system level prevents any UWP app from activating it. This is the most thorough camera privacy protection measure."),
        ["privacy.disable_background_apps"] = (
            "UWP 后台应用会在不可见的情况下持续运行，占用 CPU、内存和网络资源。禁用后这些应用只在前台打开时才运行，释放大量后台资源给游戏使用。",
            "UWP background apps run continuously while invisible, consuming CPU, memory, and network resources. Disabling makes them run only when in foreground, freeing significant background resources for gaming."),
        // ========== 系统服务 ==========
        ["service.disable_print_spooler"] = (
            "Print Spooler 服务管理打印队列，即使没有打印机也会在后台运行。它是已知的安全漏洞目标（PrintNightmare）。在不需要打印的游戏场景下禁用可以减少攻击面和后台资源占用。",
            "Print Spooler manages print queues and runs in the background even without printers. It's a known security vulnerability target (PrintNightmare). Disabling in gaming scenarios reduces attack surface and background resource usage."),
        ["service.disable_fax"] = (
            "传真服务在现代桌面环境中几乎不会被使用，但仍会占用系统资源。禁用后释放这些资源。",
            "Fax service is rarely used in modern desktop environments but still consumes system resources. Disabling frees these resources."),
        ["service.disable_remote_registry"] = (
            "远程注册表服务允许远程计算机修改本机注册表。这是一个严重的安全风险。禁用后阻止远程注册表访问，提升系统安全性。",
            "Remote Registry service allows remote computers to modify local registry. This is a serious security risk. Disabling blocks remote registry access, improving system security."),
        ["service.disable_xbox_services"] = (
            "Xbox 相关服务（XboxGipSvc、XblAuthManager、XblGameSave 等）为 Xbox 生态提供支持。如果不使用 Xbox 功能，这些服务会白白占用内存和 CPU。禁用后释放这些资源。",
            "Xbox services (XboxGipSvc, XblAuthManager, XblGameSave, etc.) support the Xbox ecosystem. If not using Xbox features, these services waste memory and CPU. Disabling frees these resources."),
        ["service.disable_spooler"] = (
            "Print Spooler 服务即使在没有打印任务时也会持续运行，监控打印队列。禁用后减少一个不必要的后台服务，释放内存和 CPU 资源。",
            "Print Spooler runs continuously even without print jobs, monitoring the print queue. Disabling removes an unnecessary background service, freeing memory and CPU resources."),
        ["service.disable_wsearch"] = (
            "Windows Search 服务持续索引文件系统，消耗大量磁盘 I/O 和 CPU。禁用后文件搜索功能会变慢，但游戏运行时不再有索引进程争抢磁盘带宽。",
            "Windows Search service continuously indexes the filesystem, consuming significant disk I/O and CPU. Disabling slows file search but eliminates indexing process competing for disk bandwidth during gaming."),
        ["service.disable_telemetry"] = (
            "DiagTrack（Connected User Experiences and Telemetry）是 Windows 遥测的核心服务，持续收集和上传系统数据。禁用后显著减少后台网络和 CPU 活动。",
            "DiagTrack (Connected User Experiences and Telemetry) is the core Windows telemetry service, continuously collecting and uploading system data. Disabling significantly reduces background network and CPU activity."),
        ["services.disable_diagtrack"] = (
            "dmwappushservice 等推送遥测服务是 DiagTrack 的辅助服务，负责将遥测数据推送到微软服务器。禁用后进一步减少后台网络活动。",
            "dmwappushservice and related push telemetry services are DiagTrack auxiliaries responsible for pushing telemetry data to Microsoft servers. Disabling further reduces background network activity."),
        ["services.disable_fax"] = (
            "传真服务是遗留功能，在桌面游戏场景中完全不需要。禁用后减少一个后台服务的内存和 CPU 占用。",
            "Fax service is a legacy feature completely unnecessary for desktop gaming. Disabling reduces memory and CPU consumption from one background service."),
        ["services.disable_maps_broker"] = (
            "MapsBroker 服务管理离线地图的下载和更新。它会在后台定期检查地图更新，占用网络带宽。桌面游戏不需要离线地图功能。",
            "MapsBroker service manages offline map downloads and updates. It periodically checks for map updates in the background, consuming network bandwidth. Desktop gaming doesn't need offline maps."),
        ["services.disable_remote_registry"] = (
            "远程注册表服务允许网络上的其他计算机读写本机注册表。这是一个重大安全隐患，禁用后提升系统安全性。",
            "Remote Registry allows other computers on the network to read/write the local registry. This is a major security concern — disabling improves system security."),
        ["services.disable_sysmain_unique"] = (
            "SysMain（原 SuperFetch）通过预加载常用应用数据来加速启动。但在 SSD 上这种预加载反而增加了不必要的磁盘写入，缩短 SSD 寿命。禁用后减少后台磁盘活动。",
            "SysMain (formerly SuperFetch) preloads frequently used app data to speed up launches. But on SSDs, this preloading adds unnecessary disk writes, shortening SSD lifespan. Disabling reduces background disk activity."),
        ["services.disable_wallet_service"] = (
            "WalletService 为 UWP 支付功能提供支持。在非 UWP 支付场景下完全不需要，禁用后减少后台服务开销。",
            "WalletService supports UWP payment functionality. Completely unnecessary for non-UWP payment scenarios — disabling reduces background service overhead."),
        ["services.disable_xbox_services"] = (
            "Xbox 关联服务包括游戏手柄支持、Xbox Live 认证、游戏存档同步等。如果不使用 Xbox 生态，禁用这些服务可以释放可观的内存和 CPU 资源。",
            "Xbox-related services include gamepad support, Xbox Live authentication, game save sync, etc. If not using the Xbox ecosystem, disabling these services frees considerable memory and CPU resources."),
        ["service.bluetooth.disable"] = (
            "蓝牙服务（bthserv）在后台持续扫描蓝牙设备。如果不使用蓝牙外设，禁用后可以减少后台扫描的 CPU 开销和潜在的无线电干扰。",
            "Bluetooth service (bthserv) continuously scans for Bluetooth devices in the background. If not using Bluetooth peripherals, disabling reduces background scanning CPU overhead and potential radio interference."),
        ["service.wer.disable"] = (
            "Windows 错误报告服务在应用崩溃时收集转储文件并上传。这个过程会占用大量磁盘 I/O。禁用后崩溃不再触发数据收集和上传。",
            "Windows Error Reporting service collects dump files on app crashes and uploads them. This process consumes significant disk I/O. Disabling prevents data collection and upload on crashes."),
        ["service.sensordata.disable"] = (
            "SensorDataService 收集传感器数据（如计步器）。桌面游戏场景不需要这些数据，禁用后减少后台数据收集开销。",
            "SensorDataService collects sensor data (like step counter). Desktop gaming doesn't need this data — disabling reduces background data collection overhead."),
        ["services.disable_game_dvr"] = (
            "Game DVR 后台录制功能持续使用 GPU 编码器录制游戏画面。即使不保存录像，编码器仍在工作，占用 GPU 资源。禁用后释放 GPU 编码器资源。",
            "Game DVR background recording continuously uses the GPU encoder to record gameplay. Even without saving recordings, the encoder is still working, consuming GPU resources. Disabling frees GPU encoder resources."),
        ["services.enable_game_mode"] = (
            "Windows 游戏模式会在检测到游戏运行时自动优化系统资源分配：减少后台更新、降低通知频率、优先为游戏分配 CPU/GPU 资源。这是微软官方推荐的游戏优化功能。",
            "Windows Game Mode automatically optimizes system resource allocation when games are detected: reduces background updates, lowers notification frequency, prioritizes CPU/GPU resources for games. This is Microsoft's officially recommended gaming optimization."),
        ["services.enable_high_performance_mode"] = (
            "仅当传统桌面平台处于交流供电、无 OEM/组织策略限制，且 Windows 实际枚举到内置高性能计划时才允许切换。该模式可能提高持续负载响应，也会增加功耗、温度和风扇噪声；不会强制修改 CPU 最小/最大状态、Boost 或核心停车。",
            "Switching is allowed only on a traditional AC-powered desktop with no OEM or organization policy restriction and only when Windows actually enumerates its built-in High Performance plan. It may improve sustained-load responsiveness while increasing power, heat, and fan noise; it never forces CPU min/max state, boost, or core parking."),
        // ========== 计划任务 / 任务调度 ==========
        ["task.disable_compatibility_appraiser"] = (
            "兼容性评估任务（CompatTelRunner.exe）会定期扫描系统中所有已安装的程序，评估与新版 Windows 的兼容性。这个扫描过程非常消耗 CPU 和磁盘 I/O，可能持续数分钟。",
            "Compatibility Appraiser (CompatTelRunner.exe) periodically scans all installed programs to evaluate compatibility with newer Windows versions. This scan is very CPU and disk I/O intensive, potentially lasting several minutes."),
        ["task.disable_customer_experience"] = (
            "CEIP 数据收集任务会定期收集系统使用统计并上传。禁用后减少周期性的后台数据收集活动。",
            "CEIP data collection tasks periodically gather system usage statistics and upload them. Disabling reduces periodic background data collection activity."),
        ["task.disable_disk_diagnostics"] = (
            "磁盘诊断任务会定期检查磁盘健康状态，执行 SMART 查询和表面扫描。这些操作会产生磁盘 I/O，在游戏运行时可能导致加载卡顿。",
            "Disk diagnostic tasks periodically check disk health, performing SMART queries and surface scans. These operations generate disk I/O that can cause loading stutters during gaming."),
        ["tasks.disable_app_experience"] = (
            "Application Experience 任务运行 Microsoft Compatibility Appraiser，收集应用兼容性数据。禁用后减少后台扫描开销。",
            "Application Experience task runs Microsoft Compatibility Appraiser, collecting app compatibility data. Disabling reduces background scanning overhead."),
        ["tasks.disable_auto_proxy_detection"] = (
            "自动代理检测任务会定期检查网络代理设置。在不使用代理的环境下，这个检查是不必要的开销。",
            "Auto proxy detection task periodically checks network proxy settings. In environments not using proxies, this check is unnecessary overhead."),
        ["tasks.disable_ceip"] = (
            "CEIP 计划任务是客户体验改善计划的执行器，定期收集和上传使用数据。禁用后停止数据收集。",
            "CEIP scheduled task is the executor for Customer Experience Improvement Program, periodically collecting and uploading usage data. Disabling stops data collection."),
        ["tasks.disable_cleanup_tasks"] = (
            "StartComponentCleanup 任务会自动运行 DISM 清理旧的系统组件。这个过程非常消耗 CPU 和磁盘 I/O，可能在游戏时意外触发。禁用后避免这种意外的性能冲击。",
            "StartComponentCleanup task automatically runs DISM to clean old system components. This process is very CPU and disk I/O intensive and may trigger unexpectedly during gaming. Disabling prevents this unexpected performance impact."),
        ["tasks.disable_defragmentation"] = (
            "磁盘碎片整理任务会在计划时间运行，对 HDD 进行碎片整理。在 SSD 上碎片整理不仅不必要，还会增加写入次数。禁用后避免不必要的磁盘操作。",
            "Disk defragmentation task runs at scheduled times to defragment HDDs. On SSDs, defragmentation is not only unnecessary but increases write operations. Disabling avoids unnecessary disk operations."),
        ["tasks.disable_disk_diagnostic"] = (
            "DiskDiagnosticDataCollector 收集磁盘诊断数据并上传。禁用后减少磁盘 I/O 和网络上传活动。",
            "DiskDiagnosticDataCollector collects disk diagnostic data and uploads it. Disabling reduces disk I/O and network upload activity."),
        ["tasks.disable_ms_store_update_tasks"] = (
            "商店自动更新任务会在后台下载和安装应用更新，占用网络带宽和磁盘 I/O。在游戏时这些后台更新会直接影响网络延迟和帧率。",
            "Store auto-update tasks download and install app updates in the background, consuming network bandwidth and disk I/O. During gaming, these background updates directly affect network latency and frame rates."),
        ["tasks.disable_ms_sync_tasks"] = (
            "Sync Center 任务管理离线文件同步。在不使用离线文件功能的场景下，禁用可以减少后台同步的磁盘和网络开销。",
            "Sync Center tasks manage offline file synchronization. When not using offline files, disabling reduces background sync disk and network overhead."),
        ["tasks.disable_notification_tasks"] = (
            "备份通知任务会定期检查备份状态并弹出提醒。这些通知在游戏全屏模式下可能导致焦点丢失。禁用后不再弹出备份提醒。",
            "Backup notification tasks periodically check backup status and display reminders. These notifications can cause focus loss in fullscreen gaming. Disabling prevents backup reminders."),
        ["tasks.disable_policy_updates"] = (
            "Windows Update 策略刷新任务会定期检查更新策略并触发更新扫描。禁用后减少后台更新检查的 CPU 和网络开销。",
            "Windows Update policy refresh tasks periodically check update policies and trigger update scans. Disabling reduces background update checking CPU and network overhead."),
        ["tasks.disable_remote_management"] = (
            "远程协助任务允许远程用户连接到你的计算机。在游戏场景下不需要此功能，禁用后减少后台服务开销并提升安全性。",
            "Remote assistance tasks allow remote users to connect to your computer. Not needed for gaming — disabling reduces background service overhead and improves security."),
        ["tasks.disable_store_features"] = (
            "Store Feature Installers 任务会在后台安装 Windows 功能和体验。这些安装操作会占用 CPU、磁盘和网络资源。禁用后避免意外的后台安装活动。",
            "Store Feature Installers tasks install Windows features and experiences in the background. These installations consume CPU, disk, and network resources. Disabling prevents unexpected background installation activity."),
        ["tasks.disable_sync_features"] = (
            "SettingSync 任务将系统设置同步到微软云端。禁用后设置仅保存在本地，减少后台同步的网络开销。",
            "SettingSync tasks sync system settings to Microsoft cloud. Disabling keeps settings local only, reducing background sync network overhead."),
        ["tasks.disable_windows_update"] = (
            "UpdateOrchestrator 扫描任务会定期检查 Windows 更新。这个扫描过程会占用 CPU 和网络资源，可能在游戏时意外触发。禁用后手动控制更新时机。",
            "UpdateOrchestrator scan tasks periodically check for Windows updates. This scanning consumes CPU and network resources and may trigger unexpectedly during gaming. Disabling gives manual control over update timing."),
        ["tasks.disable_xbox_features"] = (
            "Xbox 功能任务（XblGameSaveTask）管理 Xbox 游戏存档的云同步。如果不使用 Xbox 云存档，禁用后减少后台同步开销。",
            "Xbox feature tasks (XblGameSaveTask) manage Xbox game save cloud sync. If not using Xbox cloud saves, disabling reduces background sync overhead."),
        ["tasks.disable_xbox_tasks"] = (
            "Xbox 登录任务在用户登录时执行 Xbox 相关的初始化操作。如果不使用 Xbox 服务，禁用后加快登录速度。",
            "Xbox login tasks perform Xbox-related initialization on user login. If not using Xbox services, disabling speeds up login."),
        // ========== 外设优化 ==========
        ["peripheral.optimize_mouse_polling"] = (
            "鼠标轮询率决定了系统每秒读取鼠标位置的次数。优化 USB 轮询率设置可以确保高端游戏鼠标（1000Hz+）的每一次位置更新都被系统及时捕获，实现更平滑的鼠标移动。",
            "Mouse polling rate determines how many times per second the system reads mouse position. Optimizing USB polling rate settings ensures every position update from high-end gaming mice (1000Hz+) is captured promptly, achieving smoother mouse movement."),
        ["peripheral.disable_usb_power_saving"] = (
            "USB 节能模式会在设备空闲时切断供电或降低功耗。这可能导致鼠标和键盘在短暂不使用后需要重新唤醒，表现为输入延迟或设备断连。禁用后设备始终保持供电。",
            "USB power saving cuts power or reduces consumption when devices are idle. This can cause mice and keyboards to need re-awakening after brief inactivity, manifesting as input lag or device disconnection. Disabling keeps devices always powered."),
        ["peripheral.optimize_keyboard_filter"] = (
            "键盘过滤驱动控制按键事件的处理方式。优化过滤设置可以减少按键事件从硬件到应用层的传递延迟，提升键盘响应速度。",
            "Keyboard filter driver controls how key events are processed. Optimizing filter settings reduces key event delivery latency from hardware to application layer, improving keyboard responsiveness."),
        ["peripheral.mouse_precision"] = (
            "鼠标 DPI 缩放和指针精度设置影响系统如何处理鼠标的原始移动数据。优化后消除系统对鼠标移动的额外处理（如加速、平滑），让鼠标移动与屏幕指针保持 1:1 的线性关系。",
            "Mouse DPI scaling and pointer precision settings affect how the system processes raw mouse movement data. Optimization eliminates extra system processing (acceleration, smoothing), maintaining a 1:1 linear relationship between mouse movement and screen pointer."),
        ["peripheral.mouse_polling"] = (
            "系统对鼠标轮询率的响应速度取决于 USB 驱动和输入子系统的配置。优化后确保系统能跟上高轮询率鼠标的数据速率，避免因系统处理不及时导致的鼠标移动不平滑。",
            "System response to mouse polling rate depends on USB driver and input subsystem configuration. Optimization ensures the system keeps up with high polling rate mouse data rates, preventing choppy mouse movement from delayed system processing."),
        ["peripheral.keyboard.delay"] = (
            "键盘首次按键延迟控制按住按键后多久开始重复输入。设为最低值后，按键响应更即时，对需要快速连续按键的游戏操作有直接帮助。",
            "Keyboard initial delay controls how long after holding a key before repeat input begins. Setting to minimum makes key response more immediate, directly helping game operations requiring rapid key presses."),
        ["peripheral.keyboard.speed"] = (
            "键盘重复率控制按住按键时字符输入的速度。设为最高值后，长按按键时输入更快，适合需要持续按键的游戏操作。",
            "Keyboard repeat rate controls character input speed when holding a key. Setting to maximum makes input faster when holding keys, suitable for game operations requiring sustained key presses."),
        ["peripheral.keyboard_response"] = (
            "综合优化键盘响应包括按键缓冲区大小、重复延迟和重复间隔。这些参数共同决定了从按下按键到游戏响应之间的延迟。",
            "Comprehensive keyboard response optimization includes key buffer size, repeat delay, and repeat interval. These parameters together determine the latency from key press to game response."),
        ["peripheral.anti_mistouch"] = (
            "游戏中误触 Windows 键会弹出开始菜单，导致游戏失去焦点甚至最小化。禁用 Win 键可以完全避免这种意外中断，是竞技游戏的必备设置。",
            "Accidentally pressing the Windows key during gaming opens the Start menu, causing the game to lose focus or minimize. Disabling the Win key completely prevents this interruption — essential for competitive gaming."),
        ["peripheral.usb.suspend"] = (
            "USB 选择性挂起允许系统在设备空闲时暂停 USB 端口供电。这可能导致游戏鼠标和键盘在短暂不操作后断连或延迟响应。禁用后所有 USB 端口始终保持活跃。",
            "USB Selective Suspend allows the system to suspend USB port power when devices are idle. This can cause gaming mice and keyboards to disconnect or respond with delay after brief inactivity. Disabling keeps all USB ports always active."),
        ["peripheral.usb_suspend"] = (
            "同步禁用 AC 和 DC 模式下的 USB 选择性挂起，确保无论是插电还是电池模式，USB 设备都不会因节能而断连。",
            "Simultaneously disabling USB Selective Suspend for both AC and DC modes ensures USB devices never disconnect due to power saving, whether plugged in or on battery."),
        // ========== 内存优化 ==========
        ["memory.optimize_paging"] = (
            "分页文件（虚拟内存）是物理内存的扩展。优化分页文件大小和位置可以减少页面换入换出的频率，避免游戏因内存不足而频繁访问磁盘导致的卡顿。",
            "Paging file (virtual memory) extends physical memory. Optimizing paging file size and location reduces page swap frequency, preventing game stuttering from frequent disk access due to insufficient memory."),
        ["memory.disable_memory_compression"] = (
            "Windows 内存压缩功能会在内存紧张时压缩不活跃的内存页面。压缩和解压过程消耗 CPU 资源，在游戏高负载时可能导致 CPU 争抢。禁用后内存管理更直接，但需要更多物理内存。",
            "Windows memory compression compresses inactive memory pages when memory is tight. Compression and decompression consume CPU resources, potentially causing CPU contention during high game load. Disabling makes memory management more direct but requires more physical memory."),
        ["memory.optimize_large_pages"] = (
            "大页面（2MB 而非默认 4KB）减少了 TLB（转换后备缓冲区）未命中的频率。TLB 未命中需要多级页表查找，每次查找消耗数十纳秒。对于内存访问密集的游戏，大页面可以显著提升性能。",
            "Large pages (2MB instead of default 4KB) reduce TLB (Translation Lookaside Buffer) miss frequency. TLB misses require multi-level page table lookups, each consuming tens of nanoseconds. For memory-intensive games, large pages can significantly improve performance."),
        ["memory.disable_compression"] = (
            "内存压缩在后台持续运行，监控内存使用并压缩不活跃页面。这个过程的 CPU 开销在游戏高负载时尤为明显。禁用后 CPU 不再花时间在内存压缩上。",
            "Memory compression runs continuously in the background, monitoring memory usage and compressing inactive pages. The CPU overhead is especially noticeable during high game load. Disabling stops the CPU from spending time on memory compression."),
        ["memory.disable_superfetch"] = (
            "SuperFetch 预加载常用应用数据到内存，但这些预加载的数据可能与游戏争抢内存空间。禁用后内存完全由当前运行的应用（游戏）使用。",
            "SuperFetch preloads frequently used app data into memory, but this preloaded data may compete with games for memory space. Disabling lets memory be fully used by currently running applications (games)."),
        ["memory.disable_kernel_paging"] = (
            "默认情况下 Windows 可能将内核代码和驱动分页到磁盘。禁用内核分页后，内核和驱动始终驻留在物理内存中，消除了因内核代码被换出导致的调度延迟。",
            "By default, Windows may page kernel code and drivers to disk. Disabling kernel paging keeps kernel and drivers always resident in physical memory, eliminating scheduling latency from kernel code being swapped out."),
        ["memory.enable_large_pages"] = (
            "启用大页面支持后，应用可以请求使用 2MB 的大页面而非 4KB 的标准页面。这大幅减少了页表条目数量和 TLB 未命中率，对内存密集型游戏有显著的性能提升。",
            "Enabling large page support allows applications to request 2MB large pages instead of 4KB standard pages. This dramatically reduces page table entries and TLB miss rates, providing significant performance improvements for memory-intensive games."),
        ["memory.optimize_system_cache"] = (
            "系统缓存大小影响 Windows 为文件缓存保留的内存量。调整到合适的大小可以平衡文件缓存和应用可用内存，确保游戏有足够的内存空间。",
            "System cache size affects how much memory Windows reserves for file caching. Adjusting to an appropriate size balances file cache and available application memory, ensuring games have sufficient memory space."),
        ["memory.optimize_pagefile"] = (
            "页面文件配置包括大小、位置和管理方式。固定大小的页面文件避免了动态扩展时的磁盘碎片和 I/O 延迟。将页面文件放在最快的磁盘上可以减少换页延迟。",
            "Pagefile configuration includes size, location, and management method. Fixed-size pagefiles avoid disk fragmentation and I/O latency from dynamic expansion. Placing the pagefile on the fastest disk reduces paging latency."),
        ["memory.optimize_priority"] = (
            "调整前台应用的内存优先级确保游戏的内存页面不会被系统优先换出。当内存紧张时，系统会优先回收低优先级应用的内存，保护游戏的内存不受影响。",
            "Adjusting foreground app memory priority ensures game memory pages aren't preferentially swapped out. When memory is tight, the system preferentially reclaims memory from lower-priority apps, protecting game memory."),
        // ========== 桌面体验 / 资源管理器 ==========
        ["explorer.disable_animations"] = (
            "窗口动画（最小化、最大化、淡入淡出等）由 DWM 渲染，占用 GPU 资源。禁用后窗口操作立即完成，不再等待动画播放，系统响应更快。",
            "Window animations (minimize, maximize, fade, etc.) are rendered by DWM, consuming GPU resources. Disabling makes window operations complete immediately without waiting for animations, making the system more responsive."),
        ["explorer.disable_thumbnails"] = (
            "缩略图缓存会为文件夹中的图片和视频生成预览图，占用磁盘空间和 CPU。禁用后文件夹打开更快，减少磁盘 I/O。",
            "Thumbnail cache generates preview images for pictures and videos in folders, consuming disk space and CPU. Disabling makes folders open faster and reduces disk I/O."),
        ["explorer.optimize_visual_effects"] = (
            "Windows 视觉效果包括窗口阴影、透明度、动画、字体平滑等。将视觉效果调整为最佳性能可以减少 GPU 和 CPU 在桌面渲染上的开销，让更多资源可用于游戏。",
            "Windows visual effects include window shadows, transparency, animations, font smoothing, etc. Adjusting visual effects for best performance reduces GPU and CPU overhead on desktop rendering, freeing more resources for gaming."),
        ["registry.show_file_extensions"] = (
            "显示文件扩展名可以帮助识别文件的真实类型，防止恶意文件伪装成安全文件（如 virus.txt.exe 伪装成 virus.txt）。这是基本的安全最佳实践。",
            "Showing file extensions helps identify true file types, preventing malicious files from disguising as safe files (e.g., virus.txt.exe disguised as virus.txt). This is a basic security best practice."),
        ["explorer.hidden_files"] = (
            "隐藏属性会阻止文件在资源管理器中显示。显示隐藏文件可查看系统与用户隐藏的文件，便于排查问题，但误删系统文件有风险。",
            "The hidden attribute prevents files from showing in Explorer. Showing hidden files reveals system and user hidden files for troubleshooting, but deleting system files by mistake is risky."),
        ["explorer.end_task"] = (
            "Windows 11 允许在任务栏右键菜单中直接“结束任务”，无需先打开任务管理器。启用后可快速关闭未响应的程序，减少卡顿时的手动操作步骤。",
            "Windows 11 lets you end a task directly from the taskbar right-click menu without opening Task Manager first. Enabling it quickly closes unresponsive apps with fewer manual steps."),
        ["registry.disable_thumbnail_delay"] = (
            "任务栏预览的悬停延迟会在鼠标悬停后等待一段时间才显示预览窗口。移除延迟后预览立即出现，提升桌面操作的响应速度。",
            "Taskbar preview hover delay waits before showing the preview window after mouse hover. Removing the delay makes previews appear immediately, improving desktop operation responsiveness."),
        ["registry.disable_startup_sound"] = (
            "禁用启动声音可以让系统启动更安静，适合共享办公环境。同时减少了音频子系统在启动时的初始化开销。",
            "Disabling startup sound makes system boot quieter, suitable for shared office environments. Also reduces audio subsystem initialization overhead during startup."),
        ["registry.disable_low_disk_check"] = (
            "低磁盘空间警报会在磁盘空间不足时反复弹出气球通知。这些通知在游戏全屏模式下可能导致焦点丢失。禁用后不再弹出这些提醒。",
            "Low disk space alerts repeatedly display balloon notifications when disk space is low. These notifications can cause focus loss in fullscreen gaming. Disabling prevents these reminders."),
        ["edge.disable_game_assist"] = (
            "Edge 游戏助手是一个覆盖层，在游戏中提供攻略和提示。这个覆盖层会占用 GPU 资源并可能导致输入延迟。禁用后消除这种额外开销。",
            "Edge Game Assist is an overlay providing walkthroughs and tips during gaming. This overlay consumes GPU resources and may cause input lag. Disabling eliminates this extra overhead."),
        ["edge.disable_game_assist_policy"] = (
            "通过组策略彻底禁用 Edge 游戏助手，确保即使 Edge 更新也不会重新启用此功能。这是比注册表设置更持久的禁用方式。",
            "Disabling Edge Game Assist via Group Policy ensures it stays disabled even after Edge updates. This is a more persistent disabling method than registry settings."),
        // ========== 电竞模式 ==========
        ["esports.enable_hags"] = (
            "HAGS 让 GPU 硬件直接管理显存和任务调度，减少 CPU 内核驱动的介入，降低渲染延迟。在支持的硬件上可以提升帧率并降低输入延迟。",
            "HAGS lets GPU hardware directly manage VRAM and task scheduling, reducing CPU kernel driver involvement and lowering rendering latency. On supported hardware, it can improve FPS and reduce input lag."),
        ["esports.prefer_dgpu"] = (
            "强制所有应用使用独立显卡，避免 Windows 将游戏错误分配给集成显卡运行。集显的性能远低于独显，误分配会导致严重的帧率下降。",
            "Forces all applications to use the discrete GPU, preventing Windows from incorrectly assigning games to the integrated GPU. Integrated GPU performance is far lower than discrete — misassignment causes severe frame rate drops."),
        ["esports.gpu_sfio_priority"] = (
            "提升 GPU 流媒体 I/O 优先级后，游戏的渲染命令在 GPU 调度队列中获得更高权重，优先被处理。这减少了因其他应用抢占 GPU 导致的帧延迟。",
            "Increasing GPU streaming I/O priority gives game render commands higher weight in the GPU scheduling queue, processing them first. This reduces frame delays from other apps competing for GPU resources."),
        ["esports.disable_fullscreen_optimization"] = (
            "Windows 全屏优化会将传统的独占全屏模式转换为无边框窗口化模式。虽然方便 Alt+Tab 切换，但增加了一层 DWM 合成延迟。禁用后游戏使用真正的独占全屏，输入延迟更低。",
            "Windows fullscreen optimization converts traditional exclusive fullscreen to borderless windowed mode. While convenient for Alt+Tab, it adds a DWM composition latency layer. Disabling lets games use true exclusive fullscreen for lower input latency."),
        ["esports.enable_game_mode"] = (
            "游戏模式让 Windows 在检测到游戏运行时自动优化资源分配：暂停后台更新、减少通知、优先为游戏分配 CPU 和 GPU 资源。这是微软官方的游戏优化方案。",
            "Game Mode makes Windows automatically optimize resource allocation when games are detected: pauses background updates, reduces notifications, prioritizes CPU and GPU resources for games. This is Microsoft's official gaming optimization."),
        ["esports.disable_game_dvr"] = (
            "Game DVR 的后台录制功能持续使用 GPU 编码器，即使不保存录像也在工作。禁用后释放 GPU 编码器资源和显存，可提升 3-5% 的 GPU 性能。",
            "Game DVR's background recording continuously uses the GPU encoder, working even without saving recordings. Disabling frees GPU encoder resources and VRAM, potentially improving GPU performance by 3-5%."),
        ["esports.windowed_optimization"] = (
            "窗口化游戏优化调整 DWM 的合成策略，减少窗口化模式下的渲染延迟。对于需要在窗口化模式下运行的游戏（如多显示器设置），这可以改善帧时间一致性。",
            "Windowed game optimization adjusts DWM composition strategy, reducing rendering latency in windowed mode. For games that need to run windowed (like multi-monitor setups), this improves frame time consistency."),
        ["esports.disable_background_apps"] = (
            "后台应用在不可见时仍然消耗 CPU、内存和网络资源。禁用后这些应用只在前台打开时运行，为游戏释放大量系统资源。",
            "Background apps consume CPU, memory, and network resources while invisible. Disabling makes them run only when in foreground, freeing significant system resources for gaming."),
        ["esports.disable_visual_effects"] = (
            "Windows 视觉效果（动画、透明、阴影等）由 GPU 渲染。禁用后 GPU 不再花费资源在桌面美化上，所有 GPU 算力都可用于游戏渲染。",
            "Windows visual effects (animations, transparency, shadows, etc.) are GPU-rendered. Disabling stops the GPU from spending resources on desktop beautification, making all GPU power available for game rendering."),
        ["esports.foreground_priority"] = (
            "提升前台应用的 CPU 调度优先级后，游戏线程在 CPU 调度中获得更多时间片。这意味着游戏的逻辑计算和渲染准备能更及时地执行，减少帧时间波动。",
            "Increasing foreground app CPU scheduling priority gives game threads more time slices in CPU scheduling. This means game logic and render preparation execute more promptly, reducing frame time fluctuations."),
        ["fps.disable_fullscreen_optimizations"] = (
            "Windows 全屏优化将独占全屏转为无边框窗口化，增加了 DWM 合成层的延迟。禁用后游戏直接控制显示输出，消除了这层额外延迟，输入到显示的延迟可降低 5-15ms。",
            "Windows fullscreen optimization converts exclusive fullscreen to borderless windowed, adding DWM composition layer latency. Disabling lets games directly control display output, eliminating this extra latency — input-to-display delay can decrease by 5-15ms."),
        ["gaming.mode.enable"] = (
            "系统游戏模式在检测到游戏时自动优化资源分配，减少后台干扰。这是一个轻量级的系统级优化，不会影响游戏本身的设置。",
            "System Game Mode automatically optimizes resource allocation when games are detected, reducing background interference. This is a lightweight system-level optimization that doesn't affect game settings."),
        ["gaming.dvr.disable"] = (
            "通过策略禁止游戏录制功能，释放 GPU 编码器资源。这比仅在设置中关闭更彻底，确保录制功能完全停止。",
            "Disabling game recording via policy frees GPU encoder resources. This is more thorough than just turning it off in settings, ensuring recording functionality completely stops."),
        // ========== 性能加速 ==========
        ["performance.disable_security_center"] = (
            "停止 SecurityHealthService 后，Windows 安全中心不再持续监控保护状态，可减少后台占用；同时不会再提供安全状态告警。",
            "Stopping SecurityHealthService prevents Windows Security Center from monitoring protection state, reducing background usage; security status alerts are no longer provided."),
        ["performance.disable_uac"] = (
            "关闭 EnableLUA 后，程序运行不再弹出权限确认，可减少交互打断；需要重启且恶意程序可能静默提权。",
            "Disabling EnableLUA removes consent prompts, reducing interaction interruptions; restart is required and malware may elevate silently."),
        ["performance.disable_firewall"] = (
            "停止 MpsSvc 后不再过滤网络流量，可能减少少量处理开销；设备将失去入站和出站防护。",
            "Stopping MpsSvc removes network traffic filtering and may reduce minor processing overhead; inbound and outbound protection is lost."),
        ["performance.disable_store_auto_update"] = (
            "商店应用自动更新会在后台下载和安装更新，占用网络带宽和磁盘 I/O。在游戏时这些后台活动会直接影响网络延迟和帧率稳定性。",
            "Store app auto-update downloads and installs updates in the background, consuming network bandwidth and disk I/O. During gaming, these background activities directly affect network latency and frame rate stability."),
        ["performance.disable_uwp_background"] = (
            "UWP 应用在后台运行时会持续消耗 CPU、内存和网络资源。禁用后台运行后，这些应用只在前台打开时才活跃，释放大量系统资源。",
            "UWP apps running in the background continuously consume CPU, memory, and network resources. Disabling background running makes them active only when in foreground, freeing significant system resources."),
        ["performance.disable_indexing"] = (
            "Windows 搜索索引服务持续扫描文件变化并更新索引。这种持续的磁盘 I/O 在游戏运行时会与游戏争抢存储带宽，导致资源加载变慢。",
            "Windows Search indexing service continuously scans file changes and updates indexes. This constant disk I/O competes with games for storage bandwidth during gameplay, slowing resource loading."),
        ["basic.disable_security_center"] = (
            "停止 SecurityHealthService 后，Windows 安全中心不再持续监控保护状态，可减少后台占用；同时不会再提供安全状态告警。",
            "Stopping SecurityHealthService prevents Windows Security Center from monitoring protection state, reducing background usage; security status alerts are no longer provided."),
        ["basic.disable_uac"] = (
            "关闭 EnableLUA 后，程序运行不再弹出权限确认，可减少交互打断；需要重启且恶意程序可能静默提权。",
            "Disabling EnableLUA removes consent prompts, reducing interaction interruptions; restart is required and malware may elevate silently."),
        ["basic.disable_firewall"] = (
            "停止 MpsSvc 后不再过滤网络流量，可能减少少量处理开销；设备将失去入站和出站防护。",
            "Stopping MpsSvc removes network traffic filtering and may reduce minor processing overhead; inbound and outbound protection is lost."),
        ["basic.disable_store_auto_update"] = (
            "关闭 Windows 商店后台自动更新，防止在游戏时触发大量下载和安装操作，避免网络和磁盘资源被占用。",
            "Disabling Windows Store background auto-update prevents large downloads and installations during gaming, avoiding network and disk resource consumption."),
        ["basic.disable_uwp_background"] = (
            "阻止通用应用在后台占用资源。UWP 应用即使不在前台也会执行后台任务，消耗 CPU 和内存。禁用后释放这些资源。",
            "Prevents universal apps from consuming resources in the background. UWP apps execute background tasks even when not in foreground, consuming CPU and memory. Disabling frees these resources."),
        ["basic.disable_indexing"] = (
            "关闭 WSearch 索引服务后，系统不再持续扫描文件变化。虽然搜索功能变慢，但消除了后台磁盘 I/O 对游戏的干扰。",
            "Disabling WSearch indexing service stops continuous file change scanning. While search becomes slower, it eliminates background disk I/O interference with gaming."),
        ["basic.disable_windows_update"] = (
            "手动控制 Windows 更新时机，防止系统在游戏过程中自动下载和安装更新。自动更新可能占用大量网络带宽和 CPU 资源，甚至触发强制重启。",
            "Manual control of Windows Update timing prevents the system from automatically downloading and installing updates during gaming. Auto-updates can consume significant network bandwidth and CPU, even triggering forced restarts."),
        ["perf.wuauserv.disable"] = (
            "禁用 Windows Update 服务后，系统不会在后台自动检查和下载更新。这消除了游戏过程中因更新下载导致的网络延迟飙升和 CPU 占用。",
            "Disabling Windows Update service prevents automatic background update checking and downloading. This eliminates network latency spikes and CPU usage from update downloads during gaming."),
        ["basic.disable_security_notifications"] = (
            "安全通知可能提示恶意软件、账户或防火墙风险。产品安全策略禁止通过整体关闭通知来隐藏这些告警。",
            "Security notifications can report malware, account, or firewall risks. Product security policy forbids hiding these alerts by disabling notifications globally."),
        ["basic.disable_edge_startup"] = (
            "Edge StartupBoost 让 Edge 浏览器在系统启动时预加载到内存中，即使你没有打开 Edge。这会占用数百 MB 内存和 CPU 资源。禁用后 Edge 只在你主动打开时才加载。",
            "Edge StartupBoost preloads Edge browser into memory at system startup, even if you haven't opened Edge. This consumes hundreds of MB of memory and CPU resources. Disabling makes Edge load only when you actively open it."),
        ["basic.disable_map_auto_update"] = (
            "离线地图自动更新会在后台下载地图数据，占用网络带宽和磁盘空间。桌面游戏场景通常不需要离线地图功能。",
            "Offline map auto-update downloads map data in the background, consuming network bandwidth and disk space. Desktop gaming typically doesn't need offline map functionality."),
        ["basic.disable_mouse_acceleration"] = (
            "增强型指针精度（鼠标加速）会根据移动速度动态调整指针位移。这破坏了物理移动和屏幕移动的线性关系，使 FPS 游戏中无法建立稳定的瞄准肌肉记忆。",
            "Enhanced Pointer Precision (mouse acceleration) dynamically adjusts pointer displacement based on movement speed. This breaks the linear relationship between physical and screen movement, preventing stable aiming muscle memory in FPS games."),
        ["basic.disable_game_bar"] = (
            "Game Bar 覆盖层在游戏中提供截图、录制等功能，但它持续占用 GPU 资源并可能导致输入延迟。禁用后消除这种额外开销。",
            "Game Bar overlay provides screenshot and recording features during gaming, but continuously consumes GPU resources and may cause input lag. Disabling eliminates this extra overhead."),
        ["basic.disable_visual_effects"] = (
            "禁用 Windows 动画、透明效果和阴影后，GPU 不再花费资源在桌面美化上。所有 GPU 算力都可用于游戏渲染，同时系统响应更快。",
            "Disabling Windows animations, transparency, and shadows stops the GPU from spending resources on desktop beautification. All GPU power becomes available for game rendering, and system response becomes faster."),
        ["system.boost_foreground_priority"] = (
            "提升前台应用的 CPU 调度优先级，确保游戏线程获得更多 CPU 时间片。这在后台有多个应用运行时尤为重要，可以减少因 CPU 争抢导致的帧时间波动。",
            "Increasing foreground app CPU scheduling priority ensures game threads get more CPU time slices. Especially important when multiple background apps are running, reducing frame time fluctuations from CPU contention."),
        // ========== 系统核心调校 ==========
        ["system.disable_hibernation"] = (
            "休眠功能会在系统盘创建一个与物理内存等大的 hiberfil.sys 文件（通常 8-32GB）。禁用后释放这些磁盘空间，同时避免休眠/唤醒过程中的电源方案冲突。",
            "Hibernation creates a hiberfil.sys file on the system drive equal to physical memory size (typically 8-32GB). Disabling frees this disk space and avoids power plan conflicts during hibernate/wake cycles."),
        ["system.disable_kernel_paging"] = (
            "确保内核代码和驱动程序始终驻留在物理内存中，不会被换出到磁盘。这消除了因内核代码被分页导致的调度延迟，对游戏的帧时间稳定性有直接帮助。",
            "Ensures kernel code and drivers always reside in physical memory, never swapped to disk. This eliminates scheduling latency from kernel code being paged, directly helping game frame time stability."),
        ["system.boost_game_priority"] = (
            "提高游戏进程的 CPU 调度权重，使其在与其他进程竞争 CPU 时间时获得优先权。这对于 CPU 密集型游戏尤为重要。",
            "Increases game process CPU scheduling weight, giving it priority when competing with other processes for CPU time. Especially important for CPU-intensive games."),
        ["system.prioritize_games"] = (
            "将游戏进程设置为高优先级调度类别，确保任务调度器始终将游戏线程放在高优先级队列中执行。这减少了游戏线程被低优先级任务抢占的概率。",
            "Sets game processes to high-priority scheduling category, ensuring the task scheduler always places game threads in the high-priority queue. This reduces the probability of game threads being preempted by low-priority tasks."),
        ["system.long_paths"] = (
            "Windows 默认将文件路径限制在 260 字符内，深层目录或长文件名会导致复制、解压和编译失败。启用 LongPathsEnabled 后移除该限制，避免这类报错。",
            "Windows limits file paths to 260 characters by default, causing copy, extraction and build failures in deep directories or with long filenames. Enabling LongPathsEnabled removes this limit."),
        ["system.disable_wpbt"] = (
            "WPBT（Windows 平台二进制表）允许 OEM 在启动时静默注入可执行程序，可能被滥用强制安装软件。DisableWpbtExecution=1 阻止这种启动注入，降低供应链安全风险。",
            "WPBT (Windows Platform Binary Table) allows OEMs to silently inject executables at boot, which can be abused to force-install software. DisableWpbtExecution=1 blocks this boot injection, reducing supply-chain security risk."),
        ["system.disable_large_system_cache"] = (
            "Large System Cache 模式让 Windows 将更多内存用于文件缓存。禁用后系统将更多内存留给前台应用（游戏），减少因内存不足导致的页面换入换出。",
            "Large System Cache mode makes Windows use more memory for file caching. Disabling reserves more memory for foreground applications (games), reducing page swapping from insufficient memory."),
        ["system.disable_prefetch"] = (
            "在 SSD 上 Prefetch 预读取是多余的，因为 SSD 的随机读取速度已经足够快。禁用后减少不必要的后台磁盘读取活动。",
            "On SSDs, Prefetch preloading is redundant since SSD random read speeds are already fast enough. Disabling reduces unnecessary background disk read activity."),
        ["power.restore_point_frequency"] = (
            "Windows 默认两次系统还原点之间至少间隔 24 小时。将 SystemRestorePointCreationFrequency 设为 0 可绕过该冷却窗口，允许在需要时连续创建多个还原点；写入前会捕获原值，关闭本项即可恢复。",
            "Windows enforces a 24-hour minimum interval between system restore points. Setting SystemRestorePointCreationFrequency to 0 bypasses that cooldown so multiple restore points can be created back-to-back; the original value is captured before writing and is restored when this item is disabled."),
        ["power.s0_network_ac"] = (
            "S0 现代待机（Modern Standby）期间，Windows 默认保持网络连接以接收通知和后台更新。将电源策略 f15576e8 的 ACSettingIndex 设为 0 会在交流电供电的待机期间断开网络，减少待机功耗与后台联网活动；只影响待机联网策略，不改变待机本身或系统保护，写入前捕获原值，关闭本项即可恢复。仅对支持 S0 现代待机的机型有意义。",
            "During S0 Modern Standby, Windows keeps the network connected by default to receive notifications and background updates. Setting ACSettingIndex of power policy f15576e8 to 0 disconnects the network during AC-powered standby, reducing standby power draw and background network activity; it only affects the standby networking policy, not standby itself or system protection, and the original value is captured before writing and restored when this item is disabled. Only meaningful on machines that support S0 Modern Standby."),
        ["power.s0_network_dc"] = (
            "S0 现代待机（Modern Standby）期间，Windows 默认保持网络连接以接收通知和后台更新。将电源策略 f15576e8 的 DCSettingIndex 设为 0 会在电池供电的待机期间断开网络，减少待机功耗与后台联网活动；只影响待机联网策略，不改变待机本身或系统保护，写入前捕获原值，关闭本项即可恢复。仅对支持 S0 现代待机的机型有意义。",
            "During S0 Modern Standby, Windows keeps the network connected by default to receive notifications and background updates. Setting DCSettingIndex of power policy f15576e8 to 0 disconnects the network during battery-powered standby, reducing standby power draw and background network activity; it only affects the standby networking policy, not standby itself or system protection, and the original value is captured before writing and restored when this item is disabled. Only meaningful on machines that support S0 Modern Standby."),
        ["system.coreparking.min"] = (
            "将最小活动核心设为 100% 确保所有 CPU 核心始终保持活跃状态。这消除了核心从停靠状态唤醒的延迟，对需要多核并行的游戏有直接帮助。",
            "Setting minimum active cores to 100% ensures all CPU cores always stay active. This eliminates core wake-up latency from parked state, directly helping games that need multi-core parallelism."),
        ["system.coreparking.max"] = (
            "将最大活动核心保持在 100% 确保系统不会因节能策略而停靠任何核心。配合最小核心设置，完全禁用核心停靠功能。",
            "Keeping maximum active cores at 100% ensures the system doesn't park any cores due to power-saving policies. Combined with minimum core setting, completely disables core parking."),
        // ========== 系统调校 (Tuning) ==========
        ["adjustments.update_storage_reserve"] = (
            "Windows Update 预留空间会在系统盘保留约 7GB 的磁盘空间用于更新。禁用后释放这些空间，但需要确保更新时有足够的可用空间。",
            "Windows Update reserved storage keeps about 7GB on the system drive for updates. Disabling frees this space, but you need to ensure sufficient free space when updating."),
        ["adjustments.disable_last_access_update"] = (
            "NTFS 默认在每次文件访问时更新 Last Access 时间戳，这意味着每次读取文件都会触发一次写入操作。禁用后减少了大量不必要的磁盘写入，提升文件系统性能。",
            "NTFS updates the Last Access timestamp on every file access by default, meaning every file read triggers a write operation. Disabling eliminates massive unnecessary disk writes, improving filesystem performance."),
        ["adjustments.disable_diagnostic_events"] = (
            "AutoLogger-DiagTrack 是 Windows 遥测的后台跟踪器，持续记录系统事件到日志文件。禁用后减少后台磁盘写入和 CPU 开销。",
            "AutoLogger-DiagTrack is Windows telemetry's background tracker, continuously logging system events to files. Disabling reduces background disk writes and CPU overhead."),
        ["tuning.largecache"] = (
            "服务器缓存策略可能导致系统将过多内存用于文件缓存，减少游戏可用内存。更新缓存保留值确保内存分配更适合桌面游戏场景。",
            "Server cache policy may cause the system to use too much memory for file caching, reducing memory available for games. Updating cache reserve values ensures memory allocation is more suitable for desktop gaming."),
        ["tuning.prefetcher"] = (
            "Prefetcher 在机械硬盘上通过预读取减少寻道时间。但在 SSD 上这种预读取反而增加了不必要的 I/O 操作，因为 SSD 没有寻道延迟。",
            "Prefetcher reduces seek time on mechanical hard drives through preloading. But on SSDs, this preloading adds unnecessary I/O operations since SSDs have no seek latency."),
        ["tuning.superfetch"] = (
            "关闭 EnableSuperfetch 注册表值，配合停用 SysMain 服务，彻底禁用预加载功能。在 SSD 系统上这可以减少不必要的后台磁盘活动。",
            "Disabling the EnableSuperfetch registry value, combined with stopping the SysMain service, completely disables preloading. On SSD systems, this reduces unnecessary background disk activity."),
        ["tuning.recentdocs.disable"] = (
            "最近访问记录会追踪你打开的每个文档，占用内存和磁盘空间来维护历史缓存。禁用后减少这种持续的数据收集开销。",
            "Recent documents tracking records every document you open, consuming memory and disk space for history cache. Disabling reduces this continuous data collection overhead."),
        ["tuning.recentapps.disable"] = (
            "最近应用记录会在开始菜单显示最近使用的应用列表。禁用后减少开始菜单的数据维护开销，同时保护使用习惯隐私。",
            "Recent apps tracking displays recently used apps in the Start menu. Disabling reduces Start menu data maintenance overhead while protecting usage habit privacy."),
        ["tuning.powerthrottling"] = (
            "Power Throttling 会自动降低后台进程的 CPU 频率以节能。但如果游戏被错误识别为后台进程，其性能会被严重限制。禁用后确保所有前台进程都以全速运行。",
            "Power Throttling automatically reduces background process CPU frequency to save power. But if a game is incorrectly identified as a background process, its performance is severely limited. Disabling ensures all foreground processes run at full speed."),
        ["tuning.powerplan.high"] = (
            "Windows 高性能模式只在平台能力可靠匹配时提供；不支持或受限设备将显示系统设置引导，不会写入 CPU 参数或标记为已优化。",
            "Windows High Performance mode is offered only when platform capability is reliably matched. Unsupported or managed devices receive Windows settings guidance and are never written or marked optimized."),
        // ========== 设备优化 ==========
        ["device.disable_system_timer"] = (
            "系统计时器设备（HPET）在某些系统上会引入额外的计时延迟。禁用后系统使用更快的 TSC 作为计时源。AMD CPU 推荐此优化，Intel CPU 需根据具体型号评估。",
            "System timer device (HPET) can introduce additional timing latency on some systems. Disabling lets the system use the faster TSC as timing source. Recommended for AMD CPUs; Intel CPUs should evaluate based on specific model."),
        ["device.disable_umbus"] = (
            "UMBus 根总线枚举器负责枚举用户模式设备。禁用后减少不必要的设备枚举开销，降低系统中断频率。",
            "UMBus root bus enumerator handles user-mode device enumeration. Disabling reduces unnecessary device enumeration overhead and lowers system interrupt frequency."),
        ["device.disable_composite_bus"] = (
            "复合总线枚举器处理 USB 复合设备的枚举。禁用后减少 USB 相关的中断和枚举延迟，对使用多个 USB 外设的游戏场景有帮助。",
            "Composite bus enumerator handles USB composite device enumeration. Disabling reduces USB-related interrupts and enumeration latency, helpful for gaming setups with multiple USB peripherals."),
        ["device.disable_hpet_device"] = (
            "在设备管理器级别禁用 HPET 设备，确保系统完全不使用 HPET 作为计时源。这比仅在 BCD 中配置更彻底，可以进一步降低计时相关的延迟。",
            "Disabling the HPET device at device manager level ensures the system completely stops using HPET as a timing source. More thorough than BCD configuration alone, further reducing timing-related latency."),
        
        // ========== 电源计划优化 ==========
        ["power.disable_usb_suspend"] = (
            "USB 选择性暂停在设备空闲时切断 USB 端口供电。游戏鼠标和键盘可能因此在短暂不操作后断连或延迟响应。禁用后 USB 设备始终保持供电，是游戏必备优化。",
            "USB Selective Suspend cuts USB port power when devices are idle. Gaming mice and keyboards may disconnect or respond with delay after brief inactivity. Disabling keeps USB devices always powered — essential for gaming."),
        ["power.disable_aspm"] = (
            "ASPM（Active State Power Management）在 PCIe 设备空闲时降低链路速度以节能。从低功耗状态恢复需要数微秒，对显卡和 NVMe SSD 来说这种延迟会影响数据传输速度。禁用后 PCIe 链路始终保持全速。",
            "ASPM (Active State Power Management) reduces PCIe link speed when devices are idle to save power. Recovery from low-power state takes microseconds, affecting data transfer speed for GPUs and NVMe SSDs. Disabling keeps PCIe links at full speed."),
        ["power.processor_min_state_100"] = (
            "将处理器最小状态设为 100% 后，CPU 永远不会降频，始终以最高频率运行。这消除了所有因频率切换导致的延迟，但会显著增加功耗和发热。",
            "Setting processor minimum state to 100% means the CPU never downclocks, always running at maximum frequency. This eliminates all frequency-switching latency but significantly increases power consumption and heat."),
        ["power.perf_increase_threshold"] = (
            "性能提升阈值控制 CPU 使用率达到多少时触发频率提升。设为 1% 后，CPU 使用率稍有提升就立即触发睿频，响应速度最快。默认值通常较高，导致频率提升有延迟。",
            "Performance increase threshold controls at what CPU usage level frequency boost is triggered. Setting to 1% triggers turbo boost immediately on slight load increase — fastest response. Default values are usually higher, causing delayed frequency boost."),
        ["power.idle_demote_threshold"] = (
            "闲置降级阈值控制 CPU 使用率降到多少时触发降频。设为 100% 后，CPU 永远不会因短暂空闲而降频，保持高性能状态。这对帧时间稳定性有直接帮助。",
            "Idle demote threshold controls at what CPU usage level frequency reduction is triggered. Setting to 100% prevents the CPU from ever downclocking due to brief idle, maintaining high-performance state. Directly helps frame time stability."),
        ["power.hetero_thread_scheduling"] = (
            "Intel 12代+ 处理器采用大小核架构（P-Core + E-Core）。优化线程调度策略确保游戏线程运行在高性能的 P-Core 上，而后台任务分配给效率 E-Core。AMD 用户选择「所有处理器」即可。",
            "Intel 12th gen+ processors use hybrid architecture (P-Core + E-Core). Optimizing thread scheduling ensures game threads run on high-performance P-Cores while background tasks are assigned to efficiency E-Cores. AMD users should select 'All Processors'."),
        ["power.perf_time_check"] = (
            "性能时间检查间隔控制 CPU 频率调整的评估周期。设为最大值后，CPU 频率切换次数大幅减少，保持更稳定的高频运行。频繁的频率切换是帧时间波动的常见原因。",
            "Performance time check interval controls the evaluation period for CPU frequency adjustments. Setting to maximum significantly reduces CPU frequency switching, maintaining more stable high-frequency operation. Frequent frequency switching is a common cause of frame time fluctuations."),
        ["power.class1_efficiency_threshold"] = (
            "第一类处理器（P-Core）效率阈值控制大核响应负载变化的灵敏度。设为 1% 后，P-Core 对负载变化的响应最快，确保游戏帧率更稳定。",
            "Class 1 processor (P-Core) efficiency threshold controls how sensitively P-Cores respond to load changes. Setting to 1% makes P-Core response to load changes fastest, ensuring more stable game frame rates."),
        ["power.class1_perf_level"] = (
            "将第一类处理器（P-Core）性能级别设为 100%，确保大核始终以最高性能运行。这对 Intel 大小核架构的游戏性能优化至关重要。",
            "Setting Class 1 processor (P-Core) performance level to 100% ensures P-Cores always run at maximum performance. Critical for gaming performance optimization on Intel hybrid architecture."),
        ["power.disable_throttle"] = (
            "CPU 节流（Throttling）在温度或功耗超限时强制降低 CPU 频率。禁用后 CPU 不再因温度或功耗限制而降频，但需要确保散热系统足够强大。",
            "CPU Throttling forcibly reduces CPU frequency when temperature or power limits are exceeded. Disabling prevents CPU from downclocking due to thermal or power limits, but requires adequate cooling."),
        // ========== 辅助功能 ==========
        ["accessibility.disable_sticky_keys"] = (
            "粘滞键功能在连按 5 次 Shift 时弹出提示对话框。在 FPS 游戏中频繁按 Shift（冲刺/蹲下）时极易误触，导致游戏失去焦点。禁用后消除这种干扰。",
            "Sticky Keys displays a prompt dialog when Shift is pressed 5 times consecutively. In FPS games where Shift is frequently pressed (sprint/crouch), accidental triggers are common, causing focus loss. Disabling eliminates this interruption."),
        ["accessibility.disable_filter_keys"] = (
            "筛选键功能在长按右 Shift 8 秒时弹出提示。在游戏中长按 Shift 是常见操作，误触会导致游戏中断。禁用后避免这种意外弹窗。",
            "Filter Keys displays a prompt when right Shift is held for 8 seconds. Holding Shift is common in games — accidental triggers cause game interruption. Disabling prevents these unexpected popups."),
        ["accessibility.disable_toggle_keys"] = (
            "切换键功能在按下 Num Lock、Caps Lock 或 Scroll Lock 时发出提示音。在游戏中这些按键可能被绑定为快捷键，提示音会干扰游戏音频。禁用后消除这些提示音。",
            "Toggle Keys plays a beep when Num Lock, Caps Lock, or Scroll Lock is pressed. In games, these keys may be bound as shortcuts — beeps interfere with game audio. Disabling eliminates these notification sounds."),
        
        // ========== 系统清理 ==========
        ["cleanup.gpu_cache"] = (
            "该旧目录入口没有 canonical allowlist、reparse 防护、预览哈希、隔离清单和恢复证明，因此固定失败关闭。",
            "This legacy catalog entry lacks canonical allowlisting, reparse protection, preview hashing, a quarantine manifest, and restore proof, so it fails closed."),
        ["cleanup.prefetch_files"] = (
            "Windows Prefetch 属于系统管理数据。该旧入口不会直接删除它；没有可恢复隔离 Owner 时操作不可用。",
            "Windows Prefetch is system-managed data. This legacy entry will not delete it; the action remains unavailable without a recoverable quarantine Owner."),
        ["cleanup.system_temp"] = (
            "Windows Temp 可能包含正在使用或需诊断的文件。旧入口固定阻断，避免不可恢复删除。",
            "Windows Temp may contain in-use or diagnostically important files. The legacy entry is blocked to prevent unrecoverable deletion."),
        ["cleanup.user_temp"] = (
            "用户临时目录不能按任意路径或通配符直接删除。请使用可审阅且可恢复的系统清理流程。",
            "User temp data cannot be deleted through arbitrary paths or wildcards. Use the reviewable, recoverable system cleanup flow."),
        ["cleanup.thumbnail_cache"] = (
            "缩略图缓存处理可能需要协调 Explorer 生命周期。本版本不会由后台或旧入口终止 Explorer 或直接删除缓存。",
            "Thumbnail cache handling may require coordinating the Explorer lifecycle. This release will not terminate Explorer or delete the cache from a legacy/background entry."),
        ["cleanup.windows_logs"] = (
            "Windows 日志可能用于故障调查。没有隔离清单和恢复证明时，旧入口不会删除日志。",
            "Windows logs may be required for diagnostics. The legacy entry will not remove them without a quarantine manifest and restore proof."),
        ["cleanup.windows_update_cache"] = (
            "Windows Update 缓存受系统服务和更新状态管理。旧入口不会在缺少权威状态与恢复证明时处理它。",
            "Windows Update cache is managed by system services and update state. The legacy entry will not change it without authoritative state and restore proof."),
        ["cleanup.recycle_bin"] = (
            "永久清空回收站不可恢复，且可能跨多个磁盘和用户作用域。本产品不会将其作为游戏优化执行。",
            "Permanently emptying the Recycle Bin is unrecoverable and may cross drives and user scopes. It is not executed as a gaming optimization."),
    };
}
