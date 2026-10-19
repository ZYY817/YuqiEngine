// File: GameOptimizer.Shell/ViewModels/CpuCoreManagementViewModel.Profiles.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameOptimizer.Core.Models;
using GameOptimizer.Core.Services;
using GameOptimizer.Core.Services.CpuAffinity;
using GameOptimizer.Core.Services.ProcessTuning;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Services;

namespace GameOptimizer.Shell.ViewModels;

public partial class CpuCoreManagementViewModel
{
    // === 配置文件数据 ===
    public ObservableCollection<ProfileViewModel> Profiles { get; } = new();
    [ObservableProperty] private ProfileViewModel? _selectedProfile;
    [ObservableProperty] private bool _hasProfiles;

    partial void OnSelectedProfileChanged(ProfileViewModel? value)
    {
        if (ActiveTab == 1 && value != null)
            ShowProfileCoresInMatrix(value.Profile.AssignedThreads);
    }

    // === 配置文件加载 ===
    private async Task LoadProfilesAsync()
    {
        try
        {
            Profiles.Clear();
            var profiles = await _profileService.GetProfilesAsync();
            foreach (var p in profiles)
                Profiles.Add(new ProfileViewModel(p, ProfileStatus.NotRunning));

            HasProfiles = Profiles.Count > 0;

            if (!HasProfiles) return;

            await RefreshProfileStatusAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CpuCoreManagement][LoadProfiles] {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task RefreshProfileStatusAsync()
    {
        // 先在 UI 线程固定快照，后台只访问配置服务；不能从 Task.Run 直接枚举
        // ObservableCollection，否则新增/删除配置时会出现跨线程竞争。
        var profileSnapshot = Profiles.ToArray();
        var updates = await Task.Run(() =>
        {
            var results = new List<(ProfileViewModel Profile, ProfileStatus Status)>();
            foreach (var pvm in profileSnapshot)
            {
                try
                {
                    results.Add((pvm, _profileService.CheckProfileStatus(pvm.Profile)));
                }
                catch
                {
                    // 单项状态探测失败不应中断其它配置的刷新。
                }
            }
            return results;
        });

        await _dispatcher.InvokeAsync(() =>
        {
            foreach (var update in updates)
                update.Profile.UpdateStatus(update.Status);
        });
    }

    [RelayCommand]
    private async Task AddProcessAsync()
    {
        IsLoading = true;
        StatusText = _loc.Get("CpuAffinity.GettingProcessList");

        try
        {
            var processes = await Task.Run(() => _profileService.GetRunningProcesses());
            var dialog = new Views.ProcessSelectorDialog(processes.ToList());

            if (dialog.ShowDialog() == true && dialog.SelectedProcess != null)
            {
                var selected = dialog.SelectedProcess;
                var type = GuessProcessType(selected.Name);
                var profile = _profileService.CreateRecommendedProfile(selected.Name + ".exe", type);
                profile.DisplayName = selected.Name;
                profile.Icon = selected.Icon;

                await _profileService.SaveProfileAsync(profile);
                await LoadProfilesAsync();

                var applied = await ApplyProfileThroughOwnerAsync(profile, selected.Pid);
                if (applied)
                    ToastService.Instance.Success(_loc.Get("CpuAffinity.AddProgram"), _loc.Get("CpuAffinity.AddedToast", profile.DisplayName, profile.AffinityDescription));
                else
                    ToastService.Instance.Warning("配置已保存，未应用", StatusText);
            }
            else
            {
                StatusText = HasProfiles
                    ? _loc.Get("CpuAffinity.ConfiguredCount", Profiles.Count)
                    : _loc.Get("CpuAffinity.NoConfigStatus");
            }
        }
        catch (Exception ex)
        {
            StatusText = _loc.Get("CpuAffinity.GetProcessFailed", ex.Message);
            ToastService.Instance.Error(_loc.Get("Common.Error"), _loc.Get("CpuAffinity.GetProcessFailedToast"));
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
        if (pvm.Profile.ActiveReceiptId is not null)
        {
            StatusText = "该配置仍有未恢复的执行记录，请先点击恢复。";
            return;
        }
        await _profileService.DeleteProfileAsync(pvm.ProcessName);
        await LoadProfilesAsync();
        ToastService.Instance.Info(
            _loc.Get("CpuAffinity.Delete"),
            _loc.Get("CpuAffinity.DeletedToast", pvm.DisplayName));
        StatusText = _loc.Get("CpuAffinity.Deleted", pvm.DisplayName);
    }

    [RelayCommand]
    private async Task EditProfileAsync(ProfileViewModel? pvm)
    {
        if (pvm == null) return;

        // 编辑器复用当前页面已验证的 CPU 拓扑，避免再启动一套 WMI 探测而得到
        // 与核心矩阵不一致的线程清单。
        var cpuInfo = CreateEditorCpuInfo();
        if (cpuInfo is null)
        {
            StatusText = _loc.Get("CoreMgmt.TopologyReadFailed");
            ToastService.Instance.Warning(_loc.Get("Common.Error"), _loc.Get("CoreMgmt.TopologyReadFailed"));
            return;
        }

        var dialog = new Views.AffinityEditDialog(pvm.Profile, cpuInfo);
        if (dialog.ShowDialog() == true)
        {
            pvm.Profile.AssignedThreads = dialog.SelectedThreads.ToList();
            pvm.Profile.AffinityMask = _profileService.CalculateAffinityMask(pvm.Profile.AssignedThreads);
            pvm.Profile.AffinityDescription = GetAffinityDescription(pvm.Profile.AssignedThreads, cpuInfo);

            await _profileService.SaveProfileAsync(pvm.Profile);

            var applied = await ApplyProfileThroughOwnerAsync(pvm.Profile);

            await LoadProfilesAsync();
            if (applied)
                ToastService.Instance.Success(_loc.Get("CpuAffinity.UpdatedTitle"), _loc.Get("CpuAffinity.UpdatedToast", pvm.DisplayName));
            else
                ToastService.Instance.Warning("配置已更新，未应用", StatusText);
        }
    }

    [RelayCommand]
    private async Task ApplyAllProfilesAsync()
    {
        if (!HasProfiles)
        {
            StatusText = _loc.Get("CpuAffinity.NoConfig");
            return;
        }

        IsLoading = true;
        StatusText = _loc.Get("CpuAffinity.Applying");

        try
        {
            var skippedCount = 0;
            var succeededCount = 0;
            var failedCount = 0;
            foreach (var pvm in Profiles.ToList())
            {
                if (!pvm.Profile.IsEnabled) continue;

                // Why: 并发冲突 — 跳过正在实时加速的进程
                if (!string.IsNullOrEmpty(_acceleratedProcessName) &&
                    (pvm.ProcessName.Equals(_acceleratedProcessName + ".exe", StringComparison.OrdinalIgnoreCase) ||
                     pvm.ProcessName.Equals(_acceleratedProcessName, StringComparison.OrdinalIgnoreCase)))
                {
                    skippedCount++;
                    continue;
                }

                if (await ApplyProfileThroughOwnerAsync(pvm.Profile)) succeededCount++;
                else failedCount++;
            }

            await RefreshProfileStatusAsync();

            if (failedCount > 0 || skippedCount > 0)
            {
                ToastService.Instance.Warning(
                    _loc.Get("CoreMgmt.ConflictWarning"),
                    $"已验证应用 {succeededCount} 项，失败 {failedCount} 项，跳过 {skippedCount} 项。");
            }
            else
            {
                ToastService.Instance.Success(
                    _loc.Get("Common.Success"),
                    _loc.Get("CpuAffinity.AppliedAll", succeededCount));
            }

            StatusText = $"已验证应用 {succeededCount} 项，失败 {failedCount} 项，跳过 {skippedCount} 项。";
        }
        catch (Exception ex)
        {
            StatusText = _loc.Get("CpuAffinity.ApplyFailed", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
        private async Task ApplySingleProfileAsync(ProfileViewModel? pvm)
    {
        if (pvm == null) return;

            var applied = await ApplyProfileThroughOwnerAsync(pvm.Profile);
        pvm.UpdateStatus(_profileService.CheckProfileStatus(pvm.Profile));

        if (applied)
        {
            ToastService.Instance.Success(
                _loc.Get("CpuAffinity.AppliedTitle"),
                _loc.Get("CpuAffinity.AppliedToast", pvm.DisplayName));
            StatusText = _loc.Get("CpuAffinity.AppliedSingle", pvm.DisplayName);
        }
        else
        {
            ToastService.Instance.Warning(
                _loc.Get("CpuAffinity.NotFoundTitle"),
                _loc.Get("CpuAffinity.NotRunningToast", pvm.DisplayName));
            StatusText = _loc.Get("CpuAffinity.NotRunning", pvm.DisplayName);
        }
    }

    [RelayCommand]
        private async Task ToggleProfileAsync(ProfileViewModel? pvm)
    {
        if (pvm == null) return;

        pvm.Profile.IsEnabled = !pvm.Profile.IsEnabled;
        await _profileService.SaveProfileAsync(pvm.Profile);

            if (pvm.Profile.IsEnabled)
            {
                await ApplyProfileThroughOwnerAsync(pvm.Profile);
            pvm.UpdateStatus(_profileService.CheckProfileStatus(pvm.Profile));
            StatusText = _loc.Get("CpuAffinity.Enabled", pvm.DisplayName);
        }
        else
        {
            StatusText = _loc.Get("CpuAffinity.Disabled", pvm.DisplayName);
        }
    }

    private async Task<bool> ApplyProfileThroughOwnerAsync(ProcessAffinityProfile profile, int? requestedPid = null)
    {
        if (profile.ActiveReceiptId is not null)
        {
            StatusText = "该配置已有未恢复的执行记录，请先恢复后再应用。";
            return false;
        }
        if (!profile.IsEnabled || profile.AffinityMask <= 0)
            return false;

        int? pid = requestedPid ?? ResolveExactlyOneRunningProcess(profile);
        if (pid is null)
        {
            StatusText = "目标进程不存在、存在多个同名实例或身份不可唯一确定，已阻断。";
            return false;
        }

        ProcessTuningResult result = await _processTuningOwner.RunInteractiveAsync(
            ProcessTuningRequest.Affinity(pid.Value, profile.AffinityMask));
        if (result.IsTerminalSuccess)
        {
            if (result.Receipt is not null)
            {
                profile.ActiveReceiptId = result.Receipt.ReceiptId;
                profile.ActiveProcessId = pid.Value;
                await _profileService.SaveProfileAsync(profile);
            }
            return true;
        }

        StatusText = result.Message;
        return false;
    }

    [RelayCommand]
    private async Task RestoreProfileAsync(ProfileViewModel? pvm)
    {
        if (pvm?.Profile.ActiveReceiptId is not Guid receiptId)
        {
            StatusText = "该配置没有可恢复的已验证执行记录。";
            return;
        }

        var result = await _processTuningOwner.RestoreAsync(new GameOptimizer.Core.Contracts.Changes.RestoreRequest(receiptId));
        if (result.Kind is ProcessTuningResultKind.RestoredVerified or ProcessTuningResultKind.NotApplicable)
        {
            pvm.Profile.ActiveReceiptId = null;
            pvm.Profile.ActiveProcessId = null;
            await _profileService.SaveProfileAsync(pvm.Profile);
        }
        StatusText = result.Message;
        pvm.UpdateStatus(_profileService.CheckProfileStatus(pvm.Profile));
    }

    private int? ResolveExactlyOneRunningProcess(ProcessAffinityProfile profile)
    {
        string expectedName = System.IO.Path.GetFileNameWithoutExtension(profile.ProcessName);
        int[] matches = _profileService.GetRunningProcesses()
            .Where(process => string.Equals(process.Name, expectedName, StringComparison.OrdinalIgnoreCase))
            .Select(process => process.Pid)
            .Distinct()
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    [RelayCommand]
    private async Task ClearAllProfilesAsync()
    {
        if (!HasProfiles) return;

        var count = Profiles.Count;
        foreach (var pvm in Profiles.ToList())
        {
            if (pvm.Profile.ActiveReceiptId is not null)
                continue;
            await _profileService.DeleteProfileAsync(pvm.ProcessName);
        }

        await LoadProfilesAsync();
        ToastService.Instance.Info(
            _loc.Get("CpuAffinity.ClearedTitle"),
            _loc.Get("CpuAffinity.ClearedToast", count));
        StatusText = _loc.Get("CpuAffinity.Cleared", count);
    }

    // === 辅助方法 ===
    private static string GetAffinityDescription(System.Collections.Generic.List<int> threads, CpuInfo? cpuInfo)
    {
        var loc = LocalizationService.Instance;
        if (cpuInfo == null) return loc.Get("CpuAffinity.CoreCountDesc", threads.Count);

        if (cpuInfo.HasHybridArchitecture)
        {
            var selectedThreadIndices = threads.ToHashSet();
            var pCount = cpuInfo.Threads.Count(thread =>
                selectedThreadIndices.Contains(thread.Index) &&
                (thread.CoreType == CpuCoreType.PerformanceCore || thread.CoreType == CpuCoreType.CacheCore));
            var eCount = cpuInfo.Threads.Count(thread =>
                selectedThreadIndices.Contains(thread.Index) &&
                (thread.CoreType == CpuCoreType.EfficiencyCore || thread.CoreType == CpuCoreType.ComputeCore));

            if (pCount > 0 && eCount == 0) return loc.Get("CpuAffinity.POnlyDesc", pCount);
            if (eCount > 0 && pCount == 0) return loc.Get("CpuAffinity.EOnlyDesc", eCount);
            return loc.Get("CpuAffinity.MixedDesc", pCount, eCount);
        }

        if (threads.Count == cpuInfo.LogicalProcessors)
            return loc.Get("CpuAffinity.AllCoresDesc");

        var coreList = string.Join(",", threads.Take(4)) + (threads.Count > 4 ? "..." : "");
        return loc.Get("CpuAffinity.CoreListDesc", coreList);
    }

    private CpuInfo? CreateEditorCpuInfo()
    {
        if (!HasUsableTopology)
            return null;

        var topology = CpuTopology;
        return new CpuInfo
        {
            Name = topology.CpuName,
            PhysicalCores = topology.PhysicalCores,
            LogicalProcessors = topology.LogicalCores,
            Vendor = topology.Vendor,
            Has3DCache = topology.HasVCache,
            HasHybridArchitecture = topology.HasHybridCores
                || topology.Architecture is CpuArchitecture.IntelHybrid or CpuArchitecture.AmdX3D,
            CacheCoreCount = topology.Cores.Count(core => core.HasVCache),
            Threads = topology.Cores
                .OrderBy(core => core.Index)
                .Select(core => new CpuThread
                {
                    Index = core.Index,
                    PhysicalCoreId = core.PhysicalIndex,
                    CoreType = MapEditorCoreType(topology, core)
                })
                .ToList()
        };
    }

    private static CpuCoreType MapEditorCoreType(CpuTopology topology, CoreInfo core)
    {
        if (topology.Architecture == CpuArchitecture.AmdX3D)
            return core.HasVCache ? CpuCoreType.CacheCore : CpuCoreType.ComputeCore;

        return core.Type switch
        {
            CoreType.PerformanceCore => CpuCoreType.PerformanceCore,
            CoreType.EfficiencyCore => CpuCoreType.EfficiencyCore,
            _ => CpuCoreType.Standard
        };
    }

    private static ProcessType GuessProcessType(string processName)
    {
        var name = processName.ToLowerInvariant();

        if (name.Contains("game") || name.Contains("valorant") || name.Contains("csgo") ||
            name.Contains("apex") || name.Contains("fortnite") || name.Contains("pubg") ||
            name.Contains("overwatch") || name.Contains("league") || name.Contains("dota") ||
            name.Contains("genshin") || name.Contains("原神") || name.Contains("minecraft") ||
            name.Contains("cyberpunk") || name.Contains("eldenring") || name.Contains("diablo") ||
            name.Contains("wow") || name.Contains("ffxiv") || name.Contains("naraka") ||
            name.Contains("崩坏") || name.Contains("honkai") || name.Contains("starrail") ||
            name.Contains("wuthering") || name.Contains("鸣潮") || name.Contains("绝区零"))
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
}
