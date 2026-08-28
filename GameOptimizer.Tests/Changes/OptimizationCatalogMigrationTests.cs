using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Optimization.Operations;
using GameOptimizer.Core.Optimization.Providers;
using GameOptimizer.Infrastructure.Windows.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace GameOptimizer.Tests.Changes;

public sealed class OptimizationCatalogMigrationTests
{
    private static readonly string[] MigratedIds =
    [
        "registry.show_file_extensions",
        "registry.disable_thumbnail_delay",
        "edge.disable_game_assist",
        "gaming.mode.enable",
        "basic.disable_game_bar",
        "basic.disable_uwp_background",
        "privacy.hide_most_used_apps",
        "privacy.disable_website_access_language_list",
        "peripheral.anti_mistouch",
        "basic.disable_edge_startup",
        "basic.disable_map_auto_update",
        "registry.disable_startup_sound",
        "edge.disable_game_assist_policy",
        "input.disable_pen_feedback",
        "registry.disable_low_disk_check",
        "input.disable_pointer_trails",
        "input.disable_snap_to",
        "peripheral.keyboard_response",
        "peripheral.mouse_precision",
        "input.disable_touch_feedback",
        "privacy.disable_handwriting_collection",
        "privacy.disable_advertising_id",
        "accessibility.disable_sticky_keys",
        "accessibility.disable_filter_keys",
        "accessibility.disable_toggle_keys",
        "windows.disable_tips",
        "windows.disable_suggested_apps",
        "fps.disable_fullscreen_optimizations",
        "privacy.deny_app_access_to_calendar",
        "privacy.deny_app_access_to_contacts",
        "privacy.deny_app_access_to_tasks",
        "privacy.deny_app_access_to_notifications",
        "privacy.feedback.frequency-zero",
        "explorer.hidden_files",
        "privacy.tailored_experiences",
        "network.disable_delivery_optimization",
        "network.ipv6_prefer_ipv4",
        "system.long_paths",
        "system.disable_wpbt",
        "explorer.end_task",
        "adjustments.disable_diagnostic_events",
        "privacy.disable_wifi_sense",
        "network.optimize_ttl",
        "network.optimize_tcp_max_dup_acks",
        "cpu.system_responsiveness",
        "basic.disable_store_auto_update",
        "adjustments.network_throttling_index",
        "privacy.disable_cortana",
        "system.disable_prefetch",
        "system.boost_game_priority",
        "cpu.enable_tsx",
        "privacy.disable_license_telemetry",
        "privacy.disable_compatibility_telemetry",
        "privacy.disable_privacy_consent",
        "power.restore_point_frequency",
        "power.s0_network_ac",
        "power.s0_network_dc",
        "privacy.deny_app_radio_access",
        "privacy.deny_app_access_to_messaging",
        "privacy.deny_app_access_to_email",
        "privacy.deny_app_access_to_account_info",
        "privacy.deny_app_access_to_diagnostic_info",
        "privacy.deny_app_access_to_phone_calls",
        "privacy.deny_app_access_to_call_history",
        "privacy.deny_app_access_to_trusted_devices",
        "privacy.deny_app_access_to_motion",
        "system.disable_hibernation",
        "system.prioritize_games",
        "network.optimize_dns",
        "privacy.disable_cloud_content",
        "privacy.disable_app_sync",
        "windows.disable_timeline",
        "network.disable_qos_scheduler",
        "network.disable_lmhosts",
        "windows.disable_clipboard_history",
        "windows.disable_focus_assist",
        "network.optimize_afd",
        "network.disable_throttling_complete",
        "memory.optimize_system_cache",
        "memory.optimize_priority",
        "audio.disable_enhancements",
        "audio.disable_spatial_sound",
        "audio.disable_service_restart",
        "audio.disable_comm_ducking",
        "input.optimize_raw_input_buffer",
        "memory.disable_superfetch",
        "audio.optimize_sample_rate",
        "audio.optimize_buffer",
        "audio.exclusive_mode_priority",
        "gpu.disable_dwm_animations",
        "gpu.disable_xbox_game_monitoring",
        "gpu.disable_vrr",
        "services.disable_diagtrack",
        "service.bluetooth.disable",
        "network.disable_tcp_auto_tuning",
        "network.disable_ecn_capability",
        "network.disable_rsc",
        "network.optimize_rss",
        "network.enable_tcp1323",
        "network.set_congestion_ctcp",
        "storage.disable_8dot3",
        "storage.optimize_io_page_lock",
        "storage.disable_ntfs_encryption",
        "gaming.dvr.disable",
        "network.disable_netbios",
        "network.disable_nagle",
        "network.disable_adapter_power_saving",
        "network.disable_flow_control",
        "network.disable_interrupt_moderation",
        "network.enable_lso",
        "network.optimize_adapter_buffers",
        "network.optimize_checksum_offload",
        "memory.enable_large_pages",
        "memory.optimize_pagefile",
        "gpu.disable_game_mode_optimizations",
        "gpu.force_high_performance_gpu",
        "gpu.set_sfio_priority",
        "basic.disable_uac",
        "basic.disable_security_notifications",
        "basic.disable_windows_update",
        "cpu.disable_spectre_meltdown",
        "security.disable_cfg",
        "security.disable_defender_realtime",
        "security.disable_hvci",
        "security.disable_sehop",
        "security.disable_smartscreen",
        "security.disable_vbs",
        "amd.radeon_anti_lag",
        "power.disable_usb_suspend",
        "power.disable_aspm",
        "power.processor_min_state_100",
        "power.perf_increase_threshold",
        "power.idle_demote_threshold",
        "power.perf_time_check",
        "power.class1_efficiency_threshold",
        "power.class1_perf_level",
        "power.disable_throttle",
        "power.hetero_thread_scheduling",
        "cpu.disable_core_parking",
        "cpu.processor_boost_mode",
        "cpu.disable_boost_mode",
        "cpu.disable_pstates",
        "intel.disable_cstates",
        "peripheral.usb.suspend",
        "services.enable_high_performance_mode",
        "tasks.disable_app_experience",
        "tasks.disable_auto_proxy_detection",
        "tasks.disable_ceip",
        "tasks.disable_cleanup_tasks",
        "tasks.disable_defragmentation",
        "tasks.disable_disk_diagnostic",
        "tasks.disable_ms_store_update_tasks",
        "tasks.disable_ms_sync_tasks",
        "tasks.disable_notification_tasks",
        "tasks.disable_policy_updates",
        "tasks.disable_remote_management",
        "tasks.disable_store_features",
        "tasks.disable_sync_features",
        "tasks.disable_windows_update",
        "tasks.disable_xbox_features",
        "tasks.disable_xbox_tasks",
        "memory.disable_compression",
        "cpu.disable_dynamic_tick",
        "cpu.configure_hpet",
        "cpu.use_platform_tick",
        "cpu.disable_tsc_sync",
        "device.disable_system_timer",
        "device.disable_umbus",
        "device.disable_composite_bus",
        "device.disable_hpet_device"
        // 注：cleanup.* 六项为 Owner 委托型编排操作（同 network.clear_dns_cache 先例），
        // 不注册 IChangeCapability，因此不进入本清单；由
        // FileShellEntryBoundaryTests 与 OrchestratedFileCleanupOperationTests 覆盖。
    ];

