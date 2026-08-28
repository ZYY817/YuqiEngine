using System;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace GameOptimizer.Core.Services.AdvancedTuning
{
    public class AdvancedTuningService : IAdvancedTuningService
    {
        private const string MultimediaKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
        private const string PriorityControlKey = @"SYSTEM\CurrentControlSet\Control\PriorityControl";
        private const string SvchostKey = @"SYSTEM\CurrentControlSet\Control";
        private const string FileSystemKey = @"SYSTEM\CurrentControlSet\Control\FileSystem";

        public int GetSystemResponsiveness()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(MultimediaKey);
                return Convert.ToInt32(key?.GetValue("SystemResponsiveness", 20) ?? 20);
            }
            catch { return 20; }
        }

        public Task<TuningResult> SetSystemResponsivenessAsync(int value)
        {
            value = Math.Clamp(value, 0, 100);
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(MultimediaKey, true);
                if (key == null)
                {
                    return Task.FromResult(new TuningResult(false, "无法打开注册表项"));
                }
                key.SetValue("SystemResponsiveness", value, RegistryValueKind.DWord);
                return Task.FromResult(new TuningResult(true, $"已设置系统响应性为 {value}"));
            }
            catch (UnauthorizedAccessException)
            {
                return Task.FromResult(new TuningResult(false, "权限不足，请以管理员身份运行"));
            }
            catch (Exception ex)
            {
                return Task.FromResult(new TuningResult(false, $"设置失败: {ex.Message}"));
            }
        }

        public Win32PrioritySeparation GetWin32PrioritySeparation()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(PriorityControlKey);
                var value = Convert.ToInt32(key?.GetValue("Win32PrioritySeparation", 2) ?? 2);
                return value switch
                {
                    0x26 => Win32PrioritySeparation.Gaming,
                    0x18 => Win32PrioritySeparation.Server,
                    _ => Win32PrioritySeparation.Default
                };
            }
            catch { return Win32PrioritySeparation.Default; }
        }

        public Task<TuningResult> SetWin32PrioritySeparationAsync(Win32PrioritySeparation preset)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(PriorityControlKey, true);
                if (key == null)
                {
                    return Task.FromResult(new TuningResult(false, "无法打开注册表项"));
                }
                key.SetValue("Win32PrioritySeparation", (int)preset, RegistryValueKind.DWord);
                return Task.FromResult(new TuningResult(true, $"已设置优先级分离为 {preset}"));
            }
            catch (UnauthorizedAccessException)
            {
                return Task.FromResult(new TuningResult(false, "权限不足，请以管理员身份运行"));
            }
            catch (Exception ex)
            {
                return Task.FromResult(new TuningResult(false, $"设置失败: {ex.Message}"));
            }
        }

        public int GetSvchostSplitThreshold()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(SvchostKey);
                return Convert.ToInt32(key?.GetValue("SvcHostSplitThresholdInKB", 380000) ?? 380000);
            }
            catch { return 380000; }
        }

        public Task<TuningResult> SetSvchostSplitThresholdAsync(int thresholdKB)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(SvchostKey, true);
                if (key == null)
                {
                    return Task.FromResult(new TuningResult(false, "无法打开注册表项"));
                }
                key.SetValue("SvcHostSplitThresholdInKB", thresholdKB, RegistryValueKind.DWord);
                return Task.FromResult(new TuningResult(true, $"已设置 Svchost 阈值为 {thresholdKB} KB"));
            }
            catch (UnauthorizedAccessException)
            {
                return Task.FromResult(new TuningResult(false, "权限不足，请以管理员身份运行"));
            }
            catch (Exception ex)
            {
                return Task.FromResult(new TuningResult(false, $"设置失败: {ex.Message}"));
            }
        }

        public bool Is8Dot3NameDisabled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(FileSystemKey);
                return Convert.ToInt32(key?.GetValue("NtfsDisable8dot3NameCreation", 0) ?? 0) != 0;
            }
            catch { return false; }
        }

        public Task<TuningResult> Set8Dot3NameDisabledAsync(bool disabled)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(FileSystemKey, true);
                if (key == null)
                {
                    return Task.FromResult(new TuningResult(false, "无法打开注册表项"));
                }
                key.SetValue("NtfsDisable8dot3NameCreation", disabled ? 1 : 0, RegistryValueKind.DWord);
                return Task.FromResult(new TuningResult(true, disabled ? "已禁用 8.3 短文件名" : "已启用 8.3 短文件名"));
            }
            catch (UnauthorizedAccessException)
            {
                return Task.FromResult(new TuningResult(false, "权限不足，请以管理员身份运行"));
            }
            catch (Exception ex)
            {
                return Task.FromResult(new TuningResult(false, $"设置失败: {ex.Message}"));
            }
        }

        public AdvancedTuningState GetCurrentState()
        {
            return new AdvancedTuningState
            {
                SystemResponsiveness = GetSystemResponsiveness(),
                PrioritySeparation = GetWin32PrioritySeparation(),
                SvchostThresholdKB = GetSvchostSplitThreshold(),
                Is8Dot3Disabled = Is8Dot3NameDisabled()
            };
        }
    }

    public record TuningResult(bool Success, string Message);
}
