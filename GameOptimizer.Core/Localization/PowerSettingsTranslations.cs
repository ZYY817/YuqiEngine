using System;
using System.Collections.Generic;

namespace GameOptimizer.Core.Localization;

public static class PowerSettingsTranslations
{
    // 子组 GUID 到翻译键的映射
    private static readonly Dictionary<Guid, string> SubgroupKeys = new()
    {
        [Guid.Parse("fea3413e-7e05-4911-9a71-700331f1c294")] = "PowerSubgroup.Ungrouped",
        [Guid.Parse("0012ee47-9041-4b5d-9b77-535fba8b1442")] = "PowerSubgroup.HardDisk",
        [Guid.Parse("02f815b5-a5cf-4c84-bf20-649d1f75d3d8")] = "PowerSubgroup.InternetExplorer",
        [Guid.Parse("0d7dbae2-4294-402a-ba8e-26777e8488cd")] = "PowerSubgroup.DesktopBackground",
        [Guid.Parse("19cbb8fa-5279-450e-9fac-8a3d5fedd0c1")] = "PowerSubgroup.WirelessAdapter",
        [Guid.Parse("238c9fa8-0aad-41ed-83f4-97be242c8f20")] = "PowerSubgroup.Sleep",
        [Guid.Parse("2a737441-1930-4402-8d77-b2bebba308a3")] = "PowerSubgroup.Usb",
        [Guid.Parse("2e601130-5351-4d9d-8e04-252966bad054")] = "PowerSubgroup.IdleResiliency",
        [Guid.Parse("48672f38-7a9a-4bb2-8bf8-3d85be19de4e")] = "PowerSubgroup.InterruptRouting",
        [Guid.Parse("4f971e89-eebd-4455-a8de-9e59040e7347")] = "PowerSubgroup.ButtonsLid",
        [Guid.Parse("501a4d13-42af-4429-9fd1-a8218c268e20")] = "PowerSubgroup.PciExpress",
        [Guid.Parse("54533251-82be-4824-96c1-47b60b740d00")] = "PowerSubgroup.Processor",
        [Guid.Parse("5fb4938d-1ee8-4b0f-9a3c-5036b0ab995c")] = "PowerSubgroup.Graphics",
        [Guid.Parse("7516b95f-f776-4464-8c53-06167f40cc99")] = "PowerSubgroup.Display",
        [Guid.Parse("8619b916-e004-4dd8-9b66-dae86f806698")] = "PowerSubgroup.PresenceAware",
        [Guid.Parse("9596fb26-9850-41fd-ac3e-f7c3c00afd4b")] = "PowerSubgroup.Multimedia",
        [Guid.Parse("de830923-a562-41af-a086-e3a2c6bad2da")] = "PowerSubgroup.EnergySaver",
        [Guid.Parse("e73a048d-bf27-4f12-9731-8b2076e8891f")] = "PowerSubgroup.Battery",
    };

