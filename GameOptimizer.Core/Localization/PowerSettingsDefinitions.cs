using System;
using System.Collections.Generic;

namespace GameOptimizer.Core.Localization;

public enum PowerSettingType { Range, Enum }

public class PowerSettingDef
{
    public Guid Guid { get; init; }
    public string NameKey { get; init; } = "";
    public PowerSettingType Type { get; init; }
    public int Min { get; init; }
    public int Max { get; init; }
    public int Increment { get; init; } = 1;
    public string? UnitKey { get; init; }
    public List<(int Value, string Key)>? EnumOptions { get; init; }
}

public class PowerSubgroupDef
{
    public Guid Guid { get; init; }
    public string NameKey { get; init; } = "";
    public List<PowerSettingDef> Settings { get; init; } = new();
}

public static class PowerSettingsDefinitions
{
    public static readonly List<PowerSubgroupDef> Subgroups = new()
    {
        new PowerSubgroupDef
        {
            Guid = Guid.Parse("fea3413e-7e05-4911-9a71-700331f1c294"),
            NameKey = "PowerSubgroup.Ungrouped",
            Settings = new()
            {
                new() { Guid = Guid.Parse("0e796bdb-100d-47d6-a2d5-f7d2daa51f51"), NameKey = "PowerSetting.RequirePasswordOnWake", Type = PowerSettingType.Enum,
                    EnumOptions = new() { (0, "PowerEnum.No"), (1, "PowerEnum.Yes") } },
                new() { Guid = Guid.Parse("245d8541-3943-4422-b025-13a784f679b7"), NameKey = "PowerSetting.PowerPlanType", Type = PowerSettingType.Enum,
                    EnumOptions = new() { (0, "PowerEnum.PowerSaving"), (1, "PowerEnum.Balanced"), (2, "PowerEnum.HighPerformance") } },
                new() { Guid = Guid.Parse("4faab71a-92e5-4726-b531-224559672d19"), NameKey = "PowerSetting.DeviceIdlePolicy", Type = PowerSettingType.Enum,
                    EnumOptions = new() { (0, "PowerEnum.Performance"), (1, "PowerEnum.PowerSaving") } },
                new() { Guid = Guid.Parse("68afb2d9-ee95-47a8-8f50-4115088073b1"), NameKey = "PowerSetting.DisconnectedStandbyMode", Type = PowerSettingType.Enum,
                    EnumOptions = new() { (0, "PowerEnum.Normal"), (1, "PowerEnum.Aggressive") } },
                new() { Guid = Guid.Parse("f15576e8-98b7-4186-b944-eafa664402d9"), NameKey = "PowerSetting.NetworkConnectivityInStandby", Type = PowerSettingType.Enum,
                    EnumOptions = new() { (0, "PowerEnum.Disabled"), (1, "PowerEnum.Enabled"), (2, "PowerEnum.ManagedByWindows") } },
            }
        },
        new PowerSubgroupDef
        {
            Guid = Guid.Parse("0012ee47-9041-4b5d-9b77-535fba8b1442"),
            NameKey = "PowerSubgroup.HardDisk",
            Settings = new()
            {
                new() { Guid = Guid.Parse("6738e2c4-e8a5-4a42-b16a-e040e769756e"), NameKey = "PowerSetting.DiskTurnOffTime", Type = PowerSettingType.Range, Min = 0, Max = 7200, UnitKey = "PowerUnit.Seconds" },
                new() { Guid = Guid.Parse("80e3c60e-bb94-4ad8-bbe0-0d3195efc663"), NameKey = "PowerSetting.DiskBurstIgnoreTime", Type = PowerSettingType.Range, Min = 0, Max = 1000, UnitKey = "PowerUnit.Seconds" },
            }
        },
        new PowerSubgroupDef
        {
            Guid = Guid.Parse("238c9fa8-0aad-41ed-83f4-97be242c8f20"),
            NameKey = "PowerSubgroup.Sleep",
            Settings = new()
            {
                new() { Guid = Guid.Parse("29f6c1db-86da-48c5-9fdb-f2b67b1f44da"), NameKey = "PowerSetting.StandbyTimeout", Type = PowerSettingType.Range, Min = 0, Max = 86400, UnitKey = "PowerUnit.Seconds" },
                new() { Guid = Guid.Parse("9d7815a6-7ee4-497e-8888-515a05f02364"), NameKey = "PowerSetting.HibernateTimeout", Type = PowerSettingType.Range, Min = 0, Max = 86400, UnitKey = "PowerUnit.Seconds" },
                new() { Guid = Guid.Parse("94ac6d29-73ce-41a6-809f-6363ba21b47e"), NameKey = "PowerSetting.AllowHybridSleep", Type = PowerSettingType.Enum,
                    EnumOptions = new() { (0, "PowerEnum.Off"), (1, "PowerEnum.On") } },
                new() { Guid = Guid.Parse("bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d"), NameKey = "PowerSetting.AllowWakeTimers", Type = PowerSettingType.Enum,
                    EnumOptions = new() { (0, "PowerEnum.Disable"), (1, "PowerEnum.Enable"), (2, "PowerEnum.ImportantOnly") } },
                new() { Guid = Guid.Parse("25dfa149-5dd1-4736-b5ab-e8a37b5b8187"), NameKey = "PowerSetting.AllowAwayMode", Type = PowerSettingType.Enum,
                    EnumOptions = new() { (0, "PowerEnum.Off"), (1, "PowerEnum.On") } },
            }
        },
        new PowerSubgroupDef
        {
            Guid = Guid.Parse("7516b95f-f776-4464-8c53-06167f40cc99"),
            NameKey = "PowerSubgroup.Display",
            Settings = new()
            {
                new() { Guid = Guid.Parse("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e"), NameKey = "PowerSetting.DisplayTimeout", Type = PowerSettingType.Range, Min = 0, Max = 86400, UnitKey = "PowerUnit.Seconds" },
                new() { Guid = Guid.Parse("17aaa29b-8b43-4b94-aafe-35f64daaf1ee"), NameKey = "PowerSetting.DisplayDimTimeout", Type = PowerSettingType.Range, Min = 0, Max = 86400, UnitKey = "PowerUnit.Seconds" },
                new() { Guid = Guid.Parse("aded5e82-b909-4619-9949-f5d71dac0bcb"), NameKey = "PowerSetting.DisplayBrightness", Type = PowerSettingType.Range, Min = 0, Max = 100, UnitKey = "PowerUnit.Percent" },
                new() { Guid = Guid.Parse("f1fbfde2-a960-4165-9f88-50667911ce96"), NameKey = "PowerSetting.DisplayDimBrightness", Type = PowerSettingType.Range, Min = 0, Max = 100, UnitKey = "PowerUnit.Percent" },
                new() { Guid = Guid.Parse("fbd9aa66-9553-4097-ba44-ed6e9d65eab8"), NameKey = "PowerSetting.DisplayAdaptiveBrightness", Type = PowerSettingType.Enum,
                    EnumOptions = new() { (0, "PowerEnum.Off"), (1, "PowerEnum.On") } },
            }
        },
        new PowerSubgroupDef
        {
            Guid = Guid.Parse("54533251-82be-4824-96c1-47b60b740d00"),
            NameKey = "PowerSubgroup.Processor",
            Settings = new()
            {
                new() { Guid = Guid.Parse("893dee8e-2bef-41e0-89c6-b55d0929964c"), NameKey = "PowerSetting.MinProcessorState", Type = PowerSettingType.Range, Min = 0, Max = 100, UnitKey = "PowerUnit.Percent" },
                new() { Guid = Guid.Parse("bc5038f7-23e0-4960-96da-33abaf5935ec"), NameKey = "PowerSetting.MaxProcessorState", Type = PowerSettingType.Range, Min = 0, Max = 100, UnitKey = "PowerUnit.Percent" },
                new() { Guid = Guid.Parse("be337238-0d82-4146-a960-4f3749d470c7"), NameKey = "PowerSetting.BoostMode", Type = PowerSettingType.Enum,
                    EnumOptions = new() { (0, "PowerEnum.Disabled"), (1, "PowerEnum.Enabled"), (2, "PowerEnum.Aggressive"), (3, "PowerEnum.EfficientEnabled"), (4, "PowerEnum.EfficientAggressive") } },
                new() { Guid = Guid.Parse("36687f9e-e3a5-4dbf-b1dc-15eb381c6863"), NameKey = "PowerSetting.EnergyPerfPreference", Type = PowerSettingType.Range, Min = 0, Max = 100, UnitKey = "PowerUnit.Percent" },
                new() { Guid = Guid.Parse("0cc5b647-c1df-4637-891a-dec35c318583"), NameKey = "PowerSetting.CoreParkingMinCores", Type = PowerSettingType.Range, Min = 0, Max = 100, UnitKey = "PowerUnit.Percent" },
                new() { Guid = Guid.Parse("ea062031-0e34-4ff1-9b6d-eb1059334028"), NameKey = "PowerSetting.CoreParkingMaxCores", Type = PowerSettingType.Range, Min = 0, Max = 100, UnitKey = "PowerUnit.Percent" },
            }
        },
        new PowerSubgroupDef
        {
            Guid = Guid.Parse("4f971e89-eebd-4455-a8de-9e59040e7347"),
            NameKey = "PowerSubgroup.ButtonsLid",
            Settings = new()
            {
                new() { Guid = Guid.Parse("5ca83367-6e45-459f-a27b-476b1d01c936"), NameKey = "PowerSetting.LidCloseAction", Type = PowerSettingType.Enum,
                    EnumOptions = new() { (0, "PowerEnum.DoNothing"), (1, "PowerEnum.Sleep"), (2, "PowerEnum.Hibernate"), (3, "PowerEnum.Shutdown") } },
                new() { Guid = Guid.Parse("7648efa3-dd9c-4e3e-b566-50f929386280"), NameKey = "PowerSetting.PowerButtonAction", Type = PowerSettingType.Enum,
                    EnumOptions = new() { (0, "PowerEnum.DoNothing"), (1, "PowerEnum.Sleep"), (2, "PowerEnum.Hibernate"), (3, "PowerEnum.Shutdown") } },
                new() { Guid = Guid.Parse("96996bc0-ad50-47ec-923b-6f41874dd9eb"), NameKey = "PowerSetting.SleepButtonAction", Type = PowerSettingType.Enum,
                    EnumOptions = new() { (0, "PowerEnum.DoNothing"), (1, "PowerEnum.Sleep"), (2, "PowerEnum.Hibernate"), (3, "PowerEnum.Shutdown") } },
            }
        },
        new PowerSubgroupDef
        {
            Guid = Guid.Parse("501a4d13-42af-4429-9fd1-a8218c268e20"),
            NameKey = "PowerSubgroup.PciExpress",
            Settings = new()
            {
                new() { Guid = Guid.Parse("ee12f906-d277-404b-b6da-e5fa1a576df5"), NameKey = "PowerSetting.PcieLinkState", Type = PowerSettingType.Enum,
                    EnumOptions = new() { (0, "PowerEnum.Off"), (1, "PowerEnum.Moderate"), (2, "PowerEnum.Maximum") } },
            }
        },
        new PowerSubgroupDef
        {
            Guid = Guid.Parse("2a737441-1930-4402-8d77-b2bebba308a3"),
            NameKey = "PowerSubgroup.Usb",
            Settings = new()
            {
                new() { Guid = Guid.Parse("48e6b7a6-50f5-4782-a5d4-53bb8f07e226"), NameKey = "PowerSetting.UsbSelectiveSuspend", Type = PowerSettingType.Enum,
                    EnumOptions = new() { (0, "PowerEnum.Disabled"), (1, "PowerEnum.Enabled") } },
            }
        },
        new PowerSubgroupDef
        {
            Guid = Guid.Parse("19cbb8fa-5279-450e-9fac-8a3d5fedd0c1"),
            NameKey = "PowerSubgroup.WirelessAdapter",
            Settings = new()
            {
                new() { Guid = Guid.Parse("12bbebe6-58d6-4636-95bb-3217ef867c1a"), NameKey = "PowerSetting.WirelessPowerSavingMode", Type = PowerSettingType.Enum,
                    EnumOptions = new() { (0, "PowerEnum.MaxPerformance"), (1, "PowerEnum.LowPowerSaving"), (2, "PowerEnum.MediumPowerSaving"), (3, "PowerEnum.MaxPowerSaving") } },
            }
        },
        new PowerSubgroupDef
        {
            Guid = Guid.Parse("5fb4938d-1ee8-4b0f-9a3c-5036b0ab995c"),
            NameKey = "PowerSubgroup.Graphics",
            Settings = new()
            {
                new() { Guid = Guid.Parse("dd848b2a-8a5d-4451-9ae2-39cd41658f6c"), NameKey = "PowerSetting.GpuPreference", Type = PowerSettingType.Enum,
                    EnumOptions = new() { (0, "PowerEnum.Default"), (1, "PowerEnum.PowerSaving"), (2, "PowerEnum.HighPerformance") } },
            }
        },
        new PowerSubgroupDef
        {
            Guid = Guid.Parse("e73a048d-bf27-4f12-9731-8b2076e8891f"),
            NameKey = "PowerSubgroup.Battery",
            Settings = new()
            {
                new() { Guid = Guid.Parse("8183ba9a-e910-48da-8769-14ae6dc1170a"), NameKey = "PowerSetting.LowBatteryLevel", Type = PowerSettingType.Range, Min = 0, Max = 100, UnitKey = "PowerUnit.Percent" },
                new() { Guid = Guid.Parse("9a66d8d7-4ff7-4ef9-b5a2-5a326ca2a469"), NameKey = "PowerSetting.CriticalBatteryLevel", Type = PowerSettingType.Range, Min = 0, Max = 100, UnitKey = "PowerUnit.Percent" },
                new() { Guid = Guid.Parse("637ea02f-bbcb-4015-8e2c-a1c7b9c0b546"), NameKey = "PowerSetting.CriticalBatteryAction", Type = PowerSettingType.Enum,
                    EnumOptions = new() { (0, "PowerEnum.DoNothing"), (1, "PowerEnum.Sleep"), (2, "PowerEnum.Hibernate"), (3, "PowerEnum.Shutdown") } },
                new() { Guid = Guid.Parse("d8742dcb-3e6a-4b3c-b3fe-374623cdcf06"), NameKey = "PowerSetting.LowBatteryAction", Type = PowerSettingType.Enum,
                    EnumOptions = new() { (0, "PowerEnum.DoNothing"), (1, "PowerEnum.Sleep"), (2, "PowerEnum.Hibernate"), (3, "PowerEnum.Shutdown") } },
            }
        },
        new PowerSubgroupDef
        {
            Guid = Guid.Parse("9596fb26-9850-41fd-ac3e-f7c3c00afd4b"),
            NameKey = "PowerSubgroup.Multimedia",
            Settings = new()
            {
                new() { Guid = Guid.Parse("34c7b99f-9a6d-4b3c-8dc7-b6693b78cef4"), NameKey = "PowerSetting.WhenPlayingVideo", Type = PowerSettingType.Enum,
                    EnumOptions = new() { (0, "PowerEnum.OptimizePowerSaving"), (1, "PowerEnum.Balanced"), (2, "PowerEnum.OptimizeQuality") } },
                new() { Guid = Guid.Parse("10778347-1370-4ee0-8bbd-33bdacaade49"), NameKey = "PowerSetting.VideoPlaybackQuality", Type = PowerSettingType.Enum,
                    EnumOptions = new() { (0, "PowerEnum.PowerSaving"), (1, "PowerEnum.Balanced"), (2, "PowerEnum.MaxQuality") } },
            }
        },
    };
}
