#define DEBUG
#pragma warning disable CS8600, CS8601, CS8602, CS8604, CS8618, CS8619, CS8625, CS4014, CS0618
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameOptimizer.Core.Constants;
using GameOptimizer.Core.Diagnostics;
using GameOptimizer.Core.Events;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Models;
using GameOptimizer.Core.Modules;
using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Optimization.Profiles;
using GameOptimizer.Core.Optimization.Providers;
using GameOptimizer.Core.Services;
using GameOptimizer.Core.Services.Snapshot;
using GameOptimizer.Core.Utilities;
using GameOptimizer.ModuleContracts.Abstractions;
using GameOptimizer.Shell.Models;
using GameOptimizer.Shell.Services;
using GameOptimizer.Shell.Storage;
using GameOptimizer.Shell.Views;
using Microsoft.Extensions.Logging;

namespace GameOptimizer.Shell.ViewModels;

public partial class DashboardViewModel : ObservableObject, IOptimizationExecutor, IDisposable
{
	private sealed record ApplyGuardResult(List<string> AllowedOperationIds, List<OperationExecutionSummary> BlockedSummaries, int PolicyBlockedCount, int HardwareBlockedCount, int ExtremeBlockedCount, int StaticBlockedCount, int UnknownOperationCount, int NonSafeOperationCount, int RequiresRestorePointOperationCount);

	private sealed class HardwareModuleFilter
	{
		private readonly HardwareInfo? _info;

		public static readonly HardwareModuleFilter ShowAll = new HardwareModuleFilter(null);

		public HardwareModuleFilter(HardwareInfo? info)
		{
			_info = info;
		}

		public bool ShouldShowModule(string moduleKey)
		{
			if (_info == null)
			{
				return true;
			}
			return true;
		}
	}

	public sealed record OperationExecutionEvidence(string Status, string Message, string BeforeValue, string AfterValue, string ErrorMessage, bool RequiresReboot, string SourceHint);

	public sealed record SingleOperationRollbackResult(bool Success, string Message);

	public sealed record OverviewEntry(string Key, string Title, string IconData, string StatusText, bool CanOpen = true);

	private static class IconPath
	{
		public const string Cpu = "M7,3 H17 C18.1,3 19,3.9 19,5 V7 H21 V9 H19 V15 H21 V17 H19 V19 C19,20.1 18.1,21 17,21 H7 C5.9,21 5,20.1 5,19 V17 H3 V15 H5 V9 H3 V7 H5 V5 C5,3.9 5.9,3 7,3 Z M9,9 V15 H15 V9 Z";

		public const string Rocket = "M12,2 C15.2,4.8 17,8.3 17,11.5 C17,12.8 16.8,14 16.3,15.1 L18,18 H14.5 L12,22 L9.5,18 H6 L7.7,15.1 C7.2,14 7,12.8 7,11.5 C7,8.3 8.8,4.8 12,2 Z M12,6.2 A2.2,2.2 0 1,1 12,10.6 A2.2,2.2 0 1,1 12,6.2 Z M7,13.5 L4.8,16.8 L8.2,15.2 Z M17,13.5 L15.8,15.2 L19.2,16.8 Z";

		public const string Gpu = "M5,6 H19 C20.1,6 21,6.9 21,8 V14 C21,15.1 20.1,16 19,16 H14 V18 H16 V20 H8 V18 H10 V16 H5 C3.9,16 3,15.1 3,14 V8 C3,6.9 3.9,6 5,6 Z M7,9 V13 H11 V9 Z M15.5,10.5 A1.5,1.5 0 1 1 15.5,13.5 A1.5,1.5 0 1 1 15.5,10.5 Z";

		public const string Memory = "M12,3 L20,7 V12 C20,17 16.6,20.8 12,22 C7.4,20.8 4,17 4,12 V7 Z M12,5.4 L6,8.3 V12 C6,15.7 8.6,18.7 12,19.9 C15.4,18.7 18,15.7 18,12 V8.3 Z M11,9 H13 V13 H16 V15 H11 Z";

		public const string Disk = "M6,6 H18 C19.1,6 20,6.9 20,8 V18 C20,19.1 19.1,20 18,20 H6 C4.9,20 4,19.1 4,18 V8 C4,6.9 4.9,6 6,6 Z M8,9 V17 H16 V9 Z M10,4 H14 V6 H10 Z";

		public const string Network = "M4,17 C6.5,12 17.5,12 20,17 L17.5,18.5 C15.8,15.6 8.2,15.6 6.5,18.5 Z M7,14 C9.2,11 14.8,11 17,14 L14.8,15.4 C13.7,13.9 10.3,13.9 9.2,15.4 Z M11.5,11 C12.3,9.7 13.7,9.7 14.5,11 L12.5,12.5 Z";

		public const string Driver = "M5,5 H11 L13,7 H19 C20.1,7 21,7.9 21,9 V19 C21,20.1 20.1,21 19,21 H5 C3.9,21 3,20.1 3,19 V7 C3,5.9 3.9,5 5,5 Z M7,9 V17 H17 V9 Z M9,11 H11 V13 H9 Z M13,11 H15 V13 H13 Z";

		public const string Repair = "M6,8 L9,11 L7.5,12.5 L12.5,17.5 L11,19 L6,14 L4.5,15.5 L3,14 L5.5,11.5 Z";

		public const string Diagnostics = "M3,13 H7 L9,9 L11,17 L13,11 L15,13 H21";

		public const string Cleanup = "M5,6 H19 V8 H17.5 L16.5,19 C16.4,20.1 15.5,21 14.4,21 H9.6 C8.5,21 7.6,20.1 7.5,19 L6.5,8 H5 Z M8.5,6 V5 C8.5,3.9 9.4,3 10.5,3 H13.5 C14.6,3 15.5,3.9 15.5,5 V6 Z";

		public const string Tuning = "M4,6 H20 V8 H4 Z M4,12 H20 V14 H4 Z M4,18 H20 V20 H4 Z M8,4 V22 H10 V4 Z";

		public const string Peripheral = "M6,7 H18 C19.1,7 20,7.9 20,9 V15 C20,16.1 19.1,17 18,17 H6 C4.9,17 4,16.1 4,15 V9 C4,7.9 4.9,7 6,7 Z M8,10 V14 H12 V10 Z M14,10 H18 V12 H14 Z M9,18 H15 V20 H9 Z";

		public const string Graphics = "M4,5 H20 C21.1,5 22,5.9 22,7 V15 C22,16.1 21.1,17 20,17 H14 V19 H16 V21 H8 V19 H10 V17 H4 C2.9,17 2,16.1 2,15 V7 C2,5.9 2.9,5 4,5 Z M6,8 V12 H10 V8 Z M12,8 H18 V12 H12 Z";

		public const string Explorer = "M4,6 H10 L12,8 H20 C21.1,8 22,8.9 22,10 V18 C22,19.1 21.1,20 20,20 H4 C2.9,20 2,19.1 2,18 V8 C2,6.9 2.9,6 4,6 Z M4,10 V18 H20 V10 Z";

		public const string Settings = "M19.14,12.94 C19.18,12.64 19.2,12.33 19.2,12 C19.2,11.68 19.18,11.36 19.13,11.06 L21.16,9.48 C21.34,9.34 21.39,9.07 21.28,8.87 L19.36,5.55 C19.24,5.33 18.99,5.26 18.77,5.33 L16.38,6.29 C15.88,5.91 15.35,5.59 14.76,5.35 L14.4,2.81 C14.36,2.57 14.16,2.4 13.92,2.4 L10.08,2.4 C9.84,2.4 9.65,2.57 9.61,2.81 L9.25,5.35 C8.66,5.59 8.12,5.92 7.63,6.29 L5.24,5.33 C5.02,5.25 4.77,5.33 4.65,5.55 L2.74,8.87 C2.62,9.08 2.66,9.34 2.86,9.48 L4.89,11.06 C4.84,11.36 4.8,11.69 4.8,12 C4.8,12.31 4.82,12.64 4.87,12.94 L2.84,14.52 C2.66,14.66 2.61,14.93 2.72,15.13 L4.64,18.45 C4.76,18.67 5.01,18.74 5.23,18.67 L7.62,17.71 C8.12,18.09 8.65,18.41 9.24,18.65 L9.6,21.19 C9.65,21.43 9.84,21.6 10.08,21.6 L13.92,21.6 C14.16,21.6 14.36,21.43 14.39,21.19 L14.75,18.65 C15.34,18.41 15.88,18.09 16.37,17.71 L18.76,18.67 C18.98,18.75 19.23,18.67 19.35,18.45 L21.27,15.13 C21.39,14.91 21.34,14.66 21.15,14.52 L19.14,12.94 Z M12,15.6 C10.02,15.6 8.4,13.98 8.4,12 C8.4,10.02 10.02,8.4 12,8.4 C13.98,8.4 15.6,10.02 15.6,12 C15.6,13.98 13.98,15.6 12,15.6 Z";

		public const string GameProfile = "M21,6 H3 C1.9,6 1,6.9 1,8 V16 C1,17.1 1.9,18 3,18 H21 C22.1,18 23,17.1 23,16 V8 C23,6.9 22.1,6 21,6 Z M11,13 H8 V16 H6 V13 H3 V11 H6 V8 H8 V11 H11 V13 Z M15.5,15 C14.67,15 14,14.33 14,13.5 C14,12.67 14.67,12 15.5,12 C16.33,12 17,12.67 17,13.5 C17,14.33 16.33,15 15.5,15 Z M19.5,12 C18.67,12 18,11.33 18,10.5 C18,9.67 18.67,9 19.5,9 C20.33,9 21,9.67 21,10.5 C21,11.33 20.33,12 19.5,12 Z";

		public const string Hardware = "M15,9 H9 V15 H15 V9 Z M13,13 H11 V11 H13 V13 Z M21,11 V13 H19 V15 C19,16.1 18.1,17 17,17 H15 V19 H13 V17 H11 V19 H9 V17 H7 C5.9,17 5,16.1 5,15 V13 H3 V11 H5 V9 H3 V7 H5 V5 C5,3.9 5.9,3 7,3 H9 V1 H11 V3 H13 V1 H15 V3 H17 C18.1,3 19,3.9 19,5 V7 H21 V9 H19 V11 H21 Z M17,5 H7 V15 H17 V5 Z";

		public const string GameModeHub = "M12,2 L22,8.5 V15.5 L12,22 L2,15.5 V8.5 L12,2 Z M12,5 L5,9.5 V14.5 L12,19 L19,14.5 V9.5 L12,5 Z M12,8 L16,10.5 V13.5 L12,16 L8,13.5 V10.5 L12,8 Z";

		public const string Benchmark = "M3,3 H21 V21 H3 Z M5,19 H7 V10 H5 Z M9,19 H11 V6 H9 Z M13,19 H15 V12 H13 Z M17,19 H19 V8 H17 Z";

		public const string NetworkTest = "M12,2 C6.48,2 2,6.48 2,12 C2,17.52 6.48,22 12,22 C17.52,22 22,17.52 22,12 C22,6.48 17.52,2 12,2 Z M12,20 C7.59,20 4,16.41 4,12 C4,7.59 7.59,4 12,4 C16.41,4 20,7.59 20,12 C20,16.41 16.41,20 12,20 Z M12.5,7 H11 V13 L16.25,16.15 L17,14.92 L12.5,12.25 V7 Z";

		public const string HardwareOpt = "M9,3 V1 H15 V3 H9 Z M11,14 H13 V8 H11 V14 Z M19.03,7.39 L20.45,5.97 C20.02,5.46 19.55,4.98 19.04,4.56 L17.62,5.98 C16.07,4.74 14.12,4 12,4 C7.03,4 3,8.03 3,13 C3,17.97 7.02,22 12,22 C16.97,22 21,17.97 21,13 C21,10.88 20.26,8.93 19.03,7.39 Z M12,20 C8.13,20 5,16.87 5,13 C5,9.13 8.13,6 12,6 C15.87,6 19,9.13 19,13 C19,16.87 15.87,20 12,20 Z";

		public const string PowerPlan = "M11,21 H5 C3.9,21 3,20.1 3,19 V5 C3,3.9 3.9,3 5,3 H11 V1 H13 V3 H19 C20.1,3 21,3.9 21,5 V11 H19 V5 H5 V19 H11 V21 Z M7,7 H9 V9 H7 V7 Z M7,11 H9 V13 H7 V11 Z M7,15 H9 V17 H7 V15 Z M22,13 L18.5,20 H15 L18,15 H14 V13 H22 Z";
	}

	private sealed record ModuleVisual(string Key, string IconData)
	{
		public string Title => LocalizationService.Instance.Get("Dashboard.Module." + Key + ".Title");

		public string Subtitle => LocalizationService.Instance.Get("Dashboard.Module." + Key + ".Subtitle");
	}

	private readonly ISystemInsightService _insightService;

	private readonly IHomeTelemetrySource _homeTelemetrySource;

	private readonly IOptimizationOperationProvider _operationProvider;

	private readonly ModuleHost _moduleHost;

	private readonly IOptimizationEngine _optimizationEngine;

	private readonly Func<IModuleWidget, object> _widgetContentFactory;

	private readonly IDiagnosticLogService _diagnosticLogService;

	private readonly IUserPrivilegeService _privilegeService;

	private readonly OptimizationProfileService _profileService;

	private readonly ISystemRestoreService _restoreService;

	private readonly ISoundNotificationService? _soundService;
	private readonly IAppSettingsStore _settingsStore;
	private readonly IActiveOperationRegistry _activeOperations;

	private readonly IEventBus _eventBus;

	private AppSettings _appSettings;

	private readonly ActivityLogStore _activityLogStore = new ActivityLogStore();

	private readonly List<string> _logBuffer = new List<string>();

	private bool _isSynchronizingNavigation;

	private bool _isBulkUpdating;

	private readonly Dictionary<OptimizationToggleModel, OptimizationModuleDefinition> _toggleModuleIndex = new Dictionary<OptimizationToggleModel, OptimizationModuleDefinition>();

	private readonly Dictionary<string, OptimizationToggleModel> _operationLookup = new Dictionary<string, OptimizationToggleModel>(StringComparer.OrdinalIgnoreCase);

	private readonly TelemetryTrendBuffer _cpuTrendBuffer = new TelemetryTrendBuffer(TrendSampleCapacity);

	private readonly TelemetryTrendBuffer _gpuTrendBuffer = new TelemetryTrendBuffer(TrendSampleCapacity);
	private TelemetryFieldStatus _cpuTelemetryStatus = TelemetryFieldStatus.Unavailable("首页遥测", "尚未采样");
	private TelemetryFieldStatus _gpuTelemetryStatus = TelemetryFieldStatus.Unavailable("首页遥测", "尚未采样");
	private GpuLoadAttribution _gpuLoadAttribution = GpuLoadAttribution.Unavailable("首页遥测", "尚未采样");
	private TelemetryFieldStatus _memoryTelemetryStatus = TelemetryFieldStatus.Unavailable("首页遥测", "尚未采样");
	private TelemetryFieldStatus _diskTelemetryStatus = TelemetryFieldStatus.Unavailable("首页遥测", "尚未采样");

	private readonly CommandExecutionGate _moduleOperationGate = new CommandExecutionGate();

	private CancellationTokenSource? _moduleOperationCts;
	private IActiveOperationLease? _moduleActiveLease;

	private readonly Dispatcher _dispatcher;

	private readonly SemaphoreSlim _syncStatusLock = new SemaphoreSlim(1, 1);

	private DispatcherTimer? _telemetryTimer;

	private bool _isTelemetryCapturing;

	private CancellationTokenSource? _eventSubscriptionCts;

	private INotifyPropertyChanged? _privilegeNotifier;

	private DateTime _lastForcedApplyBackupUtc = DateTime.MinValue;

	private const int TrendSampleCapacity = 48;


	private const double TrendCanvasHeight = 140.0;

	private static readonly TimeSpan ForcedApplyBackupReuseWindow = TimeSpan.FromMinutes(15.0);

	private static readonly IReadOnlyList<ModuleVisual> ModuleMetadata = new ModuleVisual[13]
	{
		new ModuleVisual("performance", "M12,2 C15.2,4.8 17,8.3 17,11.5 C17,12.8 16.8,14 16.3,15.1 L18,18 H14.5 L12,22 L9.5,18 H6 L7.7,15.1 C7.2,14 7,12.8 7,11.5 C7,8.3 8.8,4.8 12,2 Z M12,6.2 A2.2,2.2 0 1,1 12,10.6 A2.2,2.2 0 1,1 12,6.2 Z M7,13.5 L4.8,16.8 L8.2,15.2 Z M17,13.5 L15.8,15.2 L19.2,16.8 Z"),
		new ModuleVisual("cpu", "M7,3 H17 C18.1,3 19,3.9 19,5 V7 H21 V9 H19 V15 H21 V17 H19 V19 C19,20.1 18.1,21 17,21 H7 C5.9,21 5,20.1 5,19 V17 H3 V15 H5 V9 H3 V7 H5 V5 C5,3.9 5.9,3 7,3 Z M9,9 V15 H15 V9 Z"),
		new ModuleVisual("graphics", "M4,5 H20 C21.1,5 22,5.9 22,7 V15 C22,16.1 21.1,17 20,17 H14 V19 H16 V21 H8 V19 H10 V17 H4 C2.9,17 2,16.1 2,15 V7 C2,5.9 2.9,5 4,5 Z M6,8 V12 H10 V8 Z M12,8 H18 V12 H12 Z"),
		new ModuleVisual("network", "M4,17 C6.5,12 17.5,12 20,17 L17.5,18.5 C15.8,15.6 8.2,15.6 6.5,18.5 Z M7,14 C9.2,11 14.8,11 17,14 L14.8,15.4 C13.7,13.9 10.3,13.9 9.2,15.4 Z M11.5,11 C12.3,9.7 13.7,9.7 14.5,11 L12.5,12.5 Z"),
		new ModuleVisual("privacy", "M12,3 L20,7 V12 C20,17 16.6,20.8 12,22 C7.4,20.8 4,17 4,12 V7 Z M12,5.4 L6,8.3 V12 C6,15.7 8.6,18.7 12,19.9 C15.4,18.7 18,15.7 18,12 V8.3 Z M11,9 H13 V13 H16 V15 H11 Z"),
		new ModuleVisual("system", "M4,6 H20 V8 H4 Z M4,12 H20 V14 H4 Z M4,18 H20 V20 H4 Z M8,4 V22 H10 V4 Z"),
		new ModuleVisual("explorer", "M4,6 H10 L12,8 H20 C21.1,8 22,8.9 22,10 V18 C22,19.1 21.1,20 20,20 H4 C2.9,20 2,19.1 2,18 V8 C2,6.9 2.9,6 4,6 Z M4,10 V18 H20 V10 Z"),
		new ModuleVisual("cleanup", "M5,6 H19 V8 H17.5 L16.5,19 C16.4,20.1 15.5,21 14.4,21 H9.6 C8.5,21 7.6,20.1 7.5,19 L6.5,8 H5 Z M8.5,6 V5 C8.5,3.9 9.4,3 10.5,3 H13.5 C14.6,3 15.5,3.9 15.5,5 V6 Z"),
		new ModuleVisual("peripheral", "M6,7 H18 C19.1,7 20,7.9 20,9 V15 C20,16.1 19.1,17 18,17 H6 C4.9,17 4,16.1 4,15 V9 C4,7.9 4.9,7 6,7 Z M8,10 V14 H12 V10 Z M14,10 H18 V12 H14 Z M9,18 H15 V20 H9 Z"),
		new ModuleVisual("tasks", "M6,6 H18 C19.1,6 20,6.9 20,8 V18 C20,19.1 19.1,20 18,20 H6 C4.9,20 4,19.1 4,18 V8 C4,6.9 4.9,6 6,6 Z M8,9 V17 H16 V9 Z M10,4 H14 V6 H10 Z"),
		new ModuleVisual("services", "M5,5 H11 L13,7 H19 C20.1,7 21,7.9 21,9 V19 C21,20.1 20.1,21 19,21 H5 C3.9,21 3,20.1 3,19 V7 C3,5.9 3.9,5 5,5 Z M7,9 V17 H17 V9 Z M9,11 H11 V13 H9 Z M13,11 H15 V13 H13 Z"),
		new ModuleVisual("memory", "M12,3 L20,7 V12 C20,17 16.6,20.8 12,22 C7.4,20.8 4,17 4,12 V7 Z M12,5.4 L6,8.3 V12 C6,15.7 8.6,18.7 12,19.9 C15.4,18.7 18,15.7 18,12 V8.3 Z M11,9 H13 V13 H16 V15 H11 Z"),
		new ModuleVisual("audio", "M6,7 H18 C19.1,7 20,7.9 20,9 V15 C20,16.1 19.1,17 18,17 H6 C4.9,17 4,16.1 4,15 V9 C4,7.9 4.9,7 6,7 Z M8,10 V14 H12 V10 Z M14,10 H18 V12 H14 Z M9,18 H15 V20 H9 Z")
	};

	private DetectedGame? activeGame;

	private SystemResourceSnapshot snapshot = new SystemResourceSnapshot(0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0);

