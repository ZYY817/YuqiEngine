// File: GameOptimizer.Core/Services/Snapshot/RegistryBackupHelper.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Constants;

namespace GameOptimizer.Core.Services.Snapshot
{
    /// <summary>
    /// 深度注册表备份的纯函数辅助方法。
    /// 所有方法均为 static，无副作用，方便单元测试。
    /// </summary>
    public static class RegistryBackupHelper
    {
        /// <summary>
        /// 从快照 Settings 的 key 列表中提取唯一的注册表父键路径。
        /// Settings key 格式: "keyPath|valueName|category"
        /// </summary>
        /// <param name="settingsKeys">快照 Settings 字典的 key 集合</param>
        /// <returns>去重后的注册表键路径列表</returns>
        public static IReadOnlyList<string> ExtractUniqueKeyPaths(IEnumerable<string> settingsKeys)
        {
            if (settingsKeys == null) return Array.Empty<string>();

            var uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var key in settingsKeys)
            {
                if (string.IsNullOrWhiteSpace(key)) continue;

                var parts = key.Split('|');
                if (parts.Length < 2)
                {
                    // 格式不对，跳过
                    Debug.WriteLine($"[RegistryBackupHelper] 跳过格式异常的 key: {key}");
                    continue;
                }

                var keyPath = parts[0].Trim();
                if (string.IsNullOrWhiteSpace(keyPath)) continue;

                // 验证路径至少包含 root\subkey 结构
                if (!keyPath.Contains('\\')) continue;

                uniquePaths.Add(keyPath);
            }

            return uniquePaths.ToList();
        }

        /// <summary>
        /// 将注册表键路径转换为安全的 .reg 文件名。
        /// HKEY_CURRENT_USER\Control Panel\Mouse → HKCU_Control_Panel_Mouse.reg
        /// HKEY_LOCAL_MACHINE\SYSTEM\... → HKLM_SYSTEM_...reg
        /// </summary>
        public static string GenerateRegFileName(string registryKeyPath)
        {
            if (string.IsNullOrWhiteSpace(registryKeyPath))
                return string.Empty;

            var path = registryKeyPath.Trim();

            // 缩写根键
            if (path.StartsWith("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase))
                path = "HKCU" + path.Substring("HKEY_CURRENT_USER".Length);
            else if (path.StartsWith("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase))
                path = "HKLM" + path.Substring("HKEY_LOCAL_MACHINE".Length);
            else if (path.StartsWith("HKEY_CLASSES_ROOT", StringComparison.OrdinalIgnoreCase))
                path = "HKCR" + path.Substring("HKEY_CLASSES_ROOT".Length);
            else if (path.StartsWith("HKEY_USERS", StringComparison.OrdinalIgnoreCase))
                path = "HKU" + path.Substring("HKEY_USERS".Length);

            // 替换反斜杠为下划线
            path = path.Replace('\\', '_');

            // 移除文件名非法字符
            var invalidChars = Path.GetInvalidFileNameChars();
            foreach (var c in invalidChars)
            {
                path = path.Replace(c.ToString(), "");
            }

            // 确保不为空
            if (string.IsNullOrWhiteSpace(path))
                return "unknown.reg";

            return path + ".reg";
        }

        /// <summary>
        /// 判断深度恢复面板的显示状态。
        /// </summary>
        /// <param name="failedCount">正常恢复的失败数</param>
        /// <param name="hasRegistryBackup">快照是否包含 .reg 备份</param>
        /// <returns>(ShowDeepRestore, ShowNoBackupHint)</returns>
        public static (bool ShowDeepRestore, bool ShowNoBackupHint) GetDeepRestoreVisibility(
            int failedCount, bool hasRegistryBackup)
        {
            if (failedCount <= 0)
                return (false, false);

            // 有失败项
            if (hasRegistryBackup)
                return (true, false);
            else
                return (false, true);
        }

        /// <summary>
        /// 检查指定快照 ID 的 .reg 备份子目录是否存在且包含 .reg 文件。
        /// </summary>
        /// <param name="snapshotDir">快照根目录</param>
        /// <param name="snapshotId">快照 ID</param>
        /// <returns>是否有可用的 .reg 备份</returns>
        public static bool CheckHasRegistryBackup(string snapshotDir, string snapshotId)
        {
            if (string.IsNullOrWhiteSpace(snapshotDir) || string.IsNullOrWhiteSpace(snapshotId))
                return false;

            try
            {
                var regDir = Path.Combine(snapshotDir, snapshotId);
                if (!Directory.Exists(regDir))
                    return false;

                return Directory.EnumerateFiles(regDir, "*.reg").Any();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RegistryBackupHelper] CheckHasRegistryBackup 异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 构造深度恢复结果。
        /// </summary>
        public static RegistryBackupRestoreResult BuildRestoreResult(
            IReadOnlyList<string> restoredFiles,
            IReadOnlyList<string> failedFiles)
        {
            var restored = restoredFiles ?? Array.Empty<string>();
            var failed = failedFiles ?? Array.Empty<string>();

            return new RegistryBackupRestoreResult
            {
                Success = failed.Count == 0 && restored.Count > 0,
                RestoredCount = restored.Count,
                FailedCount = failed.Count,
                RestoredFiles = restored.ToList(),
                FailedFiles = failed.ToList(),
                RequiresReboot = restored.Count > 0
            };
        }

        /// <summary>
        /// 旧 raw 注册表导出入口已永久 fail-closed；新快照只记录 typed manifest。
        /// </summary>
        /// <param name="registryKeyPath">完整注册表键路径，如 HKEY_CURRENT_USER\Control Panel\Mouse</param>
        /// <param name="outputFilePath">输出 .reg 文件路径</param>
        /// <param name="timeoutMs">超时毫秒数</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>(success, errorMessage)</returns>
        public static Task<(bool Success, string? Error)> ExportSingleKeyAsync(
            string registryKeyPath, string outputFilePath, int timeoutMs = ProcessTimeouts.QuickCommand, CancellationToken ct = default)
        {
            _ = registryKeyPath;
            _ = outputFilePath;
            _ = timeoutMs;
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<(bool Success, string? Error)>(
                (false, "snapshot.raw-registry-export-disabled"));
        }

        /// <summary>
        /// 旧 .reg 导入入口已永久 fail-closed；Snapshot 恢复只能使用唯一 typed Owner。
        /// </summary>
        /// <param name="regFilePath">.reg 文件路径</param>
        /// <param name="timeoutMs">超时毫秒数</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>(success, errorMessage)</returns>
        public static Task<(bool Success, string? Error)> ImportSingleRegFileAsync(
            string regFilePath, int timeoutMs = ProcessTimeouts.StandardCommand, CancellationToken ct = default)
        {
            _ = regFilePath;
            _ = timeoutMs;
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<(bool Success, string? Error)>(
                (false, "snapshot.deep-registry-typed-owner-unavailable"));
        }
    }
}