    [Fact]
    public void Provider_ContainsEachMigratedOperationExactlyOnceThroughOrchestratedAdapter()
    {
        var operations = new BuiltInOptimizationOperationProvider()
            .GetModules()
            .SelectMany(module => module.Operations)
            .ToArray();

        foreach (var id in MigratedIds)
        {
            var operation = Assert.Single(operations, candidate => candidate.Id == id);
            Assert.IsAssignableFrom<OrchestratedChangeOperation>(operation);
        }

        var gameDvr = Assert.Single(operations, candidate => candidate.Id == "gpu.disable_game_dvr_complete");
        Assert.IsAssignableFrom<OrchestratedChangeOperation>(gameDvr);
        Assert.True(gameDvr.RequiresExplicitConfirmation);
        Assert.False(gameDvr.RequiresAdmin);

        var ceip = Assert.Single(operations, candidate => candidate.Id == "privacy.disable_ceip");
        Assert.IsAssignableFrom<OrchestratedChangeOperation>(ceip);
        Assert.True(ceip.RequiresAdmin);
        Assert.True(ceip.RequiresExplicitConfirmation);

        var feedback = Assert.Single(operations, candidate => candidate.Id == "privacy.disable_feedback");
        Assert.IsAssignableFrom<OrchestratedChangeOperation>(feedback);
        Assert.True(feedback.RequiresAdmin);
        Assert.True(feedback.RequiresExplicitConfirmation);

        var usbPolling = Assert.Single(operations, candidate => candidate.Id == "input.optimize_usb_polling");
        Assert.IsAssignableFrom<OrchestratedChangeOperation>(usbPolling);
        Assert.True(usbPolling.RequiresAdmin);
        Assert.True(usbPolling.RequiresExplicitConfirmation);

        var upnp = Assert.Single(operations, candidate => candidate.Id == "privacy.disable_upnp_sharing");
        Assert.IsAssignableFrom<OrchestratedChangeOperation>(upnp);
        Assert.True(upnp.RequiresAdmin);
        Assert.True(upnp.RequiresExplicitConfirmation);

        var sensors = Assert.Single(operations, candidate => candidate.Id == "privacy.disable_sensor_services");
        Assert.IsAssignableFrom<OrchestratedChangeOperation>(sensors);
        Assert.True(sensors.RequiresAdmin);
        Assert.True(sensors.RequiresExplicitConfirmation);

        var xbox = Assert.Single(operations, candidate => candidate.Id == "services.disable_xbox_services");
        Assert.IsAssignableFrom<OrchestratedChangeOperation>(xbox);
        Assert.True(xbox.RequiresAdmin);
        Assert.True(xbox.RequiresExplicitConfirmation);


        foreach (var id in new[]
                 {
                     "basic.disable_game_bar",
                     "basic.disable_uwp_background",
                     "privacy.hide_most_used_apps",
                     "privacy.disable_website_access_language_list",
                     "peripheral.anti_mistouch",
                     "basic.disable_edge_startup",
                     "basic.disable_map_auto_update",
                     "registry.disable_startup_sound",
                     "edge.disable_game_assist_policy",
                     "input.disable_pen_feedback",
                     "registry.disable_low_disk_check",
                     "input.disable_pointer_trails",
                     "input.disable_snap_to",
                     "peripheral.keyboard_response",
                     "peripheral.mouse_precision",
                     "input.disable_touch_feedback",
                     "privacy.disable_handwriting_collection",
                     "privacy.disable_advertising_id",
                     "accessibility.disable_sticky_keys",
                     "accessibility.disable_filter_keys",
                     "accessibility.disable_toggle_keys",
                     "windows.disable_tips",
                     "windows.disable_suggested_apps",
                     "fps.disable_fullscreen_optimizations",
                     "privacy.deny_app_access_to_calendar",
                     "privacy.deny_app_access_to_contacts",
                     "privacy.deny_app_access_to_tasks",
                     "privacy.deny_app_access_to_notifications",
                     "privacy.feedback.frequency-zero",
                     "explorer.hidden_files",
                     "explorer.end_task",
                     "privacy.tailored_experiences",
                     "network.disable_delivery_optimization",
                     "adjustments.disable_diagnostic_events",
                     "privacy.disable_wifi_sense",
                     "network.optimize_ttl",
                     "network.optimize_tcp_max_dup_acks",
                     "cpu.system_responsiveness",
                     "basic.disable_store_auto_update",
                     "adjustments.network_throttling_index",
                     "privacy.disable_cortana",
                     "system.disable_prefetch"
                 })
        {
            var operation = Assert.Single(operations, candidate => candidate.Id == id);
            Assert.True(operation.RequiresExplicitConfirmation);
        }

        Assert.Equal(
            "REG_SZ",
            Assert.Single(operations, operation => operation.Id == "input.disable_pointer_trails").ValueType);
        Assert.Equal(
            "REG_SZ",
            Assert.Single(operations, operation => operation.Id == "input.disable_snap_to").ValueType);
        foreach (var id in new[]
                 {
                     "privacy.deny_app_access_to_calendar",
                     "privacy.deny_app_access_to_contacts",
                     "privacy.deny_app_access_to_tasks",
                     "privacy.deny_app_access_to_notifications",
                     "privacy.deny_app_radio_access",
                     "privacy.deny_app_access_to_messaging",
                     "privacy.deny_app_access_to_email",
                     "privacy.deny_app_access_to_account_info",
                     "privacy.deny_app_access_to_diagnostic_info",
                     "privacy.deny_app_access_to_phone_calls",
                     "privacy.deny_app_access_to_call_history",
                     "privacy.deny_app_access_to_trusted_devices",
                     "privacy.deny_app_access_to_motion"
                 })
        {
            Assert.Equal("REG_SZ", Assert.Single(operations, operation => operation.Id == id).ValueType);
        }
        Assert.Equal(
            "REGISTRY VALUE SET",
            Assert.Single(operations, operation => operation.Id == "peripheral.keyboard_response").ValueType);
        Assert.Equal(
            "REGISTRY VALUE SET",
            Assert.Single(operations, operation => operation.Id == "peripheral.mouse_precision").ValueType);
        Assert.Equal(
            "REGISTRY VALUE SET",
            Assert.Single(operations, operation => operation.Id == "input.disable_touch_feedback").ValueType);
        Assert.Equal(
            "REGISTRY VALUE SET",
            Assert.Single(operations, operation => operation.Id == "privacy.disable_handwriting_collection").ValueType);
        Assert.Equal(
            "REGISTRY VALUE SET",
            Assert.Single(operations, operation => operation.Id == "privacy.disable_advertising_id").ValueType);
    }

