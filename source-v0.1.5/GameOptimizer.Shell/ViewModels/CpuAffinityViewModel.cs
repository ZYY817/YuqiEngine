using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Services.CpuAffinity;
using GameOptimizer.Core.Services.ProcessTuning;
using GameOptimizer.Shell.Services;

namespace GameOptimizer.Shell.ViewModels
{
    public partial class ProfileViewModel : ObservableObject
    {
        public ProcessAffinityProfile Profile { get; }
        
        [ObservableProperty] private ProfileStatus _status;
        [ObservableProperty] private string _statusText = "";
        [ObservableProperty] private string _statusColor = "#71717A";

        public string ProcessName => Profile.ProcessName;
        public string DisplayName => Profile.DisplayName;
        public string Icon => Profile.Icon;
        public string AffinityDescription => Profile.AffinityDescription;
        public ProcessType Type => Profile.Type;

        public ProfileViewModel(ProcessAffinityProfile profile, ProfileStatus status)
        {
            Profile = profile;
            UpdateStatus(status);
        }

        public void UpdateStatus(ProfileStatus status)
        {
            var loc = LocalizationService.Instance;
            Status = status;
            (StatusText, StatusColor) = status switch
            {
                ProfileStatus.Applied => (loc.Get("CpuAffinity.StatusApplied"), "#22C55E"),
                ProfileStatus.Pending => (loc.Get("CpuAffinity.StatusPending"), "#F59E0B"),
                ProfileStatus.Unknown => ("状态未知（读取被拒绝）", "#F59E0B"),
                _ => (loc.Get("CpuAffinity.StatusNotRunning"), "#71717A")
            };
        }
    }

    public partial class CpuAffinityViewModel : ObservableObject
    {
        private readonly IProcessAffinityService _affinityService;
        private readonly IProcessTuningOwner _processTuningOwner;
        private readonly List<ActiveProcessTuningReceipt> _activeReceipts = [];

        [ObservableProperty] private CpuInfo? _cpuInfo;
        [ObservableProperty] private string _statusText = "";
        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private bool _hasProfiles;
        [ObservableProperty] private string _cpuDisplayName = string.Empty;
        [ObservableProperty] private string _coreCountDisplay = string.Empty;
        [ObservableProperty] private bool _isHybridCpu;
        [ObservableProperty] private int _pCoreCount;
        [ObservableProperty] private int _eCoreCount;

        public ObservableCollection<ProfileViewModel> Profiles { get; } = new();
        public ObservableCollection<RunningProcess> RunningProcesses { get; } = new();

        public CpuAffinityViewModel(IProcessAffinityService affinityService, IProcessTuningOwner processTuningOwner)
        {
            _affinityService = affinityService ?? throw new ArgumentNullException(nameof(affinityService));
            _processTuningOwner = processTuningOwner ?? throw new ArgumentNullException(nameof(processTuningOwner));
            _statusText = LocalizationService.Instance.Get("CpuAffinity.Ready");
        }

        [RelayCommand]
        private async Task LoadDataAsync()
        {
            var loc = LocalizationService.Instance;
            IsLoading = true;
            try
            {
                CpuInfo = _affinityService.GetCpuInfo();
                CpuDisplayName = CpuInfo.Name;
                
                IsHybridCpu = CpuInfo.HasHybridArchitecture;
                if (IsHybridCpu)
                {
                    PCoreCount = CpuInfo.Threads.Count(t => 
                        t.CoreType == CpuCoreType.PerformanceCore || t.CoreType == CpuCoreType.CacheCore);
                    ECoreCount = CpuInfo.Threads.Count(t => 
                        t.CoreType == CpuCoreType.EfficiencyCore || t.CoreType == CpuCoreType.ComputeCore);
                    CoreCountDisplay = loc.Get("CpuAffinity.CoreCountHybrid", PCoreCount, ECoreCount);
                }
                else
                {
                    CoreCountDisplay = loc.Get("CpuAffinity.CoreCountThreads", CpuInfo.LogicalProcessors);
                }

                await LoadProfilesAsync();
                StatusText = HasProfiles ? loc.Get("CpuAffinity.ConfiguredCount", Profiles.Count) : loc.Get("CpuAffinity.NoConfigStatus");
            }
            catch (Exception ex)
            {
                StatusText = loc.Get("CpuAffinity.LoadFailed", ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadProfilesAsync()
        {
            Profiles.Clear();
            var profiles = await _affinityService.GetProfilesAsync();
            foreach (var p in profiles)
            {
                Profiles.Add(new ProfileViewModel(p, ProfileStatus.NotRunning));
            }
            HasProfiles = Profiles.Count > 0;
            
            // 异步刷新状态，不阻塞 UI，使用 BeginInvoke 避免死锁
            if (HasProfiles)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // 添加小延迟，让 UI 先渲染完成
                        await Task.Delay(100);
                        
                        foreach (var pvm in Profiles.ToList())
                        {
                            try
                            {
                                var status = _affinityService.CheckProfileStatus(pvm.Profile);
                                // 使用 BeginInvoke 而不是 Invoke，避免阻塞后台线程
                                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => pvm.UpdateStatus(status));
                            }
                            catch
                            {
                                // 忽略单个配置的状态检查失败
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CpuAffinityViewModel] 后台刷新状态失败: {ex.Message}");
                    }
                });
            }
        }

