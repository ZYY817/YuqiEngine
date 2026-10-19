using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using GameOptimizer.Core.Localization;

namespace GameOptimizer.Shell.Services;

public enum DynamicSettingType { Range, Enum }

internal static class EncodingInitializer
{
    private static bool _initialized;
    public static void EnsureInitialized()
    {
        if (_initialized) return;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _initialized = true;
    }
}

public class DynamicSettingDef
{
    public Guid Guid { get; set; }
    public string Name { get; set; } = "";
    public string? Alias { get; set; }
    public DynamicSettingType Type { get; set; } = DynamicSettingType.Range;
    public int Min { get; set; }
    public int Max { get; set; }
    public int Increment { get; set; } = 1;
    public string Unit { get; set; } = "";
    public List<DynamicEnumOption>? EnumOptions { get; set; }
}

public class DynamicEnumOption
{
    public int Value { get; set; }
    public string Name { get; set; } = "";
}

public class LocalizedEnumOption
{
    public int Value { get; set; }
    public string LocalizedName { get; set; } = "";
}

public class PowerSettingItem
{
    public DynamicSettingDef Definition { get; set; } = new();
    public Guid SubgroupGuid { get; set; }
    public int CurrentAcValue { get; set; }
    public int CurrentDcValue { get; set; }
    
    public string LocalizedName => PowerSettingsTranslations.GetSettingName(Definition.Guid) ?? Definition.Name;
    public string LocalizedUnit => GetLocalizedUnit();
    
    private string GetLocalizedUnit()
    {
        var u = Definition.Unit;
        if (string.IsNullOrEmpty(u)) return "";

        return u switch
        {
            "秒" or "Seconds" => LocalizationService.Instance.Get("PowerUnit.Seconds"),
            "毫秒" or "Milliseconds" => LocalizationService.Instance.Get("PowerUnit.Milliseconds"),
            "分钟" or "Minutes" => LocalizationService.Instance.Get("PowerUnit.Minutes"),
            "%" or "百分比" => "%",
            _ => u
        };
    }

    public List<LocalizedEnumOption> GetLocalizedEnumOptions()
    {
        if (Definition.EnumOptions == null) return new();
        return Definition.EnumOptions.Select(o => new LocalizedEnumOption
        {
            Value = o.Value,
            LocalizedName = PowerSettingsTranslations.GetEnumOptionName(Definition.Guid, o.Value) ?? o.Name
        }).ToList();
    }
}

public class PowerSubgroupItem
{
    public Guid Guid { get; set; }
    public string Name { get; set; } = "";
    public string? Alias { get; set; }
    public List<PowerSettingItem> Settings { get; set; } = new();
    public string LocalizedName => PowerSettingsTranslations.GetSubgroupName(Guid) ?? Name;
}

[SupportedOSPlatform("windows")]
public class PowerSettingsQueryService
{
    private static readonly Regex GuidRegex = new(@"GUID:\s*([0-9a-fA-F\-]{36})", RegexOptions.Compiled);

    static PowerSettingsQueryService() => EncodingInitializer.EnsureInitialized();

    public List<PowerSubgroupItem> QueryAllSettings()
    {
        var subgroups = new List<PowerSubgroupItem>();
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = "/query",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                // 动态获取系统OEM代码页，非中文系统不一定是936
                StandardOutputEncoding = GetOemEncoding()
            };