    [Fact]
    public void Infrastructure_RegistersOneCapabilityForEachMigratedId()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddGameOptimizerWindowsInfrastructure();
        using var provider = services.BuildServiceProvider();
        var capabilities = provider.GetServices<IChangeCapability>().ToArray();

        foreach (var id in MigratedIds)
        {
            Assert.Single(capabilities, capability => capability.Descriptor.CapabilityId == id);
        }


        foreach (var id in new[]
                 {
                     "basic.disable_game_bar",
                     "basic.disable_uwp_background",
                     "privacy.hide_most_used_apps",
                     "privacy.disable_website_access_language_list",
                     "peripheral.anti_mistouch",
                     "basic.disable_edge_startup",
                     "basic.disable_map_auto_update",
                     "registry.disable_startup_sound",
                     "edge.disable_game_assist_policy",
                     "input.disable_pen_feedback",
                     "registry.disable_low_disk_check",
                     "input.disable_pointer_trails",
                     "input.disable_snap_to",
                     "peripheral.keyboard_response",
                     "peripheral.mouse_precision",
                     "input.disable_touch_feedback",
                     "privacy.disable_handwriting_collection",
                     "privacy.disable_advertising_id",
                     "accessibility.disable_sticky_keys",
                     "accessibility.disable_filter_keys",
                     "accessibility.disable_toggle_keys",
                     "windows.disable_tips",
                     "windows.disable_suggested_apps",
                     "fps.disable_fullscreen_optimizations",
                     "privacy.deny_app_access_to_calendar",
                     "privacy.deny_app_access_to_contacts",
                     "privacy.deny_app_access_to_tasks",
                     "privacy.deny_app_access_to_notifications",
                     "privacy.deny_app_radio_access",
                     "privacy.deny_app_access_to_messaging",
                     "privacy.deny_app_access_to_email",
                     "privacy.deny_app_access_to_account_info",
                     "privacy.deny_app_access_to_diagnostic_info",
                     "privacy.deny_app_access_to_phone_calls",
                     "privacy.deny_app_access_to_call_history",
                     "privacy.deny_app_access_to_trusted_devices",
                     "privacy.deny_app_access_to_motion"
                 })
        {
            var capability = Assert.Single(
                capabilities,
                candidate => candidate.Descriptor.CapabilityId == id);
            Assert.True(capability.Descriptor.RequiresExplicitConfirmation);
        }