        [RelayCommand]
        private async Task RefreshStatusAsync()
        {
            await Task.Run(async () =>
            {
                foreach (var pvm in Profiles.ToList())
                {
                    try
                    {
                        var status = _affinityService.CheckProfileStatus(pvm.Profile);
                        // 使用 BeginInvoke 而不是 Invoke
                        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => pvm.UpdateStatus(status));
                        // 添加小延迟，避免过快的 UI 更新
                        await Task.Delay(10);
                    }
                    catch
                    {
                        // 忽略单个配置的状态检查失败
                    }
                }
            });
        }

        [RelayCommand]
        private async Task AddProcessAsync()
        {
        // 在后台线程获取进程列表，避免 UI 卡顿
            IsLoading = true;
            var loc = LocalizationService.Instance;
            StatusText = loc.Get("CpuAffinity.GettingProcessList");
            
            try
            {
                var processes = await Task.Run(() => _affinityService.GetRunningProcesses());
                
                RunningProcesses.Clear();
                foreach (var p in processes)
                    RunningProcesses.Add(p);

                var dialog = new Views.ProcessSelectorDialog(RunningProcesses.ToList());
                if (dialog.ShowDialog() == true && dialog.SelectedProcess != null)
                {
                    var selected = dialog.SelectedProcess;
                    var type = GuessProcessType(selected.Name);
                    var profile = _affinityService.CreateRecommendedProfile(selected.Name + ".exe", type);
                    profile.DisplayName = selected.Name;
                    profile.Icon = selected.Icon;

                    await _affinityService.SaveProfileAsync(profile);
                    await LoadProfilesAsync();
                    
                    await ApplyProfileThroughOwnerAsync(profile, selected.Pid);
                    ToastService.Instance.Success(loc.Get("CpuAffinity.AddProgram"), loc.Get("CpuAffinity.AddedToast", profile.DisplayName, profile.AffinityDescription));
                    StatusText = loc.Get("CpuAffinity.Added", profile.DisplayName);
                }
                else
                {
                    StatusText = HasProfiles ? loc.Get("CpuAffinity.ConfiguredCount", Profiles.Count) : loc.Get("CpuAffinity.NoConfigStatus");
                }
            }
            catch (Exception ex)
            {
                StatusText = loc.Get("CpuAffinity.GetProcessFailed", ex.Message);
                ToastService.Instance.Error(loc.Get("Common.Error"), loc.Get("CpuAffinity.GetProcessFailedToast"));
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task DeleteProfileAsync(ProfileViewModel? pvm)
        {
            if (pvm == null) return;
            var loc = LocalizationService.Instance;

            await _affinityService.DeleteProfileAsync(pvm.ProcessName);
            await LoadProfilesAsync();
            ToastService.Instance.Info(loc.Get("CpuAffinity.Delete"), loc.Get("CpuAffinity.DeletedToast", pvm.DisplayName));
            StatusText = loc.Get("CpuAffinity.Deleted", pvm.DisplayName);
        }

        [RelayCommand]
        private async Task ApplyAllAsync()
        {
            var loc = LocalizationService.Instance;
            if (!HasProfiles)
            {
                StatusText = loc.Get("CpuAffinity.NoConfig");
                return;
            }

            IsLoading = true;
            StatusText = loc.Get("CpuAffinity.Applying");
            try
            {
                var appliedCount = 0;
                foreach (var profile in Profiles.Where(profile => profile.Profile.IsEnabled).ToList())
                {
                    if (await ApplyProfileThroughOwnerAsync(profile.Profile))
                    {
                        appliedCount++;
                    }
                }
                await RefreshStatusAsync();
                if (appliedCount == Profiles.Count)
                {
                    ToastService.Instance.Success(loc.Get("Common.Success"), loc.Get("CpuAffinity.AppliedAll", Profiles.Count));
                    StatusText = loc.Get("CpuAffinity.AppliedAll", Profiles.Count);
                }
                else
                {
                    StatusText = $"{appliedCount}/{Profiles.Count} 个配置已通过 Owner 验证；其余已阻断。";
                }
            }
            catch (Exception ex)
            {
                StatusText = loc.Get("CpuAffinity.ApplyFailed", ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task ClearAllProfilesAsync()
        {
            if (!HasProfiles) return;
            var loc = LocalizationService.Instance;

            var count = Profiles.Count;
            foreach (var pvm in Profiles.ToList())
            {
                await _affinityService.DeleteProfileAsync(pvm.ProcessName);
            }

            await LoadProfilesAsync();
            ToastService.Instance.Info(loc.Get("CpuAffinity.ClearedTitle"), loc.Get("CpuAffinity.ClearedToast", count));
            StatusText = loc.Get("CpuAffinity.Cleared", count);
        }

        [RelayCommand]
        private async Task ToggleProfileAsync(ProfileViewModel? pvm)
        {
            if (pvm == null) return;
            var loc = LocalizationService.Instance;

            pvm.Profile.IsEnabled = !pvm.Profile.IsEnabled;
            await _affinityService.SaveProfileAsync(pvm.Profile);

            if (pvm.Profile.IsEnabled)
            {
                await ApplyProfileThroughOwnerAsync(pvm.Profile);
                pvm.UpdateStatus(_affinityService.CheckProfileStatus(pvm.Profile));
                StatusText = loc.Get("CpuAffinity.Enabled", pvm.DisplayName);
            }
            else
            {
                StatusText = loc.Get("CpuAffinity.Disabled", pvm.DisplayName);
            }
        }

        [RelayCommand]
        private async Task ApplySingleProfileAsync(ProfileViewModel? pvm)
        {
            if (pvm == null) return;
            var loc = LocalizationService.Instance;

            var applied = await ApplyProfileThroughOwnerAsync(pvm.Profile);
            pvm.UpdateStatus(_affinityService.CheckProfileStatus(pvm.Profile));
            
            if (applied)
            {
                ToastService.Instance.Success(loc.Get("CpuAffinity.AppliedTitle"), loc.Get("CpuAffinity.AppliedToast", pvm.DisplayName));
                StatusText = loc.Get("CpuAffinity.AppliedSingle", pvm.DisplayName);
            }
            else
            {
                ToastService.Instance.Warning(loc.Get("CpuAffinity.NotFoundTitle"), loc.Get("CpuAffinity.NotRunningToast", pvm.DisplayName));
                StatusText = loc.Get("CpuAffinity.NotRunning", pvm.DisplayName);
            }
        }

        [RelayCommand]
        private async Task EditProfileAsync(ProfileViewModel? pvm)
        {
            if (pvm == null || CpuInfo == null) return;
            var loc = LocalizationService.Instance;

            var dialog = new Views.AffinityEditDialog(pvm.Profile, CpuInfo);
            if (dialog.ShowDialog() == true)
            {
                pvm.Profile.AssignedThreads = dialog.SelectedThreads.ToList();
                pvm.Profile.AffinityMask = _affinityService.CalculateAffinityMask(pvm.Profile.AssignedThreads);
                pvm.Profile.AffinityDescription = GetAffinityDescription(pvm.Profile.AssignedThreads);

                await _affinityService.SaveProfileAsync(pvm.Profile);
                await ApplyProfileThroughOwnerAsync(pvm.Profile);
                
                await LoadProfilesAsync();
                ToastService.Instance.Success(loc.Get("CpuAffinity.UpdatedTitle"), loc.Get("CpuAffinity.UpdatedToast", pvm.DisplayName));
                StatusText = loc.Get("CpuAffinity.Updated", pvm.DisplayName);
            }
        }

        private string GetAffinityDescription(System.Collections.Generic.List<int> threads)
        {
            var loc = LocalizationService.Instance;
            if (CpuInfo == null) return loc.Get("CpuAffinity.CoreCountDesc", threads.Count);

            if (IsHybridCpu)
            {
                var pCount = threads.Count(t => CpuInfo.Threads[t].CoreType == CpuCoreType.PerformanceCore);
                var eCount = threads.Count(t => CpuInfo.Threads[t].CoreType == CpuCoreType.EfficiencyCore);

                if (pCount > 0 && eCount == 0) return loc.Get("CpuAffinity.POnlyDesc", pCount);
                if (eCount > 0 && pCount == 0) return loc.Get("CpuAffinity.EOnlyDesc", eCount);
                return loc.Get("CpuAffinity.MixedDesc", pCount, eCount);
            }

            if (threads.Count == CpuInfo.LogicalProcessors)
                return loc.Get("CpuAffinity.AllCoresDesc");

            var coreList = string.Join(",", threads.Take(4)) + (threads.Count > 4 ? "..." : "");
            return loc.Get("CpuAffinity.CoreListDesc", coreList);
        }

        private async Task<bool> ApplyProfileThroughOwnerAsync(ProcessAffinityProfile profile, int? requestedPid = null)
        {
            if (!profile.IsEnabled || profile.AffinityMask <= 0)
            {
                return false;
            }

            var pid = requestedPid ?? ResolveExactlyOneRunningProcess(profile);
            if (pid is null)
            {
                StatusText = "目标进程不存在、存在多个同名实例或身份不可唯一确定，已阻断。";
                return false;
            }

            var result = await _processTuningOwner.RunInteractiveAsync(
                ProcessTuningRequest.Affinity(pid.Value, profile.AffinityMask)).ConfigureAwait(true);
            if (result.IsTerminalSuccess)
            {
                if (result.Receipt is not null)
                {
                    _activeReceipts.Add(new ActiveProcessTuningReceipt(pid.Value, result.Receipt.ReceiptId));
                }
                return true;
            }

            StatusText = result.Message;
            return false;
        }

        public async Task DeactivateAsync()
        {
            var conflictedProcesses = new HashSet<int>();
            for (var index = _activeReceipts.Count - 1; index >= 0; index--)
            {
                var active = _activeReceipts[index];
                if (conflictedProcesses.Contains(active.ProcessId))
                {
                    continue;
                }

                var result = await _processTuningOwner.RestoreAsync(
                    new GameOptimizer.Core.Contracts.Changes.RestoreRequest(active.ReceiptId)).ConfigureAwait(true);
                if (result.IsTerminalSuccess || result.Kind == ProcessTuningResultKind.NotApplicable)
                {
                    _activeReceipts.RemoveAt(index);
                    continue;
                }

                conflictedProcesses.Add(active.ProcessId);
                StatusText = result.Message;
            }
        }

        private int? ResolveExactlyOneRunningProcess(ProcessAffinityProfile profile)
        {
            var expectedName = System.IO.Path.GetFileNameWithoutExtension(profile.ProcessName);
            var matches = _affinityService.GetRunningProcesses()
                .Where(process => string.Equals(process.Name, expectedName, StringComparison.OrdinalIgnoreCase))
                .Select(process => process.Pid)
                .Distinct()
                .Take(2)
                .ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        private sealed record ActiveProcessTuningReceipt(int ProcessId, Guid ReceiptId);

        private static ProcessType GuessProcessType(string processName)
        {
            var name = processName.ToLowerInvariant();

            // 游戏进程识别 - 包含常见游戏和游戏平台
            if (name.Contains("game") || name.Contains("valorant") || name.Contains("csgo") ||
                name.Contains("apex") || name.Contains("fortnite") || name.Contains("pubg") ||
                name.Contains("overwatch") || name.Contains("league") || name.Contains("dota") ||
                name.Contains("genshin") || name.Contains("原神") ||
                name.Contains("deltaforce") || name.Contains("delta") || name.Contains("三角洲") ||
                name.Contains("client-win") || name.Contains("shipping") ||
                name.Contains("warzone") || name.Contains("callofduty") || name.Contains("cod") ||
                name.Contains("minecraft") || name.Contains("roblox") || name.Contains("gta") ||
                name.Contains("cyberpunk") || name.Contains("eldenring") || name.Contains("darksouls") ||
                name.Contains("diablo") || name.Contains("pathofexile") || name.Contains("poe") ||
                name.Contains("lostark") || name.Contains("newworld") || name.Contains("destiny") ||
                name.Contains("rainbow") || name.Contains("r6") || name.Contains("siege") ||
                name.Contains("tarkov") || name.Contains("escape") || name.Contains("hunt") ||
                name.Contains("dayz") || name.Contains("rust") || name.Contains("ark") ||
                name.Contains("wow") || name.Contains("warcraft") || name.Contains("ffxiv") ||
                name.Contains("naraka") || name.Contains("永劫") ||
                name.Contains("崩坏") || name.Contains("honkai") || name.Contains("starrail") ||
                name.Contains("wuthering") || name.Contains("鸣潮") || name.Contains("zenless") ||
                name.Contains("绝区零"))
                return ProcessType.Game;

            if (name.Contains("chrome") || name.Contains("firefox") || name.Contains("edge") ||
                name.Contains("opera") || name.Contains("brave"))
                return ProcessType.Browser;

            if (name.Contains("discord") || name.Contains("telegram") || name.Contains("slack") ||
                name.Contains("teams") || name.Contains("zoom") || name.Contains("wechat") ||
                name.Contains("qq"))
                return ProcessType.Communication;

            return ProcessType.Background;
        }

        public void Initialize() => _ = LoadDataAsync().ContinueWith(t => 
        {
            if (t.IsFaulted && t.Exception != null)
                System.Diagnostics.Debug.WriteLine($"[CpuAffinityViewModel] LoadDataAsync failed: {t.Exception.InnerException?.Message}");
        }, TaskContinuationOptions.OnlyOnFaulted);
    }
}
