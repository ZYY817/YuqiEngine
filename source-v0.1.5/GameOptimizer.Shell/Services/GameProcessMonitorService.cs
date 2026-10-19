using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Shell.Models;
using Microsoft.Extensions.Logging;

namespace GameOptimizer.Shell.Services;

public interface IGameProcessMonitorService
{
    event EventHandler<GameProcessEventArgs>? GameStarted;
    event EventHandler<GameProcessEventArgs>? GameExited;
    bool IsMonitoring { get; }
    void StartMonitoring();
    void StopMonitoring();
    IReadOnlyList<RunningGameInfo> GetRunningGames();
}

public class GameProcessEventArgs : EventArgs
{
    public required string ProcessName { get; init; }
    public required string ExecutablePath { get; init; }
    public required int ProcessId { get; init; }
    public GameProfile? MatchedProfile { get; init; }
}

public class RunningGameInfo
{
    public required string ProcessName { get; init; }
    public required string ExecutablePath { get; init; }
    public required int ProcessId { get; init; }
    public DateTime StartTime { get; init; }
}

public sealed class GameProcessMonitorService : IGameProcessMonitorService, IDisposable
{
    private readonly ILogger<GameProcessMonitorService> _logger;
    private readonly GameProfileManager _profileManager;
    private readonly Dictionary<int, string> _trackedProcessNames = new();
    private readonly object _lock = new();
    private Timer? _pollTimer;
    private ManagementEventWatcher? _startWatcher;
    private ManagementEventWatcher? _stopWatcher;
    private bool _isMonitoring;
    private bool _disposed;

    public event EventHandler<GameProcessEventArgs>? GameStarted;
    public event EventHandler<GameProcessEventArgs>? GameExited;
    public bool IsMonitoring => _isMonitoring;

    public GameProcessMonitorService(ILogger<GameProcessMonitorService> logger)
    {
        _logger = logger;
        _profileManager = GameProfileManager.Instance;
    }

    public void StartMonitoring()
    {
        if (_isMonitoring) return;
        _isMonitoring = true;

        try
        {
            // 使用WMI监控进程启动
            var startQuery = new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace");
            _startWatcher = new ManagementEventWatcher(startQuery);
            _startWatcher.EventArrived += OnProcessStarted;
            _startWatcher.Start();

            // 使用WMI监控进程退出
            var stopQuery = new WqlEventQuery("SELECT * FROM Win32_ProcessStopTrace");
            _stopWatcher = new ManagementEventWatcher(stopQuery);
            _stopWatcher.EventArrived += OnProcessStopped;
            _stopWatcher.Start();

            _logger.LogInformation("[GameMonitor] WMI进程监控已启动");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[GameMonitor] WMI监控启动失败，回退到轮询模式");
            // 回退到轮询模式
            _pollTimer = new Timer(PollProcesses, null, TimeSpan.Zero, TimeSpan.FromSeconds(3));
        }

        // 初始扫描已运行的游戏
        ScanRunningGames();
    }

    public void StopMonitoring()
    {
        if (!_isMonitoring) return;
        _isMonitoring = false;

        _startWatcher?.Stop();
        _startWatcher?.Dispose();
        _startWatcher = null;

        _stopWatcher?.Stop();
        _stopWatcher?.Dispose();
        _stopWatcher = null;

        _pollTimer?.Dispose();
        _pollTimer = null;

        lock (_lock) { _trackedProcessNames.Clear(); }
        _logger.LogInformation("[GameMonitor] 进程监控已停止");
    }

    public IReadOnlyList<RunningGameInfo> GetRunningGames()
    {
        var result = new List<RunningGameInfo>();
        var profiles = _profileManager.Profiles;

        foreach (var profile in profiles)
        {
            var processName = Path.GetFileNameWithoutExtension(profile.ExecutablePath);
            var processes = Process.GetProcessesByName(processName);
            foreach (var proc in processes)
            {
                using (proc)
                {
                    try
                    {
                        // StartTime 可能因权限不足抛出 Win32Exception
                        DateTime startTime;
                        try { startTime = proc.StartTime; }
                        catch { startTime = DateTime.Now; }
                        
                        result.Add(new RunningGameInfo
                        {
                            ProcessName = proc.ProcessName,
                            ExecutablePath = profile.ExecutablePath,
                            ProcessId = proc.Id,
                            StartTime = startTime
                        });
                    }
                    catch (Exception ex) 
                    { 
                        System.Diagnostics.Debug.WriteLine($"[GameMonitor] 获取进程信息失败: {ex.Message}"); 
                    }
                }
            }
        }
        return result;
    }

