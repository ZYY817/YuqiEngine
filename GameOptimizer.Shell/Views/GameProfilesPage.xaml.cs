using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Constants;
using GameOptimizer.Shell.Models;
using GameOptimizer.Shell.Services;
using Microsoft.Win32;

namespace GameOptimizer.Shell.Views;

public partial class GameProfilesPage : UserControl
{
    private readonly GameProfileManager _profileManager = GameProfileManager.Instance;
    private readonly GameLibraryScannerService _scanner = new(
        Microsoft.Extensions.Logging.Abstractions.NullLogger<GameLibraryScannerService>.Instance);

    public GameProfilesPage()
    {
        PageDiagnosticService.Log("GameProfilesPage", "Constructor called");
        InitializeComponent();
        Unloaded += OnUnloaded;
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            PageDiagnosticService.Log("GameProfilesPage", "UserControl_Loaded - Reloading profiles");
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            _profileManager.Reload();
            
            AutoDetectAndApplyRecommendations();
            
            RefreshGameList();
            UpdateLocalizedTexts();
            PageDiagnosticService.LogLoaded("GameProfilesPage", true, $"{_profileManager.Profiles.Count} profiles loaded");
        }
        catch (Exception ex)
        {
            PageDiagnosticService.LogError("GameProfilesPage", "Loaded error", ex);
        }
    }

    private void AutoDetectAndApplyRecommendations()
    {
        var needsSave = false;
        foreach (var profile in _profileManager.Profiles)
        {
            // 如果游戏类型是 General 且没有优化项，尝试重新检测
            if (profile.GameType == GameType.General || profile.EnabledOperationIds.Count == 0)
            {
                var detectedType = GameTypeDetector.DetectType(profile.GameName, profile.ExecutablePath);
                if (detectedType != profile.GameType || profile.EnabledOperationIds.Count == 0)
                {
                    profile.GameType = detectedType;
                    var recommended = GameOptimizationPresets.GetRecommendedOperations(detectedType);
                    profile.EnabledOperationIds = new List<string>(recommended);
                    needsSave = true;
                }
            }
        }
        if (needsSave)
        {
            _profileManager.Save();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, string e)
    {
        // Why: GameProfileViewModel 的本地化属性在构造时计算，
        //      语言切换后必须重建 ViewModel 才能刷新绑定文本
        _ = Dispatcher.InvokeAsync(RefreshGameList);
    }

    private void UpdateLocalizedTexts()
    {
        var loc = LocalizationService.Instance;
        if (FindName("PageTitle") is TextBlock title)
            title.Text = loc.Get("GameProfiles.Title");
        if (FindName("PageSubtitle") is TextBlock subtitle)
            subtitle.Text = loc.Get("GameProfiles.Subtitle");
        if (FindName("ScanButtonText") is TextBlock scanText)
            scanText.Text = loc.Get("GameProfiles.ScanAdd");
        if (FindName("EmptyTitle") is TextBlock emptyTitle)
            emptyTitle.Text = loc.Get("GameProfiles.EmptyTitle");
        if (FindName("EmptySubtitle") is TextBlock emptySubtitle)
            emptySubtitle.Text = loc.Get("GameProfiles.EmptySubtitle");
        if (FindName("AddCardText") is TextBlock addText)
            addText.Text = loc.Get("GameProfiles.AddNew");
    }

    private void RefreshGameList()
    {
        var profiles = _profileManager.Profiles;
        
        if (profiles.Count == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            GameCardsContainer.Visibility = Visibility.Collapsed;
        }
        else
        {
            EmptyState.Visibility = Visibility.Collapsed;
            GameCardsContainer.Visibility = Visibility.Visible;
            var viewModels = profiles.Select(p => new GameProfileViewModel(p)).ToList();
            GameCardsContainer.ItemsSource = viewModels;
        }
        UpdateLocalizedTexts();
    }

    private void AddGameButton_Click(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationService.Instance;
        var dialog = new OpenFileDialog
        {
            Title = loc.Get("GameProfiles.SelectExe"),
            Filter = loc.Get("GameProfiles.ExeFilter"),
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            var fileName = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
            
            if (_profileManager.FindByExecutable(dialog.FileName) != null)
            {
                InfoDialog.Show(Window.GetWindow(this), loc.Get("Dialog.Info"), loc.Get("GameProfiles.AlreadyAdded"));
                return;
            }

            var detectedType = GameTypeDetector.DetectType(fileName, dialog.FileName);
            var recommendedOps = GameOptimizationPresets.GetRecommendedOperations(detectedType);

            var profile = new GameProfile
            {
                GameName = fileName,
                ExecutablePath = dialog.FileName,
                GameType = detectedType,
                AutoOptimizeOnLaunch = true,
                RestoreOnExit = true,
                EnabledOperationIds = new List<string>(recommendedOps)
            };

            _profileManager.Add(profile);
            RefreshGameList();
            
            var isEn = loc.CurrentLanguage == "en-US";
            var typeName = GameTypeDetector.GetTypeDisplayName(detectedType, isEn);
            InfoDialog.Show(Window.GetWindow(this), loc.Get("GameProfiles.AddSuccess"), 
                loc.Get("GameProfiles.AddSuccessMsgSmart", fileName, typeName, recommendedOps.Count));
        }
    }

    private void EditGameButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string profileId)
        {
            var profile = _profileManager.Profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile == null) return;

            var availableOps = GetAvailableOptimizations();
            var dialog = new GameProfileEditDialog(profile, availableOps) { Owner = Window.GetWindow(this) };
            dialog.ShowDialog();

            if (dialog.IsSaved)
            {
                var loc = LocalizationService.Instance;
                _profileManager.Update(profile);
                RefreshGameList();
                InfoDialog.Show(Window.GetWindow(this), loc.Get("GameProfiles.SaveSuccess"), 
                    loc.Get("GameProfiles.SaveSuccessMsg", profile.GameName));
            }
        }
    }

    private List<OptimizationItemInfo> GetAvailableOptimizations()
    {
        var isEn = LocalizationService.Instance.CurrentLanguage == "en-US";
        var result = new List<OptimizationItemInfo>();
        
        try
        {
            var mainWindow = Window.GetWindow(this) as MainWindow;
            if (mainWindow?.DataContext is ViewModels.DashboardViewModel vm)
            {
                foreach (var module in vm.OptimizationModules)
                {
                    foreach (var toggle in module.Toggles)
                    {
                        result.Add(new OptimizationItemInfo
                        {
                            OperationId = toggle.OperationId ?? "",
                            Title = toggle.Title ?? "",
                            Description = toggle.Description ?? "",
                            ModuleName = module.Title ?? "",
                            RiskLevel = toggle.RiskLevel switch
                            {
                                Core.Optimization.OperationRiskLevel.Safe => "Safe",
                                Core.Optimization.OperationRiskLevel.Balanced => "Medium",
                                Core.Optimization.OperationRiskLevel.Extreme => "High",
                                _ => "Safe"
                            }
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GameProfilesPage] GetAvailableOptimizations error: {ex.Message}");
        }
        
        if (result.Count == 0)
            result = GetFallbackOptimizations(isEn);
        
        return result;
    }
    
    // 备用静态优化项列表（当无法从ViewModel获取时使用）
    private static List<OptimizationItemInfo> GetFallbackOptimizations(bool isEn)
    {
        var loc = LocalizationService.Instance;
        
        // 定义优化项的基础数据
        var items = new (string Id, string TitleZh, string TitleEn, string DescZh, string DescEn, string ModuleZh, string ModuleEn, string Risk)[]
        {
            ("gpu.enable_hags", "显卡硬件加速调度", "Hardware-Accelerated GPU Scheduling", "让显卡自己管理显存，减少CPU干预", "Let GPU manage its own memory, reduce CPU intervention", "图形渲染", "Graphics", "Safe"),
            ("gpu.force_high_performance_gpu", "全局优先独显", "Prefer Discrete GPU", "强制所有程序使用独立显卡", "Force all programs to use discrete GPU", "图形渲染", "Graphics", "Medium"),
            ("gpu.set_sfio_priority", "游戏读写优先", "Game I/O Priority", "优先处理游戏的数据读写请求", "Prioritize game data read/write requests", "图形渲染", "Graphics", "High"),
            ("network.disable_nagle", "关闭数据包合并", "Disable Nagle Algorithm", "禁用Nagle算法，减少网络延迟", "Disable Nagle algorithm to reduce network latency", "网络优化", "Network", "Safe"),
            ("network.disable_auto_tuning", "稳定网络延迟", "Stable Network Latency", "禁用TCP自动调优，保持稳定延迟", "Disable TCP auto-tuning for stable latency", "网络优化", "Network", "Medium"),
            ("network.enable_ctcp", "优化网络拥堵处理", "Optimize Congestion Control", "启用复合TCP拥塞控制算法", "Enable Compound TCP congestion control", "网络优化", "Network", "Safe"),
            ("network.enable_rss", "多核网络加速", "Multi-Core Network Acceleration", "启用接收端缩放，多核处理网络", "Enable RSS for multi-core network processing", "网络优化", "Network", "Safe"),
            ("cpu.high_precision_timer", "高精度计时器", "High Precision Timer", "启用高精度系统计时器", "Enable high precision system timer", "处理器", "Processor", "Safe"),
            ("gaming.disable_game_dvr", "关闭后台录制", "Disable Game DVR", "禁用Xbox Game DVR后台录制", "Disable Xbox Game DVR background recording", "游戏模式", "Gaming Mode", "Safe"),
            ("gaming.game_mode", "Windows游戏模式", "Windows Game Mode", "启用Windows内置游戏模式", "Enable Windows built-in game mode", "游戏模式", "Gaming Mode", "Safe"),
            ("services.enable_high_performance_mode", "Windows 性能模式（按设备能力）", "Windows Performance Mode (capability matched)", "仅在传统交流供电桌面且系统实际提供内置高性能计划时切换；否则显示引导", "Switches only on a traditional AC desktop when Windows exposes its built-in High Performance plan; otherwise guidance is shown", "性能加速", "Performance", "Conditional"),
            ("system.disable_superfetch", "禁用预读取服务", "Disable Superfetch", "禁用Superfetch/SysMain服务", "Disable Superfetch/SysMain service", "系统调校", "System Tuning", "Medium")
        };

        return items.Select(i => new OptimizationItemInfo
        {
            OperationId = i.Id,
            Title = isEn ? i.TitleEn : i.TitleZh,
            Description = isEn ? i.DescEn : i.DescZh,
            ModuleName = isEn ? i.ModuleEn : i.ModuleZh,
            RiskLevel = i.Risk
        }).ToList();
    }

    private async void LaunchGameButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string profileId) return;
        
        var profile = _profileManager.Profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile == null) return;

        var loc = LocalizationService.Instance;
        
        // FIX-R7-#1: 用局部变量控制本次是否执行优化，避免污染持久化模型
        var shouldOptimize = profile.AutoOptimizeOnLaunch && profile.EnabledOperationIds.Count > 0;
        
        // P1-GAME-001: 权限检查 - 游戏优化和启动可能需要管理员权限
        if (shouldOptimize)
        {
            if (!GameOptimizer.Core.Services.PermissionChecker.IsRunningAsAdministrator())
            {
                var continueWithoutOptimization = ConfirmDialog.Show(
                    Window.GetWindow(this),
                    loc.Get("Common.AdminRequired"),
                    loc.Get("GameProfiles.AdminRequiredForOptimization"),
                    IconGlyphs.Warning);
                
                if (!continueWithoutOptimization)
                {
                    return;
                }
                // 用户选择继续但不优化，仅本次跳过（不修改 profile 对象）
                shouldOptimize = false;
            }
        }
        
        try
        {
            _profileManager.RecordUsage(profileId);
            RefreshGameList();

            int successCount = 0;
            int failedCount = 0;

            if (shouldOptimize)
            {
                (successCount, failedCount) = await ApplyProfileOptimizationsAsync(profile);
            }

            if (!System.IO.File.Exists(profile.ExecutablePath))
            {
                InfoDialog.Show(Window.GetWindow(this), loc.Get("Common.Error"), 
                    loc.Get("GameProfiles.ExeNotFound", profile.ExecutablePath), IconGlyphs.Error);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = profile.ExecutablePath,
                UseShellExecute = true
            });

            // P2-GAME-003: 游戏退出后自动恢复优化
            if (profile.RestoreOnExit && shouldOptimize && successCount > 0)
            {
                _ = MonitorGameProcessAndRestoreAsync(profile, System.IO.Path.GetFileNameWithoutExtension(profile.ExecutablePath));
            }

            var msg = loc.Get("GameProfiles.LaunchSuccess", profile.GameName);
            if (shouldOptimize)
            {
                if (failedCount == 0)
                {
                    msg += "\n" + loc.Get("GameProfiles.OptimizationsApplied", successCount);
                }
                else if (successCount > 0)
                {
                    msg += "\n" + loc.Get("GameProfiles.OptimizationsPartial", successCount, failedCount);
                }
                else
                {
                    msg += "\n" + loc.Get("GameProfiles.OptimizationsFailed", failedCount);
                }
            }
            
            var icon = failedCount > 0 ? IconGlyphs.Warning : IconGlyphs.Success;
            InfoDialog.Show(Window.GetWindow(this), loc.Get("Common.Success"), msg, icon);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Debug.WriteLine($"[GameProfiles] Launch failed (Win32): {ex}");
            InfoDialog.Show(Window.GetWindow(this), loc.Get("Common.Error"), 
                loc.Get("GameProfiles.LaunchFailedPermission"), IconGlyphs.Error);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GameProfiles] Launch failed: {ex}");
            InfoDialog.Show(Window.GetWindow(this), loc.Get("Common.Error"), 
                loc.Get("GameProfiles.LaunchFailed", ex.Message), IconGlyphs.Error);
        }
    }

    private async Task<(int Success, int Failed)> ApplyProfileOptimizationsAsync(GameProfile profile)
    {
        try
        {
            var mainWindow = Window.GetWindow(this) as MainWindow;
            if (mainWindow?.DataContext is not ViewModels.DashboardViewModel vm) 
                return (0, profile.EnabledOperationIds.Count);

            var moduleOps = new Dictionary<string, List<string>>();
            foreach (var opId in profile.EnabledOperationIds)
            {
                foreach (var module in vm.OptimizationModules)
                {
                    var toggle = module.Toggles.FirstOrDefault(t => t.OperationId == opId);
                    if (toggle != null)
                    {
                        if (!moduleOps.ContainsKey(module.Key))
                            moduleOps[module.Key] = new List<string>();
                        moduleOps[module.Key].Add(opId);
                        break;
                    }
                }
            }

            int successCount = 0;
            int failedCount = 0;

            foreach (var kvp in moduleOps)
            {
                try
                {
                    await vm.ApplyOperationsAsync(kvp.Key, kvp.Value);
                    successCount += kvp.Value.Count;
                }
                catch (Exception ex)
                {
                    failedCount += kvp.Value.Count;
                    Debug.WriteLine($"[GameProfiles] 应用优化失败 ({kvp.Key}): {ex.Message}");
                }
            }

            return (successCount, failedCount);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GameProfiles] ApplyProfileOptimizationsAsync error: {ex.Message}");
            return (0, profile.EnabledOperationIds.Count);
        }
    }

    private void DeleteGameButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string profileId)
        {
            var profile = _profileManager.Profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile == null) return;

            var loc = LocalizationService.Instance;
            if (ConfirmDialog.Show(Window.GetWindow(this), 
                loc.Get("GameProfiles.ConfirmDelete"), 
                loc.Get("GameProfiles.ConfirmDeleteMsg", profile.GameName), IconGlyphs.Warning))
            {
                _profileManager.Remove(profileId);
                RefreshGameList();
            }
        }
    }

    private async void ScanGamesButton_Click(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationService.Instance;
        ScanGamesBtn.IsEnabled = false;
        if (FindName("ScanButtonText") is TextBlock scanText)
            scanText.Text = loc.Get("GameProfiles.Scanning");

        try
        {
            var detectedGames = await _scanner.ScanAllLibrariesAsync();
            
            if (detectedGames.Count == 0)
            {
                InfoDialog.Show(Window.GetWindow(this), loc.Get("GameProfiles.ScanComplete"), 
                    loc.Get("GameProfiles.NoGamesFound"));
                return;
            }

            var newGames = detectedGames.Where(g => 
                _profileManager.FindByExecutable(g.ExecutablePath) == null).ToList();

            if (newGames.Count == 0)
            {
                InfoDialog.Show(Window.GetWindow(this), loc.Get("GameProfiles.ScanComplete"), 
                    loc.Get("GameProfiles.AllGamesAdded", detectedGames.Count));
                return;
            }

            var dialog = new GameScanResultDialog(newGames) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true && dialog.SelectedGames.Count > 0)
            {
                foreach (var game in dialog.SelectedGames)
                {
                    var detectedType = GameTypeDetector.DetectType(game.Name, game.ExecutablePath);
                    var recommendedOps = GameOptimizationPresets.GetRecommendedOperations(detectedType);
                    
                    var profile = new GameProfile
                    {
                        GameName = game.Name,
                        ExecutablePath = game.ExecutablePath,
                        GameType = detectedType,
                        AutoOptimizeOnLaunch = true,
                        RestoreOnExit = true,
                        EnabledOperationIds = new List<string>(recommendedOps)
                    };
                    _profileManager.Add(profile);
                }
                
                RefreshGameList();
                InfoDialog.Show(Window.GetWindow(this), loc.Get("GameProfiles.AddSuccess"), 
                    loc.Get("GameProfiles.GamesAddedSmart", dialog.SelectedGames.Count));
            }
        }
        catch (Exception ex)
        {
            InfoDialog.Show(Window.GetWindow(this), loc.Get("Common.Error"), 
                loc.Get("GameProfiles.ScanFailed", ex.Message), IconGlyphs.Error);
        }
        finally
        {
            ScanGamesBtn.IsEnabled = true;
            if (FindName("ScanButtonText") is TextBlock scanBtnText)
                scanBtnText.Text = loc.Get("GameProfiles.ScanAdd");
        }
    }
    
    private async Task MonitorGameProcessAndRestoreAsync(GameProfile profile, string processName)
    {
        try
        {
            Debug.WriteLine($"[GameProfiles] Starting process monitoring for: {processName}");
            
            await Task.Run(async () =>
            {
                await Task.Delay(2000);
                
                var gameProcesses = Process.GetProcessesByName(processName);
                if (gameProcesses.Length == 0)
                {
                    Debug.WriteLine($"[GameProfiles] Process {processName} not found, monitoring cancelled");
                    return;
                }
                
                var gameProcess = gameProcesses[0];
                Debug.WriteLine($"[GameProfiles] Monitoring process {processName} (PID: {gameProcess.Id})");
                
                try
                {
                    await gameProcess.WaitForExitAsync();
                    Debug.WriteLine($"[GameProfiles] Process {processName} exited, restoring optimizations...");
                    
                    await Dispatcher.InvokeAsync(async () =>
                    {
                        try
                        {
                            var mainWindow = Window.GetWindow(this) as MainWindow;
                            if (mainWindow?.DataContext is ViewModels.DashboardViewModel vm)
                            {
                                var moduleOps = new Dictionary<string, List<string>>();
                                foreach (var opId in profile.EnabledOperationIds)
                                {
                                    foreach (var module in vm.OptimizationModules)
                                    {
                                        var toggle = module.Toggles.FirstOrDefault(t => t.OperationId == opId);
                                        if (toggle != null)
                                        {
                                            if (!moduleOps.ContainsKey(module.Key))
                                                moduleOps[module.Key] = new List<string>();
                                            moduleOps[module.Key].Add(opId);
                                            break;
                                        }
                                    }
                                }
                                
                                foreach (var kvp in moduleOps)
                                {
                                    try
                                    {
                                        await vm.RestoreOperationsAsync(kvp.Key, kvp.Value);
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.WriteLine($"[GameProfiles] Restore failed ({kvp.Key}): {ex.Message}");
                                    }
                                }
                                
                                var loc = LocalizationService.Instance;
                                ToastService.Instance.Success(
                                    loc.Get("GameProfiles.Title"),
                                    loc.Get("GameProfiles.RestoreComplete"));
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[GameProfiles] Restore error: {ex.Message}");
                            var loc = LocalizationService.Instance;
                            ToastService.Instance.Warning(
                                loc.Get("GameProfiles.Title"),
                                loc.Get("GameProfiles.RestoreFailed", ex.Message));
                        }
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[GameProfiles] Process monitoring error: {ex.Message}");
                }
                finally
                {
                    foreach (var p in gameProcesses)
                    {
                        p.Dispose();
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GameProfiles] MonitorGameProcessAndRestoreAsync error: {ex.Message}");
        }
    }
}
