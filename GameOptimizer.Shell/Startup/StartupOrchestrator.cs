// File: GameOptimizer.Shell/Startup/StartupOrchestrator.cs
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;
using GameOptimizer.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace GameOptimizer.Shell.Startup;

/// <summary>
/// 启动编排器 — 协调 Splash、阶段执行、遥测、Mutex 单实例。
/// 不依赖 DI 容器（在 DI 构建之前就需要工作）。
/// </summary>
public sealed class StartupOrchestrator : IDisposable
{
    private const string MutexName = "YuqiEngine_SingleInstance";
    private const string RecoveryDevelopmentMutexName = "YuqiEngine_RecoveryDevelopment_SingleInstance";
    private const string AcceptanceUiMutexName = "YuqiEngine_AcceptanceUi_SingleInstance";
    private const int StageWarningMs = 15_000;
    private const int TotalTimeoutMs = 30_000;

    private readonly string[] _args;
    private readonly StartupTelemetry _telemetry = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Stopwatch _totalTimer = Stopwatch.StartNew();

    private SplashWindow? _splash;
    private Mutex? _appMutex;
    private bool _disposed;
    private bool _cancelled;

    public StartupOrchestrator(string[] args)
    {
        _args = args ?? Array.Empty<string>();
    }

    /// <summary>
    /// 是否为 autotest 模式（跳过 Splash）。
    /// </summary>
    public bool IsAutoTestMode => Array.Exists(_args, a =>
        a.Equals("--autotest", StringComparison.OrdinalIgnoreCase) ||
        a.Equals("/autotest", StringComparison.OrdinalIgnoreCase));

    public bool ShouldShowSplash => Array.Exists(_args, a =>
        a.Equals("--show-splash", StringComparison.OrdinalIgnoreCase) ||
        a.Equals("/show-splash", StringComparison.OrdinalIgnoreCase));

    public bool IsRecoveryDevelopmentInstance => Array.Exists(_args, a =>
        a.Equals("--recovery-dev", StringComparison.OrdinalIgnoreCase));

    public bool IsAcceptanceUiInstance => Array.Exists(_args, a =>
        a.Equals("--acceptance-ui", StringComparison.OrdinalIgnoreCase));

