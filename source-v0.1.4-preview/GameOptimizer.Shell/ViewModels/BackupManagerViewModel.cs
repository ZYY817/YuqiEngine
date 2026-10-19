// File: GameOptimizer.Shell/ViewModels/BackupManagerViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameOptimizer.Core.Constants;
using GameOptimizer.Core.Diagnostics;
using GameOptimizer.Core.Events;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Services;
using GameOptimizer.Core.Services.Snapshot;
using GameOptimizer.Core.Utilities;
using GameOptimizer.Shell.Services;
using GameOptimizer.Shell.Services.Baseline;
using Microsoft.Win32;

namespace GameOptimizer.Shell.ViewModels
{
    public enum BackupManagerPageState
    {
        Loading,
        Empty,
        Ready,
        Partial,
        Error,
        Unauthorized,
        Recovering,
        InterruptedManual,
        CompensatedVerified,
        RebootRequired
    }

    /// <summary>
    /// Timeline choices map only to data this page actually owns: ordinary snapshots,
    /// the verified first-run baseline, and comprehensive backup packages.
    /// </summary>
    public enum BackupTimelineFilter
    {
        All,
        OrdinarySnapshots,
        FirstRunBaseline,
        ComprehensivePackages
    }

    public enum BackupTimelineEntryKind
    {
        Baseline,
        Snapshot,
        Package
    }

    /// <summary>
    /// One row in the unified backup timeline. Wraps the first-run baseline, an ordinary
    /// snapshot, or a comprehensive backup package so all three record types can share
    /// a single editorial list.
    /// </summary>
    public sealed class BackupTimelineEntryView : ObservableObject
    {
        public BackupTimelineEntryKind Kind { get; init; }
        public string Index { get; set; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string TagText { get; init; } = string.Empty;
        public string MetaText { get; init; } = string.Empty;
        public string GroupLabel { get; set; } = string.Empty;
        public bool ShowGroupLabel { get; set; }
        public SnapshotInfo? Snapshot { get; init; }
        public ComprehensiveBackupPackageInfo? Package { get; init; }

        public bool IsBaseline => Kind == BackupTimelineEntryKind.Baseline;
        public bool IsSnapshot => Kind == BackupTimelineEntryKind.Snapshot;
        public bool IsPackage => Kind == BackupTimelineEntryKind.Package;

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }

    public sealed class BackupRestoreReceiptView
    {
        public string CapabilityId { get; init; } = string.Empty;
        public string CapabilityDisplayName => CapabilityDisplayFormatter.GetDisplayName(CapabilityId);
        public string Outcome { get; init; } = string.Empty;
        public string ReasonCode { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public bool MutationOccurred { get; init; }
        public string MutationText => MutationOccurred ? "发生写入，需以回执为准" : "未发生写入";
    }

    /// <summary>
    /// Read-only presentation of one recorded baseline domain.  This is deliberately
    /// derived from the manifest instead of promising an all-system restore.
    /// </summary>
    public sealed class BackupCoverageStatusItem
    {
        public string Name { get; init; } = string.Empty;
        public string CaptureStatus { get; init; } = string.Empty;
        public string RecoveryStatus { get; init; } = string.Empty;
        public string Status => $"{CaptureStatus} · {RecoveryStatus}";
        public string Detail { get; init; } = string.Empty;
        public bool IsAutomaticRecovery { get; init; }
    }