    private void ScanRunningGames()
    {
        _profileManager.Reload();
        var profiles = _profileManager.Profiles;
        foreach (var profile in profiles)
        {
            var processName = Path.GetFileNameWithoutExtension(profile.ExecutablePath);
            var processes = Process.GetProcessesByName(processName);
            foreach (var proc in processes)
            {
                using (proc)
                {
                    lock (_lock)
                    {
                        if (!_trackedProcessNames.ContainsKey(proc.Id))
                        {
                            _trackedProcessNames[proc.Id] = processName;
                            _logger.LogInformation("[GameMonitor] 检测到已运行的游戏: {Name} (PID: {Pid})", processName, proc.Id);
                        }
                    }
                }
            }
        }
    }

    private void OnProcessStarted(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var processName = e.NewEvent.Properties["ProcessName"].Value?.ToString() ?? "";
            var processId = Convert.ToInt32(e.NewEvent.Properties["ProcessID"].Value);

            if (string.IsNullOrEmpty(processName)) return;

            var nameWithoutExt = Path.GetFileNameWithoutExtension(processName);
            _profileManager.Reload();
            var profile = _profileManager.FindByProcessName(nameWithoutExt);

            if (profile != null)
            {
                lock (_lock) { _trackedProcessNames[processId] = nameWithoutExt; }
                _logger.LogInformation("[GameMonitor] 游戏启动: {Name} (PID: {Pid})", processName, processId);

                GameStarted?.Invoke(this, new GameProcessEventArgs
                {
                    ProcessName = nameWithoutExt,
                    ExecutablePath = profile.ExecutablePath,
                    ProcessId = processId,
                    MatchedProfile = profile
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GameMonitor] 处理进程启动事件失败");
        }
    }

    private void OnProcessStopped(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var processName = e.NewEvent.Properties["ProcessName"].Value?.ToString() ?? "";
            var processId = Convert.ToInt32(e.NewEvent.Properties["ProcessID"].Value);

            string? trackedName;
            lock (_lock)
            {
                _trackedProcessNames.TryGetValue(processId, out trackedName);
                _trackedProcessNames.Remove(processId);
            }

            if (trackedName != null)
            {
                var nameWithoutExt = Path.GetFileNameWithoutExtension(processName);
                var profile = _profileManager.FindByProcessName(nameWithoutExt);

                _logger.LogInformation("[GameMonitor] 游戏退出: {Name} (PID: {Pid})", processName, processId);

                GameExited?.Invoke(this, new GameProcessEventArgs
                {
                    ProcessName = nameWithoutExt,
                    ExecutablePath = profile?.ExecutablePath ?? "",
                    ProcessId = processId,
                    MatchedProfile = profile
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GameMonitor] 处理进程退出事件失败");
        }
    }

    private void PollProcesses(object? state)
    {
        if (!_isMonitoring) return;

        try
        {
            _profileManager.Reload();
            var profiles = _profileManager.Profiles;
            var currentPids = new HashSet<int>();

            foreach (var profile in profiles)
            {
                var processName = Path.GetFileNameWithoutExtension(profile.ExecutablePath);
                var processes = Process.GetProcessesByName(processName);

                foreach (var proc in processes)
                {
                    using (proc)
                    {
                        currentPids.Add(proc.Id);
                        bool isNew;
                        lock (_lock)
                        {
                            isNew = !_trackedProcessNames.ContainsKey(proc.Id);
                            if (isNew) _trackedProcessNames[proc.Id] = processName;
                        }

                        if (isNew)
                        {
                            _logger.LogInformation("[GameMonitor] 游戏启动(轮询): {Name} (PID: {Pid})", processName, proc.Id);
                            GameStarted?.Invoke(this, new GameProcessEventArgs
                            {
                                ProcessName = processName,
                                ExecutablePath = profile.ExecutablePath,
                                ProcessId = proc.Id,
                                MatchedProfile = profile
                            });
                        }
                    }
                }
            }

            // 检测退出的进程
            List<(int Pid, string Name)> exitedProcesses;
            lock (_lock)
            {
                exitedProcesses = _trackedProcessNames
                    .Where(kvp => !currentPids.Contains(kvp.Key))
                    .Select(kvp => (kvp.Key, kvp.Value))
                    .ToList();
                foreach (var (pid, _) in exitedProcesses) _trackedProcessNames.Remove(pid);
            }

            foreach (var (pid, name) in exitedProcesses)
            {
                var profile = _profileManager.FindByProcessName(name);
                _logger.LogInformation("[GameMonitor] 游戏退出(轮询): {Name} (PID: {Pid})", name, pid);
                GameExited?.Invoke(this, new GameProcessEventArgs
                {
                    ProcessName = name,
                    ExecutablePath = profile?.ExecutablePath ?? "",
                    ProcessId = pid,
                    MatchedProfile = profile
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GameMonitor] 轮询进程失败");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopMonitoring();
    }
}