    // 设置 GUID 到翻译键的映射
    private static readonly Dictionary<Guid, string> SettingKeys = new()
    {
        // 不属于任何子组的设置
        [Guid.Parse("0e796bdb-100d-47d6-a2d5-f7d2daa51f51")] = "PowerSetting.RequirePasswordOnWake",
        [Guid.Parse("245d8541-3943-4422-b025-13a784f679b7")] = "PowerSetting.PowerPlanType",
        [Guid.Parse("4faab71a-92e5-4726-b531-224559672d19")] = "PowerSetting.DeviceIdlePolicy",
        [Guid.Parse("68afb2d9-ee95-47a8-8f50-4115088073b1")] = "PowerSetting.DisconnectedStandbyMode",
        [Guid.Parse("f15576e8-98b7-4186-b944-eafa664402d9")] = "PowerSetting.NetworkConnectivityInStandby",
        
        // 硬盘
        [Guid.Parse("51dea550-bb38-4bc4-991b-eacf37be5ec8")] = "PowerSetting.DiskMaxPowerLevel",
        [Guid.Parse("6738e2c4-e8a5-4a42-b16a-e040e769756e")] = "PowerSetting.DiskTurnOffTime",
        [Guid.Parse("80e3c60e-bb94-4ad8-bbe0-0d3195efc663")] = "PowerSetting.DiskBurstIgnoreTime",
        
        // Internet Explorer
        [Guid.Parse("4c793e7d-a264-42e1-87d3-7a0d2f523ccd")] = "PowerSetting.JavaScriptTimerFrequency",
        
        // 桌面背景
        [Guid.Parse("309dce9b-bef4-4119-9921-a851fb12f0f4")] = "PowerSetting.DesktopSlideshow",
        
        // 无线适配器
        [Guid.Parse("12bbebe6-58d6-4636-95bb-3217ef867c1a")] = "PowerSetting.WirelessPowerSavingMode",

        // 睡眠
        [Guid.Parse("1a34bdc3-7e6b-442e-a9d0-64b6ef378e84")] = "PowerSetting.LegacyRtcMigration",
        [Guid.Parse("25dfa149-5dd1-4736-b5ab-e8a37b5b8187")] = "PowerSetting.AllowAwayMode",
        [Guid.Parse("29f6c1db-86da-48c5-9fdb-f2b67b1f44da")] = "PowerSetting.StandbyTimeout",
        [Guid.Parse("7bc4a2f9-d8fc-4469-b07b-33eb785aaca0")] = "PowerSetting.UnattendedSleepTimeout",
        [Guid.Parse("94ac6d29-73ce-41a6-809f-6363ba21b47e")] = "PowerSetting.AllowHybridSleep",
        [Guid.Parse("9d7815a6-7ee4-497e-8888-515a05f02364")] = "PowerSetting.HibernateTimeout",
        [Guid.Parse("a4b195f5-8225-47d8-8012-9d41369786e2")] = "PowerSetting.AllowSystemRequiredPolicy",
        [Guid.Parse("abfc2519-3608-4c2a-94ea-171b0ed546ab")] = "PowerSetting.AllowStandbyStates",
        [Guid.Parse("bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d")] = "PowerSetting.AllowWakeTimers",
        [Guid.Parse("d4c1d4c8-d5cc-43d3-b83e-fc51215cb04d")] = "PowerSetting.AllowSleepWithRemoteOpens",
        
        // USB
        [Guid.Parse("48e6b7a6-50f5-4782-a5d4-53bb8f07e226")] = "PowerSetting.UsbSelectiveSuspend",
        
        // 空闲复原
        [Guid.Parse("3166bc41-7e98-4e03-b34e-ec0f5f2b218e")] = "PowerSetting.ExecutionRequiredTimeout",
        [Guid.Parse("c36f0eb4-2988-4a70-8eee-0884fc2c2433")] = "PowerSetting.IoCoalescingTimeout",
        [Guid.Parse("c42b79aa-aa3a-484b-a98f-2cf32aa90a28")] = "PowerSetting.ProcessorIdleResiliencyTimer",
        [Guid.Parse("d502f7ee-1dc7-4efd-a55d-f04b6f5c0545")] = "PowerSetting.DeepSleepEnabled",
        
        // 中断路由控制
        [Guid.Parse("2bfc24f9-5ea2-4801-8213-3dbae01aa39d")] = "PowerSetting.InterruptRoutingMode",
        [Guid.Parse("73cde64d-d720-4bb2-a860-c755afe77ef2")] = "PowerSetting.InterruptTargetLoad",
        [Guid.Parse("d6ba4903-386f-4c2c-8adb-5c21b3328d25")] = "PowerSetting.InterruptTimeTrigger",

        // 电源按钮和盖子
        [Guid.Parse("5ca83367-6e45-459f-a27b-476b1d01c936")] = "PowerSetting.LidCloseAction",
        [Guid.Parse("7648efa3-dd9c-4e3e-b566-50f929386280")] = "PowerSetting.PowerButtonAction",
        [Guid.Parse("833a6b62-dfa4-46d1-82f8-e09e34d029d6")] = "PowerSetting.EnableForcedShutdown",
        [Guid.Parse("96996bc0-ad50-47ec-923b-6f41874dd9eb")] = "PowerSetting.SleepButtonAction",
        [Guid.Parse("99ff10e7-23b1-4c07-a9d1-5c3206d741b4")] = "PowerSetting.LidOpenAction",
        [Guid.Parse("a7066653-8d6c-40a8-910e-a1f54b84c7e5")] = "PowerSetting.StartMenuPowerButton",
        
        // PCI Express
        [Guid.Parse("ee12f906-d277-404b-b6da-e5fa1a576df5")] = "PowerSetting.PcieLinkState",
        
        // 图形设置
        [Guid.Parse("dd848b2a-8a5d-4451-9ae2-39cd41658f6c")] = "PowerSetting.GpuPreference",
        
        // 显示
        [Guid.Parse("17aaa29b-8b43-4b94-aafe-35f64daaf1ee")] = "PowerSetting.DisplayDimTimeout",
        [Guid.Parse("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e")] = "PowerSetting.DisplayTimeout",
        [Guid.Parse("8ec4b3a5-6868-48c2-be75-4f3044be88a7")] = "PowerSetting.ConsoleLockDisplayTimeout",
        [Guid.Parse("90959d22-d6a1-49b9-af93-bce885ad335b")] = "PowerSetting.AdaptiveDisplay",
        [Guid.Parse("a9ceb8da-cd46-44fb-a98b-02af69de4623")] = "PowerSetting.AllowDisplayRequired",
        [Guid.Parse("aded5e82-b909-4619-9949-f5d71dac0bcb")] = "PowerSetting.DisplayBrightness",
        [Guid.Parse("f1fbfde2-a960-4165-9f88-50667911ce96")] = "PowerSetting.DisplayDimBrightness",
        [Guid.Parse("fbd9aa66-9553-4097-ba44-ed6e9d65eab8")] = "PowerSetting.DisplayAdaptiveBrightness",

        // 状态感知电源操作
        [Guid.Parse("468fe7e5-1158-46ec-88bc-5b96c9e44fd0")] = "PowerSetting.StandbyReserveTime",
        [Guid.Parse("49cb11a5-56e2-4afb-9d38-3df47872e21b")] = "PowerSetting.StandbyResetPercent",
        [Guid.Parse("5adbbfbc-074e-4da1-ba38-db8b36b2c8f3")] = "PowerSetting.NonSensorInputTimeout",
        [Guid.Parse("60c07fe1-0556-45cf-9903-d56e32210242")] = "PowerSetting.StandbyBudgetGracePeriod",
        [Guid.Parse("82011705-fb95-4d46-8d35-4042b1d20def")] = "PowerSetting.UserPresencePrediction",
        [Guid.Parse("9fe527be-1b70-48da-930d-7bcf17b44990")] = "PowerSetting.StandbyBudgetPercent",
        [Guid.Parse("c763ee92-71e8-4127-84eb-f6ed043a3e3d")] = "PowerSetting.StandbyReserveGracePeriod",
        
        // 多媒体设置
        [Guid.Parse("03680956-93bc-4294-bba6-4e0f09bb717f")] = "PowerSetting.WhenSharingMedia",
        [Guid.Parse("10778347-1370-4ee0-8bbd-33bdacaade49")] = "PowerSetting.VideoPlaybackQuality",
        [Guid.Parse("34c7b99f-9a6d-4b3c-8dc7-b6693b78cef4")] = "PowerSetting.WhenPlayingVideo",
        
        // 节能模式设置
        [Guid.Parse("13d09884-f74e-474a-a852-b6bde8ad03a8")] = "PowerSetting.EnergySaverBrightnessWeight",
        [Guid.Parse("5c5bb349-ad29-4ee2-9d0b-2b25270f7a81")] = "PowerSetting.EnergySaverPolicy",
        [Guid.Parse("e69653ca-cf7f-4f05-aa73-cb833fa90ad4")] = "PowerSetting.EnergySaverBatteryLevel",
        
        // 电池
        [Guid.Parse("5dbb7c9f-38e9-40d2-9749-4f8a0e9f640f")] = "PowerSetting.LowBatteryNotification",
        [Guid.Parse("637ea02f-bbcb-4015-8e2c-a1c7b9c0b546")] = "PowerSetting.CriticalBatteryAction",
        [Guid.Parse("8183ba9a-e910-48da-8769-14ae6dc1170a")] = "PowerSetting.LowBatteryLevel",
        [Guid.Parse("9a66d8d7-4ff7-4ef9-b5a2-5a326ca2a469")] = "PowerSetting.CriticalBatteryLevel",
        [Guid.Parse("bcded951-187b-4d05-bccc-f7e51960c258")] = "PowerSetting.LowBatteryNotificationOn",
        [Guid.Parse("d8742dcb-3e6a-4b3c-b3fe-374623cdcf06")] = "PowerSetting.LowBatteryAction",
        [Guid.Parse("f3c5027d-cd16-4930-aa6b-90db844a8f00")] = "PowerSetting.ReserveBatteryLevel",

        // 处理器电源管理
        [Guid.Parse("06cadf0e-64ed-448a-8927-ce7bf90eb35d")] = "PowerSetting.PerfIncreaseThreshold",
        [Guid.Parse("06cadf0e-64ed-448a-8927-ce7bf90eb35e")] = "PowerSetting.PerfIncreaseThresholdClass1",
        [Guid.Parse("0cc5b647-c1df-4637-891a-dec35c318583")] = "PowerSetting.CoreParkingMinCores",
        [Guid.Parse("0cc5b647-c1df-4637-891a-dec35c318584")] = "PowerSetting.CoreParkingMinCoresClass1",
        [Guid.Parse("12a0ab44-fe28-4fa9-b3bd-4b64f44960a6")] = "PowerSetting.PerfDecreaseThreshold",
        [Guid.Parse("12a0ab44-fe28-4fa9-b3bd-4b64f44960a7")] = "PowerSetting.PerfDecreaseThresholdClass1",
        [Guid.Parse("1facfc65-a930-4bc5-9f38-504ec097bbc0")] = "PowerSetting.InitialPerfClass1",
        [Guid.Parse("2430ab6f-a520-44a2-9601-f7f23b5134b1")] = "PowerSetting.CoreParkingConcurrencyThreshold",
        [Guid.Parse("2ddd5a84-5a71-437e-912a-db0b8c788732")] = "PowerSetting.CoreParkingIncreaseTime",
        [Guid.Parse("36687f9e-e3a5-4dbf-b1dc-15eb381c6863")] = "PowerSetting.EnergyPerfPreference",
        [Guid.Parse("3b04d4fd-1cc7-4f23-ab1c-d1337819c4bb")] = "PowerSetting.AllowThrottleStates",
        [Guid.Parse("4009efa7-e72d-4cba-9edf-91084ea8cbc3")] = "PowerSetting.PerfBoostTimeClass1",
        [Guid.Parse("40fbefc7-2e9d-4d25-a185-0cfd8574bac6")] = "PowerSetting.PerfDecreasePolicy",
        [Guid.Parse("40fbefc7-2e9d-4d25-a185-0cfd8574bac7")] = "PowerSetting.PerfDecreasePolicyClass1",
        [Guid.Parse("447235c7-6a8d-4cc0-8e24-9eaf70b96e2b")] = "PowerSetting.CoreParkingDisabledStates",
        [Guid.Parse("447235c7-6a8d-4cc0-8e24-9eaf70b96e2c")] = "PowerSetting.CoreParkingDisabledStatesClass1",
        [Guid.Parse("45bcc044-d885-43e2-8605-ee0ec6e96b59")] = "PowerSetting.PerfBoostPolicy",
        [Guid.Parse("465e1f50-b610-473a-ab58-00d1077dc418")] = "PowerSetting.PerfIncreasePolicy",
        [Guid.Parse("465e1f50-b610-473a-ab58-00d1077dc419")] = "PowerSetting.PerfIncreasePolicyClass1",
        [Guid.Parse("4b92d758-5a24-4851-a470-815d78aee119")] = "PowerSetting.IdleDemoteThreshold",
        [Guid.Parse("4bdaf4e9-d103-46d7-a5f0-6280121616ef")] = "PowerSetting.CoreParkingDistributeThreshold",
        [Guid.Parse("4d2b0152-7d5c-498b-88e2-34345392a2c5")] = "PowerSetting.PerfTimeCheck",
        [Guid.Parse("4e4450b3-6179-4e91-b8f1-5bb9938f81a1")] = "PowerSetting.ProcessorBusy",
        [Guid.Parse("5d76a2ca-e8c0-402f-a133-2158492d58ad")] = "PowerSetting.IdleDisable",
        [Guid.Parse("616cdaa5-695e-4545-97ad-97dc2d1bdd88")] = "PowerSetting.LatencyMinUnparkedCores",
        [Guid.Parse("616cdaa5-695e-4545-97ad-97dc2d1bdd89")] = "PowerSetting.LatencyMinUnparkedCoresClass1",
        [Guid.Parse("619b7505-003b-4e82-b7a6-4dd29c300971")] = "PowerSetting.LatencyPerfHint",
        [Guid.Parse("619b7505-003b-4e82-b7a6-4dd29c300972")] = "PowerSetting.LatencyPerfHintClass1",
        [Guid.Parse("6c2993b0-8f48-481f-bcc6-00dd2742aa06")] = "PowerSetting.IdleThresholdScaling",
        [Guid.Parse("71021b41-c749-4d21-be74-a00f335d582b")] = "PowerSetting.CoreParkingDecreasePolicy",
        [Guid.Parse("75b0ae3f-bce0-45a7-8c89-c9611c25e100")] = "PowerSetting.MaxProcessorFrequency",
        [Guid.Parse("75b0ae3f-bce0-45a7-8c89-c9611c25e101")] = "PowerSetting.MaxProcessorFrequencyClass1",
        [Guid.Parse("7b224883-b3cc-4d79-819f-8374152cbe7c")] = "PowerSetting.IdlePromoteThreshold",
        [Guid.Parse("7d24baa7-0b84-480f-840c-1b0743c00f5f")] = "PowerSetting.PerfHistoryCount",
        [Guid.Parse("7d24baa7-0b84-480f-840c-1b0743c00f60")] = "PowerSetting.PerfHistoryCountClass1",
        [Guid.Parse("7f2492b6-60b1-45e5-ae55-773f8cd5caec")] = "PowerSetting.PerfDecreaseTimeClass1",
        [Guid.Parse("7f2f5cfa-f10c-4823-b5e1-e93ae85f46b5")] = "PowerSetting.HeterogeneousPolicy",
        [Guid.Parse("893dee8e-2bef-41e0-89c6-b55d0929964c")] = "PowerSetting.MinProcessorState",
        [Guid.Parse("893dee8e-2bef-41e0-89c6-b55d0929964d")] = "PowerSetting.MinProcessorStateClass1",
        [Guid.Parse("8baa4a8a-14c6-4451-8e8b-14bdbd197537")] = "PowerSetting.ProcessorAutonomousMode",
        [Guid.Parse("93b8b6dc-0698-4d1c-9ee4-0644e900c85d")] = "PowerSetting.HeterogeneousThreadPolicy",
        [Guid.Parse("943c8cb6-6f93-4227-ad87-e9a3feec08d1")] = "PowerSetting.CoreParkingOverUtilThreshold",
        [Guid.Parse("984cf492-3bed-4488-a8f9-4286c97bf5aa")] = "PowerSetting.PerfBoostTime",
        [Guid.Parse("984cf492-3bed-4488-a8f9-4286c97bf5ab")] = "PowerSetting.PerfBoostTimeClass1Alt",
        [Guid.Parse("9943e905-9a30-4ec1-9b99-44dd3b76f7a2")] = "PowerSetting.IdleStateMax",
        [Guid.Parse("b000397d-9b0b-483d-98c9-692a6060cfbf")] = "PowerSetting.PerfLevelIncreaseThresholdClass1",
        [Guid.Parse("bae08b81-2d5e-4688-ad6a-13243356654b")] = "PowerSetting.HeterogeneousShortThread",
        [Guid.Parse("bc5038f7-23e0-4960-96da-33abaf5935ec")] = "PowerSetting.MaxProcessorState",
        [Guid.Parse("bc5038f7-23e0-4960-96da-33abaf5935ed")] = "PowerSetting.MaxProcessorStateClass1",
        [Guid.Parse("be337238-0d82-4146-a960-4f3749d470c7")] = "PowerSetting.BoostMode",
        [Guid.Parse("c4581c31-89ab-4597-8e2b-9c9cab440e6b")] = "PowerSetting.IdleTimeCheck",
        [Guid.Parse("c7be0679-2817-4d69-9d02-519a537ed0c6")] = "PowerSetting.CoreParkingIncreasePolicy",
        [Guid.Parse("cfeda3d0-7697-4566-a922-a9086cd49dfa")] = "PowerSetting.ProcessorAutonomousWindow",
        [Guid.Parse("d8edeb9b-95cf-4f95-a73c-b061973693c8")] = "PowerSetting.PerfDecreaseTime",
        [Guid.Parse("d8edeb9b-95cf-4f95-a73c-b061973693c9")] = "PowerSetting.PerfDecreaseTimeClass1Alt",
        [Guid.Parse("dfd10d17-d5eb-45dd-877a-9a34ddd15c82")] = "PowerSetting.CoreParkingDecreaseTime",
        [Guid.Parse("e0007330-f589-42ed-a401-5ddb10e785d3")] = "PowerSetting.CoreParkingUtilityDistribution",
        [Guid.Parse("ea062031-0e34-4ff1-9b6d-eb1059334028")] = "PowerSetting.CoreParkingMaxCores",
        [Guid.Parse("ea062031-0e34-4ff1-9b6d-eb1059334029")] = "PowerSetting.CoreParkingMaxCoresClass1",
        [Guid.Parse("f735a673-2066-4f80-a0c5-ddee0cf1bf5d")] = "PowerSetting.CoreParkingConcurrencyHeadroom",
        [Guid.Parse("f8861c27-95e7-475c-865b-13c0cb3f9d6b")] = "PowerSetting.PerfLevelDecreaseThresholdClass1",
        [Guid.Parse("fddc842b-8364-4edc-94cf-c17f60de1c80")] = "PowerSetting.SoftParkLatencyClass0",
    };

