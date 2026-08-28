using System;
using System.IO;
using GameOptimizer.Core.Diagnostics;

namespace GameOptimizer.Shell.Services;

public sealed class LogRetentionService
{
    private readonly IDiagnosticLogService _logService;
    private readonly IAppSettingsStore _settingsStore;
    private readonly TimeProvider _timeProvider;

    public LogRetentionService(
        IDiagnosticLogService logService,
        IAppSettingsStore settingsStore,
        TimeProvider timeProvider)
    {
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public void ApplyRetentionPolicy()
    {
        var retentionDays = _settingsStore.Current.LogRetentionDays;
        if (retentionDays <= 0)
        {
            return;
        }

        var cutoff = _timeProvider.GetLocalNow().DateTime.AddDays(-retentionDays);
        var directory = _logService.LogDirectory;
        if (!Directory.Exists(directory))
        {
            return;
        }

        try
        {
            var files = Directory.GetFiles(directory, "*.log*")
                .Where(path => path.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
                               path.EndsWith(".log.old", StringComparison.OrdinalIgnoreCase));
            foreach (var file in files)
            {
                try
                {
                    var info = new FileInfo(file);
                    if (info.LastWriteTime < cutoff)
                    {
                        var (success, error) = GameOptimizer.Core.Services.SafeFileHelper.DeleteFile(file);
                        if (!success)
                        {
                            System.Diagnostics.Debug.WriteLine($"[LogRetention] 删除日志文件失败 {file}: {error}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[LogRetention] 处理文件失败 {file}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LogRetention] 读取日志目录失败: {ex.Message}");
        }
    }
}