    public partial class BackupManagerViewModel : ObservableObject, IDisposable
    {
        private readonly ISnapshotService _snapshotService;
        private readonly IEventBus? _eventBus;
        private readonly BackupDiagnosticService _diagnosticService;
        private readonly AutoRepairService? _repairService;
        private readonly IBaselineCatalogService? _baselineCatalogService;
        private readonly IBaselineRestoreOrchestrator? _baselineRestoreOrchestrator;
        private readonly ISystemRestoreService? _systemRestoreService;
        private readonly IComprehensiveBackupService? _comprehensiveBackupService;
        private CancellationTokenSource? _eventSubscriptionCts;
        private CancellationTokenSource? _restoreExecutionCts;
        private int _operationsSinceLastBackup;
        private string _activeBaselinePackageId = string.Empty;
        private DateTime? _baselineCreatedAtLocal;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanPrepareRestore))]
        [NotifyPropertyChangedFor(nameof(CanExecutePendingRestore))]
        [NotifyPropertyChangedFor(nameof(RestoreExecutionSafetyText))]
        [NotifyCanExecuteChangedFor(nameof(PrepareComprehensiveRestoreCommand))]
        [NotifyCanExecuteChangedFor(nameof(PrepareRestoreSnapshotCommand))]
        private bool _isLoading;
        [ObservableProperty] private string _statusText = string.Empty;
        [ObservableProperty] private string _newSnapshotName = string.Empty;
        [ObservableProperty] private SnapshotInfo? _selectedSnapshot;
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(PrepareComprehensiveRestoreCommand))]
        private ComprehensiveBackupPackageInfo? _selectedComprehensiveBackup;
        [ObservableProperty] private bool _needsBackup;
        [ObservableProperty] private string _backupSuggestion = string.Empty;
        [ObservableProperty] private int _totalSnapshots;
        [ObservableProperty] private SnapshotInfo? _latestSnapshot;
        [ObservableProperty] private bool _hasSnapshots;
        [ObservableProperty] private bool _showPreview;
        [ObservableProperty] private OptimizationSnapshot? _previewSnapshot;
        [ObservableProperty] private bool _showDeleteConfirm;
        [ObservableProperty] private SnapshotInfo? _pendingDeleteSnapshot;
        [ObservableProperty] private bool _showClearAllConfirm;
        [ObservableProperty] private string _totalSizeText = "0 KB";
        [ObservableProperty] private string _storageVolumeText = "存储卷不可用";
        [ObservableProperty] private string _storageVolumeDetail = "未能读取备份存储卷容量";
        [ObservableProperty] private string _backupStoragePathText = "备份目录暂不可用";
        [ObservableProperty] private string _baselinePackageSizeText = "首次基线包：未找到";
        [ObservableProperty] private string _restoreCoverageSummary = "尚未读取到首次基线覆盖范围";
        [ObservableProperty] private string _ordinarySnapshotCoverageText = "普通快照覆盖范围尚未读取";
        [ObservableProperty] private string _comprehensiveBackupStatusText = "完整用户配置备份尚未创建";
        [ObservableProperty] private string _comprehensiveBackupCountText = "完整备份包：未读取";
        [ObservableProperty] private string _comprehensiveBackupCoverageText = "完整备份覆盖明细尚未读取";
        [ObservableProperty] private string _lastBackupText = string.Empty;
        [ObservableProperty] private string _snapshotCountDisplay = string.Empty;
        [ObservableProperty] private bool _hasAnyBackup;
        [ObservableProperty] private bool _hasFirstRunBaseline;
        [ObservableProperty] private string _backupProtectionText = "尚未建立保护";
        [ObservableProperty] private string _firstRunBaselineStatusTitle = "尚未建立首次安全基线";
        [ObservableProperty] private string _firstRunBaselineStatusSubText = "首次备份尚未接入备份中心";
        [ObservableProperty] private string _firstRunBaselineStatusDescription = "首次进入后完成安全备份，这里会显示可恢复的首次基线和系统还原点状态。";
        [ObservableProperty] private bool _canRestoreFirstRunBaseline;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsAllTimelineSelected))]
        [NotifyPropertyChangedFor(nameof(IsOrdinaryTimelineSelected))]
        [NotifyPropertyChangedFor(nameof(IsBaselineTimelineSelected))]
        [NotifyPropertyChangedFor(nameof(IsPackageTimelineSelected))]
        [NotifyPropertyChangedFor(nameof(TimelineCountText))]
        [NotifyPropertyChangedFor(nameof(IsSnapshotTimelineVisible))]
        [NotifyPropertyChangedFor(nameof(IsBaselineTimelineVisible))]
        [NotifyPropertyChangedFor(nameof(HasNoTimelineEntries))]
        [NotifyPropertyChangedFor(nameof(TimelineTitle))]
        [NotifyPropertyChangedFor(nameof(TimelineDescription))]
        [NotifyPropertyChangedFor(nameof(TimelineEmptyTitle))]
        [NotifyPropertyChangedFor(nameof(TimelineEmptyDescription))]
        private BackupTimelineFilter _timelineFilter = BackupTimelineFilter.All;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PageStateText))]
        [NotifyPropertyChangedFor(nameof(CanResolveInterruptedRestore))]
        [NotifyCanExecuteChangedFor(nameof(KeepInterruptedRestoreBlockedCommand))]
        [NotifyCanExecuteChangedFor(nameof(ReleaseVerifiedZeroWriteCommand))]
        [NotifyCanExecuteChangedFor(nameof(AcknowledgeManualRecoveryCommand))]
        private BackupManagerPageState _pageState = BackupManagerPageState.Loading;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasPendingRestorePlan))]
        [NotifyPropertyChangedFor(nameof(CanExecutePendingRestore))]
        [NotifyPropertyChangedFor(nameof(RestorePlanHashText))]
        [NotifyPropertyChangedFor(nameof(RestorePlanStepsText))]
        [NotifyPropertyChangedFor(nameof(RestorePlanExpiryText))]
        [NotifyPropertyChangedFor(nameof(RestoreExecutionSafetyText))]
        private SnapshotRestorePlan? _pendingRestorePlan;
        [ObservableProperty] private string _restoreCompatibilityText = "请选择一个快照后检查其恢复兼容性。";
        [ObservableProperty] private string _restorePlanStatusText = "尚未准备恢复计划。";
        [ObservableProperty] private string _restorePlanSourceText = "尚未选择恢复来源。";
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanPrepareRestore))]
        [NotifyPropertyChangedFor(nameof(CanExecutePendingRestore))]
        [NotifyPropertyChangedFor(nameof(CanReconcilePendingRestore))]
        [NotifyCanExecuteChangedFor(nameof(PrepareComprehensiveRestoreCommand))]
        [NotifyCanExecuteChangedFor(nameof(PrepareRestoreSnapshotCommand))]
        [NotifyCanExecuteChangedFor(nameof(ReconcilePendingRestoreCommand))]
        private bool _isPreparingRestore;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanPrepareRestore))]
        [NotifyPropertyChangedFor(nameof(CanExecutePendingRestore))]
        [NotifyPropertyChangedFor(nameof(CanCancelRestore))]
        [NotifyPropertyChangedFor(nameof(CanReconcilePendingRestore))]
        [NotifyPropertyChangedFor(nameof(CanResolveInterruptedRestore))]
        [NotifyCanExecuteChangedFor(nameof(PrepareComprehensiveRestoreCommand))]
        [NotifyCanExecuteChangedFor(nameof(PrepareRestoreSnapshotCommand))]
        [NotifyCanExecuteChangedFor(nameof(CancelRestoreCommand))]
        [NotifyCanExecuteChangedFor(nameof(ReconcilePendingRestoreCommand))]
        [NotifyCanExecuteChangedFor(nameof(KeepInterruptedRestoreBlockedCommand))]
        [NotifyCanExecuteChangedFor(nameof(ReleaseVerifiedZeroWriteCommand))]
        [NotifyCanExecuteChangedFor(nameof(AcknowledgeManualRecoveryCommand))]
        private bool _isExecutingRestore;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanReconcilePendingRestore))]
        [NotifyPropertyChangedFor(nameof(CanResolveInterruptedRestore))]
        [NotifyPropertyChangedFor(nameof(ActiveRecoveryIdentityText))]
        [NotifyCanExecuteChangedFor(nameof(ReconcilePendingRestoreCommand))]
        [NotifyCanExecuteChangedFor(nameof(KeepInterruptedRestoreBlockedCommand))]
        [NotifyCanExecuteChangedFor(nameof(ReleaseVerifiedZeroWriteCommand))]
        [NotifyCanExecuteChangedFor(nameof(AcknowledgeManualRecoveryCommand))]
        private SnapshotRestoreBatchIdentity? _activeRecoveryBatch;
        [ObservableProperty] private string _recoveryStatusText = "当前没有可对账的恢复批次。";
        
        // Deep Registry Backup
        [ObservableProperty] private bool _showDeepRestore;
        [ObservableProperty] private string _deepRestoreSnapshotId = string.Empty;
        [ObservableProperty] private int _deepRestoreFileCount;
        [ObservableProperty] private bool _isDeepRestoring;
        [ObservableProperty] private bool _showNoRegBackupHint;

        public ObservableCollection<SnapshotInfo> Snapshots { get; } = new();
        public ObservableCollection<SnapshotSettingItem> PreviewSettings { get; } = new();
        public ObservableCollection<BackupRestoreReceiptView> RestoreReceipts { get; } = new();
        public ObservableCollection<BackupCoverageStatusItem> RestoreCoverage { get; } = new();
        public ObservableCollection<ComprehensiveBackupPackageInfo> ComprehensiveBackups { get; } = new();
        public ObservableCollection<BackupTimelineEntryView> TimelineEntries { get; } = new();

        public string PageStateText => PageState switch
        {
            BackupManagerPageState.Loading => "加载中",
            BackupManagerPageState.Empty => "暂无快照",
            BackupManagerPageState.Ready => "已就绪",
            BackupManagerPageState.Partial => "部分可用",
            BackupManagerPageState.Error => "读取失败",
            BackupManagerPageState.Unauthorized => "需要权限",
            BackupManagerPageState.Recovering => "恢复处理中",
            BackupManagerPageState.InterruptedManual => "需要人工介入",
            BackupManagerPageState.CompensatedVerified => "已补偿并验证",
            BackupManagerPageState.RebootRequired => "需要重启",
            _ => "状态未知"
        };

        public bool CanPrepareRestore => SelectedSnapshot is not null && !IsLoading && !IsPreparingRestore && !IsExecutingRestore;
        private bool CanPrepareRestoreForSnapshot(SnapshotInfo? snapshot) => snapshot is not null
            && !IsLoading
            && !IsPreparingRestore
            && !IsExecutingRestore;
        public bool CanPrepareComprehensiveRestore => CanPrepareComprehensiveRestoreForPackage(SelectedComprehensiveBackup);
        // 列表行按钮把包实例作为命令参数传入；选中属性只在命令执行后才赋值，
        // CanExecute 必须看参数本身，否则行内按钮永远禁用。
        private bool CanPrepareComprehensiveRestoreForPackage(ComprehensiveBackupPackageInfo? package) => package is not null
            && package.IntegrityVerified
            && !IsLoading
            && !IsPreparingRestore
            && !IsExecutingRestore;
        public bool CanExecutePendingRestore => PendingRestorePlan is not null
            && !IsLoading
            && !IsPreparingRestore
            && !IsExecutingRestore
            && PendingRestorePlan.ExpiresAtUtc > DateTimeOffset.UtcNow
            && !PendingRestorePlan.Steps.Any(step => !step.Supported);
        public bool HasPendingRestorePlan => PendingRestorePlan is not null;
        public bool CanCancelRestore => IsExecutingRestore && _restoreExecutionCts is not null;
        public bool CanReconcilePendingRestore => GetActiveRecoveryIdentity() is not null && !IsExecutingRestore && !IsPreparingRestore;
        public bool CanResolveInterruptedRestore => GetActiveRecoveryIdentity() is not null
            && PageState == BackupManagerPageState.InterruptedManual
            && !IsExecutingRestore;
        public string ActiveRecoveryIdentityText => ActiveRecoveryBatch is null
            ? PendingRestorePlan is null
                ? "批次身份：未选择"
                : $"批次身份：{PendingRestorePlan.PlanId:N}（当前计划）"
            : $"批次身份：{ActiveRecoveryBatch.PlanId:N}；快照 {ActiveRecoveryBatch.SnapshotId}；持久化状态 {ActiveRecoveryBatch.State}";
        public bool IsAllTimelineSelected => TimelineFilter == BackupTimelineFilter.All;
        public bool IsOrdinaryTimelineSelected => TimelineFilter == BackupTimelineFilter.OrdinarySnapshots;
        public bool IsBaselineTimelineSelected => TimelineFilter == BackupTimelineFilter.FirstRunBaseline;
        public bool IsPackageTimelineSelected => TimelineFilter == BackupTimelineFilter.ComprehensivePackages;
        public bool IsSnapshotTimelineVisible => HasSnapshots && TimelineFilter != BackupTimelineFilter.FirstRunBaseline;
        public bool IsBaselineTimelineVisible => HasFirstRunBaseline && TimelineFilter != BackupTimelineFilter.OrdinarySnapshots;
        public bool HasNoTimelineEntries => !IsLoading && TimelineEntries.Count == 0;
        public string TimelineCountText => $"{TimelineEntries.Count} 份";
        public int TimelineEntryCount => TimelineEntries.Count;
        public string TimelineTitle => TimelineFilter switch
        {
            BackupTimelineFilter.OrdinarySnapshots => "普通快照时间线",
            BackupTimelineFilter.FirstRunBaseline => "首次安全基线",
            BackupTimelineFilter.ComprehensivePackages => "完整备份包",
            _ => "备份记录"
        };
        public string TimelineDescription => TimelineFilter switch
        {
            BackupTimelineFilter.OrdinarySnapshots => "仅显示本机真实读取到的普通快照；选择一项后才能准备恢复。",
            BackupTimelineFilter.FirstRunBaseline => "仅显示经完整性状态确认的首次安全基线；恢复前会再次准备并核验。",
            BackupTimelineFilter.ComprehensivePackages => "仅显示完整用户配置备份包；自动恢复仅限明确契约资产。",
            _ => "普通快照、首次安全基线与完整备份包按时间统一展示；选中记录后可准备恢复。"
        };
        public string TimelineEmptyTitle => TimelineFilter switch
        {
            BackupTimelineFilter.OrdinarySnapshots => "暂无普通快照",
            BackupTimelineFilter.FirstRunBaseline => "暂无首次安全基线",
            BackupTimelineFilter.ComprehensivePackages => "暂无完整备份包",
            _ => "暂无可用恢复记录"
        };
        public string TimelineEmptyDescription => TimelineFilter switch
        {
            BackupTimelineFilter.OrdinarySnapshots => "当前没有普通快照；可以从上方创建一份备份。",
            BackupTimelineFilter.FirstRunBaseline => "当前没有已接入并可核验的首次安全基线。",
            BackupTimelineFilter.ComprehensivePackages => "可在下方高级选项中创建完整用户配置备份包。",
            _ => "当前没有普通快照或已接入的首次安全基线；可以从上方创建一份备份。"
        };
        public string RestorePlanHashText => PendingRestorePlan?.PlanSha256 ?? "未生成";
        public string RestorePlanStepsText => PendingRestorePlan is null
            ? "步骤数：未生成"
            : $"自动恢复步骤：{PendingRestorePlan.Steps.Count(step => step.Supported)} 项" +
              (PendingRestorePlan.Steps.Any(step => !step.Supported) ? $"；阻断：{PendingRestorePlan.Steps.Count(step => !step.Supported)} 项" : "") +
              " · 快照中其余记录仅保存状态，不自动写回";
        public string RestorePlanExpiryText => PendingRestorePlan is null
            ? "计划有效期：未生成"
            : $"计划有效期至：{PendingRestorePlan.ExpiresAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
        public string RestoreExecutionSafetyText => CanExecutePendingRestore
            ? "执行前请核对计划 hash、步骤和快照身份；执行结果以逐步回执和独立验证为准。"
            : "执行按钮仅在存在未过期、无不支持步骤的真实计划时启用；V1-V3 快照永久不可恢复。";
        
        // UI文本属性 — 供卡片按钮绑定
        public string PreviewButtonText => Loc.Get("Backup.Preview");
        public string ExportButtonText => Loc.Get("Backup.Export");
        
        private static LocalizationService Loc => LocalizationService.Instance;

        // File: GameOptimizer.Shell/ViewModels/BackupManagerViewModel.cs
        public BackupManagerViewModel() : this(new SnapshotService(), null, null, null, null, null, null, null) { }

        public BackupManagerViewModel(
            ISnapshotService snapshotService, 
            IEventBus? eventBus = null,
            BackupDiagnosticService? diagnosticService = null,
            AutoRepairService? repairService = null,
            IBaselineCatalogService? baselineCatalogService = null,
            IBaselineRestoreOrchestrator? baselineRestoreOrchestrator = null,
            ISystemRestoreService? systemRestoreService = null,
            IComprehensiveBackupService? comprehensiveBackupService = null)
        {
            _snapshotService = snapshotService;
            _eventBus = eventBus;
            _diagnosticService = diagnosticService ?? new BackupDiagnosticService();
            _repairService = repairService;
            _baselineCatalogService = baselineCatalogService;
            _baselineRestoreOrchestrator = baselineRestoreOrchestrator;
            _systemRestoreService = systemRestoreService;
            _comprehensiveBackupService = comprehensiveBackupService;
            StatusText = Loc.Get("Common.Ready");
            LastBackupText = Loc.Get("Backup.NoBackupYet");
            
            // VM-002: 订阅优化完成事件
            if (_eventBus != null)
            {
                SubscribeToOptimizationEvents();
            }
            
            // 确保错误日志目录存在
            EnsureErrorLogDirectory();
        }
        
        private static void EnsureErrorLogDirectory()
        {
            try
            {
                if (!Directory.Exists(AppPaths.LogsDir))
                {
                    SafeDirectoryOperations.TryCreateDirectory(AppPaths.LogsDir, out _);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BackupManagerVM] 创建日志目录失败: {ex.Message}");
            }
        }
        
        private void SubscribeToOptimizationEvents()
        {
            _eventSubscriptionCts = new CancellationTokenSource();
            var ct = _eventSubscriptionCts.Token;
            
            _ = Task.Run(async () =>
            {
                try
                {
                    await foreach (var evt in _eventBus!.SubscribeAsync<OptimizationCompletedEvent>(ct))
                    {
                        Debug.WriteLine($"[BackupManagerVM] Received OptimizationCompletedEvent: {evt.ChangedCount} changes");
                        _operationsSinceLastBackup += evt.ChangedCount;
                        
                        // 如果自上次备份后有超过10个更改，建议备份
                        if (_operationsSinceLastBackup >= 10)
                        {
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                NeedsBackup = true;
                                BackupSuggestion = Loc.Get("Backup.SuggestAfterOptimization", _operationsSinceLastBackup);
                            });
                        }
                    }
                }
                catch (OperationCanceledException) { /* 正常取消 */ }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[BackupManagerVM] OptimizationCompletedEvent subscription failed: {ex.Message}");
                }
            }, ct);
        }
        
        public void Dispose()
        {
            _eventSubscriptionCts?.Cancel();
            _eventSubscriptionCts?.Dispose();
            _restoreExecutionCts?.Cancel();
            _restoreExecutionCts?.Dispose();
        }

        partial void OnSelectedSnapshotChanged(SnapshotInfo? value)
        {
            if (value is not null && SelectedComprehensiveBackup is not null)
            {
                SelectedComprehensiveBackup = null;
            }

            PendingRestorePlan = null;
            RestoreReceipts.Clear();
            RestorePlanStatusText = "尚未准备恢复计划。";
            RestoreCompatibilityText = value is null
                ? "请选择一个快照后检查其恢复兼容性。"
                : "正在读取快照版本和类型化清单…";
            OnPropertyChanged(nameof(CanPrepareRestore));
            SyncTimelineSelection();
        }

        partial void OnSelectedComprehensiveBackupChanged(ComprehensiveBackupPackageInfo? value)
        {
            if (value is not null && SelectedSnapshot is not null)
            {
                SelectedSnapshot = null;
            }

            SyncTimelineSelection();
        }

        partial void OnPendingRestorePlanChanged(SnapshotRestorePlan? value)
        {
            if (value is not null)
            {
                ActiveRecoveryBatch = null;
            }

            OnPropertyChanged(nameof(CanReconcilePendingRestore));
            OnPropertyChanged(nameof(CanResolveInterruptedRestore));
            OnPropertyChanged(nameof(ActiveRecoveryIdentityText));
        }

        partial void OnTimelineFilterChanged(BackupTimelineFilter value)
        {
            if (value is BackupTimelineFilter.FirstRunBaseline or BackupTimelineFilter.ComprehensivePackages)
            {
                SelectedSnapshot = null;
            }
            if (value is BackupTimelineFilter.OrdinarySnapshots or BackupTimelineFilter.FirstRunBaseline)
            {
                SelectedComprehensiveBackup = null;
            }

            RefreshTimelinePresentation();
        }

        partial void OnHasSnapshotsChanged(bool value) => RefreshTimelinePresentation();

        partial void OnHasFirstRunBaselineChanged(bool value) => RefreshTimelinePresentation();

        partial void OnIsLoadingChanged(bool value) => RefreshTimelinePresentation();

        private void RefreshTimelinePresentation()
        {
            RebuildTimelineEntries();
            OnPropertyChanged(nameof(IsSnapshotTimelineVisible));
            OnPropertyChanged(nameof(IsBaselineTimelineVisible));
            OnPropertyChanged(nameof(HasNoTimelineEntries));
            OnPropertyChanged(nameof(TimelineTitle));
            OnPropertyChanged(nameof(TimelineDescription));
            OnPropertyChanged(nameof(TimelineEmptyTitle));
            OnPropertyChanged(nameof(TimelineEmptyDescription));
            OnPropertyChanged(nameof(TimelineCountText));
            OnPropertyChanged(nameof(TimelineEntryCount));
        }

        /// <summary>
        /// Merge baseline, ordinary snapshots and comprehensive packages into one
        /// timeline ordered by recency. The baseline stays pinned at the top.
        /// </summary>
        private void RebuildTimelineEntries()
        {
            var items = new List<(BackupTimelineEntryView Entry, DateTime SortKey)>();

            var showBaseline = HasFirstRunBaseline
                && TimelineFilter != BackupTimelineFilter.OrdinarySnapshots
                && TimelineFilter != BackupTimelineFilter.ComprehensivePackages;
            if (showBaseline)
            {
                var metaDate = _baselineCreatedAtLocal?.ToString("yyyy-MM-dd");
                items.Add((
                    new BackupTimelineEntryView
                    {
                        Kind = BackupTimelineEntryKind.Baseline,
                        Name = "首次使用前的备份",
                        TagText = "安全基线",
                        MetaText = string.IsNullOrEmpty(metaDate)
                            ? "兜底恢复点"
                            : $"{metaDate} · 兜底恢复点"
                    },
                    DateTime.MaxValue));
            }

            var showSnapshots = TimelineFilter != BackupTimelineFilter.FirstRunBaseline
                && TimelineFilter != BackupTimelineFilter.ComprehensivePackages;
            if (showSnapshots)
            {
                foreach (var snapshot in Snapshots)
                {
                    items.Add((
                        new BackupTimelineEntryView
                        {
                            Kind = BackupTimelineEntryKind.Snapshot,
                            Name = snapshot.Name,
                            TagText = "快照",
                            MetaText = $"{FormatEntryTime(snapshot.CreatedAt)} · {snapshot.SettingsCount} 项可核验设置 · 操作级回滚仅限已保存有效恢复材料的操作",
                            Snapshot = snapshot
                        },
                        snapshot.CreatedAt));
                }
            }

            var showPackages = TimelineFilter == BackupTimelineFilter.All
                || TimelineFilter == BackupTimelineFilter.ComprehensivePackages;
            if (showPackages)
            {
                foreach (var package in ComprehensiveBackups)
                {
                    var created = package.Manifest.CreatedUtc.LocalDateTime;
                    var assetCount = package.Manifest.Artifacts.Count;
                    var packageState = !package.Manifest.IsComplete
                        ? "备份不完整"
                        : package.IntegrityVerified
                            ? "完整性已验证"
                            : "完整性待验证";
                    items.Add((
                        new BackupTimelineEntryView
                        {
                            Kind = BackupTimelineEntryKind.Package,
                            Name = $"完整配置备份 {created:MM-dd HH:mm}",
                            TagText = package.Manifest.IsComplete ? "完整包" : "备份不完整",
                            MetaText = $"{FormatEntryTime(created)} · {packageState} · {assetCount} 个资产 · typed 快照 + 恢复材料 + HKCU 导出 + 应用配置",
                            Package = package
                        },
                        created));
                }
            }

            var ordered = items
                .OrderByDescending(item => item.Entry.IsBaseline)
                .ThenByDescending(item => item.SortKey)
                .ToList();

            TimelineEntries.Clear();
            string? lastGroup = null;
            var index = 0;
            foreach (var item in ordered)
            {
                index++;
                var group = item.Entry.IsBaseline ? "已固定" : DateGroupLabel(item.SortKey);
                item.Entry.GroupLabel = group;
                item.Entry.ShowGroupLabel = !string.Equals(group, lastGroup, StringComparison.Ordinal);
                lastGroup = group;
                item.Entry.Index = index.ToString("D2");
                item.Entry.IsSelected = item.Entry.Snapshot is not null
                    ? ReferenceEquals(item.Entry.Snapshot, SelectedSnapshot)
                    : item.Entry.Package is not null
                        && ReferenceEquals(item.Entry.Package, SelectedComprehensiveBackup);
                TimelineEntries.Add(item.Entry);
            }
        }

        private static string DateGroupLabel(DateTime date)
        {
            var today = DateTime.Today;
            if (date.Date == today) return "今天";
            if (date.Date == today.AddDays(-1)) return "昨天";
            return "更早";
        }

        private static string FormatEntryTime(DateTime date)
        {
            var today = DateTime.Today;
            if (date.Date == today) return $"今天 {date:HH:mm}";
            if (date.Date == today.AddDays(-1)) return $"昨天 {date:HH:mm}";
            return date.ToString("yyyy-MM-dd HH:mm");
        }

        private void SyncTimelineSelection()
        {
            foreach (var entry in TimelineEntries)
            {
                entry.IsSelected = entry.Snapshot is not null
                    ? ReferenceEquals(entry.Snapshot, SelectedSnapshot)
                    : entry.Package is not null
                        && ReferenceEquals(entry.Package, SelectedComprehensiveBackup);
            }
        }

        [RelayCommand]
        private void SelectTimelineEntry(BackupTimelineEntryView? entry)
        {
            if (entry is null) return;
            switch (entry.Kind)
            {
                case BackupTimelineEntryKind.Snapshot:
                    SelectedSnapshot = entry.Snapshot;
                    break;
                case BackupTimelineEntryKind.Package:
                    SelectedComprehensiveBackup = entry.Package;
                    break;
                case BackupTimelineEntryKind.Baseline:
                    SelectedSnapshot = null;
                    SelectedComprehensiveBackup = null;
                    break;
            }
        }

        [RelayCommand]
        private void SelectTimelineFilter(string? filter)
        {
            TimelineFilter = filter switch
            {
                "Ordinary" or "OrdinarySnapshots" => BackupTimelineFilter.OrdinarySnapshots,
                "Baseline" or "FirstRunBaseline" => BackupTimelineFilter.FirstRunBaseline,
                "Packages" or "ComprehensivePackages" => BackupTimelineFilter.ComprehensivePackages,
                _ => BackupTimelineFilter.All
            };
        }

        [RelayCommand]
        private async Task LoadSnapshotsAsync()
        {
            IsLoading = true;
            PageState = BackupManagerPageState.Loading;
            try
            {
                Snapshots.Clear();
                var catalog = await _snapshotService.GetSnapshotCatalogAsync();
                foreach (var snapshot in catalog.Snapshots)
                {
                    Snapshots.Add(snapshot);
                }
                
                TotalSnapshots = Snapshots.Count;
                HasSnapshots = Snapshots.Count > 0;
                LatestSnapshot = Snapshots.OrderByDescending(s => s.CreatedAt).FirstOrDefault();
                SnapshotCountDisplay = Loc.Get("Backup.SnapshotCount", TotalSnapshots);
                
                var totalBytes = Snapshots.Sum(s => s.FileSize);
                TotalSizeText = FormatFileSize(totalBytes);
                RefreshStorageVolumeSummary();
                var coverage = await _snapshotService.GetCoverageReportAsync();
                OrdinarySnapshotCoverageText = coverage.IsComplete
                    ? $"普通快照：{coverage.Summary}；{coverage.CapturedScalarValueCount} 个标量值。"
                    : $"普通快照：可自动核验恢复 {coverage.CapturedOperationCount}/{coverage.CatalogOperationCount} 项（typed 契约）；" +
                      $"其余 {coverage.UncapturedOperationIds.Count} 项未纳入 typed 自动恢复；逐操作回滚仅对已成功保存有效恢复材料的操作可用。";

                ComprehensiveBackups.Clear();
                if (_comprehensiveBackupService != null)
                {
                    foreach (var package in await _comprehensiveBackupService.GetPackagesAsync())
                        ComprehensiveBackups.Add(package);
                    ComprehensiveBackupCountText = $"完整用户配置备份包：{ComprehensiveBackups.Count} 个；自动恢复仅限明确契约资产。";
                    var latestComprehensive = ComprehensiveBackups.FirstOrDefault();
                    if (latestComprehensive is null)
                    {
                        ComprehensiveBackupCoverageText = "暂无完整备份包，尚未读取操作级覆盖范围。";
                    }
                    else
                    {
                        var states = latestComprehensive.Manifest.OperationStates;
                        var automatic = states.Count(item => item.AutomaticRestoreSupported);
                        var probeFailed = states.Count(item => item.IsCompliant is null);
                        var notAutomatic = Math.Max(0, states.Count - automatic - probeFailed);
                        ComprehensiveBackupCoverageText =
                            $"最新包 {latestComprehensive.PackageId}：可自动恢复 {automatic} 项 · 仅手动/未接入 {notAutomatic} 项 · 探测失败 {probeFailed} 项。";
                    }
                }
                else
                {
                    ComprehensiveBackupCountText = "完整用户配置备份服务尚未接入";
                    ComprehensiveBackupCoverageText = "完整备份覆盖明细暂不可用";
                }
                
                await LoadBaselineBridgeAsync();
                
                AnalyzeBackupNeeds();
                if (HasFirstRunBaseline && HasSnapshots)
                {
                    StatusText = $"已加载 {Snapshots.Count} 个普通快照，首次安全基线已联动到备份中心。";
                }
                else if (HasFirstRunBaseline)
                {
                    StatusText = "已检测到首次安全基线，当前备份中心暂无普通快照。";
                }
                else
                {
                    StatusText = HasSnapshots ? Loc.Get("Backup.LoadedCount", Snapshots.Count) : Loc.Get("Backup.NoSnapshotsSuggest");
                }

                PageState = !HasSnapshots && !HasFirstRunBaseline
                    ? BackupManagerPageState.Empty
                    : catalog.Diagnostics.Count > 0
                        ? BackupManagerPageState.Partial
                    : HasFirstRunBaseline && !HasSnapshots
                        ? BackupManagerPageState.Partial
                        : BackupManagerPageState.Ready;

                if (catalog.Diagnostics.Count > 0)
                {
                    StatusText += $"；已隔离 {catalog.Diagnostics.Count} 个未通过完整性校验的快照文件。";
                }

                await DiscoverAndReconcilePendingRestoresAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BackupManager][LoadSnapshots] {ex.Message}");
                StatusText = Loc.Get("Backup.LoadFailed", ex.Message);
                PageState = IsAuthorizationFailure(ex)
                    ? BackupManagerPageState.Unauthorized
                    : BackupManagerPageState.Error;
            }
            finally
            {
                IsLoading = false;
            }
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }

        private void AnalyzeBackupNeeds()
        {
            if (Snapshots.Count == 0 && !HasFirstRunBaseline)
            {
                NeedsBackup = true;
                BackupSuggestion = Loc.Get("Backup.SuggestFirstBackup");
                return;
            }

            if (Snapshots.Count == 0)
            {
                NeedsBackup = false;
                BackupSuggestion = "已存在首次安全基线，如有较大改动再创建普通快照即可。";
                return;
            }

            var latest = Snapshots.OrderByDescending(s => s.CreatedAt).First();
            var daysSinceLastBackup = (DateTime.Now - latest.CreatedAt).TotalDays;

            // P1-014: 同时检查时间和操作数量
            if (_operationsSinceLastBackup >= 10)
            {
                NeedsBackup = true;
                BackupSuggestion = Loc.Get("Backup.SuggestAfterOptimization", _operationsSinceLastBackup);
            }
            else if (daysSinceLastBackup > 7)
            {
                NeedsBackup = true;
                BackupSuggestion = Loc.Get("Backup.SuggestBackupDays", (int)daysSinceLastBackup);
            }
            else if (daysSinceLastBackup > 3 && _operationsSinceLastBackup >= 5)
            {
                NeedsBackup = true;
                BackupSuggestion = Loc.Get("Backup.SuggestBackupDaysAndOps", (int)daysSinceLastBackup, _operationsSinceLastBackup);
            }
            else if (daysSinceLastBackup > 3)
            {
                NeedsBackup = true;
                BackupSuggestion = Loc.Get("Backup.SuggestRegularBackup");
            }
            else
            {
                NeedsBackup = false;
                BackupSuggestion = string.Empty;
            }
        }

        private void RefreshStorageVolumeSummary()
        {
            try
            {
                var snapshotDirectory = _snapshotService.GetSnapshotDirectory();
                BackupStoragePathText = snapshotDirectory;
                var root = Path.GetPathRoot(Path.GetFullPath(snapshotDirectory));
                if (string.IsNullOrWhiteSpace(root))
                {
                    StorageVolumeText = "存储卷不可用";
                    StorageVolumeDetail = "未能确定快照目录所在卷";
                    return;
                }

                var drive = new DriveInfo(root);
                if (!drive.IsReady)
                {
                    StorageVolumeText = $"{drive.Name} 不可用";
                    StorageVolumeDetail = "备份存储卷当前未就绪";
                    return;
                }

                var usedBytes = Math.Max(0, drive.TotalSize - drive.AvailableFreeSpace);
                StorageVolumeText = $"{drive.Name} 已用 {FormatFileSize(usedBytes)} / {FormatFileSize(drive.TotalSize)}";
                StorageVolumeDetail = $"可用 {FormatFileSize(drive.AvailableFreeSpace)} · 应用未设置容量上限";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                StorageVolumeText = "存储卷不可用";
                StorageVolumeDetail = ex is UnauthorizedAccessException ? "没有权限读取备份存储卷容量" : "未能读取备份存储卷容量";
                BackupStoragePathText = "备份目录暂不可用";
            }
        }

        private async Task LoadBaselineBridgeAsync()
        {
            HasFirstRunBaseline = false;
            CanRestoreFirstRunBaseline = false;
            _activeBaselinePackageId = string.Empty;
            _baselineCreatedAtLocal = null;
            RestoreCoverage.Clear();
            RestoreCoverageSummary = "尚未读取到首次基线覆盖范围";
            BaselinePackageSizeText = "首次基线包：未找到";

            var latestSnapshotTime = LatestSnapshot?.CreatedAt;
            BaselineCatalogEntry? latestBaseline = null;

            if (_baselineCatalogService != null)
            {
                var entries = await _baselineCatalogService.GetEntriesAsync();
                latestBaseline = entries
                    .Where(entry => string.Equals(entry.PackageType, "BaselineFirstRun", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(entry => entry.CreatedUtc)
                    .FirstOrDefault();
            }

            SystemRestoreStatus? restoreStatus = null;
            if (_systemRestoreService != null)
            {
                restoreStatus = await _systemRestoreService.GetSystemRestoreStatusAsync();
            }

            if (latestBaseline != null)
            {
                HasFirstRunBaseline = true;
                _activeBaselinePackageId = latestBaseline.PackageId;
                CanRestoreFirstRunBaseline =
                    latestBaseline.BaselineState == FirstRunBaselineState.CompletedVerified &&
                    string.Equals(latestBaseline.IntegrityState, "Verified", StringComparison.OrdinalIgnoreCase);

                var baselineCreatedLocal = latestBaseline.CreatedUtc.ToLocalTime();
                _baselineCreatedAtLocal = baselineCreatedLocal;
                FirstRunBaselineStatusTitle = CanRestoreFirstRunBaseline
                    ? "首次安全基线已建立"
                    : "首次安全基线存在，但状态待确认";
                FirstRunBaselineStatusSubText = $"基线包：{latestBaseline.PackageId} | 创建时间：{baselineCreatedLocal:yyyy-MM-dd HH:mm}";
                FirstRunBaselineStatusDescription = BuildBaselineDescription(latestBaseline, restoreStatus);
                SnapshotCountDisplay = $"普通快照 {TotalSnapshots} | 首次基线 1";
                await PopulateBaselineCoverageAsync(latestBaseline);

                var baselineCreatedAt = baselineCreatedLocal;
                if (!latestSnapshotTime.HasValue || baselineCreatedAt > latestSnapshotTime.Value)
                {
                    LastBackupText = FormatRelativeTime(baselineCreatedAt);
                }
                else
                {
                    LastBackupText = FormatRelativeTime(latestSnapshotTime.Value);
                }
            }
            else
            {
                FirstRunBaselineStatusTitle = "尚未发现首次安全基线";
                FirstRunBaselineStatusSubText = "备份中心当前只显示普通快照";
                FirstRunBaselineStatusDescription = restoreStatus == null
                    ? "首次基线尚未接入，系统还原点状态暂不可用。"
                    : BuildRestorePointOnlyDescription(restoreStatus);
                SnapshotCountDisplay = Loc.Get("Backup.SnapshotCount", TotalSnapshots);
                LastBackupText = latestSnapshotTime.HasValue ? FormatRelativeTime(latestSnapshotTime.Value) : Loc.Get("Backup.NoBackupYet");
                RestoreCoverageSummary = "未找到首次基线；普通快照仅保存其自身列出的受支持设置。";
            }

            BackupProtectionText = BuildProtectionBadge(latestBaseline, restoreStatus);
            HasAnyBackup = HasSnapshots || HasFirstRunBaseline;
        }

        private async Task PopulateBaselineCoverageAsync(BaselineCatalogEntry entry)
        {
            if (_baselineCatalogService is null)
            {
                RestoreCoverageSummary = "首次基线目录未接入，无法读取覆盖范围。";
                return;
            }

            try
            {
                var manifest = await _baselineCatalogService.GetManifestAsync(entry.PackageId);
                var packageDirectory = await _baselineCatalogService.GetPackageDirectoryAsync(entry.PackageId);
                if (!string.IsNullOrWhiteSpace(packageDirectory))
                {
                    var packageSize = TryGetDirectorySize(packageDirectory);
                    BaselinePackageSizeText = packageSize.Success
                        ? $"首次基线包：{FormatFileSize(packageSize.SizeBytes)}"
                        : "首次基线包：无法读取";
                }
                else
                {
                    BaselinePackageSizeText = "首次基线包：目录不可用";
                }

                if (manifest is null)
                {
                    RestoreCoverageSummary = "首次基线清单不可读；无法确认可恢复范围。";
                    return;
                }

                var packageVerified = entry.BaselineState == FirstRunBaselineState.CompletedVerified
                    && string.Equals(entry.IntegrityState, "Verified", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(entry.RecoverabilityCheckState, "Passed", StringComparison.OrdinalIgnoreCase);

                foreach (var domain in manifest.Domains.OrderBy(domain => domain.DomainId, StringComparer.OrdinalIgnoreCase))
                {
                    var assessment = await ReadCoverageAssessmentAsync(packageDirectory, domain, packageVerified);
                    RestoreCoverage.Add(new BackupCoverageStatusItem
                    {
                        Name = string.IsNullOrWhiteSpace(domain.DomainName) ? domain.DomainId : domain.DomainName,
                        CaptureStatus = assessment.CaptureStatus,
                        RecoveryStatus = assessment.RecoveryStatus,
                        Detail = assessment.Detail,
                        IsAutomaticRecovery = assessment.IsAutomaticRecovery
                    });
                }

                var automaticCount = RestoreCoverage.Count(item => item.IsAutomaticRecovery);
                var manualCount = RestoreCoverage.Count(item => item.RecoveryStatus == "仅手动恢复");
                var unavailableCount = RestoreCoverage.Count - automaticCount - manualCount;
                RestoreCoverageSummary = $"自动恢复 {automaticCount} 项 · 仅手动恢复 {manualCount} 项 · 不可自动恢复 {unavailableCount} 项";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                RestoreCoverageSummary = ex is UnauthorizedAccessException
                    ? "没有权限读取首次基线覆盖范围。"
                    : "首次基线覆盖范围读取失败。";
                BaselinePackageSizeText = "首次基线包：无法读取";
            }
        }

        private static async Task<BaselineRecoveryCoverageAssessment> ReadCoverageAssessmentAsync(
            string? packageDirectory,
            BaselineDomainArtifact domain,
            bool packageVerified)
        {
            if (domain.Status != BaselineDomainStatus.Succeeded)
            {
                return BaselineRecoveryCoverageClassifier.Evaluate(domain, packageVerified, null, null);
            }

            if (string.IsNullOrWhiteSpace(packageDirectory))
            {
                return BaselineRecoveryCoverageClassifier.Evaluate(domain, packageVerified, null, "首次基线目录不可用，无法读取恢复步骤。");
            }

            try
            {
                var relativePath = string.IsNullOrWhiteSpace(domain.RelativePath)
                    ? domain.DomainId + ".bundle.zip"
                    : domain.RelativePath;
                var artifactPath = Path.Combine(packageDirectory, relativePath);
                if (!File.Exists(artifactPath))
                {
                    return BaselineRecoveryCoverageClassifier.Evaluate(domain, packageVerified, null, "恢复资产文件缺失，已阻断自动恢复。");
                }

                if (!artifactPath.EndsWith(".bundle.zip", StringComparison.OrdinalIgnoreCase))
                {
                    return BaselineRecoveryCoverageClassifier.Evaluate(domain, packageVerified, null, "恢复资产不是受支持的域包格式，已阻断自动恢复。");
                }

                using var archive = ZipFile.OpenRead(artifactPath);
                var manifestEntry = archive.Entries.FirstOrDefault(entry => entry.FullName.Equals("domain-manifest.json", StringComparison.OrdinalIgnoreCase));
                if (manifestEntry is null)
                {
                    return BaselineRecoveryCoverageClassifier.Evaluate(domain, packageVerified, null, "域包缺少恢复步骤清单，已阻断自动恢复。");
                }

                await using var stream = manifestEntry.Open();
                var bundle = await JsonSerializer.DeserializeAsync<BaselineDomainBundleManifest>(stream, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (bundle is null || !string.Equals(bundle.DomainId, domain.DomainId, StringComparison.OrdinalIgnoreCase))
                {
                    return BaselineRecoveryCoverageClassifier.Evaluate(domain, packageVerified, null, "域包与首次基线清单不匹配，已阻断自动恢复。");
                }

                return BaselineRecoveryCoverageClassifier.Evaluate(domain, packageVerified, bundle.RestoreSteps, null);
            }
            catch (InvalidDataException)
            {
                return BaselineRecoveryCoverageClassifier.Evaluate(domain, packageVerified, null, "域包损坏，已阻断自动恢复。");
            }
            catch (JsonException)
            {
                return BaselineRecoveryCoverageClassifier.Evaluate(domain, packageVerified, null, "域包清单不可读，已阻断自动恢复。");
            }
            catch (IOException)
            {
                return BaselineRecoveryCoverageClassifier.Evaluate(domain, packageVerified, null, "读取恢复资产失败，已阻断自动恢复。");
            }
            catch (UnauthorizedAccessException)
            {
                return BaselineRecoveryCoverageClassifier.Evaluate(domain, packageVerified, null, "没有权限读取恢复资产，已阻断自动恢复。");
            }
        }

        private static (bool Success, long SizeBytes) TryGetDirectorySize(string directory)
        {
            try
            {
                if (!Directory.Exists(directory))
                {
                    return (false, 0);
                }

                return (true, Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                    .Sum(path => new FileInfo(path).Length));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return (false, 0);
            }
        }

        private static string BuildBaselineDescription(BaselineCatalogEntry entry, SystemRestoreStatus? restoreStatus)
        {
            var integrityText = string.Equals(entry.IntegrityState, "Verified", StringComparison.OrdinalIgnoreCase)
                ? "已通过完整性校验"
                : $"完整性状态：{entry.IntegrityState}";
            var recoverabilityText = string.Equals(entry.RecoverabilityCheckState, "Passed", StringComparison.OrdinalIgnoreCase)
                ? "已通过可恢复性自检"
                : string.IsNullOrWhiteSpace(entry.RecoverabilityCheckState)
                    ? "尚未执行可恢复性自检"
                    : $"自检状态：{entry.RecoverabilityCheckState}";
            var restoreText = restoreStatus == null
                ? "系统还原点状态暂不可用"
                : BuildRestorePointOnlyDescription(restoreStatus);

            return $"首次备份已纳入备份中心。成功域 {entry.DomainSucceededCount}，失败域 {entry.DomainFailedCount}，{integrityText}，{recoverabilityText}。{restoreText}";
        }

        private static string BuildRestorePointOnlyDescription(SystemRestoreStatus status)
        {
            var enabledText = status.IsEnabled ? "系统还原已开启" : "系统还原未开启";
            var lastPointText = status.LastRestorePointTime.HasValue
                ? $"最近还原点：{status.LastRestorePointTime.Value:yyyy-MM-dd HH:mm}"
                : "最近还原点：暂无";
            return $"{enabledText}，还原点数量 {status.RestorePointCount}，{lastPointText}。";
        }

        private static string BuildProtectionBadge(BaselineCatalogEntry? entry, SystemRestoreStatus? restoreStatus)
        {
            var baselineReady = entry != null &&
                entry.BaselineState == FirstRunBaselineState.CompletedVerified &&
                string.Equals(entry.IntegrityState, "Verified", StringComparison.OrdinalIgnoreCase);
            var restoreReady = restoreStatus?.IsEnabled == true && restoreStatus.RestorePointCount > 0;

            if (baselineReady && restoreReady)
            {
                return "首次基线和系统还原点已保护";
            }

            if (baselineReady)
            {
                return "首次基线已保护，还原点待确认";
            }

            if (restoreReady)
            {
                return "系统还原点已就绪，首次基线待完成";
            }

            return "首次保护尚未完整建立";
        }

        private static string FormatRelativeTime(DateTime time)
        {
            var span = DateTime.Now - time;
            if (span.TotalMinutes < 1)
            {
                return Loc.Get("Backup.JustNow");
            }

            if (span.TotalHours < 1)
            {
                return Loc.Get("Backup.MinutesAgo", (int)span.TotalMinutes);
            }

            if (span.TotalDays < 1)
            {
                return Loc.Get("Backup.HoursAgo", (int)span.TotalHours);
            }

            return Loc.Get("Backup.DaysAgo", (int)span.TotalDays);
        }

        [RelayCommand]
        private async Task RestoreFirstRunBaselineAsync()
        {
            if (!CanRestoreFirstRunBaseline || string.IsNullOrWhiteSpace(_activeBaselinePackageId) || _baselineRestoreOrchestrator == null)
            {
                StatusText = "当前没有可恢复的首次安全基线。";
                return;
            }

            var confirmed = Views.ConfirmDialog.Show(
                Application.Current.MainWindow,
                "恢复首次安全基线",
                "这会把系统恢复到首次使用本软件时的安全基线状态。执行前会先创建应急快照，部分设置可能需要重启后生效。",
                Constants.IconGlyphs.Warning);

            if (!confirmed)
            {
                StatusText = "已取消恢复首次安全基线。";
                return;
            }

            IsLoading = true;
            StatusText = "正在恢复首次安全基线，请稍候...";

            try
            {
                var result = await _baselineRestoreOrchestrator.RestoreAsync(_activeBaselinePackageId);
                if (result.Success)
                {
                    var rebootHint = result.Report.RequiresReboot ? " 部分设置需要重启后生效。" : string.Empty;
                    StatusText = $"首次安全基线恢复完成：成功 {result.Report.SuccessCount} 项，失败 {result.Report.FailedCount} 项。{rebootHint}".Trim();
                }
                else
                {
                    StatusText = string.IsNullOrWhiteSpace(result.Message)
                        ? "首次安全基线恢复失败。"
                        : $"首次安全基线恢复失败：{result.Message}";
                }

                await LoadSnapshotsAsync();
            }
            catch (Exception ex)
            {
                StatusText = $"首次安全基线恢复失败：{ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task QuickBackupAsync()
        {
            // P0-002: 检查磁盘空间
            var snapshotDir = AppPaths.SnapshotsDir;
            var diskCheck = SystemResourceChecker.CheckDiskSpace(snapshotDir, 10 * 1024 * 1024);
            if (!diskCheck.Success)
            {
                StatusText = diskCheck.ErrorMessage ?? Loc.Get("Backup.Error.DiskFull");
                Debug.WriteLine($"[BackupManagerVM] QuickBackup disk check failed: {diskCheck.ErrorMessage}");
                
                Views.InfoDialog.Show(Application.Current.MainWindow,
                    Loc.Get("Common.Error"),
                    diskCheck.ErrorMessage ?? Loc.Get("Backup.Error.DiskFull"),
                    Constants.IconGlyphs.Warning);
                return;
            }
            
            var name = $"受支持设置快照_{DateTime.Now:yyyyMMdd_HHmmss}";
            IsLoading = true;
            StatusText = Loc.Get("Backup.ScanningSystem");
            try
            {
                var snapshot = await _snapshotService.CreateSnapshotAsync(name);
                await LoadSnapshotsAsync();
                var typedCount = snapshot.TypedRegistryDwordStates.Count + snapshot.TypedRegistryCapabilityStates.Count;
                StatusText = $"受支持设置快照已创建：{typedCount} 个可核验 typed capability。";
                _operationsSinceLastBackup = 0;
                NeedsBackup = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BackupManagerVM] QuickBackup failed: {ex}");
                StatusText = Loc.Get("Backup.BackupFailed", ex.Message);
                
                // 记录详细错误日志
                LogBackupError("QuickBackup", ex, $"快照名称: {name}");
                
                var diagnostic = _diagnosticService.DiagnoseBackupFailure(ex, "QuickBackup");
                var repairOptions = _diagnosticService.GetRepairOptions(diagnostic);
                
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var dialog = _repairService != null 
                        ? new Views.SmartErrorDialog(_repairService) 
                        : new Views.SmartErrorDialog();
                    
                    dialog.Owner = Application.Current.MainWindow;
                    dialog.SetError(Loc.Get("Backup.QuickBackup"), ex.Message, diagnostic, repairOptions);
                    
                    var result = dialog.ShowDialog();
                    if (result == true)
                    {
                        _ = LoadSnapshotsAsync().ContinueWith(t =>
                        {
                            if (t.IsFaulted && t.Exception != null)
                                Debug.WriteLine($"[BackupManager][QuickBackup] Reload failed: {t.Exception.GetBaseException().Message}");
                        }, TaskScheduler.Default);
                    }
                });
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task CreateSnapshotAsync()
        {
            if (string.IsNullOrWhiteSpace(NewSnapshotName))
            {
                StatusText = Loc.Get("Backup.EnterSnapshotName");
                return;
            }

            // P0-002: 检查磁盘空间
            var snapshotDir = AppPaths.SnapshotsDir;
            var diskCheck = SystemResourceChecker.CheckDiskSpace(snapshotDir, 10 * 1024 * 1024);
            if (!diskCheck.Success)
            {
                StatusText = diskCheck.ErrorMessage ?? Loc.Get("Backup.Error.DiskFull");
                Debug.WriteLine($"[BackupManagerVM] CreateSnapshot disk check failed: {diskCheck.ErrorMessage}");
                
                Views.InfoDialog.Show(Application.Current.MainWindow,
                    Loc.Get("Common.Error"),
                    diskCheck.ErrorMessage ?? Loc.Get("Backup.Error.DiskFull"),
                    Constants.IconGlyphs.Warning);
                return;
            }

            IsLoading = true;
            StatusText = Loc.Get("Backup.ScanningSystem");
            try
            {
                var snapshot = await _snapshotService.CreateSnapshotAsync(NewSnapshotName);
                NewSnapshotName = string.Empty;
                await LoadSnapshotsAsync();
                var typedCount = snapshot.TypedRegistryDwordStates.Count + snapshot.TypedRegistryCapabilityStates.Count;
                StatusText = $"受支持设置快照“{snapshot.Name}”已创建：{typedCount} 个可核验 typed capability。";
                _operationsSinceLastBackup = 0;
                NeedsBackup = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BackupManagerVM] CreateSnapshot failed: {ex}");
                StatusText = Loc.Get("Backup.CreateFailed", ex.Message);
                
                // 记录详细错误日志
                LogBackupError("CreateSnapshot", ex, $"快照名称: {NewSnapshotName}");
                
                var diagnostic = _diagnosticService.DiagnoseBackupFailure(ex, "CreateSnapshot");
                var repairOptions = _diagnosticService.GetRepairOptions(diagnostic);
                
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var dialog = _repairService != null 
                        ? new Views.SmartErrorDialog(_repairService) 
                        : new Views.SmartErrorDialog();
                    
                    dialog.Owner = Application.Current.MainWindow;
                    dialog.SetError(Loc.Get("Backup.CreateSnapshot"), ex.Message, diagnostic, repairOptions);
                    
                    var result = dialog.ShowDialog();
                    if (result == true)
                    {
                        _ = LoadSnapshotsAsync().ContinueWith(t =>
                        {
                            if (t.IsFaulted && t.Exception != null)
                                Debug.WriteLine($"[BackupManager][CreateSnapshot] Reload failed: {t.Exception.GetBaseException().Message}");
                        }, TaskScheduler.Default);
                    }
                });
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// 后台生成完整用户配置备份包。该入口只采集当前用户 HKCU、应用配置和
        /// 已有 typed 快照；未经逐项恢复契约验证的注册表 hive 始终标记为仅手动恢复。
        /// </summary>
        [RelayCommand]
        private async Task CreateComprehensiveBackupAsync()
        {
            if (_comprehensiveBackupService is null)
            {
                ComprehensiveBackupStatusText = "完整用户配置备份服务尚未接入。";
                return;
            }

            if (IsLoading)
            {
                ComprehensiveBackupStatusText = "已有备份任务正在后台运行。";
                return;
            }

            var name = string.IsNullOrWhiteSpace(NewSnapshotName)
                ? $"完整用户配置备份_{DateTime.Now:yyyyMMdd_HHmmss}"
                : NewSnapshotName.Trim();
            IsLoading = true;
            StatusText = "正在后台采集完整用户配置…";
            ComprehensiveBackupStatusText = "正在采集当前用户注册表和应用配置…";
            try
            {
                var progress = new Progress<ComprehensiveBackupProgress>(value =>
                {
                    ComprehensiveBackupStatusText = $"{value.Message}（{value.Completed}/{value.Total}）";
                });
                var package = await _comprehensiveBackupService.CreatePackageAsync(name, progress).ConfigureAwait(true);
                ComprehensiveBackupStatusText = package.Manifest.IsComplete
                    ? $"完整备份包已生成：{package.Manifest.Artifacts.Count} 个资产；HKCU 导出仅可人工恢复。"
                    : $"备份包已生成，但有资产未完成：{package.Manifest.IncompleteReason}";
                StatusText = ComprehensiveBackupStatusText;
                NewSnapshotName = string.Empty;
                await LoadSnapshotsAsync();
            }
            catch (OperationCanceledException)
            {
                ComprehensiveBackupStatusText = "完整用户配置备份已取消，未发布不完整备份包。";
                StatusText = ComprehensiveBackupStatusText;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                ComprehensiveBackupStatusText = "完整用户配置备份失败，未发布不完整备份包。";
                StatusText = ComprehensiveBackupStatusText;
                Debug.WriteLine($"[BackupManagerVM] Comprehensive backup failed: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand(CanExecute = nameof(CanPrepareComprehensiveRestoreForPackage))]
        private async Task PrepareComprehensiveRestoreAsync(ComprehensiveBackupPackageInfo? package)
        {
            package ??= SelectedComprehensiveBackup;
            if (package is null || _comprehensiveBackupService is null)
            {
                RestorePlanStatusText = "没有可用的完整备份包，恢复计划未生成。";
                return;
            }

            SelectedComprehensiveBackup = package;
            IsPreparingRestore = true;
            PendingRestorePlan = null;
            RestoreReceipts.Clear();
            RestorePlanSourceText = $"完整备份包 {package.PackageId}";
            RestorePlanStatusText = "正在校验完整包并准备 typed 恢复计划…";
            try
            {
                var preparation = await _comprehensiveBackupService.PrepareTypedRestoreAsync(package.PackageId);
                PendingRestorePlan = preparation.Plan;
                var unsupported = preparation.Plan.Steps.Count(step => !step.Supported);
                RestoreCompatibilityText = unsupported == 0
                    ? "完整包中的 typed 设置已导入并通过兼容性准备。"
                    : $"完整包恢复计划包含 {unsupported} 个不支持步骤，执行按钮保持禁用。";
                RestorePlanStatusText = unsupported == 0
                    ? "完整包恢复计划已准备，等待人工核对并执行。"
                    : "完整包恢复计划已阻断，未执行写入。";
                PageState = unsupported == 0 ? BackupManagerPageState.Ready : BackupManagerPageState.Partial;
            }
            catch (Exception ex)
            {
                RestorePlanStatusText = $"完整包恢复准备失败：{ex.Message}";
                RestoreCompatibilityText = "完整包未生成可执行计划，当前没有任何恢复写入。";
                PageState = IsAuthorizationFailure(ex)
                    ? BackupManagerPageState.Unauthorized
                    : BackupManagerPageState.Partial;
            }
            finally
            {
                IsPreparingRestore = false;
            }
        }

        // File: GameOptimizer.Shell/ViewModels/BackupManagerViewModel.cs
        [RelayCommand]
        private async Task RestoreSnapshotAsync()
        {
            if (SelectedSnapshot == null)
            {
                StatusText = Loc.Get("Backup.SelectSnapshotFirst");
                return;
            }

            SnapshotRestorePlan plan;
            try
            {
                plan = await _snapshotService.PrepareRestoreAsync(SelectedSnapshot.Id, SnapshotRestoreKind.Normal);
            }
            catch (Exception ex)
            {
                StatusText = Loc.Get("Backup.RestoreFailed", ex.Message);
                return;
            }
            IsLoading = true;
            StatusText = Loc.Get("Backup.Restoring");
            try
            {
                var result = await _snapshotService.ExecuteRestoreAsync(plan);
                StatusText = result.Success
                    ? Loc.Get("Backup.RestoreSuccess", result.RestoredCount)
                    : result.Message;
                
                // VM-008: 发布快照恢复事件，通知仪表盘刷新模块状态
                if (result.Success && _eventBus != null)
                {
                    _ = _eventBus.PublishAsync(new SnapshotRestoredEvent(
                        SelectedSnapshot.Id, result.RestoredCount));
                    _operationsSinceLastBackup = 0; // 恢复后重置计数
                }
            }
            catch (Exception ex)
            {
                StatusText = Loc.Get("Backup.RestoreFailed", ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand(CanExecute = nameof(CanPrepareRestoreForSnapshot))]
        private async Task PrepareRestoreSnapshotAsync(SnapshotInfo? info)
        {
            info ??= SelectedSnapshot;
            if (info is null || IsLoading || IsPreparingRestore || IsExecutingRestore)
            {
                RestorePlanStatusText = "请先选择一个快照，且等待当前读取完成。";
                return;
            }

            SelectedSnapshot = info;
            IsPreparingRestore = true;
            PageState = BackupManagerPageState.Loading;
            RestoreReceipts.Clear();
            PendingRestorePlan = null;
            RestorePlanStatusText = $"正在读取“{info.Name}”的身份、版本和类型化清单…";
            try
            {
                var snapshot = await _snapshotService.GetSnapshotAsync(info.Id);
                if (snapshot is null)
                {
                    RestoreCompatibilityText = "快照内容无法读取，未生成恢复计划。";
                    RestorePlanStatusText = "读取失败：快照不存在或内容不可用。";
                    PageState = BackupManagerPageState.Error;
                    return;
                }

                if (snapshot.Version < 4 || (snapshot.TypedRegistryDwordStates.Count == 0 && snapshot.TypedRegistryCapabilityStates.Count == 0))
                {
                    RestoreCompatibilityText = $"版本 V{snapshot.Version} 不是当前类型化恢复契约；V1-V3 或缺少类型化清单的快照永久不可恢复，未执行写入。";
                    RestorePlanStatusText = "恢复已阻断：不支持的旧快照。";
                    PageState = BackupManagerPageState.Partial;
                    return;
                }

                var plan = await _snapshotService.PrepareRestoreAsync(info.Id, SnapshotRestoreKind.Normal);
                PendingRestorePlan = plan;
                var unsupported = plan.Steps.Count(step => !step.Supported);
                RestoreCompatibilityText = unsupported == 0
                    ? $"V{snapshot.Version} 类型化清单已读取，计划可进入显式确认。"
                    : $"计划包含 {unsupported} 个不支持步骤；执行按钮保持禁用，未执行写入。";
                RestorePlanStatusText = unsupported == 0
                    ? "恢复计划已准备，等待人工核对并执行。"
                    : "恢复计划已准备，但存在不支持步骤，已阻断。";
                PageState = unsupported == 0 ? BackupManagerPageState.Ready : BackupManagerPageState.Partial;
            }
            catch (Exception ex)
            {
                RestorePlanStatusText = $"准备恢复计划失败：{ex.Message}";
                RestoreCompatibilityText = "未生成可执行计划，当前没有任何恢复写入。";
                PageState = IsAuthorizationFailure(ex)
                    ? BackupManagerPageState.Unauthorized
                    : BackupManagerPageState.Error;
            }
            finally
            {
                IsPreparingRestore = false;
            }
        }

        [RelayCommand]
        private async Task ExecutePreparedRestoreAsync()
        {
            var plan = PendingRestorePlan;
            if (!CanExecutePendingRestore || plan is null)
            {
                RestorePlanStatusText = "当前没有满足安全门禁的恢复计划，执行已阻断。";
                return;
            }

            var confirmed = Views.ConfirmDialog.Show(
                Application.Current.MainWindow,
                "确认执行恢复",
                $"将自动恢复“{RestorePlanSourceText}”中已准备且受支持的 {plan.Steps.Count} 项设置。\n\n"
                + "注意：这只是快照内带自动恢复契约的子集——快照还记录了更多状态项，它们仅作记录或手动恢复材料，本次不会写入。\n\n"
                + "恢复会再次验证快照身份和当前状态；发生漂移、权限不足或任一步失败时会停止并生成回执。是否继续？",
                Constants.IconGlyphs.Warning);
            if (!confirmed)
            {
                RestorePlanStatusText = "已取消恢复；没有执行任何写入。";
                return;
            }

            IsExecutingRestore = true;
            _restoreExecutionCts?.Dispose();
            _restoreExecutionCts = new CancellationTokenSource();
            OnPropertyChanged(nameof(CanCancelRestore));
            PageState = BackupManagerPageState.Recovering;
            RestorePlanStatusText = "正在执行已确认计划；结果将以逐步回执和独立验证为准。";
            try
            {
                var result = await _snapshotService.ExecuteRestoreAsync(plan, _restoreExecutionCts.Token);
                RestoreReceipts.Clear();
                foreach (var receipt in result.TypedReceipts.Concat(result.CompensationReceipts))
                {
                    RestoreReceipts.Add(new BackupRestoreReceiptView
                    {
                        CapabilityId = receipt.CapabilityId,
                        Outcome = receipt.Outcome,
                        ReasonCode = receipt.ReasonCode,
                        Message = receipt.Message,
                        MutationOccurred = receipt.MutationOccurred
                    });
                }

                RestorePlanStatusText = string.IsNullOrWhiteSpace(result.Message)
                    ? $"恢复结果：{result.BatchState}；成功 {result.RestoredCount}，失败 {result.FailedCount}，不支持 {result.UnsupportedCount}。"
                    : result.Message;
                PageState = MapRestoreResultState(result);

                if (result.Success && result.BatchState == SnapshotRestoreBatchState.RestoredVerified && _eventBus != null)
                {
                    _ = _eventBus.PublishAsync(new SnapshotRestoredEvent(plan.SnapshotId, result.RestoredCount));
                    _operationsSinceLastBackup = 0;
                }
            }
            catch (OperationCanceledException)
            {
                RestorePlanStatusText = "已请求取消；正在对账持久化回执，确认是否发生写入。";
                await ReconcilePendingRestoreCoreAsync(plan);
            }
            catch (Exception ex)
            {
                RestorePlanStatusText = $"恢复执行失败：{ex.Message}";
                PageState = IsAuthorizationFailure(ex)
                    ? BackupManagerPageState.Unauthorized
                    : BackupManagerPageState.InterruptedManual;
            }
            finally
            {
                _restoreExecutionCts?.Dispose();
                _restoreExecutionCts = null;
                IsExecutingRestore = false;
                OnPropertyChanged(nameof(CanCancelRestore));
            }
        }

        [RelayCommand(CanExecute = nameof(CanCancelRestore))]
        private void CancelRestore()
        {
            RestorePlanStatusText = "正在请求取消；取消后将按持久化回执对账，不能假设零写入。";
            _restoreExecutionCts?.Cancel();
        }

        [RelayCommand(CanExecute = nameof(CanReconcilePendingRestore))]
        private async Task ReconcilePendingRestoreAsync()
        {
            var identity = GetActiveRecoveryIdentity();
            if (identity is null) return;
            await ReconcilePendingRestoreCoreAsync(identity.Value.PlanId, identity.Value.PlanSha256);
        }

        private async Task ReconcilePendingRestoreCoreAsync(SnapshotRestorePlan plan)
            => await ReconcilePendingRestoreCoreAsync(plan.PlanId, plan.PlanSha256);

        private async Task ReconcilePendingRestoreCoreAsync(Guid planId, string planSha256)
        {
            try
            {
                var report = await _snapshotService.ReconcileRestoreAsync(planId, planSha256);
                RecoveryStatusText = $"对账：{report.Status}；批次 {report.BatchState}；写入状态 {report.MutationStatus}；未知 {report.UnknownResourceCount?.ToString() ?? "?"}；人工 {report.ManualResourceCount?.ToString() ?? "?"}。{report.Message}";
                PageState = report.Status switch
                {
                    SnapshotRestoreRecoveryStatus.CompensatedVerified => BackupManagerPageState.CompensatedVerified,
                    SnapshotRestoreRecoveryStatus.ManualInterventionRequired or SnapshotRestoreRecoveryStatus.StoreFailure => BackupManagerPageState.InterruptedManual,
                    _ => BackupManagerPageState.Ready
                };
                OnPropertyChanged(nameof(CanResolveInterruptedRestore));
            }
            catch (Exception ex)
            {
                RecoveryStatusText = $"恢复对账失败：{ex.Message}";
                PageState = BackupManagerPageState.InterruptedManual;
            }
        }

        [RelayCommand(CanExecute = nameof(CanResolveInterruptedRestore))]
        private Task KeepInterruptedRestoreBlockedAsync() => ResolveInterruptedRestoreAsync(SnapshotRestoreManualDecision.KeepBlocked);

        [RelayCommand(CanExecute = nameof(CanResolveInterruptedRestore))]
        private Task ReleaseVerifiedZeroWriteAsync() => ResolveInterruptedRestoreAsync(SnapshotRestoreManualDecision.ReleaseVerifiedZeroWrite);

        [RelayCommand(CanExecute = nameof(CanResolveInterruptedRestore))]
        private Task AcknowledgeManualRecoveryAsync() => ResolveInterruptedRestoreAsync(SnapshotRestoreManualDecision.AcknowledgeManualRecovery);

        private async Task ResolveInterruptedRestoreAsync(SnapshotRestoreManualDecision decision)
        {
            var identity = GetActiveRecoveryIdentity();
            if (identity is null) return;
            try
            {
                var report = await _snapshotService.ResolveInterruptedRestoreAsync(identity.Value.PlanId, identity.Value.PlanSha256, decision);
                RecoveryStatusText = $"人工处置：{report.Status}；{report.Message}";
                PageState = report.Status == SnapshotRestoreRecoveryStatus.CompensatedVerified
                    ? BackupManagerPageState.CompensatedVerified
                    : report.Status == SnapshotRestoreRecoveryStatus.ManualInterventionRequired
                        ? BackupManagerPageState.InterruptedManual
                        : BackupManagerPageState.Ready;
            }
            catch (Exception ex)
            {
                RecoveryStatusText = $"人工处置失败：{ex.Message}";
                PageState = BackupManagerPageState.InterruptedManual;
            }
        }

        private (Guid PlanId, string PlanSha256)? GetActiveRecoveryIdentity()
        {
            if (ActiveRecoveryBatch is not null)
            {
                return (ActiveRecoveryBatch.PlanId, ActiveRecoveryBatch.PlanSha256);
            }

            return PendingRestorePlan is null
                ? null
                : (PendingRestorePlan.PlanId, PendingRestorePlan.PlanSha256);
        }

        private async Task DiscoverAndReconcilePendingRestoresAsync()
        {
            var batches = await _snapshotService.GetPendingRestoreBatchesAsync();
            if (batches.Count == 0)
            {
                ActiveRecoveryBatch = null;
                RecoveryStatusText = "未发现跨进程遗留的恢复批次。";
                return;
            }

            SnapshotRestoreBatchIdentity? selected = null;
            var summaries = new List<string>(batches.Count);
            foreach (var batch in batches.OrderByDescending(item => item.UpdatedAtUtc))
            {
                try
                {
                    var report = await _snapshotService.ReconcileRestoreAsync(batch.PlanId, batch.PlanSha256);
                    summaries.Add($"{batch.PlanId:N}: {report.Status}/{report.MutationStatus}");
                    if (selected is null && report.Status is SnapshotRestoreRecoveryStatus.ManualInterventionRequired or SnapshotRestoreRecoveryStatus.StoreFailure)
                    {
                        selected = batch;
                    }
                }
                catch (Exception ex)
                {
                    summaries.Add($"{batch.PlanId:N}: 对账失败（{ex.Message}）");
                    selected ??= batch;
                }
            }

            ActiveRecoveryBatch = selected ?? batches.OrderByDescending(item => item.UpdatedAtUtc).First();
            RecoveryStatusText = $"已发现并逐项对账 {batches.Count} 个持久化批次；当前选中 {ActiveRecoveryBatch.PlanId:N}。{string.Join("；", summaries)}";
            if (selected is not null)
            {
                PageState = BackupManagerPageState.InterruptedManual;
            }

            OnPropertyChanged(nameof(CanReconcilePendingRestore));
            OnPropertyChanged(nameof(CanResolveInterruptedRestore));
        }

        private static BackupManagerPageState MapRestoreResultState(SnapshotRestoreResult result)
        {
            if (result.RequiresReboot)
            {
                return BackupManagerPageState.RebootRequired;
            }

            if (result.BatchState == SnapshotRestoreBatchState.CompensatedVerified
                || result.CompensationStatus == SnapshotCompensationStatus.VerifiedBeforeState)
            {
                return BackupManagerPageState.CompensatedVerified;
            }

            if (result.BatchState == SnapshotRestoreBatchState.InterruptedManual
                || result.MutationStatus == SnapshotMutationStatus.Unknown
                || result.CompensationStatus == SnapshotCompensationStatus.FailedOrUnknown)
            {
                return BackupManagerPageState.InterruptedManual;
            }

            if (result.UnsupportedCount > 0 || result.FailedCount > 0 || result.Warnings.Count > 0)
            {
                return BackupManagerPageState.Partial;
            }

            return result.Success && result.BatchState == SnapshotRestoreBatchState.RestoredVerified
                ? BackupManagerPageState.Ready
                : BackupManagerPageState.Error;
        }

        private static bool IsAuthorizationFailure(Exception exception)
        {
            var text = exception.Message.ToLowerInvariant();
            return text.Contains("unauthorized", StringComparison.Ordinal)
                || text.Contains("access denied", StringComparison.Ordinal)
                || text.Contains("permission", StringComparison.Ordinal)
                || text.Contains("权限", StringComparison.Ordinal)
                || text.Contains("管理员", StringComparison.Ordinal);
        }

        [RelayCommand]
        private async Task RequestDeleteSnapshotAsync(SnapshotInfo? snapshot)
        {
            if (snapshot == null && SelectedSnapshot == null) return;
            var targetSnapshot = snapshot ?? SelectedSnapshot;
            if (targetSnapshot == null) return;
            
            // 使用自定义 ConfirmDialog 确认删除
            var confirmed = Views.ConfirmDialog.Show(
                Application.Current.MainWindow,
                Loc.Get("Backup.ConfirmDeleteTitle"),
                Loc.Get("Backup.ConfirmDelete", targetSnapshot.Name),
                Constants.IconGlyphs.Warning);
            
            if (!confirmed)
            {
                StatusText = Loc.Get("Backup.DeleteCancelled");
                return;
            }
            
            // 执行删除
            IsLoading = true;
            try
            {
                await _snapshotService.DeleteSnapshotAsync(targetSnapshot.Id);
                await LoadSnapshotsAsync();
                StatusText = Loc.Get("Backup.DeleteSuccess", targetSnapshot.Name);
            }
            catch (Exception ex)
            {
                StatusText = Loc.Get("Backup.DeleteFailed", ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task ConfirmDeleteAsync()
        {
            if (PendingDeleteSnapshot == null) return;

            IsLoading = true;
            ShowDeleteConfirm = false;
            try
            {
                await _snapshotService.DeleteSnapshotAsync(PendingDeleteSnapshot.Id);
                await LoadSnapshotsAsync();
                StatusText = Loc.Get("Backup.DeleteSuccess", PendingDeleteSnapshot.Name);
            }
            catch (Exception ex)
            {
                StatusText = Loc.Get("Backup.DeleteFailed", ex.Message);
            }
            finally
            {
                IsLoading = false;
                PendingDeleteSnapshot = null;
            }
        }

        [RelayCommand]
        private void CancelDelete()
        {
            ShowDeleteConfirm = false;
            PendingDeleteSnapshot = null;
            StatusText = Loc.Get("Backup.DeleteCancelled");
        }

        [RelayCommand]
        private async Task PreviewSnapshotAsync(SnapshotInfo? info)
        {
            if (info == null) return;

            IsLoading = true;
            try
            {
                PreviewSnapshot = await _snapshotService.GetSnapshotAsync(info.Id);
                if (PreviewSnapshot != null)
                {
                    PreviewSettings.Clear();
                    foreach (var item in PreviewSnapshot.TypedRegistryDwordStates)
                    {
                        PreviewSettings.Add(new SnapshotSettingItem
                        {
                            Key = item.CapabilityId,
                            Value = item.ValueExists ? item.Value?.ToString() ?? "(无效)" : "(不存在，将恢复为不存在)",
                            FullPath = item.SourceKey,
                            Category = "Typed DWORD capability"
                        });
                    }
                    foreach (var capability in PreviewSnapshot.TypedRegistryCapabilityStates)
                    {
                        foreach (var value in capability.Values)
                        {
                            PreviewSettings.Add(new SnapshotSettingItem
                            {
                                Key = $"{capability.CapabilityId} / {value.ValueName}",
                                Value = value.ValueExists
                                    ? value.Kind == SnapshotRegistryScalarKind.String ? value.StringValue ?? "(空字符串)" : value.DwordValue?.ToString() ?? "(无效)"
                                    : "(不存在，将恢复为不存在)",
                                FullPath = $"{capability.SubKeyPath}\\{value.ValueName}",
                                Category = $"Typed {value.Kind} capability"
                            });
                        }
                    }
                    ShowPreview = true;
                    StatusText = Loc.Get("Backup.PreviewLoaded", info.Name, PreviewSettings.Count);
                }
            }
            catch (Exception ex)
            {
                StatusText = Loc.Get("Backup.PreviewFailed", ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private void ClosePreview()
        {
            ShowPreview = false;
            PreviewSnapshot = null;
            PreviewSettings.Clear();
        }

        private static string GetFriendlyName(string key)
        {
            // 解析格式: keyPath|valueName|category
            var parts = key.Split('|');
            if (parts.Length >= 2)
            {
                return parts[1]; // 返回 valueName
            }
            // 兼容旧格式
            var pathParts = key.Split('\\');
            return pathParts.Length > 0 ? pathParts[^1] : key;
        }

        private static string GetCategory(string key)
        {
            var parts = key.Split('|');
            return parts.Length >= 3 ? parts[2] : Loc.Get("Backup.CategoryOther");
        }

        private static string FormatValue(object? value)
        {
            if (value == null) return Loc.Get("Backup.ValueEmpty");
            if (value is byte[] bytes) return Loc.Get("Backup.ValueBinary", bytes.Length);
            if (value is int intVal) return intVal == 0 ? Loc.Get("Backup.ValueDisabled") : intVal == 1 ? Loc.Get("Backup.ValueEnabled") : intVal.ToString();
            return value.ToString() ?? Loc.Get("Backup.ValueEmpty");
        }

        [RelayCommand]
        private async Task ExportSnapshotAsync()
        {
            if (SelectedSnapshot == null) return;

            var dialog = new SaveFileDialog
            {
                Filter = Loc.Get("Backup.JsonFilter"),
                FileName = $"{SelectedSnapshot.Name}.json"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    await _snapshotService.ExportSnapshotAsync(SelectedSnapshot.Id, dialog.FileName);
                    StatusText = Loc.Get("Backup.ExportSuccess");
                }
                catch (Exception ex)
                {
                    StatusText = Loc.Get("Backup.ExportFailed", ex.Message);
                }
            }
        }

        [RelayCommand]
        private async Task ImportSnapshotAsync()
        {
            var dialog = new OpenFileDialog
            {
                Filter = Loc.Get("Backup.JsonFilter")
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var snapshot = await _snapshotService.ImportSnapshotAsync(dialog.FileName);
                    if (snapshot != null)
                    {
                        await LoadSnapshotsAsync();
                        StatusText = Loc.Get("Backup.ImportSuccess", snapshot.Name);
                    }
                    else
                    {
                        StatusText = Loc.Get("Backup.ImportInvalid");
                    }
                }
                catch (Exception ex)
                {
                    StatusText = Loc.Get("Backup.ImportFailed", ex.Message);
                }
            }
        }

        [RelayCommand]
        private void OpenSnapshotFolder()
        {
            var dir = _snapshotService.GetSnapshotDirectory();
            if (!System.IO.Directory.Exists(dir))
            {
                if (!SafeDirectoryOperations.TryCreateDirectory(dir, out var error))
                {
                    Views.InfoDialog.Show(Application.Current.MainWindow,
                        Loc.Get("Common.Error"),
                        Loc.Get("Backup.CannotCreateDir", error ?? ""),
                        Constants.IconGlyphs.Warning);
                    return;
                }
            }
            
            var (process, processError) = SafeProcessHelper.StartProcess(new ProcessStartInfo("explorer.exe", dir)
            {
                UseShellExecute = true
            });
            
            if (process == null)
            {
                Views.InfoDialog.Show(Application.Current.MainWindow,
                    Loc.Get("Common.Error"),
                    Loc.Get("Backup.CannotOpenFolder", processError ?? ""),
                    Constants.IconGlyphs.Warning);
            }
        }

        [RelayCommand]
        private void OpenPackageFolder(ComprehensiveBackupPackageInfo? package)
        {
            if (package is null || string.IsNullOrWhiteSpace(package.DirectoryPath)
                || !Directory.Exists(package.DirectoryPath))
            {
                StatusText = "备份包目录不可用。";
                return;
            }

            var (process, processError) = SafeProcessHelper.StartProcess(
                new ProcessStartInfo("explorer.exe", $"\"{package.DirectoryPath}\"")
                {
                    UseShellExecute = true
                });
            if (process == null)
            {
                StatusText = $"无法打开备份包目录：{processError ?? "未知错误"}";
            }
        }

        [RelayCommand]
        private void RequestClearAll()
        {
            if (!HasSnapshots) return;
            
            // P2-020: 清空所有快照需要更强的二次确认
            var confirmed = Views.ConfirmDialog.Show(
                Application.Current.MainWindow,
                Loc.Get("Common.Warning"),
                Loc.Get("Backup.ClearAllWarning", Snapshots.Count),
                Constants.IconGlyphs.Warning);
            
            if (!confirmed)
            {
                StatusText = Loc.Get("Backup.ClearAllCancelled");
                return;
            }
            
            ShowClearAllConfirm = true;
            StatusText = Loc.Get("Backup.ConfirmClearAll", Snapshots.Count);
        }

        [RelayCommand]
        private async Task ConfirmClearAllAsync()
        {
            IsLoading = true;
            ShowClearAllConfirm = false;
            var count = Snapshots.Count;
            try
            {
                foreach (var snapshot in Snapshots.ToList())
                    await _snapshotService.DeleteSnapshotAsync(snapshot.Id);
                await LoadSnapshotsAsync();
                StatusText = Loc.Get("Backup.ClearAllSuccess", count);
            }
            catch (Exception ex)
            {
                StatusText = Loc.Get("Backup.ClearAllFailed", ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private void CancelClearAll()
        {
            ShowClearAllConfirm = false;
            StatusText = Loc.Get("Backup.ClearAllCancelled");
        }

        // File: GameOptimizer.Shell/ViewModels/BackupManagerViewModel.cs
        [RelayCommand]
        private async Task RestoreSpecificSnapshotAsync(SnapshotInfo? info)
        {
            if (info == null) return;

            SnapshotRestorePlan plan;
            try
            {
                plan = await _snapshotService.PrepareRestoreAsync(info.Id, SnapshotRestoreKind.Normal);
            }
            catch (Exception ex)
            {
                StatusText = Loc.Get("Backup.RestoreFailed", ex.Message);
                return;
            }

            IsLoading = true;
            StatusText = Loc.Get("Backup.RestoringSnapshot", info.Name);
            try
            {
                var result = await _snapshotService.ExecuteRestoreAsync(plan);
                
                // 重置深度恢复状态
                ShowDeepRestore = false;
                ShowNoRegBackupHint = false;
                
                StatusText = result.Success
                    ? Loc.Get("Backup.RestoreSuccess", result.RestoredCount)
                    : result.Message;
                
                // 判断是否需要显示深度恢复面板
                if (result.FailedCount > 0)
                {
                    var hasRegBackup = _snapshotService.HasRegistryBackup(info.Id);
                    if (hasRegBackup)
                    {
                        DeepRestoreSnapshotId = info.Id;
                        var regDir = Path.Combine(_snapshotService.GetSnapshotDirectory(), info.Id);
                        DeepRestoreFileCount = Directory.Exists(regDir) 
                            ? Directory.GetFiles(regDir, "*.reg").Length 
                            : 0;
                        ShowDeepRestore = DeepRestoreFileCount > 0;
                    }
                    else
                    {
                        ShowNoRegBackupHint = true;
                    }
                }
                
                // VM-008: 发布快照恢复事件
                if (result.Success && _eventBus != null)
                {
                    _ = _eventBus.PublishAsync(new SnapshotRestoredEvent(info.Id, result.RestoredCount));
                    _operationsSinceLastBackup = 0;
                }
            }
            catch (Exception ex)
            {
                StatusText = Loc.Get("Backup.RestoreFailed", ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        // File: GameOptimizer.Shell/ViewModels/BackupManagerViewModel.cs
        [RelayCommand]
        private async Task RestoreRegistryBackupAsync()
        {
            if (string.IsNullOrEmpty(DeepRestoreSnapshotId)) return;
            if (IsDeepRestoring || IsLoading) return;

            // 统一 Elevation 门禁
            var elevation = GameOptimizer.Core.Services.ElevationGate.Evaluate(
                "注册表备份恢复",
                operationRequiresAdministrator: true);
            if (elevation.NeedsElevation)
            {
                StatusText = GameOptimizer.Core.Services.ElevationGate.ShortLabel(elevation.Attribution);
                Views.InfoDialog.Show(Application.Current.MainWindow,
                    Loc.Get("Common.AdminRequired"),
                    elevation.UserMessage,
                    Constants.IconGlyphs.Lock);
                return;
            }

            SnapshotRestorePlan plan;
            try
            {
                plan = await _snapshotService.PrepareRestoreAsync(DeepRestoreSnapshotId, SnapshotRestoreKind.DeepRegistry);
            }
            catch (Exception ex)
            {
                StatusText = Loc.Get("Backup.DeepRestore.Failed", ex.Message);
                return;
            }

            IsDeepRestoring = true;
            IsLoading = true;
            StatusText = Loc.Get("Backup.DeepRestore.Restoring", 0, DeepRestoreFileCount);
            try
            {
                var result = await _snapshotService.ExecuteRegistryRestoreAsync(plan);

                if (result.FailedCount == 0 && result.RestoredCount > 0)
                {
                    StatusText = Loc.Get("Backup.DeepRestore.AllSuccess");
                }
                else if (result.RestoredCount > 0)
                {
                    StatusText = Loc.Get("Backup.DeepRestore.Success", result.RestoredCount, result.FailedCount);
                }
                else
                {
                    StatusText = Loc.Get("Backup.DeepRestore.Failed", 
                        result.FailedFiles.Count > 0 ? result.FailedFiles[0] : "Unknown error");
                }

                ShowDeepRestore = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BackupManagerVM][RestoreRegistryBackup] {ex}");
                StatusText = Loc.Get("Backup.DeepRestore.Failed", ex.Message);
                LogBackupError("RestoreRegistryBackup", ex, $"SnapshotId: {DeepRestoreSnapshotId}");
            }
            finally
            {
                IsDeepRestoring = false;
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task ExportSpecificSnapshotAsync(SnapshotInfo? info)
        {
            if (info == null) return;

            var dialog = new SaveFileDialog
            {
                Filter = Loc.Get("Backup.JsonFilter"),
                FileName = $"{info.Name}.json"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    await _snapshotService.ExportSnapshotAsync(info.Id, dialog.FileName);
                    StatusText = Loc.Get("Backup.ExportSuccessName", info.Name);
                }
                catch (Exception ex)
                {
                    StatusText = Loc.Get("Backup.ExportFailed", ex.Message);
                }
            }
        }

        public void Initialize() => _ = LoadSnapshotsAsync().ContinueWith(t => 
        {
            if (t.IsFaulted && t.Exception != null)
                System.Diagnostics.Debug.WriteLine($"[BackupManagerViewModel] LoadSnapshotsAsync failed: {t.Exception.InnerException?.Message}");
        }, TaskContinuationOptions.OnlyOnFaulted);
        
        // PAGE-018: 供页面复用时刷新数据
        public void RefreshSnapshots() => _ = LoadSnapshotsAsync().ContinueWith(t => 
        {
            if (t.IsFaulted && t.Exception != null)
                System.Diagnostics.Debug.WriteLine($"[BackupManagerViewModel] RefreshSnapshots failed: {t.Exception.InnerException?.Message}");
        }, TaskContinuationOptions.OnlyOnFaulted);
        
        // 错误日志记录
        private static void LogBackupError(string operation, Exception ex, string? additionalInfo = null)
        {
            try
            {
                var logPath = Path.Combine(AppPaths.LogsDir, "backup_errors.log");
                var logEntry = new StringBuilder();
                logEntry.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ========== 备份错误 ==========");
                logEntry.AppendLine($"操作: {operation}");
                logEntry.AppendLine($"错误类型: {ex.GetType().Name}");
                logEntry.AppendLine($"错误消息: {ex.Message}");
                if (!string.IsNullOrEmpty(additionalInfo))
                {
                    logEntry.AppendLine($"附加信息: {additionalInfo}");
                }
                logEntry.AppendLine($"堆栈跟踪:\n{ex.StackTrace}");
                logEntry.AppendLine();
                
                File.AppendAllText(logPath, logEntry.ToString());
                Debug.WriteLine($"[BackupManagerVM] 错误已记录到: {logPath}");
            }
            catch (Exception logEx)
            {
                Debug.WriteLine($"[BackupManagerVM] 记录错误日志失败: {logEx.Message}");
            }
        }

        [RelayCommand]
        private void OpenLogDirectory()
        {
            try
            {
                var logsDir = AppPaths.LogsDir;
                if (!Directory.Exists(logsDir))
                {
                    SafeDirectoryOperations.TryCreateDirectory(logsDir, out _);
                }
                
                var (process, error) = SafeProcessHelper.StartProcess(new ProcessStartInfo("explorer.exe", logsDir)
                {
                    UseShellExecute = true
                });
                
                if (process == null)
                {
                    StatusText = Loc.Get("Backup.CannotOpenLogDir", error ?? "");
                }
            }
            catch (Exception ex)
            {
                StatusText = Loc.Get("Backup.OpenLogDirFailed", ex.Message);
            }
        }

        [RelayCommand]
        private async Task ExportDiagnosticPackageAsync()
        {
            IsLoading = true;
            StatusText = Loc.Get("Backup.CreatingDiagnosticPackage");
            
            try
            {
                var diagnosticService = new DiagnosticPackageService();
                var (success, packagePath, error) = await diagnosticService.CreateDiagnosticPackageAsync();
                
                if (success)
                {
                    StatusText = Loc.Get("Backup.DiagnosticPackageCreated", packagePath);
                    
                    var confirmed = Views.ConfirmDialog.Show(
                        Application.Current.MainWindow,
                        Loc.Get("Backup.DiagPackageCreatedTitle"),
                        Loc.Get("Backup.DiagPackageCreatedMsg", packagePath),
                        Constants.IconGlyphs.Check);
                    
                    if (confirmed)
                    {
                        var dir = Path.GetDirectoryName(packagePath);
                        if (!string.IsNullOrEmpty(dir))
                        {
                            SafeProcessHelper.StartProcess(new ProcessStartInfo("explorer.exe", $"/select,\"{packagePath}\"")
                            {
                                UseShellExecute = true
                            });
                        }
                    }
                }
                else
                {
                    StatusText = Loc.Get("Backup.DiagPackageFailed", error);
                    LogBackupError("ExportDiagnosticPackage", new Exception(error));
                }
            }
            catch (Exception ex)
            {
                StatusText = Loc.Get("Backup.DiagPackageFailed", ex.Message);
                LogBackupError("ExportDiagnosticPackage", ex);
            }
            finally
            {
                IsLoading = false;
            }
        }
    }

    public class SnapshotSettingItem
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
    }
}