        foreach (var id in new[]
                 {
                     "input.disable_pen_feedback",
                     "registry.disable_low_disk_check",
                     "input.disable_pointer_trails",
                     "input.disable_snap_to",
                     "peripheral.keyboard_response",
                     "peripheral.mouse_precision",
                     "input.disable_touch_feedback",
                     "privacy.disable_handwriting_collection",
                     "privacy.disable_advertising_id",
                     "accessibility.disable_sticky_keys",
                     "accessibility.disable_filter_keys",
                     "accessibility.disable_toggle_keys",
                     "windows.disable_tips",
                     "windows.disable_suggested_apps",
                     "fps.disable_fullscreen_optimizations",
                     "privacy.deny_app_access_to_calendar",
                     "privacy.deny_app_access_to_contacts",
                     "privacy.deny_app_access_to_tasks",
                     "privacy.deny_app_access_to_notifications",
                     "privacy.deny_app_radio_access",
                     "privacy.deny_app_access_to_messaging",
                     "privacy.deny_app_access_to_email",
                     "privacy.deny_app_access_to_account_info",
                     "privacy.deny_app_access_to_diagnostic_info",
                     "privacy.deny_app_access_to_phone_calls",
                     "privacy.deny_app_access_to_call_history",
                     "privacy.deny_app_access_to_trusted_devices",
                     "privacy.deny_app_access_to_motion"
                 })
        {
            var capability = Assert.Single(
                capabilities,
                candidate => candidate.Descriptor.CapabilityId == id);
            Assert.Equal(ChangeRisk.MediumRebootOrExperience, capability.Descriptor.Risk);
            Assert.False(capability.Descriptor.RequiresAdministrator);
            Assert.Contains(TargetScopeKind.CurrentUser, capability.Descriptor.SupportedScopes);
            Assert.DoesNotContain(TargetScopeKind.Machine, capability.Descriptor.SupportedScopes);
        }