            using var process = Process.Start(startInfo);
            if (process == null) return subgroups;
            // Why: 同步 ReadToEnd 在缓冲区满时死锁（powercfg /query 输出量大）
            var outputTask = process.StandardOutput.ReadToEndAsync();
            process.WaitForExit(10000);
            var output = outputTask.GetAwaiter().GetResult();
            subgroups = ParsePowercfgOutput(output);
        }
        catch (Exception ex) { Debug.WriteLine($"[PowerSettingsQuery] Error: {ex.Message}"); }
        return subgroups;
    }

    private List<PowerSubgroupItem> ParsePowercfgOutput(string output)
    {
        var subgroups = new Dictionary<Guid, PowerSubgroupItem>();
        var lines = output.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        
        PowerSubgroupItem? currentSubgroup = null;
        PowerSettingItem? currentSetting = null;
        var enumOptions = new List<DynamicEnumOption>();
        int? lastEnumIndex = null;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            
            if (line.Contains("子组 GUID:") || line.Contains("Subgroup GUID:"))
            {
                SaveCurrentSetting(currentSubgroup, currentSetting, enumOptions);
                currentSetting = null;
                enumOptions.Clear();
                lastEnumIndex = null;

                var guidMatch = GuidRegex.Match(line);
                if (guidMatch.Success && Guid.TryParse(guidMatch.Groups[1].Value, out var subGuid))
                {
                    if (!subgroups.TryGetValue(subGuid, out currentSubgroup))
                    {
                        currentSubgroup = new PowerSubgroupItem { Guid = subGuid };
                        // 格式: 子组 GUID: fea3413e-...  (不属于任何子组的设置)
                        var nameMatch = Regex.Match(line, @"\(([^)]+)\)\s*$");
                        if (nameMatch.Success) 
                            currentSubgroup.Name = nameMatch.Groups[1].Value.Trim();
                        subgroups[subGuid] = currentSubgroup;
                    }
                }
                continue;
            }

            if (line.Contains("电源设置 GUID:") || line.Contains("Power Setting GUID:"))
            {
                SaveCurrentSetting(currentSubgroup, currentSetting, enumOptions);
                enumOptions.Clear();
                lastEnumIndex = null;

                var guidMatch = GuidRegex.Match(line);
                if (guidMatch.Success && Guid.TryParse(guidMatch.Groups[1].Value, out var setGuid) && currentSubgroup != null)
                {
                    currentSetting = new PowerSettingItem
                    {
                        Definition = new DynamicSettingDef { Guid = setGuid },
                        SubgroupGuid = currentSubgroup.Guid
                    };
                    // 格式: 电源设置 GUID: 0e796bdb-...  (唤醒时需要密码)
                    var nameMatch = Regex.Match(line, @"\(([^)]+)\)\s*$");
                    if (nameMatch.Success) 
                        currentSetting.Definition.Name = nameMatch.Groups[1].Value.Trim();
                }
                continue;
            }

            if (currentSetting == null) continue;

            if (line.Contains("当前交流电源设置索引") || line.Contains("Current AC Power Setting Index"))
            {
                var m = Regex.Match(line, @"0x([0-9a-fA-F]+)");
                if (m.Success) currentSetting.CurrentAcValue = Convert.ToInt32(m.Groups[1].Value, 16);
            }
            else if (line.Contains("当前直流电源设置索引") || line.Contains("Current DC Power Setting Index"))
            {
                var m = Regex.Match(line, @"0x([0-9a-fA-F]+)");
                if (m.Success) currentSetting.CurrentDcValue = Convert.ToInt32(m.Groups[1].Value, 16);
            }
            else if (line.Contains("最小可能的设置") || line.Contains("Minimum Possible Setting"))
            {
                var m = Regex.Match(line, @"0x([0-9a-fA-F]+)");
                if (m.Success) currentSetting.Definition.Min = Convert.ToInt32(m.Groups[1].Value, 16);
            }
            else if (line.Contains("最大可能的设置") || line.Contains("Maximum Possible Setting"))
            {
                var m = Regex.Match(line, @"0x([0-9a-fA-F]+)");
                if (m.Success) currentSetting.Definition.Max = Convert.ToInt32(m.Groups[1].Value, 16);
            }
            else if (line.Contains("可能的设置增量") || line.Contains("Possible Settings increment"))
            {
                var m = Regex.Match(line, @"0x([0-9a-fA-F]+)");
                if (m.Success) currentSetting.Definition.Increment = Math.Max(1, Convert.ToInt32(m.Groups[1].Value, 16));
            }
            else if (line.Contains("可能的设置单位") || line.Contains("Possible Settings units"))
            {
                var m = Regex.Match(line, @"(?:可能的设置单位|Possible Settings units):\s*(.+)", RegexOptions.IgnoreCase);
                if (m.Success) currentSetting.Definition.Unit = m.Groups[1].Value.Trim();
            }
            else if (line.Contains("可能的设置索引") || line.Contains("Possible Setting Index"))
            {
                var m = Regex.Match(line, @"(\d+)");
                if (m.Success) lastEnumIndex = int.Parse(m.Groups[1].Value);
            }
            else if (line.Contains("可能的设置友好名称") || line.Contains("Possible Setting Friendly Name"))
            {
                var m = Regex.Match(line, @"(?:可能的设置友好名称|Possible Setting Friendly Name):\s*(.+)", RegexOptions.IgnoreCase);
                if (m.Success && lastEnumIndex.HasValue)
                {
                    enumOptions.Add(new DynamicEnumOption { Value = lastEnumIndex.Value, Name = m.Groups[1].Value.Trim() });
                    currentSetting.Definition.Type = DynamicSettingType.Enum;
                    lastEnumIndex = null;
                }
            }
        }

        SaveCurrentSetting(currentSubgroup, currentSetting, enumOptions);
        return subgroups.Values.Where(s => s.Settings.Count > 0).OrderBy(s => s.LocalizedName).ToList();
    }

    private void SaveCurrentSetting(PowerSubgroupItem? subgroup, PowerSettingItem? setting, List<DynamicEnumOption> enumOptions)
    {
        if (subgroup == null || setting == null) return;
        if (enumOptions.Count > 0)
        {
            setting.Definition.EnumOptions = new List<DynamicEnumOption>(enumOptions);
            setting.Definition.Type = DynamicSettingType.Enum;
        }
        subgroup.Settings.Add(setting);
    }


    public bool SetSettingValue(Guid subgroupGuid, Guid settingGuid, int value, bool isAc)
    {
        Debug.WriteLine("[PowerSettingsQuery] Legacy write blocked: use IPowerManagementOwner prepare/confirm/execute workflow.");
        return false;
    }

    public bool SetSettingValueBoth(Guid subgroupGuid, Guid settingGuid, int value)
    {
        var acOk = SetSettingValue(subgroupGuid, settingGuid, value, true);
        var dcOk = SetSettingValue(subgroupGuid, settingGuid, value, false);
        return acOk || dcOk;
    }

    private static Encoding GetOemEncoding()
    {
        try
        {
            var oemCp = System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
            return Encoding.GetEncoding(oemCp);
        }
        catch
        {
            return Encoding.UTF8;
        }
    }
}
