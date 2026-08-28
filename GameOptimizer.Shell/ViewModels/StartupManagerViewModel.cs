using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameOptimizer.Core.Services.Startup;
using GameOptimizer.Infrastructure.Windows.Startup;

namespace GameOptimizer.Shell.ViewModels
{
    public partial class StartupManagerViewModel : ObservableObject
    {
        private readonly IStartupService _startupService;
        private readonly IStartupEntryOwner? _startupOwner;
        private readonly Func<ManagedAutostartPlan, bool>? _confirmPlan;

        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private string _statusText = "准备就绪";
        [ObservableProperty] private StartupItem? _selectedItem;
        [ObservableProperty] private StartupItemType? _selectedFilter;
        [ObservableProperty] private string _newItemName = string.Empty;
        [ObservableProperty] private string _newItemPath = string.Empty;
        [ObservableProperty] private int _totalCount;
        [ObservableProperty] private int _disabledCount;
        [ObservableProperty] private int _highImpactCount;
        [ObservableProperty] private bool _hasRecommendations;
        [ObservableProperty] private bool _showDeleteConfirm;
        [ObservableProperty] private StartupItem? _pendingDeleteItem;

        public ObservableCollection<StartupItem> StartupItems { get; } = new();
        public ObservableCollection<StartupItem> FilteredItems { get; } = new();
        public ObservableCollection<StartupRecommendation> Recommendations { get; } = new();

        public StartupManagerViewModel(IStartupService startupService, IStartupEntryOwner? startupOwner = null,
            Func<ManagedAutostartPlan, bool>? confirmPlan = null)
        {
            _startupService = startupService;
            _startupOwner = startupOwner;
            _confirmPlan = confirmPlan;
        }