    // 枚举选项翻译
    private static readonly Dictionary<(Guid, int), string> EnumOptionKeys = new()
    {
        // 通用开关
        [(Guid.Parse("0e796bdb-100d-47d6-a2d5-f7d2daa51f51"), 0)] = "PowerEnum.Generic.No",
        [(Guid.Parse("0e796bdb-100d-47d6-a2d5-f7d2daa51f51"), 1)] = "PowerEnum.Generic.Yes",
        [(Guid.Parse("94ac6d29-73ce-41a6-809f-6363ba21b47e"), 0)] = "PowerEnum.Generic.Off",
        [(Guid.Parse("94ac6d29-73ce-41a6-809f-6363ba21b47e"), 1)] = "PowerEnum.Generic.On",
        [(Guid.Parse("d4c1d4c8-d5cc-43d3-b83e-fc51215cb04d"), 0)] = "PowerEnum.Generic.Off",
        [(Guid.Parse("d4c1d4c8-d5cc-43d3-b83e-fc51215cb04d"), 1)] = "PowerEnum.Generic.On",
        [(Guid.Parse("25dfa149-5dd1-4736-b5ab-e8a37b5b8187"), 0)] = "PowerEnum.Generic.Off",
        [(Guid.Parse("25dfa149-5dd1-4736-b5ab-e8a37b5b8187"), 1)] = "PowerEnum.Generic.On",
        [(Guid.Parse("abfc2519-3608-4c2a-94ea-171b0ed546ab"), 0)] = "PowerEnum.Generic.Off",
        [(Guid.Parse("abfc2519-3608-4c2a-94ea-171b0ed546ab"), 1)] = "PowerEnum.Generic.On",
        [(Guid.Parse("d502f7ee-1dc7-4efd-a55d-f04b6f5c0545"), 0)] = "PowerEnum.Generic.Off",
        [(Guid.Parse("d502f7ee-1dc7-4efd-a55d-f04b6f5c0545"), 1)] = "PowerEnum.Generic.On",
        [(Guid.Parse("fbd9aa66-9553-4097-ba44-ed6e9d65eab8"), 0)] = "PowerEnum.Generic.Off",
        [(Guid.Parse("fbd9aa66-9553-4097-ba44-ed6e9d65eab8"), 1)] = "PowerEnum.Generic.On",
        [(Guid.Parse("833a6b62-dfa4-46d1-82f8-e09e34d029d6"), 0)] = "PowerEnum.Generic.Off",
        [(Guid.Parse("833a6b62-dfa4-46d1-82f8-e09e34d029d6"), 1)] = "PowerEnum.Generic.On",
        [(Guid.Parse("bcded951-187b-4d05-bccc-f7e51960c258"), 0)] = "PowerEnum.Generic.Off",
        [(Guid.Parse("bcded951-187b-4d05-bccc-f7e51960c258"), 1)] = "PowerEnum.Generic.On",
        [(Guid.Parse("5dbb7c9f-38e9-40d2-9749-4f8a0e9f640f"), 0)] = "PowerEnum.Generic.Off",
        [(Guid.Parse("5dbb7c9f-38e9-40d2-9749-4f8a0e9f640f"), 1)] = "PowerEnum.Generic.On",

        // 设备空闲策略
        [(Guid.Parse("4faab71a-92e5-4726-b531-224559672d19"), 0)] = "PowerEnum.DeviceIdlePolicy.Performance",
        [(Guid.Parse("4faab71a-92e5-4726-b531-224559672d19"), 1)] = "PowerEnum.DeviceIdlePolicy.PowerSaving",
        
        // 断开连接待机模式
        [(Guid.Parse("68afb2d9-ee95-47a8-8f50-4115088073b1"), 0)] = "PowerEnum.DisconnectedStandby.Normal",
        [(Guid.Parse("68afb2d9-ee95-47a8-8f50-4115088073b1"), 1)] = "PowerEnum.DisconnectedStandby.Aggressive",
        
        // 网络连接性
        [(Guid.Parse("f15576e8-98b7-4186-b944-eafa664402d9"), 0)] = "PowerEnum.NetworkConnectivity.Disconnected",
        [(Guid.Parse("f15576e8-98b7-4186-b944-eafa664402d9"), 1)] = "PowerEnum.NetworkConnectivity.Enabled",
        [(Guid.Parse("f15576e8-98b7-4186-b944-eafa664402d9"), 2)] = "PowerEnum.NetworkConnectivity.ManagedByWindows",
        
        // 唤醒定时器
        [(Guid.Parse("bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d"), 0)] = "PowerEnum.WakeTimers.Disable",
        [(Guid.Parse("bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d"), 1)] = "PowerEnum.WakeTimers.Enable",
        [(Guid.Parse("bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d"), 2)] = "PowerEnum.WakeTimers.ImportantOnly",
        
        // USB 选择性暂停
        [(Guid.Parse("48e6b7a6-50f5-4782-a5d4-53bb8f07e226"), 0)] = "PowerEnum.UsbSuspend.Disabled",
        [(Guid.Parse("48e6b7a6-50f5-4782-a5d4-53bb8f07e226"), 1)] = "PowerEnum.UsbSuspend.Enabled",
        
        // 无线适配器节能模式
        [(Guid.Parse("12bbebe6-58d6-4636-95bb-3217ef867c1a"), 0)] = "PowerEnum.WirelessPowerSaving.MaxPerformance",
        [(Guid.Parse("12bbebe6-58d6-4636-95bb-3217ef867c1a"), 1)] = "PowerEnum.WirelessPowerSaving.LowPowerSaving",
        [(Guid.Parse("12bbebe6-58d6-4636-95bb-3217ef867c1a"), 2)] = "PowerEnum.WirelessPowerSaving.MediumPowerSaving",
        [(Guid.Parse("12bbebe6-58d6-4636-95bb-3217ef867c1a"), 3)] = "PowerEnum.WirelessPowerSaving.MaxPowerSaving",

        // 桌面幻灯片放映
        [(Guid.Parse("309dce9b-bef4-4119-9921-a851fb12f0f4"), 0)] = "PowerEnum.Slideshow.Available",
        [(Guid.Parse("309dce9b-bef4-4119-9921-a851fb12f0f4"), 1)] = "PowerEnum.Slideshow.Paused",
        
        // PCI Express 链接状态
        [(Guid.Parse("ee12f906-d277-404b-b6da-e5fa1a576df5"), 0)] = "PowerEnum.PcieLinkState.Off",
        [(Guid.Parse("ee12f906-d277-404b-b6da-e5fa1a576df5"), 1)] = "PowerEnum.PcieLinkState.Moderate",
        [(Guid.Parse("ee12f906-d277-404b-b6da-e5fa1a576df5"), 2)] = "PowerEnum.PcieLinkState.Maximum",
        
        // GPU 首选项
        [(Guid.Parse("dd848b2a-8a5d-4451-9ae2-39cd41658f6c"), 0)] = "PowerEnum.GpuPreference.Default",
        [(Guid.Parse("dd848b2a-8a5d-4451-9ae2-39cd41658f6c"), 1)] = "PowerEnum.GpuPreference.PowerSaving",
        [(Guid.Parse("dd848b2a-8a5d-4451-9ae2-39cd41658f6c"), 2)] = "PowerEnum.GpuPreference.HighPerformance",
        
        // 电源按钮操作
        [(Guid.Parse("7648efa3-dd9c-4e3e-b566-50f929386280"), 0)] = "PowerEnum.ButtonAction.DoNothing",
        [(Guid.Parse("7648efa3-dd9c-4e3e-b566-50f929386280"), 1)] = "PowerEnum.ButtonAction.Sleep",
        [(Guid.Parse("7648efa3-dd9c-4e3e-b566-50f929386280"), 2)] = "PowerEnum.ButtonAction.Hibernate",
        [(Guid.Parse("7648efa3-dd9c-4e3e-b566-50f929386280"), 3)] = "PowerEnum.ButtonAction.Shutdown",
        [(Guid.Parse("7648efa3-dd9c-4e3e-b566-50f929386280"), 4)] = "PowerEnum.ButtonAction.TurnOffDisplay",
        
        // 睡眠按钮操作
        [(Guid.Parse("96996bc0-ad50-47ec-923b-6f41874dd9eb"), 0)] = "PowerEnum.ButtonAction.DoNothing",
        [(Guid.Parse("96996bc0-ad50-47ec-923b-6f41874dd9eb"), 1)] = "PowerEnum.ButtonAction.Sleep",
        [(Guid.Parse("96996bc0-ad50-47ec-923b-6f41874dd9eb"), 2)] = "PowerEnum.ButtonAction.Hibernate",
        [(Guid.Parse("96996bc0-ad50-47ec-923b-6f41874dd9eb"), 3)] = "PowerEnum.ButtonAction.Shutdown",

        // 合上盖子操作
        [(Guid.Parse("5ca83367-6e45-459f-a27b-476b1d01c936"), 0)] = "PowerEnum.ButtonAction.DoNothing",
        [(Guid.Parse("5ca83367-6e45-459f-a27b-476b1d01c936"), 1)] = "PowerEnum.ButtonAction.Sleep",
        [(Guid.Parse("5ca83367-6e45-459f-a27b-476b1d01c936"), 2)] = "PowerEnum.ButtonAction.Hibernate",
        [(Guid.Parse("5ca83367-6e45-459f-a27b-476b1d01c936"), 3)] = "PowerEnum.ButtonAction.Shutdown",
        
        // 打开盖子操作
        [(Guid.Parse("99ff10e7-23b1-4c07-a9d1-5c3206d741b4"), 0)] = "PowerEnum.LidOpenAction.DoNothing",
        [(Guid.Parse("99ff10e7-23b1-4c07-a9d1-5c3206d741b4"), 1)] = "PowerEnum.LidOpenAction.TurnOnDisplay",
        
        // 开始菜单电源按钮
        [(Guid.Parse("a7066653-8d6c-40a8-910e-a1f54b84c7e5"), 0)] = "PowerEnum.StartMenuPower.Sleep",
        [(Guid.Parse("a7066653-8d6c-40a8-910e-a1f54b84c7e5"), 1)] = "PowerEnum.StartMenuPower.Hibernate",
        [(Guid.Parse("a7066653-8d6c-40a8-910e-a1f54b84c7e5"), 2)] = "PowerEnum.StartMenuPower.Shutdown",
        
        // 电池操作
        [(Guid.Parse("637ea02f-bbcb-4015-8e2c-a1c7b9c0b546"), 0)] = "PowerEnum.BatteryAction.DoNothing",
        [(Guid.Parse("637ea02f-bbcb-4015-8e2c-a1c7b9c0b546"), 1)] = "PowerEnum.BatteryAction.Sleep",
        [(Guid.Parse("637ea02f-bbcb-4015-8e2c-a1c7b9c0b546"), 2)] = "PowerEnum.BatteryAction.Hibernate",
        [(Guid.Parse("637ea02f-bbcb-4015-8e2c-a1c7b9c0b546"), 3)] = "PowerEnum.BatteryAction.Shutdown",
        [(Guid.Parse("d8742dcb-3e6a-4b3c-b3fe-374623cdcf06"), 0)] = "PowerEnum.BatteryAction.DoNothing",
        [(Guid.Parse("d8742dcb-3e6a-4b3c-b3fe-374623cdcf06"), 1)] = "PowerEnum.BatteryAction.Sleep",
        [(Guid.Parse("d8742dcb-3e6a-4b3c-b3fe-374623cdcf06"), 2)] = "PowerEnum.BatteryAction.Hibernate",
        [(Guid.Parse("d8742dcb-3e6a-4b3c-b3fe-374623cdcf06"), 3)] = "PowerEnum.BatteryAction.Shutdown",

        // 处理器性能提升模式
        [(Guid.Parse("be337238-0d82-4146-a960-4f3749d470c7"), 0)] = "PowerEnum.BoostMode.Disabled",
        [(Guid.Parse("be337238-0d82-4146-a960-4f3749d470c7"), 1)] = "PowerEnum.BoostMode.Enabled",
        [(Guid.Parse("be337238-0d82-4146-a960-4f3749d470c7"), 2)] = "PowerEnum.BoostMode.Aggressive",
        [(Guid.Parse("be337238-0d82-4146-a960-4f3749d470c7"), 3)] = "PowerEnum.BoostMode.EfficientEnabled",
        [(Guid.Parse("be337238-0d82-4146-a960-4f3749d470c7"), 4)] = "PowerEnum.BoostMode.EfficientAggressive",
        [(Guid.Parse("be337238-0d82-4146-a960-4f3749d470c7"), 5)] = "PowerEnum.BoostMode.AggressiveGuaranteed",
        [(Guid.Parse("be337238-0d82-4146-a960-4f3749d470c7"), 6)] = "PowerEnum.BoostMode.EfficientAggressiveGuaranteed",

        // 处理器性能提升策略
        [(Guid.Parse("45bcc044-d885-43e2-8605-ee0ec6e96b59"), 0)] = "PowerEnum.BoostPolicy.Disabled",
        [(Guid.Parse("45bcc044-d885-43e2-8605-ee0ec6e96b59"), 100)] = "PowerEnum.BoostPolicy.Enabled",

        // 处理器性能提升/降低策略
        [(Guid.Parse("465e1f50-b610-473a-ab58-00d1077dc418"), 0)] = "PowerEnum.PerfPolicy.Ideal",
        [(Guid.Parse("465e1f50-b610-473a-ab58-00d1077dc418"), 1)] = "PowerEnum.PerfPolicy.Single",
        [(Guid.Parse("465e1f50-b610-473a-ab58-00d1077dc418"), 2)] = "PowerEnum.PerfPolicy.Rocket",
        [(Guid.Parse("465e1f50-b610-473a-ab58-00d1077dc418"), 3)] = "PowerEnum.PerfPolicy.IdealAggressive",
        [(Guid.Parse("40fbefc7-2e9d-4d25-a185-0cfd8574bac6"), 0)] = "PowerEnum.PerfPolicy.Ideal",
        [(Guid.Parse("40fbefc7-2e9d-4d25-a185-0cfd8574bac6"), 1)] = "PowerEnum.PerfPolicy.Single",
        [(Guid.Parse("40fbefc7-2e9d-4d25-a185-0cfd8574bac6"), 2)] = "PowerEnum.PerfPolicy.Rocket",

        // 异构策略
        [(Guid.Parse("7f2f5cfa-f10c-4823-b5e1-e93ae85f46b5"), 0)] = "PowerEnum.HeterogeneousPolicy.Auto",
        [(Guid.Parse("7f2f5cfa-f10c-4823-b5e1-e93ae85f46b5"), 1)] = "PowerEnum.HeterogeneousPolicy.PreferEfficient",
        [(Guid.Parse("7f2f5cfa-f10c-4823-b5e1-e93ae85f46b5"), 2)] = "PowerEnum.HeterogeneousPolicy.PreferPerformance",
        [(Guid.Parse("7f2f5cfa-f10c-4823-b5e1-e93ae85f46b5"), 3)] = "PowerEnum.HeterogeneousPolicy.Auto2",
        [(Guid.Parse("7f2f5cfa-f10c-4823-b5e1-e93ae85f46b5"), 4)] = "PowerEnum.HeterogeneousPolicy.EfficientOnly",

        // 异构短线程调度策略
        [(Guid.Parse("bae08b81-2d5e-4688-ad6a-13243356654b"), 0)] = "PowerEnum.HeterogeneousShortThread.Auto",
        [(Guid.Parse("bae08b81-2d5e-4688-ad6a-13243356654b"), 1)] = "PowerEnum.HeterogeneousShortThread.PreferEfficient",
        [(Guid.Parse("bae08b81-2d5e-4688-ad6a-13243356654b"), 2)] = "PowerEnum.HeterogeneousShortThread.PreferPerformance",
        [(Guid.Parse("bae08b81-2d5e-4688-ad6a-13243356654b"), 3)] = "PowerEnum.HeterogeneousShortThread.Auto2",
        [(Guid.Parse("bae08b81-2d5e-4688-ad6a-13243356654b"), 4)] = "PowerEnum.HeterogeneousShortThread.EfficientOnly",

        // 异构线程调度策略
        [(Guid.Parse("93b8b6dc-0698-4d1c-9ee4-0644e900c85d"), 0)] = "PowerEnum.HeterogeneousThreadPolicy.Auto",
        [(Guid.Parse("93b8b6dc-0698-4d1c-9ee4-0644e900c85d"), 1)] = "PowerEnum.HeterogeneousThreadPolicy.PreferEfficient",
        [(Guid.Parse("93b8b6dc-0698-4d1c-9ee4-0644e900c85d"), 2)] = "PowerEnum.HeterogeneousThreadPolicy.PreferPerformance",
        [(Guid.Parse("93b8b6dc-0698-4d1c-9ee4-0644e900c85d"), 3)] = "PowerEnum.HeterogeneousThreadPolicy.Auto2",
        [(Guid.Parse("93b8b6dc-0698-4d1c-9ee4-0644e900c85d"), 4)] = "PowerEnum.HeterogeneousThreadPolicy.EfficientOnly",

        // 处理器自主模式
        [(Guid.Parse("8baa4a8a-14c6-4451-8e8b-14bdbd197537"), 0)] = "PowerEnum.ProcessorAutonomous.Disabled",
        [(Guid.Parse("8baa4a8a-14c6-4451-8e8b-14bdbd197537"), 1)] = "PowerEnum.ProcessorAutonomous.Enabled",

        // 允许节流状态
        [(Guid.Parse("3b04d4fd-1cc7-4f23-ab1c-d1337819c4bb"), 0)] = "PowerEnum.ThrottleStates.Off",
        [(Guid.Parse("3b04d4fd-1cc7-4f23-ab1c-d1337819c4bb"), 1)] = "PowerEnum.ThrottleStates.On",
        [(Guid.Parse("3b04d4fd-1cc7-4f23-ab1c-d1337819c4bb"), 2)] = "PowerEnum.ThrottleStates.Automatic",

        // 核心停放增加/减少策略
        [(Guid.Parse("c7be0679-2817-4d69-9d02-519a537ed0c6"), 0)] = "PowerEnum.CoreParkingPolicy.Ideal",
        [(Guid.Parse("c7be0679-2817-4d69-9d02-519a537ed0c6"), 1)] = "PowerEnum.CoreParkingPolicy.Single",
        [(Guid.Parse("c7be0679-2817-4d69-9d02-519a537ed0c6"), 2)] = "PowerEnum.CoreParkingPolicy.Rocket",
        [(Guid.Parse("c7be0679-2817-4d69-9d02-519a537ed0c6"), 3)] = "PowerEnum.CoreParkingPolicy.IdealAggressive",
        [(Guid.Parse("71021b41-c749-4d21-be74-a00f335d582b"), 0)] = "PowerEnum.CoreParkingPolicy.Ideal",
        [(Guid.Parse("71021b41-c749-4d21-be74-a00f335d582b"), 1)] = "PowerEnum.CoreParkingPolicy.Single",
        [(Guid.Parse("71021b41-c749-4d21-be74-a00f335d582b"), 2)] = "PowerEnum.CoreParkingPolicy.Rocket",

        // 中断路由模式
        [(Guid.Parse("2bfc24f9-5ea2-4801-8213-3dbae01aa39d"), 0)] = "PowerEnum.InterruptRouting.Default",
        [(Guid.Parse("2bfc24f9-5ea2-4801-8213-3dbae01aa39d"), 1)] = "PowerEnum.InterruptRouting.AnyProcessor",
        [(Guid.Parse("2bfc24f9-5ea2-4801-8213-3dbae01aa39d"), 2)] = "PowerEnum.InterruptRouting.AnyUnparked",
        [(Guid.Parse("2bfc24f9-5ea2-4801-8213-3dbae01aa39d"), 3)] = "PowerEnum.InterruptRouting.AnyUnparkedIdle",
        [(Guid.Parse("2bfc24f9-5ea2-4801-8213-3dbae01aa39d"), 4)] = "PowerEnum.InterruptRouting.AnyUnparkedIdlePrefer",
        [(Guid.Parse("2bfc24f9-5ea2-4801-8213-3dbae01aa39d"), 5)] = "PowerEnum.InterruptRouting.LowestUnparked",
        [(Guid.Parse("2bfc24f9-5ea2-4801-8213-3dbae01aa39d"), 6)] = "PowerEnum.InterruptRouting.LowestUnparkedIdle",

        // 多媒体设置
        [(Guid.Parse("03680956-93bc-4294-bba6-4e0f09bb717f"), 0)] = "PowerEnum.WhenSharingMedia.AllowSleep",
        [(Guid.Parse("03680956-93bc-4294-bba6-4e0f09bb717f"), 1)] = "PowerEnum.WhenSharingMedia.PreventIdleSleep",
        [(Guid.Parse("03680956-93bc-4294-bba6-4e0f09bb717f"), 2)] = "PowerEnum.WhenSharingMedia.AllowAwayMode",
        [(Guid.Parse("10778347-1370-4ee0-8bbd-33bdacaade49"), 0)] = "PowerEnum.VideoPlaybackQuality.PowerSaving",
        [(Guid.Parse("10778347-1370-4ee0-8bbd-33bdacaade49"), 1)] = "PowerEnum.VideoPlaybackQuality.Balanced",
        [(Guid.Parse("10778347-1370-4ee0-8bbd-33bdacaade49"), 2)] = "PowerEnum.VideoPlaybackQuality.MaxQuality",
        [(Guid.Parse("34c7b99f-9a6d-4b3c-8dc7-b6693b78cef4"), 0)] = "PowerEnum.WhenPlayingVideo.OptimizePowerSaving",
        [(Guid.Parse("34c7b99f-9a6d-4b3c-8dc7-b6693b78cef4"), 1)] = "PowerEnum.WhenPlayingVideo.Balanced",
        [(Guid.Parse("34c7b99f-9a6d-4b3c-8dc7-b6693b78cef4"), 2)] = "PowerEnum.WhenPlayingVideo.OptimizeQuality",

        // 节能模式策略
        [(Guid.Parse("5c5bb349-ad29-4ee2-9d0b-2b25270f7a81"), 0)] = "PowerEnum.EnergySaverPolicy.Off",
        [(Guid.Parse("5c5bb349-ad29-4ee2-9d0b-2b25270f7a81"), 1)] = "PowerEnum.EnergySaverPolicy.On",

        // 自适应显示
        [(Guid.Parse("90959d22-d6a1-49b9-af93-bce885ad335b"), 0)] = "PowerEnum.Generic.Off",
        [(Guid.Parse("90959d22-d6a1-49b9-af93-bce885ad335b"), 1)] = "PowerEnum.Generic.On",

        // 允许显示所需
        [(Guid.Parse("a9ceb8da-cd46-44fb-a98b-02af69de4623"), 0)] = "PowerEnum.Generic.No",
        [(Guid.Parse("a9ceb8da-cd46-44fb-a98b-02af69de4623"), 1)] = "PowerEnum.Generic.Yes",

        // 用户存在预测
        [(Guid.Parse("82011705-fb95-4d46-8d35-4042b1d20def"), 0)] = "PowerEnum.Generic.Disabled",
        [(Guid.Parse("82011705-fb95-4d46-8d35-4042b1d20def"), 1)] = "PowerEnum.Generic.Enabled",

        // 空闲阈值缩放
        [(Guid.Parse("6c2993b0-8f48-481f-bcc6-00dd2742aa06"), 0)] = "PowerEnum.IdleThresholdScaling.Disabled",
        [(Guid.Parse("6c2993b0-8f48-481f-bcc6-00dd2742aa06"), 1)] = "PowerEnum.IdleThresholdScaling.Enabled",

        // 核心停放效用分布
        [(Guid.Parse("e0007330-f589-42ed-a401-5ddb10e785d3"), 0)] = "PowerEnum.UtilityDistribution.Disabled",
        [(Guid.Parse("e0007330-f589-42ed-a401-5ddb10e785d3"), 1)] = "PowerEnum.UtilityDistribution.Enabled",
    };

    public static string? GetSubgroupName(Guid guid)
    {
        if (SubgroupKeys.TryGetValue(guid, out var key))
        {
            return LocalizationService.Instance.Get(key);
        }
        return null;
    }

    public static string? GetSettingName(Guid guid)
    {
        if (SettingKeys.TryGetValue(guid, out var key))
        {
            return LocalizationService.Instance.Get(key);
        }
        return null;
    }

    public static string? GetEnumOptionName(Guid settingGuid, int index)
    {
        if (EnumOptionKeys.TryGetValue((settingGuid, index), out var key))
        {
            var result = LocalizationService.Instance.Get(key);
            return result;
        }
        return null;
    }
}