        foreach (var id in new[]
                 {
                     "basic.disable_edge_startup",
                     "basic.disable_map_auto_update",
                     "registry.disable_startup_sound",
                     "edge.disable_game_assist_policy",
                     "network.disable_delivery_optimization",
                     "network.ipv6_prefer_ipv4",
                     "system.long_paths",
                     "system.disable_wpbt",
                     "adjustments.disable_diagnostic_events",
                     "privacy.disable_wifi_sense",
                     "network.optimize_ttl",
                     "network.optimize_tcp_max_dup_acks",
                     "cpu.system_responsiveness",
                     "basic.disable_store_auto_update",
                     "adjustments.network_throttling_index",
                     "privacy.disable_cortana",
                     "system.disable_prefetch",
                     "system.boost_game_priority",
                     "cpu.enable_tsx",
                     "privacy.disable_license_telemetry",
                     "privacy.disable_compatibility_telemetry",
                     "privacy.disable_privacy_consent",
                     "power.restore_point_frequency",
                     "power.s0_network_ac",
                     "power.s0_network_dc",
                     "system.disable_hibernation",
                     "system.prioritize_games",
                     "network.disable_tcp_auto_tuning",
                     "network.disable_ecn_capability",
                     "network.disable_rsc",
                     "network.optimize_rss",
                     "network.enable_tcp1323",
                     "network.disable_netbios",
                     "network.disable_nagle",
                     "network.disable_adapter_power_saving",
                     "network.disable_flow_control",
                     "network.disable_interrupt_moderation",
                     "network.enable_lso",
                     "network.optimize_adapter_buffers",
                     "network.optimize_checksum_offload",
                     "amd.radeon_anti_lag"
                 })
        {
            var capability = Assert.Single(
                capabilities,
                candidate => candidate.Descriptor.CapabilityId == id);
            Assert.True(capability.Descriptor.RequiresAdministrator);
            Assert.Contains(TargetScopeKind.Machine, capability.Descriptor.SupportedScopes);
        }