        [RelayCommand]
        private async Task LoadItemsAsync()
        {
            IsLoading = true;
            try
            {
                StartupItems.Clear();
                var items = await _startupService.GetStartupItemsAsync();
                foreach (var item in items)
                    StartupItems.Add(item);
                ApplyFilter();
                UpdateCounts();
                StatusText = $"已加载 {StartupItems.Count} 个启动项";
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task ToggleItemAsync(StartupItem? item)
        {
            if (item == null) return;

            if (_startupOwner is null || _confirmPlan is null)
            {
                StatusText = "启动项 Owner 未接入；已阻断修改，未使用旧写入回退。";
                return;
            }

            if (item.Type != StartupItemType.Registry
                || !string.Equals(item.RegistryValue, ManagedAutostartApplicationIdentity.ValueName, StringComparison.Ordinal)
                || !string.Equals(item.RegistryKey,
                    $@"HKEY_CURRENT_USER\{ManagedAutostartApplicationIdentity.RegistryPath}",
                    StringComparison.OrdinalIgnoreCase))
            {
                StatusText = "当前仅允许修改 YuqiEngine 自有的 HKCU Run 启动项；第三方、系统和计划任务保持只读。";
                return;
            }

            IsLoading = true;
            try
            {
                var identity = new ManagedAutostartIdentity(
                    ManagedAutostartKind.RegistryRun,
                    "current-user",
                    ManagedAutostartApplicationIdentity.ResourceId);
                var prepared = await _startupOwner.PrepareAsync(
                    identity,
                    item.IsEnabled ? ManagedAutostartAction.Disable : ManagedAutostartAction.Enable,
                    default);
                if (!prepared.Success || prepared.Plan is null)
                {
                    StatusText = $"准备失败：{prepared.Message}";
                    return;
                }

                var plan = prepared.Plan;
                if (!_confirmPlan(plan))
                {
                    StatusText = "已取消；不可变计划未执行。";
                    return;
                }

                var evidence = new ManagedAutostartConfirmationEvidence(
                    plan.PlanId,
                    plan.PlanHash,
                    DateTimeOffset.UtcNow,
                    true);
                var confirmation = _startupOwner.Confirm(plan, evidence, TimeSpan.FromMinutes(2));
                var result = await _startupOwner.ExecuteAsync(plan, confirmation, default);
                StatusText = result.Success ? result.Message : $"操作失败：{result.Message}";
                await LoadItemsAsync();
            }
            catch (Exception ex)
            {
                StatusText = $"操作失败：{ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private void RequestDeleteItem(StartupItem? item)
        {
            if (item == null && SelectedItem == null) return;
            PendingDeleteItem = item ?? SelectedItem;
            ShowDeleteConfirm = true;
            StatusText = $"⚠️ 确认删除启动项 '{PendingDeleteItem?.Name}'？此操作将修改注册表！";
        }

        [RelayCommand]
        private async Task ConfirmDeleteItemAsync()
        {
            if (PendingDeleteItem == null) return;

            IsLoading = true;
            ShowDeleteConfirm = false;
            try
            {
                var result = await _startupService.DeleteItemAsync(PendingDeleteItem.Id);
                StatusText = result.Success ? $"✅ 已删除: {PendingDeleteItem.Name}" : $"删除失败: {result.Message}";
                await LoadItemsAsync();
            }
            catch (Exception ex)
            {
                StatusText = $"删除失败: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
                PendingDeleteItem = null;
            }
        }

        [RelayCommand]
        private void CancelDeleteItem()
        {
            ShowDeleteConfirm = false;
            PendingDeleteItem = null;
            StatusText = "已取消删除";
        }

        [RelayCommand]
        private async Task AddItemAsync()
        {
            if (string.IsNullOrWhiteSpace(NewItemName) || string.IsNullOrWhiteSpace(NewItemPath))
            {
                StatusText = "请输入名称和路径";
                return;
            }

            IsLoading = true;
            try
            {
                var result = await _startupService.AddItemAsync(NewItemName, NewItemPath);
                if (result.Success)
                {
                    NewItemName = string.Empty;
                    NewItemPath = string.Empty;
                    await LoadItemsAsync();
                }
                StatusText = result.Success ? "已添加" : $"添加失败: {result.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private void SetFilter(StartupItemType? type)
        {
            SelectedFilter = type;
            ApplyFilter();
        }

        [RelayCommand]
        private void LocateItem(StartupItem? item)
        {
            if (item == null || string.IsNullOrEmpty(item.Path)) return;

            try
            {
                var path = item.Path;
                if (System.IO.File.Exists(path))
                {
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
                    StatusText = $"已定位: {item.Name}";
                }
                else
                {
                    StatusText = $"文件不存在: {path}";
                }
            }
            catch (Exception ex)
            {
                StatusText = $"定位失败: {ex.Message}";
            }
        }

        [RelayCommand]
        private void AnalyzeImpact()
        {
            Recommendations.Clear();
            foreach (var item in StartupItems.Where(i => i.IsEnabled))
            {
                var rec = AnalyzeStartupItem(item);
                if (rec != null)
                    Recommendations.Add(rec);
            }
            HasRecommendations = Recommendations.Count > 0;
            StatusText = $"💡 分析完成: 发现 {Recommendations.Count} 个可优化项";
        }

        [RelayCommand]
        private async Task ApplyAllRecommendationsAsync()
        {
            if (Recommendations.Count == 0)
            {
                AnalyzeImpact();
                if (Recommendations.Count == 0)
                {
                    StatusText = "✅ 启动项已是最优状态";
                    return;
                }
            }

            IsLoading = true;
            var successCount = 0;
            foreach (var rec in Recommendations.Where(r => r.ShouldDisable))
            {
                var result = await _startupService.DisableItemAsync(rec.Item.Id);
                if (result.Success) successCount++;
            }
            await LoadItemsAsync();
            IsLoading = false;
            StatusText = $"✅ 已应用全部建议: 已禁用 {successCount} 个非必要启动项";
        }

        private StartupRecommendation? AnalyzeStartupItem(StartupItem item)
        {
            var fileName = Path.GetFileName(item.Path).ToLowerInvariant();
            
            // 已知可安全禁用的程序
            var safeToDisable = new Dictionary<string, string>
            {
                { "onedrive", "云同步服务，可手动启动" },
                { "spotify", "音乐播放器，可手动启动" },
                { "discord", "聊天软件，可手动启动" },
                { "steam", "游戏平台，可手动启动" },
                { "epicgames", "游戏平台，可手动启动" },
                { "skype", "通讯软件，可手动启动" },
                { "teams", "通讯软件，可手动启动" },
                { "zoom", "会议软件，可手动启动" },
                { "adobe", "Adobe服务，通常非必要" },
                { "itunes", "iTunes助手，可手动启动" },
                { "dropbox", "云同步服务，可手动启动" },
                { "googledrive", "云同步服务，可手动启动" }
            };

            foreach (var (key, reason) in safeToDisable)
            {
                if (fileName.Contains(key) || item.Name.ToLowerInvariant().Contains(key))
                {
                    return new StartupRecommendation
                    {
                        Item = item,
                        Reason = reason,
                        ShouldDisable = true,
                        ImpactLevel = "中等"
                    };
                }
            }

            // 更新程序通常可禁用
            if (fileName.Contains("update") || item.Name.ToLowerInvariant().Contains("update"))
            {
                return new StartupRecommendation
                {
                    Item = item,
                    Reason = "更新程序，可手动检查更新",
                    ShouldDisable = true,
                    ImpactLevel = "低"
                };
            }

            return null;
        }

        private void ApplyFilter()
        {
            FilteredItems.Clear();
            var items = SelectedFilter.HasValue
                ? StartupItems.Where(i => i.Type == SelectedFilter.Value)
                : StartupItems;

            foreach (var item in items)
                FilteredItems.Add(item);
        }

        private void UpdateCounts()
        {
            TotalCount = StartupItems.Count;
            DisabledCount = StartupItems.Count(i => !i.IsEnabled);
            HighImpactCount = StartupItems.Count(i => i.Impact == StartupImpact.High && i.IsEnabled);
        }

        public void Initialize() => _ = LoadItemsAsync();
    }

    public class StartupRecommendation
    {
        public StartupItem Item { get; set; } = new();
        public string Reason { get; set; } = string.Empty;
        public bool ShouldDisable { get; set; }
        public string ImpactLevel { get; set; } = string.Empty;
    }
}