	private int optimizationScore = 90;

	private bool isApplyingModule;

	private string applyProgressText = "";

	private string optimizationStateHint = "";

	private NavigationSectionModel? selectedNavigationSection;

	private OptimizationModuleDefinition? selectedOptimizationModule;

	private bool isAdministrator;

	private QuickActionTab selectedQuickAction = QuickActionTab.HealthCheck;

	private bool isQuickActionBusy;

	private string quickActionStatusHint = "";

	private ModuleStrategyFilter moduleStrategyFilter = ModuleStrategyFilter.All;

	private string moduleSearchQuery = string.Empty;

	private bool isResultDialogVisible;

	private string resultDialogTitle = "";

	private string? resultDialogSubtitle;

	private string resultDialogSummary = string.Empty;

	private bool resultDialogHasFailure;

	private static readonly Dictionary<string, string> ModuleKeyToNavKeyMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		["memory"] = "memory-optimization",
		["performance"] = "performance-boost",
		["cpu"] = "cpu-nav",
		["graphics"] = "graphics-nav",
		["network"] = "network-nav",
		["audio"] = "audio-optimization",
		["privacy"] = "privacy-nav",
		["tuning"] = "system-nav",
		["system"] = "system-nav",
		["explorer"] = "explorer-nav",
		["scheduler"] = "tasks-nav",
		["tasks"] = "tasks-nav",
		["services"] = "services-nav",
		["cleanup"] = "system-cleanup",
		["peripheral"] = "peripheral-nav",
		["hardware"] = "hardware-info",
		["nettest"] = "network-test",
		["restore"] = "restore-point",
		["repair"] = "repair-center",
		["backup"] = "backup-manager"
	};

	private static readonly string[] OverviewModuleOrder =
	{
		"memory", "performance", "cpu", "graphics", "network", "audio", "privacy",
		"system", "explorer", "tasks", "services", "cleanup", "peripheral"
	};

	private bool _disposed;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? clearModuleSearchCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? clearActivityHistoryCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? exportActivityHistoryCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? hideResultDialogCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? openSettingsCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? navigateGameModeCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? navigateRestorePointCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<string?>? navigateToModuleCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? navigateToRepairCenterCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? navigateToSystemCleanupCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand<string?>? executeQuickActionCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? openDiagnosticsFolderCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? applySelectedModuleCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? restoreSelectedModuleCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? restoreModuleBaselineFromProfileCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? restoreModuleWindowsDefaultsCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? saveModuleSnapshotCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? selectAllTogglesCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? deselectAllTogglesCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? restoreWindowsDefaultCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? restoreUserBackupCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	public int TotalOptimizationCount => OptimizationModules.Where((OptimizationModuleDefinition m) => m.SupportsExecution).Sum((OptimizationModuleDefinition m) => m.Toggles.Count);

	public int AppliedOptimizationCount => OptimizationModules.Where((OptimizationModuleDefinition m) => m.SupportsExecution).Sum((OptimizationModuleDefinition m) => m.Toggles.Count((OptimizationToggleModel t) => t.IsApplied));

	public int PendingOptimizationCount => Math.Max(0, TotalOptimizationCount - AppliedOptimizationCount);

	public string AppliedCountDisplay => (TotalOptimizationCount == 0) ? LocalizationService.Instance.Get("Overview.Scanning") : $"{AppliedOptimizationCount} / {TotalOptimizationCount}";

	public int SelectedModuleAppliedCount => SelectedOptimizationModule?.Toggles.Count((OptimizationToggleModel t) => t.IsApplied) ?? 0;

	public ObservableCollection<WidgetHostModel> Widgets { get; } = new ObservableCollection<WidgetHostModel>();

	public ObservableCollection<ActivityRecord> ActivityLogs { get; } = new ObservableCollection<ActivityRecord>();

	public ObservableCollection<OverviewEntry> OverviewEntries { get; } = new ObservableCollection<OverviewEntry>();

	public ObservableCollection<OptimizationModuleDefinition> OptimizationModules { get; } = new ObservableCollection<OptimizationModuleDefinition>();

	public ObservableCollection<NavigationSectionModel> NavigationSections { get; } = new ObservableCollection<NavigationSectionModel>();

	public ObservableCollection<DiagnosticLogEntry> DiagnosticEvents { get; } = new ObservableCollection<DiagnosticLogEntry>();

	public ObservableCollection<OperationResultItem> LatestOperationResults { get; } = new ObservableCollection<OperationResultItem>();

	public string PrivilegeHint => IsAdministrator ? LocalizationService.Instance.Get("Dashboard.Privilege.Admin") : LocalizationService.Instance.Get("Dashboard.Privilege.Normal");

	public bool HasThermalAlert => Snapshot.TemperatureCpu >= 80.0 || Snapshot.TemperatureGpu >= 82.0;

	public bool HasNetworkAlert => Snapshot.NetworkLatencyMs >= 80.0;

	public bool HasRealCpuSample => _cpuTelemetryStatus.IsLive;
	public bool HasRealGpuSample => _gpuTelemetryStatus.IsLive;
	public bool HasRealMemorySample => _memoryTelemetryStatus.IsLive && Snapshot.MemoryTotalGb > 0.0;
	public bool HasRealDiskSample => _diskTelemetryStatus.IsLive && Snapshot.DiskTotalGb > 0.0;
	private static readonly Brush CpuNormalUsageBrush = CreateUsageBrush("#60A5FA");
	private static readonly Brush GpuNormalUsageBrush = CreateUsageBrush("#A78BFA");
	private static readonly Brush MemoryNormalUsageBrush = CreateUsageBrush("#4DBA90");
	private static readonly Brush DiskNormalUsageBrush = CreateUsageBrush("#C4A668");
	private static readonly Brush UsageWarningBrush = CreateUsageBrush("#FBBF24");
	private static readonly Brush UsageCriticalBrush = CreateUsageBrush("#F87171");

	public Brush CpuUsageBrush => GetUsageBrush(HasRealCpuSample, CpuUsagePercent, CpuNormalUsageBrush);
	public Brush GpuUsageBrush => GetUsageBrush(HasRealGpuSample, GpuUsagePercent, GpuNormalUsageBrush);
	public Brush MemoryUsageBrush => GetUsageBrush(HasRealMemorySample, MemoryUsagePercent, MemoryNormalUsageBrush);
	public Brush DiskUsageBrush => GetUsageBrush(HasRealDiskSample, DiskUsagePercent, DiskNormalUsageBrush);
	public string CpuUsageDisplay => HasRealCpuSample ? $"{Snapshot.CpuUsage:F0}%" : LocalizationService.Instance.Get("Overview.DataUnavailable");
	public string GpuUsageDisplay => HasRealGpuSample ? $"{Snapshot.GpuUsage:F0}%" : LocalizationService.Instance.Get("Overview.DataUnavailable");
	// 仅在真实采样可用时由视图显示；不可用值不得作为 0% 呈现。
	public double CpuUsagePercent => Math.Clamp(Snapshot.CpuUsage, 0.0, 100.0);
	public double GpuUsagePercent => Math.Clamp(Snapshot.GpuUsage, 0.0, 100.0);
	public bool HasCpuLiveTrend => _cpuTrendBuffer.HasHistory;
	public bool HasGpuLiveTrend => _gpuTrendBuffer.HasHistory;
	public string CpuLiveTrendPoints => BuildLiveTrendShape(_cpuTrendBuffer.Values);
	public string GpuLiveTrendPoints => BuildLiveTrendShape(_gpuTrendBuffer.Values);
	public string OperatingSystemDisplay
	{
		get
		{
			var version = Environment.OSVersion.Version;
			if (version.Major < 10)
				return "Windows";

			var family = version.Build >= 22000 ? "Windows 11" : "Windows 10";
			try
			{
				var displayVersion = Registry.GetValue(
					@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion",
					"DisplayVersion",
					null) as string;
				return string.IsNullOrWhiteSpace(displayVersion) ? family : $"{family} {displayVersion}";
			}
			catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
			{
				return family;
			}
		}
	}
	public string LogicalProcessorDisplay => LocalizationService.Instance.CurrentLanguage == "en-US"
		? $"{Environment.ProcessorCount} logical processors"
		: $"{Environment.ProcessorCount} 线程";
	public string CpuTemperatureDisplay => Snapshot.TemperatureCpu > 0.0 ? $"{Snapshot.TemperatureCpu:F1} °C" : LocalizationService.Instance.Get("Overview.DataUnavailable");

	public string GpuTemperatureDisplay => Snapshot.TemperatureGpu > 0.0 ? $"{Snapshot.TemperatureGpu:F1} °C" : LocalizationService.Instance.Get("Overview.DataUnavailable");

	public string CpuStatusLabel => !HasRealCpuSample ? LocalizationService.Instance.Get("Overview.DataUnavailable") : (Snapshot.CpuUsage >= 75.0 || HasThermalAlert) ? LocalizationService.Instance.Get("Dashboard.Status.HighLoad") : LocalizationService.Instance.Get("Dashboard.Status.Stable");

	public string GpuStatusLabel => !HasRealGpuSample ? LocalizationService.Instance.Get("Overview.DataUnavailable") : (Snapshot.GpuUsage >= 75.0 || HasThermalAlert) ? LocalizationService.Instance.Get("Dashboard.Status.HighLoad") : LocalizationService.Instance.Get("Dashboard.Status.Stable");

	public string GpuLoadAdapterDisplay
	{
		get
		{
			if (!HasRealGpuSample)
				return LocalizationService.Instance.Get("Overview.GpuLoadUnavailable");

			if (!_gpuLoadAttribution.IsMapped || string.IsNullOrWhiteSpace(_gpuLoadAttribution.AdapterName))
				return LocalizationService.Instance.Get("Overview.GpuLoadUnmapped");

			return LocalizationService.Instance.Get(
				"Overview.GpuLoadMapped",
				_gpuLoadAttribution.AdapterName,
				GpuKindDisplay(_gpuLoadAttribution.AdapterKind));
		}
	}

	private static string GpuKindDisplay(GpuAdapterKind? kind) => kind switch
	{
		GpuAdapterKind.Integrated => LocalizationService.Instance.Get("Overview.GpuKind.Integrated"),
		GpuAdapterKind.Discrete => LocalizationService.Instance.Get("Overview.GpuKind.Discrete"),
		GpuAdapterKind.SoftwareOrVirtual => LocalizationService.Instance.Get("Overview.GpuKind.SoftwareOrVirtual"),
		_ => LocalizationService.Instance.Get("Overview.GpuKind.Unknown")
	};

	public string MemorySummary => HasRealMemorySample ? $"{Snapshot.MemoryUsageGb:F1} / {Snapshot.MemoryTotalGb:F1} GB" : LocalizationService.Instance.Get("Overview.DataUnavailable");
	public string MemoryUsagePercentDisplay => HasRealMemorySample ? string.Format(LocalizationService.Instance.Get("Overview.UsedPercentFormat"), MemoryUsagePercent) : LocalizationService.Instance.Get("Overview.DataUnavailable");
	public string MemoryAvailableDisplay => HasRealMemorySample ? string.Format(LocalizationService.Instance.Get("Overview.AvailableSpaceFormat"), Math.Max(0.0, Snapshot.MemoryTotalGb - Snapshot.MemoryUsageGb)) : LocalizationService.Instance.Get("Overview.DataUnavailable");

	public string DiskSummary => HasRealDiskSample ? $"{Snapshot.DiskUsedGb:F1} / {Snapshot.DiskTotalGb:F1} GB" : LocalizationService.Instance.Get("Overview.DataUnavailable");
	public string DiskUsagePercentDisplay => HasRealDiskSample ? string.Format(LocalizationService.Instance.Get("Overview.UsedPercentFormat"), DiskUsagePercent) : LocalizationService.Instance.Get("Overview.DataUnavailable");
	public string DiskAvailableDisplay => HasRealDiskSample ? string.Format(LocalizationService.Instance.Get("Overview.AvailableSpaceFormat"), Math.Max(0.0, Snapshot.DiskTotalGb - Snapshot.DiskUsedGb)) : LocalizationService.Instance.Get("Overview.DataUnavailable");

	public double DiskUsagePercent => (Snapshot.DiskTotalGb <= 0.0) ? 0.0 : Math.Clamp(Snapshot.DiskUsedGb / Snapshot.DiskTotalGb * 100.0, 0.0, 100.0);

	public double MemoryUsagePercent => (Snapshot.MemoryTotalGb <= 0.0) ? 0.0 : Math.Clamp(Snapshot.MemoryUsageGb / Snapshot.MemoryTotalGb * 100.0, 0.0, 100.0);

	public string NetworkSummary => $"{Snapshot.NetworkLatencyMs:F1} ms";

	public string DpcLatencySummary => $"{Snapshot.DpcLatencyUs:F0} 渭s";

	public bool HasDpcAlert => Snapshot.DpcLatencyUs >= 500.0;

	public string DpcStatusLabel => (Snapshot.DpcLatencyUs >= 500.0) ? LocalizationService.Instance.Get("Dashboard.Dpc.High") : ((Snapshot.DpcLatencyUs >= 200.0) ? LocalizationService.Instance.Get("Dashboard.Dpc.Average") : LocalizationService.Instance.Get("Dashboard.Dpc.Excellent"));

	public int MaxToggleCount => (OptimizationModules.Count == 0) ? 1 : OptimizationModules.Max((OptimizationModuleDefinition m) => m.Toggles.Count);

	public string QuickActionTitle
	{
		get
		{
			QuickActionTab quickActionTab = SelectedQuickAction;
			string result = quickActionTab switch
			{
				QuickActionTab.HealthCheck => LocalizationService.Instance.Get("Dashboard.QuickAction.HealthCheck"), 
				QuickActionTab.ExportReport => LocalizationService.Instance.Get("Dashboard.QuickAction.ExportReport"), 
				_ => LocalizationService.Instance.Get("Dashboard.QuickAction.SolutionCenter"), 
			};
			return result;
		}
	}

	public string QuickActionDescription
	{
		get
		{
			QuickActionTab quickActionTab = SelectedQuickAction;
			string result = quickActionTab switch
			{
				QuickActionTab.HealthCheck => LocalizationService.Instance.Get("Dashboard.QuickAction.HealthCheckDesc"), 
				QuickActionTab.ExportReport => LocalizationService.Instance.Get("Dashboard.QuickAction.ExportReportDesc"), 
				_ => LocalizationService.Instance.Get("Dashboard.QuickAction.SolutionCenterDesc"), 
			};
			return result;
		}
	}

	public string QuickActionMeta
	{
		get
		{
			QuickActionTab quickActionTab = SelectedQuickAction;
			string result = quickActionTab switch
			{
				QuickActionTab.HealthCheck => LocalizationService.Instance.Get("Dashboard.QuickAction.HealthCheckMeta"), 
				QuickActionTab.ExportReport => ActivityLogs.FirstOrDefault()?.Display ?? LocalizationService.Instance.Get("Common.NoData"), 
				_ => LocalizationService.Instance.Get("Dashboard.QuickAction.SolutionCenterMeta"), 
			};
			return result;
		}
	}

	public string QuickActionButtonLabel
	{
		get
		{
			QuickActionTab quickActionTab = SelectedQuickAction;
			string result = quickActionTab switch
			{
				QuickActionTab.HealthCheck => LocalizationService.Instance.Get("Dashboard.QuickAction.HealthCheckBtn"), 
				QuickActionTab.ExportReport => LocalizationService.Instance.Get("Dashboard.QuickAction.ExportReportBtn"), 
				_ => LocalizationService.Instance.Get("Dashboard.QuickAction.SolutionCenterBtn"), 
			};
			return result;
		}
	}

	public int CriticalDiagnosticCount => DiagnosticEvents.Count((DiagnosticLogEntry entry) => entry.Level >= LogLevel.Warning);

	public bool HasCriticalDiagnostics => CriticalDiagnosticCount > 0;

	public string DiagnosticSummary => HasCriticalDiagnostics ? LocalizationService.Instance.Get("Dashboard.DiagnosticWarning", CriticalDiagnosticCount) : LocalizationService.Instance.Get("Dashboard.DiagnosticNormal");

	public int PendingToggleCount => SelectedOptimizationModule?.Toggles.Count((OptimizationToggleModel t) => t.HasPending) ?? 0;

	public bool HasPendingToggle => PendingToggleCount > 0;

	public bool HasModuleSelected => SelectedOptimizationModule != null;

	public bool HasAppliedToggles => SelectedOptimizationModule?.Toggles.Any((OptimizationToggleModel t) => t.IsApplied) ?? false;

	public bool HasModuleSearch => !string.IsNullOrWhiteSpace(ModuleSearchQuery);

	public IEnumerable<OptimizationToggleModel> FilteredStrategies
	{
		get
		{
			IEnumerable<OptimizationToggleModel> enumerable = SelectedOptimizationModule?.Toggles;
			IEnumerable<OptimizationToggleModel> enumerable2 = enumerable ?? Array.Empty<OptimizationToggleModel>();
			if (string.IsNullOrWhiteSpace(ModuleSearchQuery))
			{
				return enumerable2;
			}
			string query = ModuleSearchQuery.Trim();
			return enumerable2.Where((OptimizationToggleModel toggle) => toggle.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrWhiteSpace(toggle.Description) && toggle.Description.Contains(query, StringComparison.OrdinalIgnoreCase)));
		}
	}

	public string ModulePendingStatus
	{
		get
		{
			OptimizationModuleDefinition optimizationModuleDefinition = SelectedOptimizationModule;
			string result = ((optimizationModuleDefinition != null) ? ((PendingToggleCount != 0) ? LocalizationService.Instance.Get("Dashboard.Module.PendingWrite", PendingToggleCount) : LocalizationService.Instance.Get("Dashboard.Module.InSync")) : LocalizationService.Instance.Get("Dashboard.Module.NotSelected"));
			return result;
		}
	}

	public string AppliedToggleStatus
	{
		get
		{
			OptimizationModuleDefinition optimizationModuleDefinition = SelectedOptimizationModule;
			string result = ((optimizationModuleDefinition != null) ? LocalizationService.Instance.Get("Dashboard.Module.Applied", SelectedOptimizationModule.Toggles.Count((OptimizationToggleModel t) => t.IsApplied)) : LocalizationService.Instance.Get("Dashboard.Module.NotSelected"));
			return result;
		}
	}

	public IOptimizationEngine OptimizationEngine => _optimizationEngine;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public DetectedGame? ActiveGame
	{
		get
		{
			return activeGame;
		}
		set
		{
			if (!EqualityComparer<DetectedGame>.Default.Equals(activeGame, value))
			{
				OnPropertyChanging(nameof(ActiveGame));
				activeGame = value;
				OnPropertyChanged(nameof(ActiveGame));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public SystemResourceSnapshot Snapshot
	{
		get
		{
			return snapshot;
		}
		[MemberNotNull("snapshot")]
		set
		{
			if (!EqualityComparer<SystemResourceSnapshot>.Default.Equals(snapshot, value))
			{
				OnPropertyChanging(nameof(Snapshot));
				snapshot = value;
				OnSnapshotChanged(value);
				OnPropertyChanged(nameof(Snapshot));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public int OptimizationScore
	{
		get
		{
			return optimizationScore;
		}
		set
		{
			if (!EqualityComparer<int>.Default.Equals(optimizationScore, value))
			{
				OnPropertyChanging(nameof(OptimizationScore));
				optimizationScore = value;
				OnPropertyChanged(nameof(OptimizationScore));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsApplyingModule
	{
		get
		{
			return isApplyingModule;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(isApplyingModule, value))
			{
				OnPropertyChanging(nameof(IsApplyingModule));
				isApplyingModule = value;
				OnPropertyChanged(nameof(IsApplyingModule));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ApplyProgressText
	{
		get
		{
			return applyProgressText;
		}
		[MemberNotNull("applyProgressText")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(applyProgressText, value))
			{
				OnPropertyChanging(nameof(ApplyProgressText));
				applyProgressText = value;
				OnPropertyChanged(nameof(ApplyProgressText));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string OptimizationStateHint
	{
		get
		{
			return optimizationStateHint;
		}
		[MemberNotNull("optimizationStateHint")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(optimizationStateHint, value))
			{
				OnPropertyChanging(nameof(OptimizationStateHint));
				optimizationStateHint = value;
				OnPropertyChanged(nameof(OptimizationStateHint));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public NavigationSectionModel? SelectedNavigationSection
	{
		get
		{
			return selectedNavigationSection;
		}
		set
		{
			if (!EqualityComparer<NavigationSectionModel>.Default.Equals(selectedNavigationSection, value))
			{
				OnPropertyChanging(nameof(SelectedNavigationSection));
				selectedNavigationSection = value;
				OnSelectedNavigationSectionChanged(value);
				OnPropertyChanged(nameof(SelectedNavigationSection));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public OptimizationModuleDefinition? SelectedOptimizationModule
	{
		get
		{
			return selectedOptimizationModule;
		}
		set
		{
			if (!EqualityComparer<OptimizationModuleDefinition>.Default.Equals(selectedOptimizationModule, value))
			{
				OnPropertyChanging(nameof(SelectedOptimizationModule));
				selectedOptimizationModule = value;
				OnSelectedOptimizationModuleChanged(value);
				OnPropertyChanged(nameof(SelectedOptimizationModule));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsAdministrator
	{
		get
		{
			return isAdministrator;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(isAdministrator, value))
			{
				OnPropertyChanging(nameof(IsAdministrator));
				isAdministrator = value;
				OnIsAdministratorChanged(value);
				OnPropertyChanged(nameof(IsAdministrator));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public QuickActionTab SelectedQuickAction
	{
		get
		{
			return selectedQuickAction;
		}
		set
		{
			if (!EqualityComparer<QuickActionTab>.Default.Equals(selectedQuickAction, value))
			{
				OnPropertyChanging(nameof(SelectedQuickAction));
				selectedQuickAction = value;
				OnSelectedQuickActionChanged(value);
				OnPropertyChanged(nameof(SelectedQuickAction));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsQuickActionBusy
	{
		get
		{
			return isQuickActionBusy;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(isQuickActionBusy, value))
			{
				OnPropertyChanging(nameof(IsQuickActionBusy));
				isQuickActionBusy = value;
				OnPropertyChanged(nameof(IsQuickActionBusy));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string QuickActionStatusHint
	{
		get
		{
			return quickActionStatusHint;
		}
		[MemberNotNull("quickActionStatusHint")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(quickActionStatusHint, value))
			{
				OnPropertyChanging(nameof(QuickActionStatusHint));
				quickActionStatusHint = value;
				OnPropertyChanged(nameof(QuickActionStatusHint));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public ModuleStrategyFilter ModuleStrategyFilter
	{
		get
		{
			return moduleStrategyFilter;
		}
		set
		{
			if (!EqualityComparer<ModuleStrategyFilter>.Default.Equals(moduleStrategyFilter, value))
			{
				OnPropertyChanging(nameof(ModuleStrategyFilter));
				moduleStrategyFilter = value;
				OnModuleStrategyFilterChanged(value);
				OnPropertyChanged(nameof(ModuleStrategyFilter));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ModuleSearchQuery
	{
		get
		{
			return moduleSearchQuery;
		}
		[MemberNotNull("moduleSearchQuery")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(moduleSearchQuery, value))
			{
				OnPropertyChanging(nameof(ModuleSearchQuery));
				moduleSearchQuery = value;
				OnModuleSearchQueryChanged(value);
				OnPropertyChanged(nameof(ModuleSearchQuery));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsResultDialogVisible
	{
		get
		{
			return isResultDialogVisible;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(isResultDialogVisible, value))
			{
				OnPropertyChanging(nameof(IsResultDialogVisible));
				isResultDialogVisible = value;
				OnPropertyChanged(nameof(IsResultDialogVisible));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ResultDialogTitle
	{
		get
		{
			return resultDialogTitle;
		}
		[MemberNotNull("resultDialogTitle")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(resultDialogTitle, value))
			{
				OnPropertyChanging(nameof(ResultDialogTitle));
				resultDialogTitle = value;
				OnPropertyChanged(nameof(ResultDialogTitle));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string? ResultDialogSubtitle
	{
		get
		{
			return resultDialogSubtitle;
		}
		set
		{
			if (!EqualityComparer<string>.Default.Equals(resultDialogSubtitle, value))
			{
				OnPropertyChanging(nameof(ResultDialogSubtitle));
				resultDialogSubtitle = value;
				OnPropertyChanged(nameof(ResultDialogSubtitle));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ResultDialogSummary
	{
		get
		{
			return resultDialogSummary;
		}
		[MemberNotNull("resultDialogSummary")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(resultDialogSummary, value))
			{
				OnPropertyChanging(nameof(ResultDialogSummary));
				resultDialogSummary = value;
				OnPropertyChanged(nameof(ResultDialogSummary));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ResultDialogHasFailure
	{
		get
		{
			return resultDialogHasFailure;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(resultDialogHasFailure, value))
			{
				OnPropertyChanging(nameof(ResultDialogHasFailure));
				resultDialogHasFailure = value;
				OnPropertyChanged(nameof(ResultDialogHasFailure));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ClearModuleSearchCommand => clearModuleSearchCommand ?? (clearModuleSearchCommand = new RelayCommand(ClearModuleSearch));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ClearActivityHistoryCommand => clearActivityHistoryCommand ?? (clearActivityHistoryCommand = new RelayCommand(ClearActivityHistory));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ExportActivityHistoryCommand => exportActivityHistoryCommand ?? (exportActivityHistoryCommand = new AsyncRelayCommand(ExportActivityHistoryAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand HideResultDialogCommand => hideResultDialogCommand ?? (hideResultDialogCommand = new RelayCommand(HideResultDialog));

	private void HideResultDialog()
	{
		IsResultDialogVisible = false;
		LatestOperationResults.Clear();
		ResultDialogSubtitle = null;
		ResultDialogSummary = string.Empty;
		ResultDialogHasFailure = false;
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand OpenSettingsCommand => openSettingsCommand ?? (openSettingsCommand = new RelayCommand(OpenSettings));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand NavigateGameModeCommand => navigateGameModeCommand ?? (navigateGameModeCommand = new RelayCommand(NavigateGameMode));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand NavigateRestorePointCommand => navigateRestorePointCommand ?? (navigateRestorePointCommand = new RelayCommand(NavigateRestorePoint));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<string?> NavigateToModuleCommand => navigateToModuleCommand ?? (navigateToModuleCommand = new RelayCommand<string>(NavigateToModule));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand NavigateToRepairCenterCommand => navigateToRepairCenterCommand ?? (navigateToRepairCenterCommand = new RelayCommand(NavigateToRepairCenter));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand NavigateToSystemCleanupCommand => navigateToSystemCleanupCommand ?? (navigateToSystemCleanupCommand = new RelayCommand(NavigateToSystemCleanup));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand<string?> ExecuteQuickActionCommand => executeQuickActionCommand ?? (executeQuickActionCommand = new AsyncRelayCommand<string>(ExecuteQuickActionAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand OpenDiagnosticsFolderCommand => openDiagnosticsFolderCommand ?? (openDiagnosticsFolderCommand = new RelayCommand(OpenDiagnosticsFolder));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ApplySelectedModuleCommand => applySelectedModuleCommand ?? (applySelectedModuleCommand = new AsyncRelayCommand(ApplySelectedModuleAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand RestoreSelectedModuleCommand => restoreSelectedModuleCommand ?? (restoreSelectedModuleCommand = new AsyncRelayCommand(RestoreSelectedModuleAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand RestoreModuleBaselineFromProfileCommand => restoreModuleBaselineFromProfileCommand ?? (restoreModuleBaselineFromProfileCommand = new AsyncRelayCommand(RestoreModuleBaselineFromProfileAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand RestoreModuleWindowsDefaultsCommand => restoreModuleWindowsDefaultsCommand ?? (restoreModuleWindowsDefaultsCommand = new AsyncRelayCommand(RestoreModuleWindowsDefaultsAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand SaveModuleSnapshotCommand => saveModuleSnapshotCommand ?? (saveModuleSnapshotCommand = new RelayCommand(SaveModuleSnapshot));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand SelectAllTogglesCommand => selectAllTogglesCommand ?? (selectAllTogglesCommand = new RelayCommand(SelectAllToggles));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand DeselectAllTogglesCommand => deselectAllTogglesCommand ?? (deselectAllTogglesCommand = new RelayCommand(DeselectAllToggles));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand RestoreWindowsDefaultCommand => restoreWindowsDefaultCommand ?? (restoreWindowsDefaultCommand = new AsyncRelayCommand(RestoreWindowsDefaultAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand RestoreUserBackupCommand => restoreUserBackupCommand ?? (restoreUserBackupCommand = new AsyncRelayCommand(RestoreUserBackupAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public event Action<string>? NavigateToPageRequested;

	public DashboardViewModel(
		ISystemInsightService insightService,
		IHomeTelemetrySource homeTelemetrySource,
		ModuleHost moduleHost,
		IOptimizationEngine optimizationEngine,
		IOptimizationOperationProvider operationProvider,
		Func<IModuleWidget, object> widgetContentFactory,
		IDiagnosticLogService diagnosticLogService,
		OptimizationProfileService profileService,
		IUserPrivilegeService privilegeService,
		ISystemRestoreService restoreService,
		IEventBus eventBus,
		IAppSettingsStore settingsStore,
		IActiveOperationRegistry activeOperations,
		ISoundNotificationService? soundService = null)
	{
		_insightService = insightService ?? throw new ArgumentNullException(nameof(insightService));
		_homeTelemetrySource = homeTelemetrySource ?? throw new ArgumentNullException(nameof(homeTelemetrySource));
		_operationProvider = operationProvider ?? throw new ArgumentNullException(nameof(operationProvider));
		_moduleHost = moduleHost ?? throw new ArgumentNullException(nameof(moduleHost));
		_optimizationEngine = optimizationEngine ?? throw new ArgumentNullException(nameof(optimizationEngine));
		_widgetContentFactory = widgetContentFactory ?? throw new ArgumentNullException(nameof(widgetContentFactory));
		_diagnosticLogService = diagnosticLogService ?? throw new ArgumentNullException(nameof(diagnosticLogService));
		_profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
		_privilegeService = privilegeService ?? throw new ArgumentNullException(nameof(privilegeService));
		_restoreService = restoreService ?? throw new ArgumentNullException(nameof(restoreService));
		_eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
		_settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
		_activeOperations = activeOperations ?? throw new ArgumentNullException(nameof(activeOperations));
		_soundService = soundService;
		_dispatcher = Dispatcher.CurrentDispatcher;
		IsAdministrator = privilegeService.IsAdministrator;
		_appSettings = _settingsStore.Current;
		_settingsStore.Changed += (_, change) => _appSettings = change.Current;
		if (privilegeService is INotifyPropertyChanged privilegeNotifier)
		{
			_privilegeNotifier = privilegeNotifier;
			_privilegeNotifier.PropertyChanged += OnPrivilegePropertyChanged;
		}
		BuildOptimizationModules();
		BuildNavigationSections();
		BuildOverviewEntries();
		SeedDiagnostics();
		LoadPersistedActivities();
		_diagnosticLogService.EntryPublished += OnDiagnosticLogPublished;
		LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
		SubscribeToApplicationEvents();
		SelectedNavigationSection = NavigationSections.FirstOrDefault();
		LocalizationService instance = LocalizationService.Instance;
		ApplyProgressText = instance.Get("Dashboard.Applying");
		OptimizationStateHint = instance.Get("Dashboard.Standby");
		QuickActionStatusHint = instance.Get("Dashboard.WaitingOperation");
		ResultDialogTitle = instance.Get("Dashboard.OperationResult");
		AppendLog(IsAdministrator ? "Log.AdminMode" : "Log.NormalMode");
	}

	private void OnPrivilegePropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == "IsAdministrator")
		{
			_dispatcher.InvokeAsync((Action)delegate
			{
				IsAdministrator = _privilegeService.IsAdministrator;
				foreach (OptimizationModuleDefinition module in OptimizationModules)
				{
					foreach (OptimizationToggleModel toggle in module.Toggles)
					{
						toggle.RefreshPrivilege(IsAdministrator);
					}
				}
				AppendLog(IsAdministrator ? "Log.AdminMode" : "Log.NormalMode");
			});
		}
	}

	private void OnLanguageChanged(object? sender, string e)
	{
		_dispatcher.InvokeAsync((Action)delegate
		{
			string currentKey = SelectedNavigationSection?.Key;
			BuildNavigationSections();
			if (currentKey != null)
			{
				SelectedNavigationSection = NavigationSections.FirstOrDefault((NavigationSectionModel n) => n.Key == currentKey);
			}
			foreach (ActivityRecord activityLog in ActivityLogs)
			{
				activityLog.NotifyLanguageChanged();
			}
			OnPropertyChanged("AppliedCountDisplay");
			OnPropertyChanged("OptimizeButtonStatus");
			OnPropertyChanged("SelectedModeTitle");
			OnPropertyChanged("SelectedModeDescription");
			OnPropertyChanged("PrivilegeHint");
			OnPropertyChanged("OptimizeButtonLabel");
			OnPropertyChanged("DiagnosticSummary");
			OnPropertyChanged("CpuStatusLabel");
			OnPropertyChanged("GpuStatusLabel");
			OnPropertyChanged(nameof(GpuLoadAdapterDisplay));
			OnPropertyChanged("DpcStatusLabel");
			OnPropertyChanged("QuickActionTitle");
			OnPropertyChanged("QuickActionDescription");
			OnPropertyChanged("QuickActionMeta");
			OnPropertyChanged("QuickActionButtonLabel");
			OnPropertyChanged("ModulePendingStatus");
			OnPropertyChanged("AppliedToggleStatus");
		});
	}

	public async Task<OptimizationBatchResult> ApplyOperationsAsync(string moduleKey, IEnumerable<string> operationIds)
	{
		return await ApplyOperationsWithGuardAsync(moduleKey, operationIds, null, CancellationToken.None, "ManualExecutor").ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<OptimizationBatchResult> ApplyOperationsAsync(string moduleKey, IEnumerable<string> operationIds, IOperationProgress? progress)
	{
		return await ApplyOperationsWithGuardAsync(moduleKey, operationIds, progress, CancellationToken.None, "ManualExecutorWithProgress").ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<OptimizationBatchResult> ApplyOperationsAsync(string moduleKey, IEnumerable<string> operationIds, IOperationProgress? progress, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return await ApplyOperationsWithGuardAsync(moduleKey, operationIds, progress, cancellationToken, "ManualExecutorWithCancellation").ConfigureAwait(continueOnCapturedContext: false);
	}

	private async Task<OptimizationBatchResult> ApplyOperationsWithGuardAsync(string moduleKey, IEnumerable<string> operationIds, IOperationProgress? progress, CancellationToken cancellationToken, string sourceTag)
	{
		List<string> requestedIds = operationIds.Where((string id) => !string.IsNullOrWhiteSpace(id)).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToList();
		if (requestedIds.Count == 0)
		{
			return new OptimizationBatchResult(moduleKey, Array.Empty<OperationExecutionSummary>());
		}
		ApplyGuardResult guard = EvaluateApplyGuard(moduleKey, requestedIds);
		AppendApplyGuardLogs(sourceTag, moduleKey, guard);
		if (guard.AllowedOperationIds.Count == 0)
		{
			return new OptimizationBatchResult(moduleKey, guard.BlockedSummaries);
		}
		(bool Allowed, string? BlockReason) backupGate = await EnsureApplyBackupGateAsync(sourceTag, guard.NonSafeOperationCount, guard.RequiresRestorePointOperationCount, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		if (!backupGate.Allowed)
		{
			List<OperationExecutionSummary> mergedBlocked = Enumerable.Concat(second: guard.AllowedOperationIds.Select((string opId) => CreateBlockedOperationSummary(opId, ResolveOperationTitle(opId), backupGate.BlockReason ?? "安全门禁阻断执行。")).ToList(), first: guard.BlockedSummaries).ToList();
			return new OptimizationBatchResult(moduleKey, mergedBlocked);
		}
		cancellationToken.ThrowIfCancellationRequested();
		OptimizationBatchResult executed = await _optimizationEngine.ApplyAsync(moduleKey, guard.AllowedOperationIds, progress, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		List<OperationExecutionSummary> merged = guard.BlockedSummaries.Concat(executed.Operations).ToList();
		AppendLog("Log.ModuleApplied", "Log.Category.System", new object[2]
		{
			moduleKey,
			merged.Count((OperationExecutionSummary s) => s.Success)
		});
		if (_appSettings.EnableSoundNotification && executed.Operations.Any((OperationExecutionSummary o) => o.Success))
		{
			_soundService?.PlaySuccess();
		}
		return new OptimizationBatchResult(moduleKey, merged);
	}

	public async Task<OptimizationBatchResult> RestoreOperationsAsync(string moduleKey, IEnumerable<string> operationIds)
	{
		OptimizationBatchResult result = await _optimizationEngine.RestoreAsync(moduleKey, operationIds).ConfigureAwait(continueOnCapturedContext: false);
		AppendLog("Log.ModuleRestored", "Log.Category.System", new object[2]
		{
			moduleKey,
			result.Operations.Count((OperationExecutionSummary s) => s.Success)
		});
		return result;
	}

	public async Task<OptimizationBatchResult> RestoreOperationsAsync(string moduleKey, IEnumerable<string> operationIds, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		OptimizationBatchResult result = await _optimizationEngine.RestoreAsync(moduleKey, operationIds, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		AppendLog("Log.ModuleRestored", "Log.Category.System", new object[2]
		{
			moduleKey,
			result.Operations.Count((OperationExecutionSummary s) => s.Success)
		});
		return result;
	}

	private bool TryBeginModuleOperation(out CancellationToken operationToken)
	{
		if (_moduleOperationGate.IsBusy || !_moduleOperationGate.TryEnter())
		{
			operationToken = default;
			return false;
		}
		_moduleOperationCts = new CancellationTokenSource();
		_moduleActiveLease = _activeOperations?.Begin(
			"dashboard.module-operation",
			"模块优化或恢复",
			ActiveOperationRisk.Mutating,
			ActiveOperationClosePolicy.CancelAndDrain,
			CancelActiveOperation);
		operationToken = _moduleOperationCts.Token;
		return true;
	}

	private void CancelActiveOperation()
	{
		CancellationTokenSource? operationCts = _moduleOperationCts;
		try
		{
			operationCts?.Cancel();
		}
		catch (ObjectDisposedException)
		{
			// The operation completed concurrently and already disposed its CTS.
		}
	}

	private void EndModuleOperation()
	{
		CancellationTokenSource? operationCts = _moduleOperationCts;
		_moduleOperationCts = null;
		_moduleActiveLease?.Complete();
		_moduleActiveLease = null;
		_moduleOperationGate.Exit();
		operationCts?.Dispose();
	}

	public async Task<OptimizationBatchResult> ApplyOperationsByIdAsync(IEnumerable<string> operationIds, IOperationProgress? progress = null)
	{
		List<string> opList = operationIds.ToList();
		if (opList.Count == 0)
		{
			return new OptimizationBatchResult("", new List<OperationExecutionSummary>());
		}
		Dictionary<string, List<string>> grouped = GroupOperationsByModule(opList);
		List<OperationExecutionSummary> allSummaries = new List<OperationExecutionSummary>();
		int successCount = 0;
		foreach (KeyValuePair<string, List<string>> item in grouped)
		{
			item.Deconstruct(out var key, out var value);
			string moduleKey = key;
			List<string> ops = value;
			OptimizationBatchResult optimizationBatchResult = await ApplyOperationsWithGuardAsync(
				moduleKey,
				ops,
				progress,
				CancellationToken.None,
				"CrossModule").ConfigureAwait(continueOnCapturedContext: false);
			OptimizationBatchResult result = optimizationBatchResult;
			allSummaries.AddRange(result.Operations);
			successCount += result.Operations.Count((OperationExecutionSummary s) => s.Success);
		}
		AppendLog("Log.CrossModuleApplied", "Log.Category.System", new object[2] { grouped.Count, successCount });
		if (_appSettings.EnableSoundNotification && allSummaries.Any((OperationExecutionSummary o) => o.Success))
		{
			_soundService?.PlaySuccess();
		}
		SaveCrossModuleConfiguration(grouped.Keys);
		await RefreshModuleStatusesAsync().ConfigureAwait(continueOnCapturedContext: false);
		await _dispatcher.InvokeAsync((Action)delegate
		{
			OnPropertyChanged("AppliedOptimizationCount");
			OnPropertyChanged("AppliedCountDisplay");
			OnPropertyChanged("PendingOptimizationCount");
			OnPropertyChanged("OptimizationModules");
		});
		return new OptimizationBatchResult("cross-module", allSummaries);
	}

	public async Task<OptimizationBatchResult> RestoreOperationsByIdAsync(IEnumerable<string> operationIds, IOperationProgress? progress = null)
	{
		List<string> opList = operationIds.ToList();
		if (opList.Count == 0)
		{
			return new OptimizationBatchResult("", new List<OperationExecutionSummary>());
		}
		Dictionary<string, List<string>> grouped = GroupOperationsByModule(opList);
		List<OperationExecutionSummary> allSummaries = new List<OperationExecutionSummary>();
		int successCount = 0;
		foreach (KeyValuePair<string, List<string>> item in grouped)
		{
			item.Deconstruct(out var key, out var value);
			string moduleKey = key;
			List<string> ops = value;
			OptimizationBatchResult optimizationBatchResult = ((progress == null) ? (await _optimizationEngine.RestoreAsync(moduleKey, ops).ConfigureAwait(continueOnCapturedContext: false)) : (await _optimizationEngine.RestoreAsync(moduleKey, ops, progress).ConfigureAwait(continueOnCapturedContext: false)));
			OptimizationBatchResult result = optimizationBatchResult;
			allSummaries.AddRange(result.Operations);
			successCount += result.Operations.Count((OperationExecutionSummary s) => s.Success);
		}
		AppendLog("Log.CrossModuleRestored", "Log.Category.System", new object[2] { grouped.Count, successCount });
		await RefreshModuleStatusesAsync().ConfigureAwait(continueOnCapturedContext: false);
		await _dispatcher.InvokeAsync((Action)delegate
		{
			OnPropertyChanged("AppliedOptimizationCount");
			OnPropertyChanged("AppliedCountDisplay");
			OnPropertyChanged("PendingOptimizationCount");
			OnPropertyChanged("OptimizationModules");
		});
		return new OptimizationBatchResult("cross-module", allSummaries);
	}

	private Dictionary<string, List<string>> GroupOperationsByModule(IEnumerable<string> operationIds)
	{
		Dictionary<string, List<string>> dictionary = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		foreach (string operationId in operationIds)
		{
			string text = ResolveModuleKeyFromOperationId(operationId);
			if (!string.IsNullOrEmpty(text))
			{
				if (!dictionary.TryGetValue(text, out var value))
				{
					value = (dictionary[text] = new List<string>());
				}
				value.Add(operationId);
			}
		}
		return dictionary;
	}

	private string? ResolveModuleKeyFromOperationId(string operationId)
	{
		foreach (OptimizationModuleDescriptor module in _optimizationEngine.Modules)
		{
			if (module.Operations.Any((IOptimizationOperation op) => string.Equals(op.Id, operationId, StringComparison.OrdinalIgnoreCase)))
			{
				return module.Key;
			}
		}
		return null;
	}

	private void SaveCrossModuleConfiguration(IEnumerable<string> affectedModuleKeys)
	{
		try
		{
			AppSettings appSettings = AppSettings.Load();
			foreach (string moduleKey in affectedModuleKeys)
			{
				OptimizationModuleDefinition optimizationModuleDefinition = OptimizationModules.FirstOrDefault((OptimizationModuleDefinition m) => m.Key == moduleKey);
				if (optimizationModuleDefinition == null)
				{
					continue;
				}
				string text = moduleKey + "OptimizationSettings";
				PropertyInfo property = appSettings.GetType().GetProperty(text);
				if (property == null)
				{
					PageDiagnosticService.Log("DashboardViewModel", "[OPT-014] No configuration property for " + text);
					continue;
				}
				ModuleOptimizationConfig value = new ModuleOptimizationConfig
				{
					EnabledToggles = optimizationModuleDefinition.Toggles.ToDictionary((OptimizationToggleModel t) => t.OperationId ?? "", (OptimizationToggleModel t) => t.IsApplied)
				};
				property.SetValue(appSettings, value);
				PageDiagnosticService.Log("DashboardViewModel", $"[OPT-014] Saved config for {moduleKey}: {optimizationModuleDefinition.Toggles.Count((OptimizationToggleModel t) => t.IsApplied)} applied");
			}
			appSettings.Save();
			PageDiagnosticService.Log("DashboardViewModel", $"[OPT-014] Cross-module configuration saved for {affectedModuleKeys.Count()} modules");
		}
		catch (Exception ex)
		{
			PageDiagnosticService.LogError("DashboardViewModel", "[OPT-014] Failed to save cross-module configuration", ex);
		}
	}

	public async Task RefreshModuleStatusesAsync()
	{
		await SyncModuleStatusesAsync().ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task RefreshModuleStatusAsync(string moduleKey)
	{
		if (!(await _syncStatusLock.WaitAsync(TimeSpan.FromSeconds(5.0)).ConfigureAwait(continueOnCapturedContext: false)))
		{
			Debug.WriteLine("[DashboardViewModel] RefreshModuleStatusAsync(" + moduleKey + ") skipped - lock timeout");
			return;
		}
		try
		{
			OptimizationModuleDefinition module = OptimizationModules.FirstOrDefault((OptimizationModuleDefinition m) => m.Key == moduleKey);
			if (module == null || !module.SupportsExecution)
			{
				return;
			}
			await SyncModuleStatusAsyncCore(module).ConfigureAwait(continueOnCapturedContext: false);
		}
		finally
		{
			_syncStatusLock.Release();
		}
		_dispatcher.InvokeAsync((Action)delegate
		{
			OnPropertyChanged("OptimizationModules");
			OnPropertyChanged("TotalOptimizationCount");
			OnPropertyChanged("AppliedOptimizationCount");
			OnPropertyChanged("AppliedCountDisplay");
			OnPropertyChanged("PendingOptimizationCount");
			OnPropertyChanged("CurrentModeOptimizationCount");
		});
	}

	private void SeedDiagnostics()
	{
		IOrderedEnumerable<DiagnosticLogEntry> orderedEnumerable = from entry in _diagnosticLogService.GetRecentEntries(6)
			orderby entry.Timestamp descending
			select entry;
		foreach (DiagnosticLogEntry item in orderedEnumerable)
		{
			DiagnosticEvents.Add(item);
		}
		UpdateDiagnosticSummary();
	}

	private void LoadPersistedActivities()
	{
		IReadOnlyList<ActivityRecord> readOnlyList = _activityLogStore.Load();
		foreach (ActivityRecord item in readOnlyList)
		{
			ActivityLogs.Add(item);
			_logBuffer.Add(item.Display);
		}
	}

	private void OnDiagnosticLogPublished(object? sender, DiagnosticLogEntry entry)
	{
		if (_dispatcher.CheckAccess())
		{
			InsertDiagnosticEntry(entry);
			return;
		}
		_dispatcher.InvokeAsync((Action)delegate
		{
			InsertDiagnosticEntry(entry);
		});
	}

	private void InsertDiagnosticEntry(DiagnosticLogEntry entry)
	{
		DiagnosticEvents.Insert(0, entry);
		while (DiagnosticEvents.Count > 6)
		{
			DiagnosticEvents.RemoveAt(DiagnosticEvents.Count - 1);
		}
		UpdateDiagnosticSummary();
	}

	private void UpdateDiagnosticSummary()
	{
		OnPropertyChanged("CriticalDiagnosticCount");
		OnPropertyChanged("HasCriticalDiagnostics");
		OnPropertyChanged("DiagnosticSummary");
	}

	public async Task InitializeAsync()
	{
		ActiveGame = await Task.Run(() => _insightService.GetActiveGame()).ConfigureAwait(continueOnCapturedContext: false);
		ReadHomeTelemetrySnapshot();
		StartTelemetryPipeline();
		_ = LocalizationService.Instance;
		Widgets.Clear();
		foreach (IModuleWidget widget in _moduleHost.Widgets)
		{
			object content = _widgetContentFactory(widget);
			Widgets.Add(new WidgetHostModel(widget.Id, widget.Descriptor, content));
			AppendLog("Log.ModuleLoaded", "Log.Category.System", new object[1] { widget.Descriptor.Title });
		}
		if (ActivityLogs.Count == 0)
		{
			AppendLog("Log.SystemCheckComplete");
		}
		await SyncModuleStatusesAsync().ConfigureAwait(continueOnCapturedContext: false);
		await _profileService.RunHealthScanAsync();
		AppendLog("Log.HealthScanComplete", "Log.Category.System", new object[2] { AppliedOptimizationCount, TotalOptimizationCount });
	}

	private void ClearModuleSearch()
	{
		ModuleSearchQuery = string.Empty;
	}

	public void ClearActivityHistory()
	{
		ActivityLogs.Clear();
		_logBuffer.Clear();
		PersistActivities();
		OnPropertyChanged("QuickActionMeta");
	}

	private async Task ExportActivityHistoryAsync()
	{
		LocalizationService loc = LocalizationService.Instance;
		try
		{
			string exportDirectory = AppPaths.ReportsExportDir;
			if (!SafeDirectoryOperations.TryCreateDirectory(exportDirectory, out string dirError))
			{
				AppendLog("Log.ActivityExportFailed", "Log.Category.Error", new object[1] { dirError ?? loc.Get("Common.Error") });
				return;
			}
			string target = Path.Combine(exportDirectory, $"ActivityLog-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
			string content = string.Join(Environment.NewLine, ActivityLogs.Select((ActivityRecord r) => r.Display));
			if (!(await Task.Run(() => SafeFileWriter.TryWriteAllText(target, content, delegate(Exception ex3)
			{
				throw new IOException(LocalizationService.Instance.Get("Dashboard.WriteFileFailed", ex3.Message));
			})).ConfigureAwait(continueOnCapturedContext: false)))
			{
				AppendLog("Log.ActivityExportFailed", "Log.Category.Error", new object[1] { LocalizationService.Instance.Get("Dashboard.WriteFileFailed", "") });
				return;
			}
			AppendLog("Log.ActivityExported", "Log.Category.System", new object[1] { target });
			var (process, processError) = SafeProcessHelper.StartProcess(new ProcessStartInfo("explorer.exe", "/select,\"" + target + "\"")
			{
				UseShellExecute = true
			});
			if (process == null)
			{
				AppendLog("Log.ActivityExportFailed", "Log.Category.Warning", new object[1] { processError ?? loc.Get("Common.Error") });
			}
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			AppendLog("Log.ActivityExportFailed", "Log.Category.Error", new object[1] { ex2.Message });
		}
	}

	private ApplyGuardResult EvaluateApplyGuard(string moduleKey, IReadOnlyList<string> requestedOperationIds)
	{
		List<string> list = new List<string>(requestedOperationIds.Count);
		List<OperationExecutionSummary> list2 = new List<OperationExecutionSummary>();
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		int num5 = 0;
		int num6 = 0;
		int num7 = 0;
		foreach (string requestedOperationId in requestedOperationIds)
		{
			if (!_operationLookup.TryGetValue(requestedOperationId, out OptimizationToggleModel value))
			{
				num5++;
				list.Add(requestedOperationId);
				continue;
			}
			if (value.IsHardwareIncompatible)
			{
				num2++;
				string message = (string.IsNullOrWhiteSpace(value.HardwareIncompatibleHint) ? "当前硬件不兼容，已自动跳过。" : value.HardwareIncompatibleHint);
				list2.Add(CreateBlockedOperationSummary(requestedOperationId, ResolveOperationTitle(requestedOperationId), message));
				continue;
			}
			if (value.RiskLevel != OperationRiskLevel.Safe)
			{
				num6++;
			}
			if (value.RequiresRestorePoint)
			{
				num7++;
			}
			list.Add(requestedOperationId);
		}
		return new ApplyGuardResult(list, list2, num, num2, num3, num4, num5, num6, num7);
	}

	private void AppendApplyGuardLogs(string sourceTag, string moduleKey, ApplyGuardResult guard)
	{
		if (guard.PolicyBlockedCount > 0)
		{
			AppendLog($"[ApplyGuard:{sourceTag}] 模块[{moduleKey}] 策略拦截 {guard.PolicyBlockedCount} 项。");
		}
		if (guard.StaticBlockedCount > 0)
		{
			AppendLog($"[ApplyGuard:{sourceTag}] 模块[{moduleKey}] 已知风险拦截 {guard.StaticBlockedCount} 项。");
		}
		if (guard.HardwareBlockedCount > 0)
		{
			AppendLog($"[ApplyGuard:{sourceTag}] 模块[{moduleKey}] 硬件不兼容跳过 {guard.HardwareBlockedCount} 项。");
		}
		if (guard.ExtremeBlockedCount > 0)
		{
			AppendLog($"[ApplyGuard:{sourceTag}] 模块[{moduleKey}] 极限风险跳过 {guard.ExtremeBlockedCount} 项。");
		}
		if (guard.UnknownOperationCount > 0)
		{
			AppendLog($"[ApplyGuard:{sourceTag}] 模块[{moduleKey}] 发现未建模操作 {guard.UnknownOperationCount} 项（按兼容策略放行）。");
		}
	}

	private async Task<(bool Allowed, string? BlockReason)> EnsureApplyBackupGateAsync(string sourceTag, int nonSafeOperationCount, int requiresRestorePointCount, CancellationToken cancellationToken)
	{
		// Non-safe operations are still protected by their per-operation confirmation,
		// typed snapshot/receipt and post-write readback. Only operations that explicitly
		// declare RequiresRestorePoint are hard-gated on Windows System Protection.
		if (requiresRestorePointCount <= 0)
		{
			return (Allowed: true, BlockReason: null);
		}
		DateTime nowUtc = DateTime.UtcNow;
		if (nowUtc - _lastForcedApplyBackupUtc <= ForcedApplyBackupReuseWindow)
		{
			AppendLog($"[ApplyGuard:{sourceTag}] 检测到近期已创建强制还原点，复用（{ForcedApplyBackupReuseWindow.TotalMinutes:F0}分钟窗口内）。");
			return (Allowed: true, BlockReason: null);
		}
		AppendLog($"[ApplyGuard:{sourceTag}] 检测到明确声明需还原点操作 {requiresRestorePointCount} 项，触发系统还原保护门禁；非安全操作由逐项确认、typed 快照和回读保护。");
		if (!(await Task.Run(() => _restoreService.IsSystemRestoreEnabled(), cancellationToken).ConfigureAwait(continueOnCapturedContext: false)))
		{
			OptimizationStateHint = "系统还原未启用，已阻断本次操作。请先在还原点页面启用系统还原。";
			AppendLog("[ApplyGuard:" + sourceTag + "] 系统还原未启用，已阻断本次操作。请先在还原点页面启用系统还原。");
			return (Allowed: false, BlockReason: "系统还原未启用，已阻断本次操作。请先在还原点页面启用系统还原。");
		}
		try
		{
			OptimizationStateHint = LocalizationService.Instance.Get("Dashboard.CreatingRestorePoint");
			string description = $"YuqiEngine Apply Guard Backup {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
			if (!(await _restoreService.CreateRestorePointAsync(description, cancellationToken).ConfigureAwait(continueOnCapturedContext: false)))
			{
				string error = (string.IsNullOrWhiteSpace(_restoreService.LastError) ? "未知错误" : _restoreService.LastError);
				string reason = (OptimizationStateHint = "系统还原点创建失败，已阻断本次操作：" + error);
				AppendLog("[ApplyGuard:" + sourceTag + "] " + reason);
				return (Allowed: false, BlockReason: reason);
			}
			_lastForcedApplyBackupUtc = DateTime.UtcNow;
			AppendLog("[ApplyGuard:" + sourceTag + "] 强制还原点创建成功。");
			return (Allowed: true, BlockReason: null);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex2)
		{
			string reason2 = (OptimizationStateHint = "系统还原点创建异常，已阻断本次操作：" + ex2.Message);
			AppendLog("[ApplyGuard:" + sourceTag + "] " + reason2);
			return (Allowed: false, BlockReason: reason2);
		}
	}

	private OperationExecutionSummary CreateBlockedOperationSummary(string operationId, string title, string message)
	{
		GameOptimizer.Core.Optimization.OperationStatus operationStatus = new GameOptimizer.Core.Optimization.OperationStatus(IsCompliant: false, message);
		return new OperationExecutionSummary(operationId, title, Success: false, Changed: false, message, operationStatus, operationStatus);
	}

	private string ResolveOperationTitle(string operationId)
	{
		if (_operationLookup.TryGetValue(operationId, out OptimizationToggleModel value))
		{
			return value.Title;
		}
		return OptimizationTranslations.Instance.GetTitle(operationId) ?? operationId;
	}

	private void OpenSettings()
	{
		AppendLog("Log.SettingsPlaceholder");
	}

	private void NavigateGameMode()
	{
		NavigationSectionModel navigationSectionModel = NavigationSections.FirstOrDefault((NavigationSectionModel section) => section.Key == "game-mode-hub");
		if (navigationSectionModel != null)
		{
			SelectedNavigationSection = navigationSectionModel;
		}
	}

	private void NavigateRestorePoint()
	{
		NavigationSectionModel navigationSectionModel = NavigationSections.FirstOrDefault((NavigationSectionModel section) => section.Key == "restore-point");
		if (navigationSectionModel != null)
		{
			SelectedNavigationSection = navigationSectionModel;
		}
	}

	private void NavigateToModule(string? moduleKey)
	{
		if (string.IsNullOrWhiteSpace(moduleKey))
		{
			return;
		}
		if (ModuleKeyToNavKeyMap.TryGetValue(moduleKey, out string navKey))
		{
			NavigationSectionModel navigationSectionModel = NavigationSections.FirstOrDefault((NavigationSectionModel s) => s.Key == navKey);
			if (navigationSectionModel != null)
			{
				SelectedNavigationSection = navigationSectionModel;
				NavigateToPageRequested?.Invoke(navKey);
				return;
			}
		}
		NavigationSectionModel navigationSectionModel2 = NavigationSections.FirstOrDefault((NavigationSectionModel s) => s.Key == moduleKey);
		if (navigationSectionModel2 != null)
		{
			SelectedNavigationSection = navigationSectionModel2;
			NavigateToPageRequested?.Invoke(navigationSectionModel2.Key);
			return;
		}
		NavigationSectionModel navigationSectionModel3 = NavigationSections.FirstOrDefault((NavigationSectionModel s) => s.TargetModuleKey == moduleKey);
		if (navigationSectionModel3 != null)
		{
			SelectedNavigationSection = navigationSectionModel3;
			NavigateToPageRequested?.Invoke(navigationSectionModel3.Key);
		}
	}

	private void NavigateToRepairCenter()
	{
		NavigationSectionModel navigationSectionModel = NavigationSections.FirstOrDefault((NavigationSectionModel section) => section.Key == "repair-center");
		if (navigationSectionModel != null)
		{
			SelectedNavigationSection = navigationSectionModel;
		}
	}

	private void NavigateToSystemCleanup()
	{
		NavigationSectionModel navigationSectionModel = NavigationSections.FirstOrDefault((NavigationSectionModel section) => section.Key == "system-cleanup");
		if (navigationSectionModel != null)
		{
			SelectedNavigationSection = navigationSectionModel;
		}
	}

	private async Task ExecuteQuickActionAsync(string? actionKey = null)
	{
		if (IsQuickActionBusy)
		{
			return;
		}
		QuickActionTab action = SelectedQuickAction;
		if (!string.IsNullOrWhiteSpace(actionKey) && Enum.TryParse<QuickActionTab>(actionKey, ignoreCase: true, out var parsed))
		{
			action = parsed;
			if (SelectedQuickAction != parsed)
			{
				SelectedQuickAction = parsed;
			}
		}
		string text = action switch
		{
			QuickActionTab.HealthCheck => LocalizationService.Instance.Get("QuickAction.HealthCheck"), 
			QuickActionTab.ExportReport => LocalizationService.Instance.Get("QuickAction.ExportReport"), 
			_ => LocalizationService.Instance.Get("QuickAction.SchemaCenter"), 
		};
		string actionName = text;
		IsQuickActionBusy = true;
		LocalizationService locQa = LocalizationService.Instance;
		QuickActionStatusHint = actionName + locQa.Get("Common.Executing");
		try
		{
			switch (action)
			{
			case QuickActionTab.HealthCheck:
			{
				AppendLog("QuickAction.RunningHealthCheck");
				OptimizationStatusSnapshot snapshot = await _profileService.RunHealthScanAsync();
				AppendLog("QuickAction.HealthCheckResult", "Log.Category.System", new object[2] { snapshot.CompliantOperations, snapshot.TotalOperations });
				QuickActionStatusHint = locQa.Get("Dashboard.HealthCheckComplete");
				ToastService.Instance.Info(locQa.Get("QuickAction.HealthCheckDone"), locQa.Get("QuickAction.HealthCheckResult", snapshot.CompliantOperations, snapshot.TotalOperations));
				ActivityService.Instance.LogTest("QuickAction.HealthCheck", "QuickAction.HealthCheckResult", new object[2] { snapshot.CompliantOperations, snapshot.TotalOperations });
				break;
			}
			case QuickActionTab.ExportReport:
			{
				string path = await _profileService.ExportStatusReportAsync();
				AppendLog("QuickAction.ReportExported", "Log.Category.System", new object[1] { path });
				QuickActionStatusHint = locQa.Get("Dashboard.ReportExported");
				ToastService.Instance.Success(locQa.Get("QuickAction.ExportDone"), locQa.Get("QuickAction.ReportSaved"));
				ActivityService.Instance.AddActivity("QuickAction.ExportReport", "QuickAction.ReportSavedToDesktop", "\ud83d\udcc4", true);
				break;
			}
			default:
				AppendLog("QuickAction.OpeningSchemaCenter");
				QuickActionStatusHint = locQa.Get("Dashboard.SchemaCenterOpened");
				break;
			}
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			AppendLog("QuickAction.Failed", "Log.Category.Error", new object[2] { actionName, ex2.Message });
			QuickActionStatusHint = actionName + locQa.Get("Common.Failed") + ": " + ex2.Message;
			ToastService.Instance.Error(actionName, ex2.Message);
		}
		finally
		{
			IsQuickActionBusy = false;
		}
	}

	private void OpenDiagnosticsFolder()
	{
		try
		{
			Process.Start(new ProcessStartInfo("explorer.exe", _diagnosticLogService.LogDirectory)
			{
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			AppendLog("Log.OpenLogDirFailed", "Log.Category.Error", new object[1] { ex.Message });
			_diagnosticLogService.Append(LogLevel.Error, "UI.Diagnostics", "Open log directory failed", ex.ToString());
		}
	}

	private async Task ApplySelectedModuleAsync()
	{
		LocalizationService loc = LocalizationService.Instance;
		if (SelectedOptimizationModule == null)
		{
			AppendLog("Log.SelectModuleFirst");
			return;
		}
		string moduleTitle = OptimizationTranslations.Instance.GetModuleName(SelectedOptimizationModule.Title);
		if (!SelectedOptimizationModule.SupportsExecution)
		{
			AppendLog("Log.ModuleNotExecutable", "Log.Category.System", new object[1] { moduleTitle });
			return;
		}
		List<OptimizationToggleModel> selectedToggles = SelectedOptimizationModule.Toggles.Where((OptimizationToggleModel toggle) => toggle.IsEnabled && !string.IsNullOrWhiteSpace(toggle.OperationId)).ToList();
		int hardwareBlockedCount = selectedToggles.Count((OptimizationToggleModel t) => t.IsHardwareIncompatible);
		if (hardwareBlockedCount > 0)
		{
			AppendLog($"[ApplyGuard:ModulePage] 模块[{moduleTitle}] 检测到硬件不兼容项 {hardwareBlockedCount} 个，将自动跳过。");
		}
		List<string> selected = (from toggle in selectedToggles
			where !toggle.IsHardwareIncompatible
			select toggle.OperationId).ToList();
		if (selected.Count == 0)
		{
			AppendLog("Log.NoSelectedPolicies", "Log.Category.System", new object[1] { moduleTitle });
			return;
		}
		IsApplyingModule = true;
		ApplyProgressText = loc.Get("Log.ApplyProgress", 0, selected.Count);
		AppendLog("Log.ApplyingPolicies", "Log.Category.System", new object[2] { moduleTitle, selected.Count });
		try
		{
			int totalCount = selected.Count;
			int completedCount = 0;
			AggregateProgress progress = new AggregateProgress(delegate
			{
				completedCount++;
				ApplyProgressText = loc.Get("Log.ApplyProgress", completedCount, totalCount);
			});
			OptimizationBatchResult result = await ApplyOperationsWithGuardAsync(
				SelectedOptimizationModule.Key,
				selected,
				progress,
				CancellationToken.None,
				"ModulePage").ConfigureAwait(continueOnCapturedContext: false);
			AppendBatchResults(SelectedOptimizationModule.Title, result);
			OptimizationStateHint = loc.Get("Log.ModuleSynced", SelectedOptimizationModule.Title);
			int changedCount = result.Operations.Count((OperationExecutionSummary o) => o.Changed);
			PresentOperationResults(subtitle: (changedCount == selected.Count) ? loc.Get("Log.PolicyCount", selected.Count) : LocalizationService.Instance.Get("Dashboard.Module.PartialApply", changedCount, selected.Count), title: loc.Get("Log.BatchApply", SelectedOptimizationModule.Title), batches: new OptimizationBatchResult[1] { result });
			int successCount = result.Operations.Count((OperationExecutionSummary o) => o.Success);
			int failCount = result.Operations.Count((OperationExecutionSummary o) => !o.Success);
			if (failCount > 0)
			{
				ToastService.Instance.Warning(moduleTitle, loc.Get("Log.SuccessFailSummary", successCount, failCount));
			}
			else
			{
				ToastService.Instance.Success(moduleTitle, loc.Get("Module.ApplyComplete", changedCount, ""));
			}
			ActivityService.Instance.LogOptimization(SelectedOptimizationModule.TitleKey, failCount == 0, changedCount);
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			AppendLog("Log.ExecuteFailed", "Log.Category.Error", new object[2] { moduleTitle, ex2.Message });
			OptimizationBatchResult failure = CreateFailureBatch(SelectedOptimizationModule.Key, selected, ex2.Message);
			PresentOperationResults(loc.Get("Log.BatchApplyFailed", moduleTitle), loc.Get("Log.PolicyCount", selected.Count), new OptimizationBatchResult[1] { failure });
			ToastService.Instance.Error(moduleTitle, loc.Get("Module.ApplyFailed", ""));
			ActivityService.Instance.LogOptimization(SelectedOptimizationModule.TitleKey, success: false);
		}
		finally
		{
			IsApplyingModule = false;
		}
		await SyncModuleStatusAsync(SelectedOptimizationModule).ConfigureAwait(continueOnCapturedContext: false);
	}

	private async Task RestoreSelectedModuleAsync()
	{
		LocalizationService loc = LocalizationService.Instance;
		if (SelectedOptimizationModule == null)
		{
			AppendLog("Log.NoModuleSelected");
			return;
		}
		string moduleTitle = OptimizationTranslations.Instance.GetModuleName(SelectedOptimizationModule.Title);
		if (!SelectedOptimizationModule.SupportsExecution)
		{
			AppendLog("Log.ModuleNoRestore", "Log.Category.System", new object[1] { moduleTitle });
			return;
		}
		List<string> applied = (from toggle in SelectedOptimizationModule.Toggles
			where toggle.IsApplied && !string.IsNullOrWhiteSpace(toggle.OperationId)
			select toggle.OperationId).ToList();
		if (applied.Count == 0)
		{
			AppendLog("Dashboard.NoAppliedOptimizationsFor", "Log.Category.System", new object[1] { moduleTitle });
			return;
		}
		if (!ConfirmDialog.Show(Application.Current.MainWindow, loc.Get("Dashboard.ConfirmRevertTitle"), loc.Get("Dashboard.ConfirmRevertMsg", moduleTitle, applied.Count)))
		{
			AppendLog("Dashboard.RevertCancelledFor", "Log.Category.System", new object[1] { moduleTitle });
			return;
		}
		IsApplyingModule = true;
		AppendLog("Dashboard.RevertingFor", "Log.Category.System", new object[2] { moduleTitle, applied.Count });
		try
		{
			OptimizationBatchResult result = await _optimizationEngine.RestoreAsync(SelectedOptimizationModule.Key, applied).ConfigureAwait(continueOnCapturedContext: false);
			AppendBatchResults(SelectedOptimizationModule.Title, result);
			PresentOperationResults(loc.Get("Dashboard.RevertedFor", moduleTitle), loc.Get("Log.PolicyCount", applied.Count), new OptimizationBatchResult[1] { result });
			int successCount = result.Operations.Count((OperationExecutionSummary o) => o.Success);
			int failCount = result.Operations.Count((OperationExecutionSummary o) => !o.Success);
			if (failCount > 0)
			{
				ToastService.Instance.Warning(moduleTitle, loc.Get("Dashboard.RevertSuccess", successCount, failCount));
			}
			else
			{
				ToastService.Instance.Success(moduleTitle, loc.Get("Dashboard.RevertedCount", successCount));
			}
			ActivityService.Instance.LogRestore(SelectedOptimizationModule.TitleKey, failCount == 0);
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			AppendLog("Dashboard.RevertFailedFor", "Log.Category.Error", new object[2] { moduleTitle, ex2.Message });
			OptimizationBatchResult failure = CreateFailureBatch(SelectedOptimizationModule.Key, applied, ex2.Message);
			PresentOperationResults("[" + moduleTitle + "] " + loc.Get("Dashboard.RevertFailed"), $"{applied.Count} {loc.Get("Dashboard.Policies")}", new OptimizationBatchResult[1] { failure });
			ToastService.Instance.Error(moduleTitle, loc.Get("Dashboard.RevertOperationFailed"));
			ActivityService.Instance.LogRestore(SelectedOptimizationModule.TitleKey, success: false);
		}
		finally
		{
			IsApplyingModule = false;
		}
		await SyncModuleStatusAsync(SelectedOptimizationModule).ConfigureAwait(continueOnCapturedContext: false);
	}

	private async Task RestoreModuleBaselineFromProfileAsync()
	{
		LocalizationService loc = LocalizationService.Instance;
		if (SelectedOptimizationModule == null)
		{
			AppendLog("Dashboard.SelectModuleFirst");
			return;
		}
		string moduleTitle = OptimizationTranslations.Instance.GetModuleName(SelectedOptimizationModule.Title);
		if (!ConfirmDialog.Show(Application.Current.MainWindow, loc.Get("Dashboard.ConfirmBaselineTitle"), loc.Get("Dashboard.ConfirmBaselineMsg", SelectedOptimizationModule.Title)))
		{
			AppendLog("Dashboard.BaselineCancelledFor", "Log.Category.System", new object[1] { moduleTitle });
			return;
		}
		AppendLog("Dashboard.RestoringBaselineFor", "Log.Category.System", new object[1] { moduleTitle });
		if (!TryBeginModuleOperation(out CancellationToken operationToken))
		{
			return;
		}
		try
		{
			await _profileService.RestoreModuleBaselineAsync(SelectedOptimizationModule.Key, operationToken).ConfigureAwait(continueOnCapturedContext: false);
			CommandResultPresentation presentation = CommandResultPresentationMapper.FromMissingReceipt(moduleTitle);
			AppendLog("Dashboard.BaselineRestoreComplete", "Log.Category.System", new object[1] { moduleTitle });
			await SyncModuleStatusAsync(SelectedOptimizationModule).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (OperationCanceledException)
		{
			AppendLog("Dashboard.BaselineCancelledFor", "Log.Category.System", new object[1] { moduleTitle });
		}
		catch (Exception ex)
		{
			CommandResultPresentation presentation = CommandResultPresentationMapper.FromException(ex);
			AppendLog("Dashboard.BaselineFailedFor", "Log.Category.Error", new object[2] { moduleTitle, presentation.DisplayText });
		}
		finally
		{
			EndModuleOperation();
		}
	}

	private async Task RestoreModuleWindowsDefaultsAsync()
	{
		LocalizationService loc = LocalizationService.Instance;
		if (SelectedOptimizationModule == null)
		{
			AppendLog("Dashboard.SelectModuleForDefaults");
			return;
		}
		string moduleTitle = OptimizationTranslations.Instance.GetModuleName(SelectedOptimizationModule.Title);
		if (!ConfirmDialog.Show(Application.Current.MainWindow, loc.Get("Dashboard.ConfirmDefaultsTitle"), loc.Get("Dashboard.ConfirmDefaultsMsg", SelectedOptimizationModule.Title), "\ue7ba"))
		{
			AppendLog("Dashboard.DefaultsCancelledFor", "Log.Category.System", new object[1] { moduleTitle });
			return;
		}
		AppendLog("Dashboard.RestoringDefaultsFor", "Log.Category.System", new object[1] { moduleTitle });
		if (!TryBeginModuleOperation(out CancellationToken operationToken))
		{
			return;
		}
		try
		{
			await _profileService.RestoreModuleDefaultsAsync(SelectedOptimizationModule.Key, operationToken).ConfigureAwait(continueOnCapturedContext: false);
			CommandResultPresentation presentation = CommandResultPresentationMapper.FromMissingReceipt(moduleTitle);
			AppendLog("Dashboard.DefaultsRestoreComplete", "Log.Category.System", new object[1] { moduleTitle });
			await SyncModuleStatusAsync(SelectedOptimizationModule).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (OperationCanceledException)
		{
			AppendLog("Dashboard.DefaultsCancelledFor", "Log.Category.System", new object[1] { moduleTitle });
		}
		catch (Exception ex)
		{
			CommandResultPresentation presentation = CommandResultPresentationMapper.FromException(ex);
			AppendLog("Log.WindowsDefaultFailed", "Log.Category.Error", new object[2] { moduleTitle, presentation.DisplayText });
		}
		finally
		{
			EndModuleOperation();
		}
	}

	private void SaveModuleSnapshot()
	{
		LocalizationService instance = LocalizationService.Instance;
		if (SelectedOptimizationModule == null)
		{
			AppendLog("Log.SelectModuleToSave");
			return;
		}
		string moduleName = OptimizationTranslations.Instance.GetModuleName(SelectedOptimizationModule.Title);
		if (!SelectedOptimizationModule.SupportsExecution)
		{
			AppendLog("Log.ModuleNoSavable", "Log.Category.System", new object[1] { moduleName });
			return;
		}
		int num = SelectedOptimizationModule.Toggles.Count((OptimizationToggleModel t) => t.IsEnabled);
		AppendLog("Log.SavedOfflinePlan", "Log.Category.System", new object[2] { moduleName, num });
	}

	private void SelectAllToggles()
	{
		if (SelectedOptimizationModule == null)
		{
			return;
		}
		_isBulkUpdating = true;
		try
		{
			string moduleName = OptimizationTranslations.Instance.GetModuleName(SelectedOptimizationModule.Title);
			List<OptimizationToggleModel> list = FilteredStrategies.ToList();
			foreach (OptimizationToggleModel item in list)
			{
				item.IsEnabled = true;
			}
			AppendLog("Log.SelectAllPolicies", "Log.Category.System", new object[2] { moduleName, list.Count });
		}
		finally
		{
			_isBulkUpdating = false;
			NotifyPendingToggleChanged();
		}
	}

	private void DeselectAllToggles()
	{
		if (SelectedOptimizationModule == null)
		{
			return;
		}
		_isBulkUpdating = true;
		try
		{
			List<OptimizationToggleModel> list = FilteredStrategies.ToList();
			foreach (OptimizationToggleModel item in list)
			{
				item.IsEnabled = false;
			}
			string moduleName = OptimizationTranslations.Instance.GetModuleName(SelectedOptimizationModule.Title);
			AppendLog("Log.DeselectAll", "Log.Category.System", new object[2] { moduleName, list.Count });
		}
		finally
		{
			_isBulkUpdating = false;
			NotifyPendingToggleChanged();
		}
	}

	private async Task RestoreWindowsDefaultAsync()
	{
		LocalizationService loc = LocalizationService.Instance;
		var allOperations = OptimizationModules.Where((OptimizationModuleDefinition m) => m.SupportsExecution).SelectMany((OptimizationModuleDefinition m) => from t in m.Toggles
			where !string.IsNullOrWhiteSpace(t.OperationId) && t.IsApplied && t.CanRestore
			select new
			{
				Module = m,
				Toggle = t,
				OperationId = t.OperationId
			}).ToList();
		Debug.WriteLine($"[RestoreDefault] 总优化项: {allOperations.Count}");
		if (allOperations.Count == 0)
		{
			AppendLog("Log.NoOperationsToRestore");
			ToastService.Instance.Info(loc.Get("RestoreOptions.RestoreTitle"), loc.Get("RestoreDetail.NoItems"));
			return;
		}
		if (!TryBeginModuleOperation(out CancellationToken operationToken))
		{
			return;
		}
		foreach (var item in allOperations)
		{
			item.Toggle.OnStartRestore();
		}
		AppendLog("Log.RestoringWindowsDefault", "Log.Category.System", new object[1] { allOperations.Count });
		RevertProgressDialog progressDialog = null;
		List<RevertResultGroup> resultGroups = new List<RevertResultGroup>();
		try
		{
			await _dispatcher.InvokeAsync((Action)delegate
			{
				Window mainWindow = Application.Current.MainWindow;
				progressDialog = new RevertProgressDialog
				{
					Owner = mainWindow
				};
				progressDialog.SetOperationType(RestoreOperationType.RestoreWindowsDefaults);
				progressDialog.Show();
			});
			var moduleGroups = (from x in allOperations
				group x by x.Module).ToList();
			int completedOperations = 0;
			int totalOperations = allOperations.Count;
			foreach (var moduleGroup in moduleGroups)
			{
				OptimizationModuleDefinition module = moduleGroup.Key;
				List<string> operationIds = moduleGroup.Select(x => x.OperationId).ToList();
				Debug.WriteLine($"[RestoreDefault] 处理模块: {module.Title}, 操作\ufffd? {operationIds.Count}");
				RevertResultGroup resultGroup = new RevertResultGroup
				{
					ModuleName = module.Title,
					Items = new List<RevertResultItem>()
				};
				try
				{
					OptimizationBatchResult result = await RestoreOperationsAsync(module.Key, operationIds, operationToken).ConfigureAwait(continueOnCapturedContext: false);
					Debug.WriteLine($"[RestoreDefault] 模块 {module.Title} 恢复结果: 成功={result.Operations.Count((OperationExecutionSummary o) => o.Success)}, 失败={result.Operations.Count((OperationExecutionSummary o) => !o.Success)}");
					OptimizationTranslations trans = OptimizationTranslations.Instance;
					foreach (OperationExecutionSummary op in result.Operations)
					{
						CommandResultPresentation presentation = CommandResultPresentationMapper.FromOptimization(op);
						var toggleItem = allOperations.FirstOrDefault(x => x.OperationId == op.OperationId);
						if (toggleItem != null)
						{
							toggleItem.Toggle.CompleteRestoreFromSummary(op);
						}
						var restoreReachedTarget = OptimizationExecutionStateEvaluator.DidRestoreReachTarget(op);
						resultGroup.Items.Add(new RevertResultItem
						{
							Title = trans.GetTitle(op.OperationId),
							Success = restoreReachedTarget,
							ErrorMessage = (restoreReachedTarget ? null : op.Message)
						});
						if (restoreReachedTarget)
						{
							resultGroup.SuccessCount++;
						}
						else
						{
							resultGroup.FailedCount++;
							Debug.WriteLine("[RestoreDefault] 操作未达成目标 " + op.OperationId + ", 原因: " + op.Message);
						}
						int num = completedOperations;
						completedOperations = num + 1;
					}
					await _dispatcher.InvokeAsync((Action)delegate
					{
						progressDialog?.UpdateProgress(module.Title, completedOperations, totalOperations);
					});
				}
				catch (OperationCanceledException)
				{
					Debug.WriteLine("[RestoreDefault] 模块 " + module.Title + " 已取消");
					AppendLog("Log.ModuleRestoreCancelled", "Log.Category.Warning", new object[1] { module.Title });
					foreach (var item2 in moduleGroup)
					{
						item2.Toggle.OnRestoreFailed();
						resultGroup.Items.Add(new RevertResultItem
						{
							Title = OptimizationTranslations.Instance.GetTitle(item2.OperationId),
							Success = false,
							ErrorMessage = "已取消"
						});
						resultGroup.FailedCount++;
						int num = completedOperations;
						completedOperations = num + 1;
					}
					await _dispatcher.InvokeAsync((Action)delegate
					{
						progressDialog?.UpdateProgress(module.Title, completedOperations, totalOperations);
					});
				}
				catch (Exception ex)
				{
					Debug.WriteLine("[RestoreDefault] 模块 " + module.Title + " 异常: " + ex.Message);
					AppendLog("Log.ModuleRestoreFailed", "Log.Category.Error", new object[2] { module.Title, ex.Message });
					OptimizationTranslations trans2 = OptimizationTranslations.Instance;
					foreach (var item2 in moduleGroup)
					{
						item2.Toggle.OnRestoreFailed();
						resultGroup.Items.Add(new RevertResultItem
						{
							Title = trans2.GetTitle(item2.OperationId),
							Success = false,
							ErrorMessage = ex.Message
						});
						resultGroup.FailedCount++;
						int num = completedOperations;
						completedOperations = num + 1;
					}
					await _dispatcher.InvokeAsync((Action)delegate
					{
						progressDialog?.UpdateProgress(module.Title, completedOperations, totalOperations);
					});
				}
				resultGroups.Add(resultGroup);
			}
			await _dispatcher.InvokeAsync((Action)delegate
			{
				progressDialog?.Close();
			});
			int totalSuccess = resultGroups.Sum((RevertResultGroup g) => g.SuccessCount);
			int totalFailed = resultGroups.Sum((RevertResultGroup g) => g.FailedCount);
			Debug.WriteLine($"[RestoreDefault] 操作统计: 成功={totalSuccess}, 失败={totalFailed}");
			Debug.WriteLine("[RestoreDefault] 等待系统生效...");
			await Task.Delay(1000).ConfigureAwait(continueOnCapturedContext: false);
			await SyncModuleStatusesAsync().ConfigureAwait(continueOnCapturedContext: false);
			await _dispatcher.InvokeAsync((Action)delegate
			{
				OnPropertyChanged("AppliedOptimizationCount");
				OnPropertyChanged("AppliedCountDisplay");
				OnPropertyChanged("PendingOptimizationCount");
				OnPropertyChanged("TotalOptimizationCount");
				OnPropertyChanged("OptimizationScore");
			});
			Debug.WriteLine($"[RestoreDefault] 最终统计: 成功={totalSuccess}, 失败={totalFailed}");
			await _dispatcher.InvokeAsync((Action)delegate
			{
				Window mainWindow = Application.Current.MainWindow;
				RevertResultDialog.Show(mainWindow, resultGroups, RestoreOperationType.RestoreWindowsDefaults);
			});
			if (totalFailed > 0 && totalSuccess > 0)
			{
				AppendLog("Log.RestorePartialSuccess", "Log.Category.Warning", new object[2] { totalSuccess, totalFailed });
				ToastService.Instance.Warning(loc.Get("RestoreOptions.RestoreTitle"), loc.Get("RestoreOptions.RestorePartial", totalSuccess, totalFailed));
				ActivityService.Instance.LogRestore("Activity.WindowsDefault", success: false);
			}
			else if (totalFailed > 0)
			{
				AppendLog("Log.RestoreAllFailed", "Log.Category.Error", new object[1] { totalFailed });
				ToastService.Instance.Error(loc.Get("RestoreOptions.RestoreTitle"), loc.Get("RestoreOptions.RestoreFailed", totalFailed));
				ActivityService.Instance.LogRestore("Activity.WindowsDefault", success: false);
			}
			else
			{
				AppendLog("Log.WindowsDefaultRestored", "Log.Category.System", new object[1] { totalSuccess });
				ToastService.Instance.Success(loc.Get("RestoreOptions.RestoreTitle"), loc.Get("RestoreOptions.RestoreSuccess", totalSuccess));
				ActivityService.Instance.LogRestore("Activity.WindowsDefault", success: true);
			}
		}
		catch (Exception ex2)
		{
			Debug.WriteLine("[RestoreDefault] 致命异常: " + ex2.Message + "\n" + ex2.StackTrace);
			await _dispatcher.InvokeAsync((Action)delegate
			{
				progressDialog?.Close();
			});
			AppendLog("Log.WindowsDefaultFailedWithMsg", "Log.Category.Error", new object[1] { ex2.Message });
			ToastService.Instance.Error(loc.Get("RestoreOptions.RestoreTitle"), ex2.Message);
			ActivityService.Instance.LogRestore("Activity.WindowsDefault", success: false);
		}
		finally
		{
			EndModuleOperation();
		}
	}

	private Task RestoreUserBackupAsync()
	{
		CommandResultPresentation presentation = LegacyRestoreEntryPresentation.Unavailable;
		AppendLog("Log.UserBackupUnavailable", "Log.Category.Warning", new object[1] { LegacyRestoreEntryPresentation.RecoveryCenterRoute });
		ToastService.Instance.Info(LocalizationService.Instance.Get("Module.RestoreComplete"), presentation.DisplayText);
		NavigateToPageRequested?.Invoke(LegacyRestoreEntryPresentation.RecoveryCenterRoute);
		return Task.CompletedTask;
	}

	public List<RestoreDetailGroup> GetRestoreDetailGroups(RestoreOptionsDialog.RestoreOptionType optionType)
	{
		List<RestoreDetailGroup> list = new List<RestoreDetailGroup>();
		OptimizationTranslations instance = OptimizationTranslations.Instance;
		foreach (OptimizationModuleDefinition item in OptimizationModules.Where((OptimizationModuleDefinition m) => m.SupportsExecution))
		{
			ObservableCollection<RestoreDetailItem> observableCollection = new ObservableCollection<RestoreDetailItem>();
			int num = 0;
			foreach (OptimizationToggleModel item2 in item.Toggles.Where((OptimizationToggleModel t) => !string.IsNullOrWhiteSpace(t.OperationId)))
			{
				bool isApplied = item2.IsApplied;
				bool willBeRestored = true;
				if (isApplied)
				{
					num++;
				}
				observableCollection.Add(new RestoreDetailItem
				{
					OperationId = item2.OperationId,
					Title = instance.GetTitle(item2.OperationId),
					IsApplied = isApplied,
					WillBeRestored = willBeRestored
				});
			}
			if (observableCollection.Count > 0)
			{
				list.Add(new RestoreDetailGroup
				{
					ModuleKey = item.Key,
					ModuleName = item.Title,
					AppliedCount = num,
					TotalCount = observableCollection.Count,
					Items = observableCollection,
					IsExpanded = (num > 0)
				});
			}
		}
		return list;
	}

	private void AppendLog(string messageKey, string categoryKey = "Log.Category.System", object[]? messageArgs = null)
	{
		if (!_dispatcher.CheckAccess())
		{
			_dispatcher.InvokeAsync((Action)delegate
			{
				AppendLog(messageKey, categoryKey, messageArgs);
			});
			return;
		}
		ActivityRecord activityRecord = new ActivityRecord(DateTime.Now, categoryKey, messageKey, messageArgs);
		_logBuffer.Insert(0, activityRecord.Display);
		ActivityLogs.Insert(0, activityRecord);
		while (ActivityLogs.Count > 100)
		{
			ActivityLogs.RemoveAt(ActivityLogs.Count - 1);
		}
		PersistActivities();
		OnPropertyChanged("QuickActionMeta");
	}

	private void PersistActivities()
	{
		try
		{
			_activityLogStore.Save(ActivityLogs);
		}
		catch
		{
		}
	}

	private void AppendBatchResults(string moduleTitle, OptimizationBatchResult result)
	{
		foreach (OperationExecutionSummary operation in result.Operations)
		{
			string text = (operation.Success ? "Log.StatusSuccess" : "Log.StatusFailed");
			string text2 = (operation.Changed ? "Log.StatusWritten" : "Log.StatusUnchanged");
			AppendLog("Log.PolicyResult", "Log.Category.PolicyExecution", new object[5] { moduleTitle, operation.OperationId, text, text2, operation.Message });
		}
	}

	public void PresentOperationResults(string title, string? subtitle, IEnumerable<OptimizationBatchResult> batches)
	{
		if (batches == null)
		{
			return;
		}
		List<OptimizationBatchResult> materialized = batches.ToList();
		if (materialized.Count == 0)
		{
			return;
		}
		if (_dispatcher.CheckAccess())
		{
			PresentOperationResultsInternal(title, subtitle, materialized);
			return;
		}
		_dispatcher.InvokeAsync((Action)delegate
		{
			PresentOperationResultsInternal(title, subtitle, materialized);
		});
	}

	public OptimizationBatchResult CreateFailureBatch(string moduleKey, IEnumerable<string> operationIds, string message)
	{
		string moduleTitle = ResolveModuleTitle(moduleKey);
		GameOptimizer.Core.Optimization.OperationStatus before = new GameOptimizer.Core.Optimization.OperationStatus(IsCompliant: false, LocalizationService.Instance.Get("Dashboard.Status.NotExecuted"));
		List<string> list = operationIds?.Where((string id) => !string.IsNullOrWhiteSpace(id)).ToList() ?? new List<string>();
		if (list.Count == 0)
		{
			list.Add(moduleKey + ".batch");
		}
		List<OperationExecutionSummary> list2 = new List<OperationExecutionSummary>();
		foreach (string item in list)
		{
			string title = ResolveOperationTitle(item, moduleTitle);
			list2.Add(new OperationExecutionSummary(item, title, Success: false, Changed: false, message, before, new GameOptimizer.Core.Optimization.OperationStatus(IsCompliant: false, message)));
		}
		return new OptimizationBatchResult(moduleKey, list2);
	}

	private void PresentOperationResultsInternal(string title, string? subtitle, IReadOnlyList<OptimizationBatchResult> batches)
	{
		LatestOperationResults.Clear();
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		foreach (OptimizationBatchResult batch in batches)
		{
			string moduleTitle = ResolveModuleTitle(batch.ModuleKey);
			foreach (OperationExecutionSummary operation in batch.Operations)
			{
				if (operation.Success)
				{
					num++;
					if (operation.Changed)
					{
						num3++;
					}
				}
				else
				{
					num2++;
				}
				LatestOperationResults.Add(new OperationResultItem(moduleTitle, operation));
			}
		}
		if (LatestOperationResults.Count != 0)
		{
			ResultDialogTitle = title;
			ResultDialogSubtitle = subtitle ?? $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}";
			ResultDialogSummary = $"成功 {num} 项 · 失败 {num2} 项 · 已更改 {num3} 项";
			ResultDialogHasFailure = num2 > 0;
			IsResultDialogVisible = true;
		}
	}

	private string ResolveModuleTitle(string moduleKey)
	{
		return OptimizationModules.FirstOrDefault((OptimizationModuleDefinition m) => string.Equals(m.Key, moduleKey, StringComparison.OrdinalIgnoreCase))?.Title ?? moduleKey;
	}

	private string ResolveOperationTitle(string operationId, string moduleTitle)
	{
		if (!string.IsNullOrWhiteSpace(operationId) && _operationLookup.TryGetValue(operationId, out OptimizationToggleModel value))
		{
			return value.Title;
		}
		return string.IsNullOrWhiteSpace(operationId) ? moduleTitle : (moduleTitle + " 路 " + operationId);
	}

	private void BuildOptimizationModules()
	{
		if (OptimizationModules.Count > 0)
		{
			return;
		}
		// Dashboard construction runs on the UI thread. Hardware detection performs
		// WMI/device discovery and must never be synchronously waited here. Individual
		// operations retain their authoritative runtime capability checks; showing the
		// complete catalog is the safe, non-blocking fallback for the initial surface.
		HardwareModuleFilter hardwareModuleFilter = HardwareModuleFilter.ShowAll;
		IReadOnlyList<OptimizationOperationManifestEntry> manifest = OptimizationProviderRuntimeManifest.Create(_operationProvider);
		Dictionary<string, OptimizationOperationManifestEntry> manifestByOperationId = manifest.ToDictionary((OptimizationOperationManifestEntry entry) => entry.OperationId, StringComparer.OrdinalIgnoreCase);
		Debug.WriteLine($"[BuildOptimizationModules] Engine modules count: {_optimizationEngine.Modules.Count}");
		foreach (OptimizationModuleDescriptor module in _optimizationEngine.Modules)
		{
			Debug.WriteLine($"[BuildOptimizationModules] Engine module: {module.Key} - {module.Title} ({module.Operations.Count} ops)");
		}
		foreach (ModuleVisual visual in ModuleMetadata)
		{
			if (!hardwareModuleFilter.ShouldShowModule(visual.Key))
			{
				Debug.WriteLine("[BuildOptimizationModules] Module '" + visual.Key + "' filtered out by hardware detection");
				continue;
			}
			OptimizationModuleDescriptor optimizationModuleDescriptor = _optimizationEngine.Modules.FirstOrDefault((OptimizationModuleDescriptor m) => string.Equals(m.Key, visual.Key, StringComparison.OrdinalIgnoreCase));
			if ((object)optimizationModuleDescriptor == null)
			{
				Debug.WriteLine("[BuildOptimizationModules] Module '" + visual.Key + "' not found in engine");
				continue;
			}
			List<OptimizationToggleModel> list = optimizationModuleDescriptor.Operations.Select((IOptimizationOperation op) =>
			{
				OptimizationOperationManifestEntry entry = manifestByOperationId.TryGetValue(op.Id, out OptimizationOperationManifestEntry found) ? found : new OptimizationOperationManifestEntry(visual.Key, op.Id, OptimizationOperationRoute.Unknown, op.GetType().FullName ?? op.GetType().Name);
				return new OptimizationToggleModel(op.Title, op.Description, isEnabled: false, isApplied: false, op.Id, op.RiskLevel, op.Benefit, op.Drawback, op.Category, requiresReboot: false, null, op.RegistryPath, op.RegistryHiveText, op.ImpactWarning ?? GetDefaultImpactWarning(op.RiskLevel, op.Title), op.RegistryValueName, op.OptimizedValue, op.DefaultValue, op.ValueType, op.OperationType, op.ManualPath, op.ManualSteps, op.TargetName, op.TargetValue, op.HardwareCompatibilityHint, null, op.RequiresAdmin, op.RequiresExplicitConfirmation, op.RequiresRestorePoint, op.AffectsUserFeeling, entry.Route, op.RequiresSnapshotForRestore, IsAdministrator);
			}).ToList();
			foreach (OptimizationToggleModel item in list)
			{
				if (!string.IsNullOrWhiteSpace(item.OperationId))
				{
					_operationLookup[item.OperationId] = item;
				}
			}
			OptimizationModuleDefinition optimizationModuleDefinition = new OptimizationModuleDefinition(visual.Key, visual.Title, visual.Subtitle, visual.IconData, list);
			optimizationModuleDefinition.SupportsExecution = true;
			OptimizationModules.Add(optimizationModuleDefinition);
			AttachToggleListeners(optimizationModuleDefinition);
			Debug.WriteLine($"[BuildOptimizationModules] Added module: {visual.Key} with {list.Count} toggles");
		}
		Debug.WriteLine($"[BuildOptimizationModules] Total OptimizationModules: {OptimizationModules.Count}");
		OnPropertyChanged("MaxToggleCount");
	}

	private void BuildNavigationSections()
	{
		if (NavigationSections.Count > 0)
		{
			NavigationSections.Clear();
		}
		LocalizationService instance = LocalizationService.Instance;
		string groupKey = "monitor";
		string groupTitle = instance.Get("NavGroup.Monitor");
		string groupKey2 = "modules";
		string groupTitle2 = instance.Get("NavGroup.Modules");
		NavigationSections.Add(new NavigationSectionModel("overview", instance.Get("Nav.Overview"), instance.Get("Nav.Overview.Subtitle"), "M3,13 H7 L9,9 L11,17 L13,11 L15,13 H21", groupKey, groupTitle));
		NavigationSections.Add(new NavigationSectionModel("memory-optimization", instance.Get("Nav.MemoryOptimization"), instance.Get("Nav.MemoryOptimization.Subtitle"), "M12,3 L20,7 V12 C20,17 16.6,20.8 12,22 C7.4,20.8 4,17 4,12 V7 Z M12,5.4 L6,8.3 V12 C6,15.7 8.6,18.7 12,19.9 C15.4,18.7 18,15.7 18,12 V8.3 Z M11,9 H13 V13 H16 V15 H11 Z", groupKey2, groupTitle2));
		NavigationSections.Add(new NavigationSectionModel("audio-optimization", instance.Get("Nav.AudioOptimization"), instance.Get("Nav.AudioOptimization.Subtitle"), "M5,5 H11 L13,7 H19 C20.1,7 21,7.9 21,9 V19 C21,20.1 20.1,21 19,21 H5 C3.9,21 3,20.1 3,19 V7 C3,5.9 3.9,5 5,5 Z M7,9 V17 H17 V9 Z M9,11 H11 V13 H9 Z M13,11 H15 V13 H13 Z", groupKey2, groupTitle2));
		NavigationSections.Add(new NavigationSectionModel("performance-boost", instance.Get("Nav.PerformanceBoost"), instance.Get("Nav.PerformanceBoost.Subtitle"), "M12,2 C15.2,4.8 17,8.3 17,11.5 C17,12.8 16.8,14 16.3,15.1 L18,18 H14.5 L12,22 L9.5,18 H6 L7.7,15.1 C7.2,14 7,12.8 7,11.5 C7,8.3 8.8,4.8 12,2 Z M12,6.2 A2.2,2.2 0 1,1 12,10.6 A2.2,2.2 0 1,1 12,6.2 Z M7,13.5 L4.8,16.8 L8.2,15.2 Z M17,13.5 L15.8,15.2 L19.2,16.8 Z", groupKey2, groupTitle2));
		NavigationSections.Add(new NavigationSectionModel("cpu-nav", instance.Get("Nav.CpuOptimization"), instance.Get("Nav.CpuOptimization.Subtitle"), "M7,3 H17 C18.1,3 19,3.9 19,5 V7 H21 V9 H19 V15 H21 V17 H19 V19 C19,20.1 18.1,21 17,21 H7 C5.9,21 5,20.1 5,19 V17 H3 V15 H5 V9 H3 V7 H5 V5 C5,3.9 5.9,3 7,3 Z M9,9 V15 H15 V9 Z", groupKey2, groupTitle2));
		NavigationSections.Add(new NavigationSectionModel("graphics-nav", instance.Get("Nav.GraphicsOptimization"), instance.Get("Nav.GraphicsOptimization.Subtitle"), "M4,5 H20 C21.1,5 22,5.9 22,7 V15 C22,16.1 21.1,17 20,17 H14 V19 H16 V21 H8 V19 H10 V17 H4 C2.9,17 2,16.1 2,15 V7 C2,5.9 2.9,5 4,5 Z M6,8 V12 H10 V8 Z M12,8 H18 V12 H12 Z", groupKey2, groupTitle2));
		NavigationSections.Add(new NavigationSectionModel("network-nav", instance.Get("Nav.NetworkOptimization"), instance.Get("Nav.NetworkOptimization.Subtitle"), "M4,17 C6.5,12 17.5,12 20,17 L17.5,18.5 C15.8,15.6 8.2,15.6 6.5,18.5 Z M7,14 C9.2,11 14.8,11 17,14 L14.8,15.4 C13.7,13.9 10.3,13.9 9.2,15.4 Z M11.5,11 C12.3,9.7 13.7,9.7 14.5,11 L12.5,12.5 Z", groupKey2, groupTitle2));
		NavigationSections.Add(new NavigationSectionModel("privacy-nav", instance.Get("Nav.PrivacyShield"), instance.Get("Nav.PrivacyShield.Subtitle"), "M12,3 L20,7 V12 C20,17 16.6,20.8 12,22 C7.4,20.8 4,17 4,12 V7 Z M12,5.4 L6,8.3 V12 C6,15.7 8.6,18.7 12,19.9 C15.4,18.7 18,15.7 18,12 V8.3 Z M11,9 H13 V13 H16 V15 H11 Z", groupKey2, groupTitle2));
		NavigationSections.Add(new NavigationSectionModel("system-nav", instance.Get("Nav.SystemTuning"), instance.Get("Nav.SystemTuning.Subtitle"), "M4,6 H20 V8 H4 Z M4,12 H20 V14 H4 Z M4,18 H20 V20 H4 Z M8,4 V22 H10 V4 Z", groupKey2, groupTitle2));
		NavigationSections.Add(new NavigationSectionModel("explorer-nav", instance.Get("Nav.DesktopExperience"), instance.Get("Nav.DesktopExperience.Subtitle"), "M4,6 H10 L12,8 H20 C21.1,8 22,8.9 22,10 V18 C22,19.1 21.1,20 20,20 H4 C2.9,20 2,19.1 2,18 V8 C2,6.9 2.9,6 4,6 Z M4,10 V18 H20 V10 Z", groupKey2, groupTitle2));
		NavigationSections.Add(new NavigationSectionModel("tasks-nav", instance.Get("Nav.TaskScheduler"), instance.Get("Nav.TaskScheduler.Subtitle"), "M6,6 H18 C19.1,6 20,6.9 20,8 V18 C20,19.1 19.1,20 18,20 H6 C4.9,20 4,19.1 4,18 V8 C4,6.9 4.9,6 6,6 Z M8,9 V17 H16 V9 Z M10,4 H14 V6 H10 Z", groupKey2, groupTitle2));
		NavigationSections.Add(new NavigationSectionModel("services-nav", instance.Get("Nav.SystemServices"), instance.Get("Nav.SystemServices.Subtitle"), "M5,5 H11 L13,7 H19 C20.1,7 21,7.9 21,9 V19 C21,20.1 20.1,21 19,21 H5 C3.9,21 3,20.1 3,19 V7 C3,5.9 3.9,5 5,5 Z M7,9 V17 H17 V9 Z M9,11 H11 V13 H9 Z M13,11 H15 V13 H13 Z", groupKey2, groupTitle2));
		NavigationSections.Add(new NavigationSectionModel("system-cleanup", instance.Get("Nav.SystemCleanup"), instance.Get("Nav.SystemCleanup.Subtitle"), "M5,6 H19 V8 H17.5 L16.5,19 C16.4,20.1 15.5,21 14.4,21 H9.6 C8.5,21 7.6,20.1 7.5,19 L6.5,8 H5 Z M8.5,6 V5 C8.5,3.9 9.4,3 10.5,3 H13.5 C14.6,3 15.5,3.9 15.5,5 V6 Z", groupKey2, groupTitle2));
		NavigationSections.Add(new NavigationSectionModel("peripheral-nav", instance.Get("Nav.PeripheralOptimization"), instance.Get("Nav.PeripheralOptimization.Subtitle"), "M6,7 H18 C19.1,7 20,7.9 20,9 V15 C20,16.1 19.1,17 18,17 H6 C4.9,17 4,16.1 4,15 V9 C4,7.9 4.9,7 6,7 Z M8,10 V14 H12 V10 Z M14,10 H18 V12 H14 Z M9,18 H15 V20 H9 Z", groupKey2, groupTitle2));
		string groupKey3 = "tools";
		string groupTitle3 = instance.Get("NavGroup.Tools");
		NavigationSections.Add(new NavigationSectionModel("hardware-info", instance.Get("Nav.HardwareInfo"), instance.Get("Nav.HardwareInfo.Subtitle"), "M15,9 H9 V15 H15 V9 Z M13,13 H11 V11 H13 V13 Z M21,11 V13 H19 V15 C19,16.1 18.1,17 17,17 H15 V19 H13 V17 H11 V19 H9 V17 H7 C5.9,17 5,16.1 5,15 V13 H3 V11 H5 V9 H3 V7 H5 V5 C5,3.9 5.9,3 7,3 H9 V1 H11 V3 H13 V1 H15 V3 H17 C18.1,3 19,3.9 19,5 V7 H21 V9 H19 V11 H21 Z M17,5 H7 V15 H17 V5 Z", groupKey3, groupTitle3));
		NavigationSections.Add(new NavigationSectionModel("network-test", instance.Get("Nav.NetworkTest"), instance.Get("Nav.NetworkTest.Subtitle"), "M12,2 C6.48,2 2,6.48 2,12 C2,17.52 6.48,22 12,22 C17.52,22 22,17.52 22,12 C22,6.48 17.52,2 12,2 Z M12,20 C7.59,20 4,16.41 4,12 C4,7.59 7.59,4 12,4 C16.41,4 20,7.59 20,12 C20,16.41 16.41,20 12,20 Z M12.5,7 H11 V13 L16.25,16.15 L17,14.92 L12.5,12.25 V7 Z", groupKey3, groupTitle3));
		NavigationSections.Add(new NavigationSectionModel("restore-point", instance.Get("Nav.RestorePoint"), instance.Get("Nav.RestorePoint.Subtitle"), "M6,8 L9,11 L7.5,12.5 L12.5,17.5 L11,19 L6,14 L4.5,15.5 L3,14 L5.5,11.5 Z", groupKey3, groupTitle3));
		NavigationSections.Add(new NavigationSectionModel("repair-center", instance.Get("Nav.RepairCenter"), instance.Get("Nav.RepairCenter.Subtitle"), "M6,8 L9,11 L7.5,12.5 L12.5,17.5 L11,19 L6,14 L4.5,15.5 L3,14 L5.5,11.5 Z", groupKey3, groupTitle3));
		NavigationSections.Add(new NavigationSectionModel("backup-manager", instance.Get("Nav.BackupManager"), instance.Get("Nav.BackupManager.Subtitle"), "M6,8 L9,11 L7.5,12.5 L12.5,17.5 L11,19 L6,14 L4.5,15.5 L3,14 L5.5,11.5 Z", groupKey3, groupTitle3));
		NavigationSections.Add(new NavigationSectionModel("cpu-core-management", instance.Get("Nav.CpuCoreManagement"), instance.Get("Nav.CpuCoreManagement.Subtitle"), "M7,3 H17 C18.1,3 19,3.9 19,5 V7 H21 V9 H19 V15 H21 V17 H19 V19 C19,20.1 18.1,21 17,21 H7 C5.9,21 5,20.1 5,19 V17 H3 V15 H5 V9 H3 V7 H5 V5 C5,3.9 5.9,3 7,3 Z M9,9 V15 H15 V9 Z", groupKey3, groupTitle3));
		NavigationSections.Add(new NavigationSectionModel("power-plan-editor", instance.Get("Nav.PowerOptimization"), instance.Get("Nav.PowerOptimization.Subtitle"), "M11,21 H5 C3.9,21 3,20.1 3,19 V5 C3,3.9 3.9,3 5,3 H11 V1 H13 V3 H19 C20.1,3 21,3.9 21,5 V11 H19 V5 H5 V19 H11 V21 Z M7,7 H9 V9 H7 V7 Z M7,11 H9 V13 H7 V11 Z M7,15 H9 V17 H7 V15 Z M22,13 L18.5,20 H15 L18,15 H14 V13 H22 Z", groupKey3, groupTitle3));
		string groupKey4 = "settings";
		string groupTitle4 = instance.Get("NavGroup.Settings");
		NavigationSections.Add(new NavigationSectionModel("settings", instance.Get("Nav.Settings"), instance.Get("Nav.Settings.Subtitle"), "M19.14,12.94 C19.18,12.64 19.2,12.33 19.2,12 C19.2,11.68 19.18,11.36 19.13,11.06 L21.16,9.48 C21.34,9.34 21.39,9.07 21.28,8.87 L19.36,5.55 C19.24,5.33 18.99,5.26 18.77,5.33 L16.38,6.29 C15.88,5.91 15.35,5.59 14.76,5.35 L14.4,2.81 C14.36,2.57 14.16,2.4 13.92,2.4 L10.08,2.4 C9.84,2.4 9.65,2.57 9.61,2.81 L9.25,5.35 C8.66,5.59 8.12,5.92 7.63,6.29 L5.24,5.33 C5.02,5.25 4.77,5.33 4.65,5.55 L2.74,8.87 C2.62,9.08 2.66,9.34 2.86,9.48 L4.89,11.06 C4.84,11.36 4.8,11.69 4.8,12 C4.8,12.31 4.82,12.64 4.87,12.94 L2.84,14.52 C2.66,14.66 2.61,14.93 2.72,15.13 L4.64,18.45 C4.76,18.67 5.01,18.74 5.23,18.67 L7.62,17.71 C8.12,18.09 8.65,18.41 9.24,18.65 L9.6,21.19 C9.65,21.43 9.84,21.6 10.08,21.6 L13.92,21.6 C14.16,21.6 14.36,21.43 14.39,21.19 L14.75,18.65 C15.34,18.41 15.88,18.09 16.37,17.71 L18.76,18.67 C18.98,18.75 19.23,18.67 19.35,18.45 L21.27,15.13 C21.39,14.91 21.34,14.66 21.15,14.52 L19.14,12.94 Z M12,15.6 C10.02,15.6 8.4,13.98 8.4,12 C8.4,10.02 10.02,8.4 12,8.4 C13.98,8.4 15.6,10.02 15.6,12 C15.6,13.98 13.98,15.6 12,15.6 Z", groupKey4, groupTitle4));
	}

	private void BuildOverviewEntries()
	{
		if (OverviewEntries.Count > 0)
		{
			OverviewEntries.Clear();
		}
		foreach (string moduleKey in OverviewModuleOrder)
		{
			OptimizationModuleDefinition? module = OptimizationModules.FirstOrDefault((OptimizationModuleDefinition m) => string.Equals(m.Key, moduleKey, StringComparison.OrdinalIgnoreCase));
			if (module == null)
			{
				continue;
			}
			int applied = module.Toggles.Count((OptimizationToggleModel t) => t.IsApplied);
			string statusText = $"{applied}/{module.Toggles.Count}";
			OverviewEntries.Add(new OverviewEntry(module.Key, module.Title, module.IconData, statusText));
		}
		OverviewEntries.Add(new OverviewEntry("hardware", LocalizationService.Instance.Get("Nav.HardwareInfo"), IconPath.Hardware, "查看设备与系统信息"));
		OverviewEntries.Add(new OverviewEntry("nettest", LocalizationService.Instance.Get("Nav.NetworkTest"), IconPath.NetworkTest, "网络延迟与稳定性检测"));
		OverviewEntries.Add(new OverviewEntry("restore", LocalizationService.Instance.Get("Nav.RestorePoint"), IconPath.Repair, "系统还原点管理"));
		OverviewEntries.Add(new OverviewEntry("repair", LocalizationService.Instance.Get("Nav.RepairCenter"), IconPath.Repair, "系统修复中心"));
		OverviewEntries.Add(new OverviewEntry("backup", LocalizationService.Instance.Get("Nav.BackupManager"), IconPath.Repair, "统一备份与恢复"));
	}

	private void OnTogglePropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (!(sender is OptimizationToggleModel optimizationToggleModel) || _isBulkUpdating)
		{
			return;
		}
		string propertyName = e.PropertyName;
		if ((propertyName == "IsEnabled" || propertyName == "IsApplied") ? true : false)
		{
			OptimizationModuleDefinition? optimizationModuleDefinition = SelectedOptimizationModule;
			if (optimizationModuleDefinition != null && optimizationModuleDefinition.Toggles.Contains(optimizationToggleModel))
			{
				NotifyPendingToggleChanged();
			}
		}
		if (_toggleModuleIndex.TryGetValue(optimizationToggleModel, out OptimizationModuleDefinition value) && e.PropertyName == "IsEnabled")
		{
			LocalizationService instance = LocalizationService.Instance;
			string value2 = (optimizationToggleModel.IsEnabled ? instance.Get("Log.ActionSelect") : instance.Get("Log.ActionDeselect"));
			string moduleName = OptimizationTranslations.Instance.GetModuleName(value.Title);
			string value3 = OptimizationTranslations.Instance.GetTitle(optimizationToggleModel.OperationId ?? "") ?? optimizationToggleModel.Title;
			AppendLog($"[{moduleName}] {value2} - {value3}");
		}
	}

	private void NotifyPendingToggleChanged()
	{
		OnPropertyChanged("PendingToggleCount");
		OnPropertyChanged("HasPendingToggle");
		OnPropertyChanged("HasModuleSelected");
		OnPropertyChanged("HasAppliedToggles");
		OnPropertyChanged("ModulePendingStatus");
		OnPropertyChanged("AppliedToggleStatus");
		OnPropertyChanged("TotalOptimizationCount");
		OnPropertyChanged("AppliedOptimizationCount");
		OnPropertyChanged("AppliedCountDisplay");
		OnPropertyChanged("PendingOptimizationCount");
		OnPropertyChanged("SelectedModuleAppliedCount");
	}

	private static string? GetDefaultImpactWarning(OperationRiskLevel risk, string title)
	{
		if (risk != OperationRiskLevel.Extreme)
		{
			return null;
		}
		LocalizationService instance = LocalizationService.Instance;
		string text = title.ToLowerInvariant();
		if (text.Contains("防火墙") || text.Contains("firewall"))
		{
			return instance.Get("Dashboard.Warning.Firewall");
		}
		if (text.Contains("安全中心") || text.Contains("security"))
		{
			return instance.Get("Dashboard.Warning.SecurityCenter");
		}
		if (text.Contains("uac"))
		{
			return instance.Get("Dashboard.Warning.UAC");
		}
		if (text.Contains("蓝牙") || text.Contains("bluetooth"))
		{
			return instance.Get("Dashboard.Warning.Bluetooth");
		}
		return instance.Get("Dashboard.Warning.Default");
	}

	private void AttachToggleListeners(OptimizationModuleDefinition module)
	{
		foreach (OptimizationToggleModel toggle in module.Toggles)
		{
			_toggleModuleIndex[toggle] = module;
			toggle.PropertyChanged -= OnTogglePropertyChanged;
			toggle.PropertyChanged += OnTogglePropertyChanged;
		}
	}

	private static ModuleStrategyFilter ResolveFilter(OptimizationToggleModel toggle)
	{
		OperationRiskLevel riskLevel = toggle.RiskLevel;
		ModuleStrategyFilter result = riskLevel switch
		{
			OperationRiskLevel.Extreme => ModuleStrategyFilter.Extreme, 
			OperationRiskLevel.Safe => ModuleStrategyFilter.Safe, 
			_ => ModuleStrategyFilter.Balance, 
		};
		return result;
	}

	private static bool IsExtremeToggle(OptimizationToggleModel toggle)
	{
		return toggle.RiskLevel == OperationRiskLevel.Extreme;
	}

	private static bool IsBalancedToggle(OptimizationToggleModel toggle)
	{
		return toggle.RiskLevel == OperationRiskLevel.Balanced;
	}

	private static bool IsSafeToggle(OptimizationToggleModel toggle)
	{
		return toggle.RiskLevel == OperationRiskLevel.Safe;
	}

	private void InitializeTrendHistory(SystemResourceSnapshot initial)
	{
		_cpuTrendBuffer.Clear();
		_gpuTrendBuffer.Clear();
		OnPropertyChanged(nameof(HasCpuLiveTrend));
		OnPropertyChanged(nameof(HasGpuLiveTrend));
		OnPropertyChanged(nameof(CpuLiveTrendPoints));
		OnPropertyChanged(nameof(GpuLiveTrendPoints));
	}

	private void StartTelemetryPipeline()
	{
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Expected O, but got Unknown
		if (_telemetryTimer == null)
		{
			int num = Math.Max(1, _appSettings.MonitoringIntervalSeconds);
			_telemetryTimer = new DispatcherTimer((DispatcherPriority)4, _dispatcher)
			{
				Interval = TimeSpan.FromSeconds(num)
			};
			_telemetryTimer.Tick += delegate
			{
				CaptureTelemetrySnapshot();
			};
			_telemetryTimer.Start();
			_ = CapturePrimedTelemetrySnapshotAsync();
		}
	}

	private async Task CapturePrimedTelemetrySnapshotAsync()
	{
		// CPU and GPU performance counters need a second read after a real sampling interval.
		// Do not make the first usable value wait for the user's longer periodic interval.
		await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(true);
		CaptureTelemetrySnapshot();
	}

	public void StopTelemetryPipeline()
	{
		DispatcherTimer? telemetryTimer = _telemetryTimer;
		if (telemetryTimer != null)
		{
			telemetryTimer.Stop();
		}
		_telemetryTimer = null;
	}

	public void RestartTelemetryPipeline()
	{
		StopTelemetryPipeline();
		_appSettings = AppSettings.Load();
		StartTelemetryPipeline();
	}

	private void ReadHomeTelemetrySnapshot()
	{
		try
		{
			HomeTelemetrySnapshot telemetry = _homeTelemetrySource.GetCurrentSnapshotWithStatus();
			ApplyHomeTelemetrySnapshot(telemetry);
			UpdateLiveTrendSamples(telemetry);
			OptimizationScore = ComputeOptimizationScore(telemetry);
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[DashboardViewModel][ReadHomeTelemetrySnapshot] " + ex.Message);
		}
	}

	private void ApplyHomeTelemetrySnapshot(HomeTelemetrySnapshot telemetry)
	{
		_cpuTelemetryStatus = telemetry.Cpu;
		_gpuTelemetryStatus = telemetry.Gpu;
		_gpuLoadAttribution = telemetry.GpuLoad;
		_memoryTelemetryStatus = telemetry.Memory;
		_diskTelemetryStatus = telemetry.Disk;
		Snapshot = telemetry.Snapshot;
		OnPropertyChanged(nameof(HasRealCpuSample));
		OnPropertyChanged(nameof(HasRealGpuSample));
		OnPropertyChanged(nameof(HasRealMemorySample));
		OnPropertyChanged(nameof(HasRealDiskSample));
		OnPropertyChanged(nameof(CpuUsageDisplay));
		OnPropertyChanged(nameof(GpuUsageDisplay));
		OnPropertyChanged(nameof(CpuUsagePercent));
		OnPropertyChanged(nameof(GpuUsagePercent));
		OnPropertyChanged(nameof(CpuUsageBrush));
		OnPropertyChanged(nameof(GpuUsageBrush));
		OnPropertyChanged(nameof(GpuLoadAdapterDisplay));
		OnPropertyChanged(nameof(MemoryUsageBrush));
		OnPropertyChanged(nameof(DiskUsageBrush));
	}

	private static Brush CreateUsageBrush(string color)
	{
		var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
		brush.Freeze();
		return brush;
	}

	private static Brush GetUsageBrush(bool hasLiveSample, double usagePercent, Brush normalBrush)
	{
		if (!hasLiveSample)
		{
			return Brushes.Transparent;
		}

		return usagePercent >= 90.0
			? UsageCriticalBrush
			: usagePercent >= 75.0
				? UsageWarningBrush
				: normalBrush;
	}

	private static int ComputeOptimizationScore(HomeTelemetrySnapshot telemetry)
	{
		double cpu = telemetry.HasRealCpuSample ? telemetry.Snapshot.CpuUsage : 0.0;
		double gpu = telemetry.HasRealGpuSample ? telemetry.Snapshot.GpuUsage : 0.0;
		double memory = telemetry.HasRealMemorySample ? telemetry.Snapshot.MemoryUsageGb : 0.0;
		double disk = telemetry.HasRealDiskSample ? telemetry.Snapshot.DiskUsedGb : 0.0;
		double loadPenalty = Math.Min(20.0, cpu * 0.1 + gpu * 0.1);
		double memoryPenalty = Math.Min(10.0, memory * 0.5);
		double diskPenalty = Math.Min(10.0, disk * 0.05);
		return Math.Clamp((int)Math.Round(95.0 - loadPenalty - memoryPenalty - diskPenalty), 60, 95);
	}

	private async void CaptureTelemetrySnapshot()
	{
		if (_isTelemetryCapturing)
		{
			return;
		}
		_isTelemetryCapturing = true;
		try
		{
			HomeTelemetrySnapshot latest = await Task.Run(() => _homeTelemetrySource.GetCurrentSnapshotWithStatus()).ConfigureAwait(continueOnCapturedContext: false);
			_dispatcher.InvokeAsync((Action)delegate
			{
				ApplyHomeTelemetrySnapshot(latest);
				UpdateLiveTrendSamples(latest);
			});
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Debug.WriteLine("[DashboardViewModel][CaptureTelemetrySnapshot] " + ex2.Message);
		}
		finally
		{
			_isTelemetryCapturing = false;
		}
	}

	private async Task SyncModuleStatusesAsync()
	{
		if (!(await _syncStatusLock.WaitAsync(TimeSpan.FromSeconds(5.0)).ConfigureAwait(continueOnCapturedContext: false)))
		{
			Debug.WriteLine("[DashboardViewModel] SyncModuleStatusesAsync skipped - lock timeout");
			return;
		}
		try
		{
			int beforeTotalApplied = AppliedOptimizationCount;
			Debug.WriteLine($"[OPT-019 SyncAll] 同步前 AppliedOptimizationCount={beforeTotalApplied}");
			List<OptimizationModuleDefinition> executableModules = OptimizationModules.Where((OptimizationModuleDefinition m) => m.SupportsExecution).ToList();
			IEnumerable<Task> tasks = executableModules.Select((OptimizationModuleDefinition m) => SyncModuleStatusAsyncCore(m));
			await Task.WhenAll(tasks).ConfigureAwait(continueOnCapturedContext: false);
		}
		finally
		{
			_syncStatusLock.Release();
		}
		_dispatcher.InvokeAsync((Action)delegate
		{
			OnPropertyChanged("OptimizationModules");
			OnPropertyChanged("TotalOptimizationCount");
			OnPropertyChanged("AppliedOptimizationCount");
			OnPropertyChanged("AppliedCountDisplay");
			OnPropertyChanged("PendingOptimizationCount");
			OnPropertyChanged("CurrentModeOptimizationCount");
			int appliedOptimizationCount = AppliedOptimizationCount;
			Debug.WriteLine($"[OPT-019 SyncAll] 同步后 AppliedOptimizationCount={appliedOptimizationCount}");
		});
	}

	private async Task SyncModuleStatusAsync(OptimizationModuleDefinition module)
	{
		if (!(await _syncStatusLock.WaitAsync(TimeSpan.FromSeconds(5.0)).ConfigureAwait(continueOnCapturedContext: false)))
		{
			Debug.WriteLine("[DashboardViewModel] SyncModuleStatusAsync skipped - lock timeout");
			return;
		}
		try
		{
			await SyncModuleStatusAsyncCore(module).ConfigureAwait(continueOnCapturedContext: false);
		}
		finally
		{
			_syncStatusLock.Release();
		}
	}

	private async Task SyncModuleStatusAsyncCore(OptimizationModuleDefinition module)
	{
		if (!module.SupportsExecution)
		{
			return;
		}
		List<GameOptimizer.Core.Optimization.OperationStatus> statuses = (await _optimizationEngine.GetStatusesAsync(module.Key).ConfigureAwait(continueOnCapturedContext: false)).ToList();
		_dispatcher.InvokeAsync((Action)delegate
		{
			int num = module.Toggles.Count((OptimizationToggleModel t) => t.IsApplied);
			_isBulkUpdating = true;
			try
			{
				for (int num2 = 0; num2 < module.Toggles.Count && num2 < statuses.Count; num2++)
				{
					module.Toggles[num2].SyncOperationContract(statuses[num2], IsAdministrator);
				}
			}
			finally
			{
				_isBulkUpdating = false;
				NotifyPendingToggleChanged();
			}
			int num4 = module.Toggles.Count((OptimizationToggleModel t) => t.IsApplied);
			if (num != num4)
			{
				Debug.WriteLine($"[SyncCore] {module.Title}: {num} -> {num4} applied");
			}
		});
	}

	public bool TryGetLatestOperationEvidence(string? operationId, out OperationExecutionEvidence? evidence)
	{
		evidence = null;
		if (string.IsNullOrWhiteSpace(operationId))
		{
			return false;
		}
		OperationResultItem operationResultItem = LatestOperationResults.FirstOrDefault((OperationResultItem item) => string.Equals(item.OperationId, operationId, StringComparison.OrdinalIgnoreCase));
		if (operationResultItem == null)
		{
			return false;
		}
		string status = (operationResultItem.IsSkipped ? "Skipped" : ((!operationResultItem.IsSuccess) ? "Failed" : (operationResultItem.HasChanged ? "Success" : "NoChange")));
		string errorMessage = (operationResultItem.IsSuccess ? string.Empty : operationResultItem.Message);
		string sourceHint = (string.IsNullOrWhiteSpace(ResultDialogSubtitle) ? ("Module: " + operationResultItem.ModuleTitle) : ("Module: " + operationResultItem.ModuleTitle + " | Batch: " + ResultDialogSubtitle));
		evidence = new OperationExecutionEvidence(status, operationResultItem.Message, operationResultItem.BeforeDetails, operationResultItem.AfterDetails, errorMessage, operationResultItem.RequiresReboot, sourceHint);
		return true;
	}

	public async Task<SingleOperationRollbackResult> TryRollbackSingleOperationAsync(string? operationId)
	{
		if (string.IsNullOrWhiteSpace(operationId))
		{
			return new SingleOperationRollbackResult(Success: false, "OperationId is empty.");
		}
		if (!_operationLookup.TryGetValue(operationId, out OptimizationToggleModel toggle))
		{
			return new SingleOperationRollbackResult(Success: false, LocalizationService.Instance.Get("Dashboard.RollbackOperationNotFound"));
		}
		if (!_toggleModuleIndex.TryGetValue(toggle, out OptimizationModuleDefinition module) || module == null || !module.SupportsExecution)
		{
			return new SingleOperationRollbackResult(Success: false, LocalizationService.Instance.Get("Dashboard.RollbackModuleUnsupported"));
		}
		if (string.IsNullOrWhiteSpace(toggle.OperationId))
		{
			return new SingleOperationRollbackResult(Success: false, LocalizationService.Instance.Get("Dashboard.RollbackOperationIdEmpty"));
		}
		try
		{
			OptimizationBatchResult result = await _optimizationEngine.RestoreAsync(module.Key, new List<string> { toggle.OperationId }).ConfigureAwait(continueOnCapturedContext: false);
			AppendBatchResults(module.Title, result);
			PresentOperationResults("[" + module.Title + "] Single Rollback", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {toggle.Title}", new OptimizationBatchResult[1] { result });
			await SyncModuleStatusAsync(module).ConfigureAwait(continueOnCapturedContext: false);
			OperationExecutionSummary summary = result.Operations.FirstOrDefault((OperationExecutionSummary op) => string.Equals(op.OperationId, toggle.OperationId, StringComparison.OrdinalIgnoreCase));
			if ((object)summary == null)
			{
				return new SingleOperationRollbackResult(Success: false, LocalizationService.Instance.Get("Dashboard.RollbackResultMissing"));
			}
			if (!summary.Success)
			{
				return new SingleOperationRollbackResult(Success: false, string.IsNullOrWhiteSpace(summary.Message) ? LocalizationService.Instance.Get("Dashboard.RollbackFailed") : summary.Message);
			}
			return new SingleOperationRollbackResult(Success: true, string.IsNullOrWhiteSpace(summary.Message) ? LocalizationService.Instance.Get("Dashboard.RollbackCompleted") : summary.Message);
		}
		catch (Exception ex)
		{
			return new SingleOperationRollbackResult(Success: false, ex.Message);
		}
	}

	private void UpdateLiveTrendSamples(HomeTelemetrySnapshot telemetry)
	{
		_cpuTrendBuffer.TryAdd(telemetry.Snapshot.CpuUsage, telemetry.Cpu);
		_gpuTrendBuffer.TryAdd(telemetry.Snapshot.GpuUsage, telemetry.Gpu);
		OnPropertyChanged(nameof(HasCpuLiveTrend));
		OnPropertyChanged(nameof(HasGpuLiveTrend));
		OnPropertyChanged(nameof(CpuLiveTrendPoints));
		OnPropertyChanged(nameof(GpuLiveTrendPoints));
	}

	private static string BuildLiveTrendShape(IReadOnlyList<double> samples)
	{
		if (samples.Count < 2)
			return string.Empty;

		const double width = 76.0;
		const double height = 28.0;
		var step = width / (samples.Count - 1);
		var points = new StringBuilder();
		for (var index = 0; index < samples.Count; index++)
		{
			var x = index * step;
			var y = height - Math.Clamp(samples[index], 0, 100) / 100.0 * height;
			if (index > 0) points.Append(' ');
			points.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0:F1},{1:F1}", x, y);
		}
		return points.ToString();
	}

	public bool TryBuildOperationDetailCard(string operationId, out OperationDetailCardModel? card)
	{
		card = null;
		if (string.IsNullOrWhiteSpace(operationId))
		{
			return false;
		}
		if (!_operationLookup.TryGetValue(operationId, out OptimizationToggleModel value))
		{
			return false;
		}
		_toggleModuleIndex.TryGetValue(value, out OptimizationModuleDefinition value2);
		try
		{
			card = OperationDetailCardFactory.Create(value, value2?.Title);
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[OperationDetailCard] Factory fallback for " + operationId + ": " + ex.Message);
			card = BuildFallbackOperationDetailCard(value, value2?.Title);
		}
		return (object)card != null;
	}

	private static OperationDetailCardModel BuildFallbackOperationDetailCard(OptimizationToggleModel toggle, string? moduleTitle)
	{
		string text = (string.IsNullOrWhiteSpace(toggle.RegistryPath) ? null : (CoalesceText(toggle.RegistryHiveText, "HKLM") + "\\" + toggle.RegistryPath));
		string applyCommand = BuildFallbackApplyCommand(toggle, text);
		string text2 = BuildFallbackVerifyCommand(toggle, text);
		string text3 = BuildFallbackRollbackCommand(toggle, text);
		List<OperationDetailDisclosureField> list = new List<OperationDetailDisclosureField>
		{
			new OperationDetailDisclosureField("OperationId", CoalesceText(toggle.OperationId, "(none)")),
			new OperationDetailDisclosureField("Module", CoalesceText(moduleTitle, "(unknown)")),
			new OperationDetailDisclosureField("Type", toggle.OperationType.ToString()),
			new OperationDetailDisclosureField("Risk", CoalesceText(toggle.RiskLevelText, "未标注")),
			new OperationDetailDisclosureField("RequiresReboot", toggle.RequiresReboot ? "Yes" : "No")
		};
		if (!string.IsNullOrWhiteSpace(toggle.RegistryValueName))
		{
			list.Add(new OperationDetailDisclosureField("RegistryValueName", toggle.RegistryValueName));
		}
		if (!string.IsNullOrWhiteSpace(toggle.ValueType))
		{
			list.Add(new OperationDetailDisclosureField("ValueType", toggle.ValueType));
		}
		if (!string.IsNullOrWhiteSpace(toggle.CompatibilityVariantLabel))
		{
			list.Add(new OperationDetailDisclosureField("CompatibilityVariant", toggle.CompatibilityVariantLabel));
		}
		if (!string.IsNullOrWhiteSpace(toggle.CompatibilityMethod))
		{
			list.Add(new OperationDetailDisclosureField("CompatibilityMethod", toggle.CompatibilityMethod));
		}
		if (!string.IsNullOrWhiteSpace(toggle.CompatibilityInformation))
		{
			list.Add(new OperationDetailDisclosureField("CompatibilityInformation", toggle.CompatibilityInformation));
		}
		string text4 = $"当前值: {CoalesceText(toggle.ValuePreview ?? toggle.DefaultValue, "(未知)")}{Environment.NewLine}目标值: {CoalesceText(toggle.TargetValue ?? toggle.OptimizedValue, "(未知)")}{Environment.NewLine}默认值: {CoalesceText(toggle.DefaultValue, "(未知)")}";
		string text5 = $"收益: {CoalesceText(toggle.Benefit, "未提供")}{Environment.NewLine}代价: {CoalesceText(toggle.Drawback, "未提供")}{Environment.NewLine}风险提示: {CoalesceText(toggle.ImpactWarning, "未提供")}";
		string rollbackAndVerify = $"校验命令: {CoalesceText(text2, "未提供")}{Environment.NewLine}回滚命令: {CoalesceText(text3, "未提供")}";
		return new OperationDetailCardModel(CoalesceText(toggle.Title, "Unnamed optimization"), CoalesceText(toggle.Description, "No description available."), toggle.OperationType.ToString(), CoalesceText(toggle.RiskLevelText, "Unknown"), "L1", "Core detail fields are available.", "Advanced disclosure fields will be completed in a later batch.", toggle.RequiresReboot, toggle.IsHardwareIncompatible, toggle.HardwareIncompatibleHint, "Module: " + CoalesceText(moduleTitle, "Uncategorized") + " / Operation: " + CoalesceText(toggle.OperationId, "unknown"), CoalesceText(text ?? toggle.ManualPath, "No change path provided."), text4, text5, rollbackAndVerify, toggle.ManualPath, text, applyCommand, text2, text2, text3, text3, "If rollback fails, restore a system restore point or application backup first.", CoalesceText(toggle.ManualSteps, "Run the commands step by step and verify the result."), text4 + Environment.NewLine + Environment.NewLine + text5, RequiresAdmin: true, RequiresExplicitConfirmation: false, RequiresRestorePoint: false, AffectsUserFeeling: false, list);
	}

	private static string CoalesceText(string? value, string fallback)
	{
		return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
	}

	private static string? BuildFallbackApplyCommand(OptimizationToggleModel toggle, string? registryFullPath)
	{
		if (string.IsNullOrWhiteSpace(registryFullPath) || string.IsNullOrWhiteSpace(toggle.RegistryValueName))
		{
			return null;
		}
		string value = CoalesceText(toggle.ValueType, "REG_SZ");
		string value2 = CoalesceText(toggle.TargetValue ?? toggle.OptimizedValue, string.Empty);
		return $"reg add \"{registryFullPath}\" /v \"{toggle.RegistryValueName}\" /t {value} /d \"{value2}\" /f";
	}

	private static string? BuildFallbackVerifyCommand(OptimizationToggleModel toggle, string? registryFullPath)
	{
		if (string.IsNullOrWhiteSpace(registryFullPath))
		{
			return null;
		}
		if (string.IsNullOrWhiteSpace(toggle.RegistryValueName))
		{
			return "reg query \"" + registryFullPath + "\"";
		}
		return $"reg query \"{registryFullPath}\" /v \"{toggle.RegistryValueName}\"";
	}

	private static string? BuildFallbackRollbackCommand(OptimizationToggleModel toggle, string? registryFullPath)
	{
		if (string.IsNullOrWhiteSpace(registryFullPath) || string.IsNullOrWhiteSpace(toggle.RegistryValueName))
		{
			return null;
		}
		string value = CoalesceText(toggle.ValueType, "REG_SZ");
		string value2 = CoalesceText(toggle.DefaultValue, string.Empty);
		return $"reg add \"{registryFullPath}\" /v \"{toggle.RegistryValueName}\" /t {value} /d \"{value2}\" /f";
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			_diagnosticLogService.EntryPublished -= OnDiagnosticLogPublished;
			LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
			if (_privilegeNotifier != null)
			{
				_privilegeNotifier.PropertyChanged -= OnPrivilegePropertyChanged;
				_privilegeNotifier = null;
			}
			DispatcherTimer? telemetryTimer = _telemetryTimer;
			if (telemetryTimer != null)
			{
				telemetryTimer.Stop();
			}
			_eventSubscriptionCts?.Cancel();
			_eventSubscriptionCts?.Dispose();
			_syncStatusLock.Dispose();
		}
	}

	private void SubscribeToApplicationEvents()
	{
		_eventSubscriptionCts = new CancellationTokenSource();
		CancellationToken ct = _eventSubscriptionCts.Token;
		Task.Run(async delegate
		{
			try
			{
				await foreach (SnapshotRestoredEvent evt in _eventBus.SubscribeAsync<SnapshotRestoredEvent>(ct))
				{
					Debug.WriteLine("[DashboardVM] Received SnapshotRestoredEvent: " + evt.SnapshotId);
					_dispatcher.InvokeAsync((Action)delegate
					{
						AppendLog("Log.SnapshotRestored", "Log.Category.System", new object[1] { evt.RestoredItemCount });
					});
					await SyncModuleStatusesAsync().ConfigureAwait(continueOnCapturedContext: false);
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex2)
			{
				Exception ex3 = ex2;
				Debug.WriteLine("[DashboardVM] SnapshotRestoredEvent subscription failed: " + ex3.Message);
			}
		}, ct);
		Task.Run(async delegate
		{
			try
			{
				await foreach (RepairCompletedEvent evt in _eventBus.SubscribeAsync<RepairCompletedEvent>(ct))
				{
					if (evt.MayAffectOptimization)
					{
						Debug.WriteLine("[DashboardVM] Received RepairCompletedEvent: " + evt.RepairItemId);
						_dispatcher.InvokeAsync((Action)delegate
						{
							AppendLog("Log.RepairMayAffectOptimization", "Log.Category.System", new object[1] { evt.RepairItemId });
						});
						await SyncModuleStatusesAsync().ConfigureAwait(continueOnCapturedContext: false);
					}
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex2)
			{
				Exception ex3 = ex2;
				Debug.WriteLine("[DashboardVM] RepairCompletedEvent subscription failed: " + ex3.Message);
			}
		}, ct);
		Task.Run(async delegate
		{
			try
			{
				await foreach (CleanupCompletedEvent evt in _eventBus.SubscribeAsync<CleanupCompletedEvent>(ct))
				{
					Debug.WriteLine($"[DashboardVM] Received CleanupCompletedEvent: {evt.BytesCleaned} bytes");
					await _dispatcher.InvokeAsync((Action)delegate
					{
						RefreshDiskUsage();
					});
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex2)
			{
				Exception ex3 = ex2;
				Debug.WriteLine("[DashboardVM] CleanupCompletedEvent subscription error: " + ex3.Message);
			}
		}, ct);
	}

	private void RefreshDiskUsage()
	{
		try
		{
			DriveInfo driveInfo = DriveInfo.GetDrives().FirstOrDefault((DriveInfo d) => d.IsReady && d.RootDirectory.FullName.Equals(Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase));
			if (driveInfo != null)
			{
				double diskUsedGb = (double)(driveInfo.TotalSize - driveInfo.AvailableFreeSpace) / 1073741824.0;
				double diskTotalGb = (double)driveInfo.TotalSize / 1073741824.0;
				Snapshot = Snapshot with
				{
					DiskUsedGb = diskUsedGb,
					DiskTotalGb = diskTotalGb
				};
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[DashboardVM] RefreshDiskUsage failed: " + ex.Message);
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSnapshotChanged(SystemResourceSnapshot value)
	{
		OnPropertyChanged("HasThermalAlert");
		OnPropertyChanged("HasNetworkAlert");
		OnPropertyChanged("HasDpcAlert");
		OnPropertyChanged("CpuTemperatureDisplay");
		OnPropertyChanged("GpuTemperatureDisplay");
		OnPropertyChanged("CpuStatusLabel");
		OnPropertyChanged("GpuStatusLabel");
		OnPropertyChanged(nameof(GpuLoadAdapterDisplay));
		OnPropertyChanged("DpcStatusLabel");
		OnPropertyChanged("MemorySummary");
		OnPropertyChanged("MemoryUsagePercent");
		OnPropertyChanged("MemoryUsagePercentDisplay");
		OnPropertyChanged("MemoryAvailableDisplay");
		OnPropertyChanged("NetworkSummary");
		OnPropertyChanged("DpcLatencySummary");
		OnPropertyChanged("DiskSummary");
		OnPropertyChanged("DiskUsagePercent");
		OnPropertyChanged("DiskUsagePercentDisplay");
		OnPropertyChanged("DiskAvailableDisplay");
		OnPropertyChanged(nameof(CpuUsageBrush));
		OnPropertyChanged(nameof(GpuUsageBrush));
		OnPropertyChanged(nameof(MemoryUsageBrush));
		OnPropertyChanged(nameof(DiskUsageBrush));
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSelectedNavigationSectionChanged(NavigationSectionModel? value)
	{
		PageDiagnosticService.Log("Navigation", $"OnSelectedNavigationSectionChanged called, _isSynchronizingNavigation: {_isSynchronizingNavigation}");
		if (_isSynchronizingNavigation)
		{
			return;
		}
		PageDiagnosticService.Log("Navigation", "Selected: " + value?.Key + ", TargetModuleKey: " + value?.TargetModuleKey);
		PageDiagnosticService.Log("Navigation", $"OptimizationModules count: {OptimizationModules.Count}");
		string moduleKey = value?.TargetModuleKey;
		if (moduleKey == null || moduleKey.Length <= 0)
		{
			try
			{
				_isSynchronizingNavigation = true;
				SelectedOptimizationModule = null;
				PageDiagnosticService.Log("Navigation", "No TargetModuleKey, clearing SelectedOptimizationModule");
				return;
			}
			finally
			{
				_isSynchronizingNavigation = false;
			}
		}
		OptimizationModuleDefinition optimizationModuleDefinition = OptimizationModules.FirstOrDefault((OptimizationModuleDefinition module) => module.Key == moduleKey);
		PageDiagnosticService.Log("Navigation", $"Looking for module '{moduleKey}', found: {optimizationModuleDefinition != null}");
		if (optimizationModuleDefinition != null)
		{
			PageDiagnosticService.Log("Navigation", $"Module '{moduleKey}' has {optimizationModuleDefinition.Toggles.Count} toggles");
		}
		else
		{
			PageDiagnosticService.Log("Navigation", "Module NOT FOUND! Available: " + string.Join(", ", OptimizationModules.Select((OptimizationModuleDefinition m) => m.Key)));
		}
		if (optimizationModuleDefinition == null || optimizationModuleDefinition == SelectedOptimizationModule)
		{
			PageDiagnosticService.Log("Navigation", "Target is null or same as current, skipping");
			return;
		}
		try
		{
			_isSynchronizingNavigation = true;
			SelectedOptimizationModule = optimizationModuleDefinition;
			PageDiagnosticService.Log("Navigation", "Set SelectedOptimizationModule to '" + optimizationModuleDefinition.Key + "'");
			if (!optimizationModuleDefinition.SupportsExecution)
			{
				return;
			}
			SyncModuleStatusAsync(optimizationModuleDefinition).ContinueWith(delegate(Task t)
			{
				if (t.IsFaulted && t.Exception != null)
				{
					Debug.WriteLine("[DashboardViewModel] SyncModuleStatusAsync failed: " + t.Exception.InnerException?.Message);
				}
			}, TaskContinuationOptions.OnlyOnFaulted);
		}
		finally
		{
			_isSynchronizingNavigation = false;
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSelectedOptimizationModuleChanged(OptimizationModuleDefinition? value)
	{
		OnPropertyChanged("HasModuleSelected");
		NotifyPendingToggleChanged();
		OnPropertyChanged("FilteredStrategies");
		if (!string.IsNullOrWhiteSpace(ModuleSearchQuery))
		{
			ModuleSearchQuery = string.Empty;
		}
		if (_isSynchronizingNavigation || value == null)
		{
			return;
		}
		NavigationSectionModel navigationSectionModel = NavigationSections.FirstOrDefault((NavigationSectionModel section) => section.TargetModuleKey == value.Key);
		if (navigationSectionModel == null || navigationSectionModel == SelectedNavigationSection)
		{
			return;
		}
		try
		{
			_isSynchronizingNavigation = true;
			SelectedNavigationSection = navigationSectionModel;
		}
		finally
		{
			_isSynchronizingNavigation = false;
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnIsAdministratorChanged(bool value)
	{
		OnPropertyChanged("PrivilegeHint");
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSelectedQuickActionChanged(QuickActionTab value)
	{
		OnPropertyChanged("QuickActionTitle");
		OnPropertyChanged("QuickActionDescription");
		OnPropertyChanged("QuickActionMeta");
		OnPropertyChanged("QuickActionButtonLabel");
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnModuleStrategyFilterChanged(ModuleStrategyFilter value)
	{
		OnPropertyChanged("FilteredStrategies");
		if (SelectedOptimizationModule == null)
		{
			return;
		}
		_isBulkUpdating = true;
		try
		{
			LocalizationService instance = LocalizationService.Instance;
			string moduleName = OptimizationTranslations.Instance.GetModuleName(SelectedOptimizationModule.Title);
			switch (value)
			{
			case ModuleStrategyFilter.Extreme:
				foreach (OptimizationToggleModel toggle in SelectedOptimizationModule.Toggles)
				{
					toggle.IsEnabled = true;
				}
				AppendLog(instance.Get("Log.PerformanceMode", moduleName, SelectedOptimizationModule.Toggles.Count));
				break;
			case ModuleStrategyFilter.Balance:
			{
				foreach (OptimizationToggleModel toggle2 in SelectedOptimizationModule.Toggles)
				{
					ModuleStrategyFilter moduleStrategyFilter2 = ResolveFilter(toggle2);
					toggle2.IsEnabled = moduleStrategyFilter2 == ModuleStrategyFilter.Balance || moduleStrategyFilter2 == ModuleStrategyFilter.Safe;
				}
				int num2 = SelectedOptimizationModule.Toggles.Count((OptimizationToggleModel t) => t.IsEnabled);
				AppendLog(instance.Get("Log.BalanceMode", moduleName, num2));
				break;
			}
			case ModuleStrategyFilter.Safe:
			{
				foreach (OptimizationToggleModel toggle3 in SelectedOptimizationModule.Toggles)
				{
					ModuleStrategyFilter moduleStrategyFilter = ResolveFilter(toggle3);
					toggle3.IsEnabled = moduleStrategyFilter == ModuleStrategyFilter.Safe;
				}
				int num = SelectedOptimizationModule.Toggles.Count((OptimizationToggleModel t) => t.IsEnabled);
				AppendLog(instance.Get("Log.SafeMode", moduleName, num));
				break;
			}
			case ModuleStrategyFilter.All:
				AppendLog(instance.Get("Log.AllMode", moduleName));
				break;
			}
		}
		finally
		{
			_isBulkUpdating = false;
			NotifyPendingToggleChanged();
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnModuleSearchQueryChanged(string value)
	{
		OnPropertyChanged("FilteredStrategies");
		OnPropertyChanged("HasModuleSearch");
	}
}