        foreach (var id in new[]
                 {
                     "power.disable_usb_suspend",
                     "power.disable_aspm",
                     "power.processor_min_state_100",
                     "power.perf_increase_threshold",
                     "power.idle_demote_threshold",
                     "power.perf_time_check",
                     "power.class1_efficiency_threshold",
                     "power.class1_perf_level",
                     "power.disable_throttle",
                     "power.hetero_thread_scheduling",
                     "cpu.disable_core_parking",
                     "cpu.processor_boost_mode",
                     "cpu.disable_boost_mode",
                     "cpu.disable_pstates",
                     "intel.disable_cstates",
                     "peripheral.usb.suspend",
                     "services.enable_high_performance_mode"
                 })
        {
            var capability = Assert.Single(
                capabilities,
                candidate => candidate.Descriptor.CapabilityId == id);
            Assert.True(capability.Descriptor.RequiresAdministrator);
            Assert.True(capability.Descriptor.RequiresExplicitConfirmation);
            Assert.Contains(TargetScopeKind.Machine, capability.Descriptor.SupportedScopes);
            Assert.DoesNotContain(TargetScopeKind.CurrentUser, capability.Descriptor.SupportedScopes);
        }

        foreach (var id in new[]
                 {
                     "tasks.disable_app_experience",
                     "tasks.disable_auto_proxy_detection",
                     "tasks.disable_ceip",
                     "tasks.disable_cleanup_tasks",
                     "tasks.disable_defragmentation",
                     "tasks.disable_disk_diagnostic",
                     "tasks.disable_ms_store_update_tasks",
                     "tasks.disable_ms_sync_tasks",
                     "tasks.disable_notification_tasks",
                     "tasks.disable_policy_updates",
                     "tasks.disable_remote_management",
                     "tasks.disable_store_features",
                     "tasks.disable_sync_features",
                     "tasks.disable_windows_update",
                     "tasks.disable_xbox_features",
                     "tasks.disable_xbox_tasks",
                     "memory.disable_compression",
                     "cpu.disable_dynamic_tick",
                     "cpu.configure_hpet",
                     "cpu.use_platform_tick",
                     "cpu.disable_tsc_sync",
                     "device.disable_system_timer",
                     "device.disable_umbus",
                     "device.disable_composite_bus",
                     "device.disable_hpet_device"
                 })
        {
            var capability = Assert.Single(
                capabilities,
                candidate => candidate.Descriptor.CapabilityId == id);
            Assert.True(capability.Descriptor.RequiresAdministrator);
            Assert.True(capability.Descriptor.RequiresExplicitConfirmation);
            Assert.True(capability.Descriptor.RequiresRecoveryMaterial);
            Assert.Contains(TargetScopeKind.Machine, capability.Descriptor.SupportedScopes);
            Assert.DoesNotContain(TargetScopeKind.CurrentUser, capability.Descriptor.SupportedScopes);
        }

        foreach (var id in new[]
                 {
                     "tasks.disable_cleanup_tasks",
                     "tasks.disable_policy_updates",
                     "tasks.disable_windows_update"
                 })
        {
            Assert.Equal(
                ChangeRisk.HighSystem,
                Assert.Single(capabilities, candidate => candidate.Descriptor.CapabilityId == id).Descriptor.Risk);
        }
    }
}