    public string AcceptanceSessionId
    {
        get
        {
            var raw = Array.Find(_args, argument =>
                argument.StartsWith("--acceptance-session=", StringComparison.OrdinalIgnoreCase))
                ?.Split('=', 2)[1];
            if (string.IsNullOrWhiteSpace(raw))
                return "default";

            var safe = new string(raw.Where(character =>
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_').Take(32).ToArray());
            return string.IsNullOrWhiteSpace(safe) ? "default" : safe;
        }
    }

    public string MutexIdentity => IsAcceptanceUiInstance
        ? $"{AcceptanceUiMutexName}_{AcceptanceSessionId}"
        : IsRecoveryDevelopmentInstance
            ? RecoveryDevelopmentMutexName
            : MutexName;

    /// <summary>
    /// 取消令牌，ESC 取消时触发。
    /// </summary>
    public CancellationToken CancellationToken => _cts.Token;

    /// <summary>
    /// 获取遥测数据。
    /// </summary>
    public StartupTelemetry GetTelemetry() => _telemetry;

    /// <summary>
    /// 获取/释放单实例 Mutex。返回 false 表示已有实例在运行。
    /// </summary>
    public bool AcquireMutex()
    {
        try
        {
            var mutexName = MutexIdentity;
            _appMutex = new Mutex(true, mutexName, out bool createdNew);
            if (createdNew)
                return true;

            // 已有实例，尝试激活其窗口
            ActivateExistingWindow();
            ReleaseMutexSafe();
            return false;
        }
        catch (AbandonedMutexException)
        {
            // Why: 上一个进程异常退出未释放 Mutex，我们可以安全接管
            Debug.WriteLine("[StartupOrchestrator][AcquireMutex] Acquired abandoned mutex");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[StartupOrchestrator][AcquireMutex] {ex.Message}");
            // Mutex 获取失败不应阻止启动，降级为允许多实例
            return true;
        }
    }

    /// <summary>
    /// 显示 Splash 窗口（autotest 模式下跳过）。
    /// </summary>
    public void ShowSplash()
    {
        if (IsAutoTestMode || !ShouldShowSplash)
            return;

        try
        {
            _splash = new SplashWindow();
            _splash.SetCancelAction(() =>
            {
                _cancelled = true;
                _cts.Cancel();
            });
            _splash.Show();
        }
        catch (Exception ex)
        {
            // Why: Splash 创建失败不应阻止启动，降级为无 Splash 模式
            Debug.WriteLine($"[StartupOrchestrator][ShowSplash] {ex.Message}");
            _splash = null;
        }
    }

    /// <summary>
    /// 执行一个同步初始化阶段。
    /// </summary>
    public void RunStage(string stageName, Action work)
    {
        if (_cancelled || _cts.IsCancellationRequested)
            return;

        if (CheckTotalTimeout())
            return;

        _splash?.UpdateStatus(stageName);
        _telemetry.BeginStage(stageName);

        Timer? warningTimer = null;
        try
        {
            warningTimer = new Timer(_ =>
                _splash?.ShowWarning($"\"{stageName}\" 初始化时间较长，请耐心等待..."),
                null, StageWarningMs, Timeout.Infinite);

            work();
            _telemetry.EndStage(success: true);
        }
        catch (OperationCanceledException)
        {
            _telemetry.EndStage(success: false);
        }
        catch (Exception ex)
        {
            _telemetry.EndStage(success: false);
            Debug.WriteLine($"[StartupOrchestrator][RunStage] {stageName} failed: {ex.Message}");
            throw;
        }
        finally
        {
            warningTimer?.Dispose();
        }
    }

    /// <summary>
    /// 执行一个异步初始化阶段。
    /// </summary>
    public async Task RunStageAsync(string stageName, Func<Task> work)
    {
        if (_cancelled || _cts.IsCancellationRequested)
            return;

        if (CheckTotalTimeout())
            return;

        _splash?.UpdateStatus(stageName);
        _telemetry.BeginStage(stageName);

        Timer? warningTimer = null;
        try
        {
            warningTimer = new Timer(_ =>
                _splash?.ShowWarning($"\"{stageName}\" 初始化时间较长，请耐心等待..."),
                null, StageWarningMs, Timeout.Infinite);

            await work().ConfigureAwait(false);
            _telemetry.EndStage(success: true);
        }
        catch (OperationCanceledException)
        {
            _telemetry.EndStage(success: false);
        }
        catch (Exception ex)
        {
            _telemetry.EndStage(success: false);
            Debug.WriteLine($"[StartupOrchestrator][RunStageAsync] {stageName} failed: {ex.Message}");
            throw;
        }
        finally
        {
            warningTimer?.Dispose();
        }
    }

    /// <summary>
    /// 在 Splash 上显示错误（DI 失败等场景）。
    /// </summary>
    public void ShowError(string errorText, Action? onRetry = null, Action? onExit = null)
    {
        _splash?.ShowError(errorText, onRetry, onExit);
    }

    /// <summary>
    /// 过渡到主窗口：先让 MainWindow 在 Splash 后面显示，等 Loaded 后淡出 Splash。
    /// </summary>
    public async Task TransitionToMainWindowAsync(Window mainWindow, bool activateMainWindow = true)
    {
        if (mainWindow == null)
            return;

        _splash?.UpdateStatus("准备就绪");
        _telemetry.BeginStage("Ready");

        try
        {
            // 等待 MainWindow.Loaded 事件
            if (!mainWindow.IsLoaded)
            {
                var loadedTcs = new TaskCompletionSource<bool>();
                void OnLoaded(object s, RoutedEventArgs e)
                {
                    mainWindow.Loaded -= OnLoaded;
                    loadedTcs.TrySetResult(true);
                }
                mainWindow.Loaded += OnLoaded;

                // 超时保护：5s 内 Loaded 未触发则强制继续
                var timeout = Task.Delay(5000);
                if (await Task.WhenAny(loadedTcs.Task, timeout).ConfigureAwait(false) == timeout)
                {
                    mainWindow.Loaded -= OnLoaded;
                    Debug.WriteLine("[StartupOrchestrator][Transition] MainWindow.Loaded timeout, forcing transition");
                }
            }

            // 淡出 Splash
            if (_splash != null)
            {
                await _splash.FadeOutAsync(300).ConfigureAwait(false);
            }

            // A recovery development run may be launched while the user is in a game.
            // In that mode the taskbar owns restoration; startup must not take foreground focus.
            if (activateMainWindow)
            {
                mainWindow.Dispatcher.Invoke(() =>
                {
                    mainWindow.Activate();
                    mainWindow.Focus();
                });
            }

            _telemetry.EndStage(success: true);
        }
        catch (Exception ex)
        {
            _telemetry.EndStage(success: false);
            Debug.WriteLine($"[StartupOrchestrator][Transition] {ex.Message}");
            // 确保 Splash 关闭，不阻塞主窗口
            try { _splash?.Dispatcher.Invoke(() => { try { _splash?.Close(); } catch { } }); }
            catch { /* 静默 */ }
        }
        finally
        {
            _telemetry.Stop();
        }
    }

    /// <summary>
    /// 写入遥测日志。
    /// </summary>
    public void WriteTelemetry(IDiagnosticLogService? logService)
    {
        try
        {
            var summary = _telemetry.ToSummary();
            Debug.WriteLine(summary);
            logService?.Append(LogLevel.Information, "StartupOrchestrator", summary, _telemetry.ToJson());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[StartupOrchestrator][WriteTelemetry] {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        ReleaseMutexSafe();

        try { _cts.Cancel(); } catch { /* 静默 */ }
        try { _cts.Dispose(); } catch { /* 静默 */ }

        // Why: Splash 可能仍然打开（异常退出路径），确保关闭
        try
        {
            if (_splash != null)
            {
                _splash.Dispatcher.Invoke(() =>
                {
                    try { if (_splash.IsLoaded) _splash.Close(); }
                    catch { /* 静默 */ }
                });
            }
        }
        catch { /* 静默 */ }
    }

    private bool CheckTotalTimeout()
    {
        if (_totalTimer.ElapsedMilliseconds <= TotalTimeoutMs)
            return false;

        Debug.WriteLine("[StartupOrchestrator] Total startup timeout exceeded, forcing transition");
        _splash?.ShowWarning("启动时间超过预期，正在强制进入主界面...");
        return true;
    }

    private void ReleaseMutexSafe()
    {
        try
        {
            _appMutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Why: 当前线程不拥有 Mutex（可能已释放或从未获取）
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[StartupOrchestrator][ReleaseMutex] {ex.Message}");
        }
        finally
        {
            try { _appMutex?.Dispose(); }
            catch { /* 静默 */ }
            _appMutex = null;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private static void ActivateExistingWindow()
    {
        try
        {
            var hwnd = FindWindow(null, "宇奇引擎");
            if (hwnd == IntPtr.Zero)
                return;

            const int SW_RESTORE = 9;
            ShowWindow(hwnd, SW_RESTORE);
            SetForegroundWindow(hwnd);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[StartupOrchestrator][ActivateExistingWindow] {ex.Message}");
        }
    }
}
