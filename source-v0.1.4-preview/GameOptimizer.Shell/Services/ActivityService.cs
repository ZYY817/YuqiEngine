using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using GameOptimizer.Core.Constants;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Controls;

namespace GameOptimizer.Shell.Services;

public class ActivityService : IDisposable
{
    private static ActivityService? _instance;
    public static ActivityService Instance => _instance ??= new ActivityService();

    public ObservableCollection<ActivityItem> Activities { get; } = new();
    private const int MaxActivities = 50;
    private bool _disposed;
    
    private static readonly string PersistPath = AppPaths.ActivityHistoryPath;

    public event EventHandler<ActivityItem>? ActivityAdded;

    public ActivityService()
    {
        LoadFromDisk();
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
    }
    
    // P2-017: 添加 Dispose 方法取消事件订阅
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        GC.SuppressFinalize(this);
    }
    
    // P2-017: 提供静态方法用于应用退出时清理
    public static void Shutdown()
    {
        _instance?.Dispose();
        _instance = null;
    }
    
    private void OnLanguageChanged(object? sender, string e)
    {
        // Why: Invoke 是同步阻塞，如果从后台线程调用且 UI 线程正忙会死锁。
        //      InvokeAsync 异步投递，不阻塞调用线程。
        _ = System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            foreach (var item in Activities)
            {
                item.NotifyLanguageChanged();
            }
        });
    }

    public void AddActivity(string titleKey, string descriptionKey, string icon = "⚡", bool? isSuccess = null, object[]? titleArgs = null, object[]? descArgs = null)
    {
        var item = new ActivityItem
        {
            TitleKey = titleKey,
            DescriptionKey = descriptionKey,
            TitleArgs = titleArgs,
            DescriptionArgs = descArgs,
            Icon = icon,
            IsSuccess = isSuccess,
            Timestamp = DateTime.Now
        };

        // Why: AddActivity 可能从后台线程调用（如 Task.Run 内的操作完成回调），
        //      Invoke 同步阻塞会与 UI 线程互等死锁。
        _ = System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            Activities.Insert(0, item);
            if (Activities.Count > MaxActivities)
                Activities.RemoveAt(Activities.Count - 1);
        });

        ActivityAdded?.Invoke(this, item);
        SaveToDisk();
    }

    public void LogOptimization(string operationNameKey, bool success, int count = 1, object[]? nameArgs = null)
    {
        var icon = success ? "✓" : "✕";
        var descKey = success ? "Activity.OptimizeSuccess" : "Activity.OptimizeFailed";
        var descArgs = success ? new object[] { count } : null;
        AddActivity(operationNameKey, descKey, icon, success, nameArgs, descArgs);
    }

    public void LogRestore(string operationNameKey, bool success, object[]? nameArgs = null)
    {
        var icon = success ? "↩" : "✕";
        var descKey = success ? "Activity.RestoreSuccess" : "Activity.RestoreFailed";
        AddActivity(operationNameKey, descKey, icon, success, nameArgs);
    }

    public void LogBackup(string snapshotName, bool success)
    {
        var icon = success ? "💾" : "✕";
        var descKey = success ? "Activity.BackupCreated" : "Activity.BackupFailed";
        var descArgs = success ? new object[] { snapshotName } : null;
        AddActivity("Activity.CreateBackup", descKey, icon, success, null, descArgs);
    }

    public void LogTest(string testNameKey, string resultKey, object[]? resultArgs = null)
    {
        AddActivity(testNameKey, resultKey, "📊", true, null, resultArgs);
    }

    public void LogCleanup(int filesDeleted, long bytesFreed)
    {
        var sizeText = FormatSize(bytesFreed);
        AddActivity("Activity.SystemCleanup", "Activity.CleanupResult", "🧹", true, null, new object[] { filesDeleted, sizeText });
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB" };
        int i = 0;
        double size = bytes;
        while (size >= 1024 && i < units.Length - 1) { size /= 1024; i++; }
        return $"{size:F2} {units[i]}";
    }

    public void Clear()
    {
        // Why: Clear 可能从后台线程调用，Invoke 同步阻塞有死锁风险。
        _ = System.Windows.Application.Current?.Dispatcher.InvokeAsync(() => Activities.Clear());
        SaveToDisk();
    }

    private void LoadFromDisk()
    {
        try
        {
            if (!File.Exists(PersistPath)) return;
            var (content, error) = GameOptimizer.Core.Services.SafeFileHelper.ReadAllText(PersistPath);
            if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(content)) return;
            
            var items = JsonSerializer.Deserialize<List<ActivityItemDto>>(content);
            if (items == null) return;
            
            foreach (var dto in items.Take(MaxActivities))
            {
                Activities.Add(new ActivityItem
                {
                    TitleKey = dto.TitleKey ?? "",
                    DescriptionKey = dto.DescriptionKey ?? "",
                    TitleArgs = dto.TitleArgs,
                    DescriptionArgs = dto.DescriptionArgs,
                    Icon = dto.Icon ?? "⚡",
                    IsSuccess = dto.IsSuccess,
                    Timestamp = dto.Timestamp
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ActivityService] LoadFromDisk failed: {ex.Message}");
        }
    }

    private void SaveToDisk()
    {
        try
        {
            // P3-003: 使用 SafeDirectoryOperations 替代裸露的 Directory.CreateDirectory
            var dir = Path.GetDirectoryName(PersistPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                if (!GameOptimizer.Core.Utilities.SafeDirectoryOperations.TryCreateDirectory(dir, out var error))
                {
                    System.Diagnostics.Debug.WriteLine($"[ActivityService] Failed to create directory: {error}");
                }
            }

            var dtos = Activities.Select(a => new ActivityItemDto
            {
                TitleKey = a.TitleKey,
                DescriptionKey = a.DescriptionKey,
                TitleArgs = a.TitleArgs,
                DescriptionArgs = a.DescriptionArgs,
                Icon = a.Icon,
                IsSuccess = a.IsSuccess,
                Timestamp = a.Timestamp
            }).ToList();
            
            var json = JsonSerializer.Serialize(dtos, new JsonSerializerOptions { WriteIndented = true });
            // Why: fire-and-forget 异步写入，避免 .Wait() 阻塞 UI 线程
            _ = GameOptimizer.Core.Utilities.SafeFileOperations.WriteTextWithRetryAsync(
                PersistPath, json, System.Threading.CancellationToken.None);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ActivityService] SaveToDisk failed: {ex.Message}");
        }
    }

    private record ActivityItemDto
    {
        public string? TitleKey { get; init; }
        public string? DescriptionKey { get; init; }
        public object[]? TitleArgs { get; init; }
        public object[]? DescriptionArgs { get; init; }
        public string? Icon { get; init; }
        public bool? IsSuccess { get; init; }
        public DateTime Timestamp { get; init; }
    }
}
