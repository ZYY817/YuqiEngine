// File: GameOptimizer.Tests/Shell/MasterOperationCapabilityContractTests.cs
using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Optimization.Operations;
using GameOptimizer.Core.Optimization.Providers;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Models;

namespace GameOptimizer.Tests.Shell;

public sealed class MasterOperationCapabilityContractTests
{
    private static readonly string[] MasterModuleKeys =
    [
        "memory", "audio", "performance", "cpu", "graphics", "network",
        "privacy", "system", "explorer", "tasks", "services", "peripheral"
    ];

    [Fact]
    public void TypedOwner_WithKnownReadback_AllowsApplyAndCreatesPendingIntent()
    {
        var model = CreateTyped(requiresAdmin: false);

        model.SyncOperationContract(Status(isApplied: false, "typed readback"), isAdministrator: false);
        model.OnUserToggled(enabled: true);

        Assert.True(model.CanApply);
        Assert.False(model.CanRestore);
        Assert.True(model.IsPending);
        Assert.True(model.IsEnabled);
        Assert.Null(model.BlockedReason);
    }

    [Theory]
    [InlineData("PolicyDenied: operation is prohibited by policy.")]
    [InlineData("Managed owner is unavailable; fail-closed.")]
    public void ManagedFailClosed_DisablesBothDirectionsAndShowsProviderReason(string reason)
    {
        var model = CreateModel(OptimizationOperationRoute.ManagedFailClosed);

        model.SyncOperationContract(Status(isApplied: false, reason), isAdministrator: true);
        model.OnUserToggled(enabled: true);

        Assert.False(model.CanApply);
        Assert.False(model.CanRestore);
        Assert.False(model.IsEnabled);
        Assert.False(model.IsPending);
        Assert.Equal(reason, model.BlockedReason);
    }

    [Fact]
    public void TypedOwner_WithoutRequiredPermission_IsFailClosedVisible()
    {
        var model = CreateTyped(requiresAdmin: true);

        model.SyncOperationContract(Status(isApplied: false, "not applied"), isAdministrator: false);

        Assert.False(model.CanApply);
        Assert.False(model.CanRestore);
        Assert.Contains("管理员权限", model.BlockedReason, StringComparison.Ordinal);
    }

    [Fact]
    public void DeniedUserToggle_DoesNotLeaveAVisualIntentBehind()
    {
        var model = CreateModel(OptimizationOperationRoute.ManagedFailClosed);
        model.SyncOperationContract(Status(isApplied: false, "PolicyDenied: operation is prohibited by policy."), isAdministrator: true);

        var accepted = model.OnUserToggled(enabled: true);

        Assert.False(accepted);
        Assert.False(model.IsEnabled);
        Assert.False(model.IsPending);
        Assert.False(model.CanToggle);
    }

    [Fact]
    public void ToggleDuringExecution_IsRejectedAndRollsBackToObservedState()
    {
        var model = CreateTyped(requiresAdmin: false);
        model.SyncOperationContract(Status(isApplied: false, "not applied"), isAdministrator: true);
        Assert.True(model.OnUserToggled(enabled: true));
        model.OnStartApply();

        var accepted = model.OnUserToggled(enabled: false);

        Assert.False(accepted);
        Assert.True(model.IsEnabled);
        Assert.True(model.IsPending);
        Assert.Equal(OptimizationState.Applying, model.CurrentState);
        Assert.False(model.CanToggle);
    }

    [Fact]
    public void UnknownOwner_IsDisabledEvenWhenReadbackReturnsAValue()
    {
        var model = CreateModel(OptimizationOperationRoute.Unknown);

        model.SyncOperationContract(Status(isApplied: false, "legacy status"), isAdministrator: true);

        Assert.False(model.CanToggle);
        Assert.Contains("typed Owner", model.BlockedReason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Status check timed out")]
    [InlineData("Status check failed: UnauthorizedAccessException")]
    public void EngineReadbackFailure_DisablesOperationWithoutTreatingFalseAsNotApplied(string details)
    {
        var model = CreateTyped(requiresAdmin: false);

        model.SyncOperationContract(Status(isApplied: false, details), isAdministrator: true);

        Assert.False(model.IsReadbackKnown);
        Assert.False(model.CanApply);
        Assert.False(model.CanRestore);
        Assert.Contains("回读不可用", model.BlockedReason, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadbackUnavailable_MapsToExplicitRetryableCapabilityState()
    {
        var model = CreateTyped(requiresAdmin: false);

        model.SyncOperationContract(Status(isApplied: false, "Status check timed out"), isAdministrator: true);

        Assert.Equal(ModuleCapabilityState.ReadbackUnavailable, model.CapabilityState);
        Assert.True(model.HasCapabilityConstraint);
        Assert.True(model.CanRetryCapabilityProbe);
        Assert.False(model.CanToggle);
    }

    [Theory]
    [InlineData("PolicyDenied: operation is prohibited by policy.", ModuleCapabilityState.PolicyManaged)]
    [InlineData("Operation is unsupported on this Windows edition.", ModuleCapabilityState.Unsupported)]
    [InlineData("A conflicting owner is active.", ModuleCapabilityState.Conflict)]
    public void ManagedProbe_MapsKnownBlockedReasonsWithoutEnablingTheToggle(
        string details,
        ModuleCapabilityState expectedState)
    {
        var model = CreateModel(OptimizationOperationRoute.ManagedFailClosed);

        model.SyncOperationContract(Status(isApplied: false, details), isAdministrator: true);

        Assert.Equal(expectedState, model.CapabilityState);
        Assert.False(model.CanToggle);
    }

    [Fact]
    public void MissingAdministratorPermission_MapsToVisibleCapabilityState()
    {
        var model = CreateTyped(requiresAdmin: true);

        model.SyncOperationContract(Status(isApplied: false, "not applied"), isAdministrator: false);

        Assert.Equal(ModuleCapabilityState.AdminRequired, model.CapabilityState);
        Assert.True(model.HasCapabilityConstraint);
        Assert.False(model.CanRetryCapabilityProbe);
        Assert.False(model.CanToggle);
    }

    [Fact]
    public void AppliedOperationWithoutRecoveryReceipt_MapsToRecoveryUnavailable()
    {
        var model = CreateTyped(requiresAdmin: false, requiresSnapshotForRestore: true);

        model.SyncOperationContract(Status(isApplied: true, "externally applied"), isAdministrator: true);

        Assert.Equal(ModuleCapabilityState.RecoveryUnavailable, model.CapabilityState);
        Assert.False(model.CanToggle);
    }

    [Fact]
    public void AppliedTypedOperation_WithoutVerifiedReceipt_DisablesRestore()
    {
        var model = CreateTyped(requiresAdmin: false, requiresSnapshotForRestore: true);

        model.SyncOperationContract(Status(isApplied: true, "applied by external state"), isAdministrator: true);

        Assert.True(model.IsApplied);
        Assert.False(model.CanApply);
        Assert.False(model.CanRestore);
        Assert.Contains("恢复回执", model.BlockedReason, StringComparison.Ordinal);
        Assert.Contains("用户或其他工具", model.BlockedReason, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangedVerifiedApplyReceipt_UnlocksRestoreAndRecordsPerOperationReceipt()
    {
        var model = CreateTyped(requiresAdmin: false, requiresSnapshotForRestore: true);
        model.SyncOperationContract(Status(isApplied: false, "not applied"), isAdministrator: true);
        model.OnUserToggled(enabled: true);
        model.OnStartApply();

        Assert.False(model.CanApply);
        Assert.False(model.CanToggle);

        model.CompleteApplyFromSummary(Summary(
            success: true,
            changed: true,
            beforeApplied: false,
            afterApplied: true,
            message: "verified"));

        Assert.True(model.IsApplied);
        Assert.True(model.IsEnabled);
        Assert.False(model.IsPending);
        Assert.False(model.CanApply);
        Assert.True(model.CanRestore);
        Assert.True(model.LastOperationSucceeded);
        Assert.Contains("逐项回读", model.LastOperationReceipt, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyReadbackMismatch_RollsIntentBackToAfterStateAndKeepsRetryCapability()
    {
        var model = CreateTyped(requiresAdmin: false, requiresSnapshotForRestore: true);
        model.SyncOperationContract(Status(isApplied: false, "not applied"), isAdministrator: true);
        model.OnUserToggled(enabled: true);
        model.OnStartApply();

        model.CompleteApplyFromSummary(Summary(
            success: true,
            changed: true,
            beforeApplied: false,
            afterApplied: false,
            message: "write returned success but target did not match"));

        Assert.False(model.IsApplied);
        Assert.False(model.IsEnabled);
        Assert.False(model.IsPending);
        Assert.True(model.CanApply);
        Assert.False(model.LastOperationSucceeded);
        Assert.Contains("回读不一致", model.LastOperationReceipt, StringComparison.Ordinal);
    }

    [Fact]
    public void RestoreFailure_RollsIntentBackToAppliedAndPreservesVerifiedReceiptForRetry()
    {
        var model = CreateTyped(requiresAdmin: false, requiresSnapshotForRestore: true);
        model.SyncOperationContract(Status(isApplied: false, "not applied"), isAdministrator: true);
        model.OnUserToggled(enabled: true);
        model.OnStartApply();
        model.CompleteApplyFromSummary(Summary(true, true, false, true, "applied"));
        model.OnUserToggled(enabled: false);
        model.OnStartRestore();

        model.CompleteRestoreFromSummary(Summary(
            success: false,
            changed: false,
            beforeApplied: true,
            afterApplied: true,
            message: "restore denied"));

        Assert.True(model.IsApplied);
        Assert.True(model.IsEnabled);
        Assert.False(model.IsPending);
        Assert.True(model.CanRestore);
        Assert.Contains("恢复失败", model.LastOperationReceipt, StringComparison.Ordinal);
    }

    [Fact]
    public void ChineseModuleSurface_DoesNotExposeRawEnglishFailureOrCapabilityDetails()
    {
        var originalLanguage = LocalizationService.Instance.CurrentLanguage;
        LocalizationService.Instance.SetLanguage("zh-CN");
        try
        {
            var model = CreateTyped(requiresAdmin: false);

            model.SyncOperationContract(
                Status(isApplied: false, "Status check timed out while reading capability."),
                isAdministrator: true);

            Assert.DoesNotContain("Status check timed out", model.OperationFeedback, StringComparison.Ordinal);
            Assert.Contains("状态", model.OperationFeedback, StringComparison.Ordinal);

            model.CompleteApplyFailure("RequiresRecovery: operation timed out and final state is pending confirmation.");

            Assert.DoesNotContain("RequiresRecovery", model.LastOperationReceipt, StringComparison.Ordinal);
            Assert.DoesNotContain("pending confirmation", model.LastOperationReceipt, StringComparison.Ordinal);
            Assert.Contains("应用失败", model.LastOperationReceipt, StringComparison.Ordinal);

            var root = FindRepositoryRoot();
            var master = File.ReadAllText(Path.Combine(root, "GameOptimizer.Shell", "Views", "MasterModuleTemplate.xaml.cs"));
            Assert.Contains("nameof(OptimizationToggleModel.OperationFeedback)", master, StringComparison.Ordinal);
            Assert.DoesNotContain("Binding(nameof(OptimizationToggleModel.BlockedReason))", master, StringComparison.Ordinal);
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(originalLanguage);
        }
    }

    [Fact]
    public void CanonicalProvider_ExposesAllTwelveMasterModulesWithoutUnknownRoutes()
    {
        var manifest = OptimizationProviderRuntimeManifest.Create(new BuiltInOptimizationOperationProvider());
        var masterEntries = manifest.Where(entry => MasterModuleKeys.Contains(entry.ModuleKey, StringComparer.OrdinalIgnoreCase)).ToArray();

        Assert.Equal(MasterModuleKeys.OrderBy(key => key), masterEntries.Select(entry => entry.ModuleKey).Distinct().OrderBy(key => key));
        Assert.DoesNotContain(masterEntries, entry => entry.Route == OptimizationOperationRoute.Unknown);

        foreach (var group in masterEntries.GroupBy(entry => entry.ModuleKey).OrderBy(group => group.Key))
        {
            Console.WriteLine(
                $"{group.Key}|typed={group.Count(entry => entry.Route == OptimizationOperationRoute.TypedExecutable)}|" +
                $"failClosed={group.Count(entry => entry.Route == OptimizationOperationRoute.ManagedFailClosed)}|total={group.Count()}");
        }
    }

    [Fact]
    public void MasterSurface_UsesCanonicalManifestAndCapabilityGatesInsteadOfGlobalPermissionGuess()
    {
        var root = FindRepositoryRoot();
        var dashboard = File.ReadAllText(Path.Combine(root, "GameOptimizer.Shell", "ViewModels", "DashboardViewModel.cs"));
        var master = File.ReadAllText(Path.Combine(root, "GameOptimizer.Shell", "Views", "MasterModuleTemplate.xaml.cs"));

        Assert.Contains("OptimizationProviderRuntimeManifest", dashboard, StringComparison.Ordinal);
        Assert.Contains("SyncOperationContract", dashboard, StringComparison.Ordinal);
        Assert.Contains("nameof(OptimizationToggleModel.CanToggle)", master, StringComparison.Ordinal);
        Assert.Contains("t.CanApply", master, StringComparison.Ordinal);
        Assert.Contains("t.CanRestore", master, StringComparison.Ordinal);
        Assert.DoesNotContain("PermissionChecker.IsRunningAsAdministrator", master, StringComparison.Ordinal);
    }

    [Fact]
    public void MasterSurface_PresentsCapabilityStateAndReadOnlyRetryWithoutAddingAnExecutionPath()
    {
        var root = FindRepositoryRoot();
        var master = File.ReadAllText(Path.Combine(root, "GameOptimizer.Shell", "Views", "MasterModuleTemplate.xaml.cs"));
        var zh = File.ReadAllText(Path.Combine(root, "GameOptimizer.Core", "Localization", "LocalizationService.ZhCN.cs"));
        var en = File.ReadAllText(Path.Combine(root, "GameOptimizer.Core", "Localization", "LocalizationService.EnUS.cs"));

        Assert.Contains("CapabilityStateLabel", master, StringComparison.Ordinal);
        Assert.Contains("CapabilityStatusReason", master, StringComparison.Ordinal);
        Assert.Contains("RetryCapabilityProbe", master, StringComparison.Ordinal);
        Assert.Contains("RefreshModuleStatusInBackground();", master, StringComparison.Ordinal);
        Assert.Contains("Module.Capability.ReadbackUnavailable", zh, StringComparison.Ordinal);
        Assert.Contains("Module.Capability.ReadbackUnavailable", en, StringComparison.Ordinal);
        Assert.Contains("Module.Capability.PolicyManaged", zh, StringComparison.Ordinal);
        Assert.Contains("Module.Capability.PolicyManaged", en, StringComparison.Ordinal);
    }

    [Fact]
    public void MasterSurface_KeepsPerOperationReceiptsVisibleAlongsideCapabilityBlocks()
    {
        var root = FindRepositoryRoot();
        var master = File.ReadAllText(Path.Combine(root, "GameOptimizer.Shell", "Views", "MasterModuleTemplate.xaml.cs"));

        Assert.Contains("nameof(OptimizationToggleModel.OperationFeedback)", master, StringComparison.Ordinal);
        Assert.Contains("执行回执：{0}", master, StringComparison.Ordinal);
        Assert.Contains("PresentBatchReceipt(combinedResult)", master, StringComparison.Ordinal);
        Assert.Contains("terminal batch receipt", master, StringComparison.Ordinal);
    }

    [Fact]
    public void AudioOperations_AreNotPlacedInTheLegacyTemporaryBlockList()
    {
        var root = FindRepositoryRoot();
        var dashboard = File.ReadAllText(Path.Combine(root, "GameOptimizer.Shell", "ViewModels", "DashboardViewModel.cs"));
        var provider = new BuiltInOptimizationOperationProvider();
        var audioOperations = provider.GetModules()
            .Single(module => module.Key == "audio")
            .Operations
            .ToArray();

        Assert.NotEmpty(audioOperations);
        Assert.All(audioOperations, operation =>
        {
            Assert.IsAssignableFrom<OrchestratedChangeOperation>(operation);
            Assert.True(operation.RequiresExplicitConfirmation);
            Assert.DoesNotContain($"\"{operation.Id}\"", dashboard, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void MasterSurface_UsesOneActivationPathForMouseKeyboardAndBusyState()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "GameOptimizer.Shell", "Views", "MasterModuleTemplate.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "GameOptimizer.Shell", "Views", "MasterModuleTemplate.xaml.cs"));

        Assert.Contains("KeyDown=\"ApplyBtn_KeyDown\"", xaml, StringComparison.Ordinal);
        Assert.Contains("KeyDown=\"RestoreBtn_KeyDown\"", xaml, StringComparison.Ordinal);
        Assert.Contains("KeyDown=\"Open_BackupManager_KeyDown\"", xaml, StringComparison.Ordinal);
        Assert.Contains("KeyDown=\"Undo_ModuleChanges_KeyDown\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Focusable=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("toggleBtn.Click +=", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Mode = System.Windows.Data.BindingMode.OneWay", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("toggleBtn.Checked +=", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("toggleBtn.Unchecked +=", codeBehind, StringComparison.Ordinal);
        Assert.Contains("private async Task ExecutePendingChangesAsync()", codeBehind, StringComparison.Ordinal);
        Assert.Contains("await ExecutePendingChangesAsync();", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ApplyBtnBorder.IsEnabled = canExecutePending", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ToggleListScrollViewer.IsEnabled = !isApplying", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void ModuleApply_RequiresExplicitConfirmationBeforeEnteringExecutionState()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "GameOptimizer.Shell", "Views", "MasterModuleTemplate.xaml.cs"));
        var applyStart = source.IndexOf("private async Task ExecutePendingChangesAsync()", StringComparison.Ordinal);
        Assert.True(applyStart >= 0);
        var apply = source[applyStart..];

        var confirmIndex = apply.IndexOf("var confirm = ConfirmDialog.Show", StringComparison.Ordinal);
        var beginIndex = apply.IndexOf("TryBeginOperation(loc.Get(\"Module.Applying\")", StringComparison.Ordinal);
        Assert.True(confirmIndex >= 0 && beginIndex > confirmIndex);
        Assert.Contains("if (!confirm)", apply, StringComparison.Ordinal);
        Assert.Contains("用户取消确认，未进入执行链", apply, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfirmDialog_DoesNotAutoConfirmFromInitialFocusOrEnterKey()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "GameOptimizer.Shell", "Views", "ConfirmDialog.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "GameOptimizer.Shell", "Views", "ConfirmDialog.xaml.cs"));

        Assert.Contains("FocusedElement=\"{Binding ElementName=CancelBtn}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("IsDefault=\"True\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("e.Key == Key.Enter", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void MasterSurface_KeepsActionIslandOutOfTheScrollableContentAndReportsUnknownReadback()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "GameOptimizer.Shell", "Views", "MasterModuleTemplate.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "GameOptimizer.Shell", "Views", "MasterModuleTemplate.xaml.cs"));

        Assert.Contains("x:Name=\"FooterActionIsland\" Grid.Row=\"3\" Panel.ZIndex=\"20\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GroupedItemsControl\" Margin=\"0,0,0,112\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Opacity=\"0.92\"", xaml, StringComparison.Ordinal);
        Assert.Contains("unavailableReadbackCount", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Module.PartialStateUnavailable", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void MasterSurface_UsesTheSameVisualSkeletonForEverySharedModuleAndPlacesMemoryActionFirst()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "GameOptimizer.Shell", "Views", "MasterModuleTemplate.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "GameOptimizer.Shell", "Views", "MasterModuleTemplate.xaml.cs"));

        Assert.Contains("private bool UsesWebParityVisualSkeleton => !string.IsNullOrWhiteSpace(ModuleKey);", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Grid.SetRow(ModuleExtensionContainer, 1);", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Grid.SetRow(ControlBarCard, 2);", codeBehind, StringComparison.Ordinal);
        Assert.Contains("CreateWebParityGroupCard", codeBehind, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ModuleTopbar\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ModuleExtensionContainer\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MasterSurface_ProvidesARealCategoryFilterInAdditionToRiskFilters()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "GameOptimizer.Shell", "Views", "MasterModuleTemplate.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "GameOptimizer.Shell", "Views", "MasterModuleTemplate.xaml.cs"));

        Assert.Contains("x:Name=\"CategoryFilterComboBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectionChanged=\"CategoryFilterComboBox_SelectionChanged\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PreviewMouseLeftButtonDown=\"CategoryFilterComboBox_PreviewMouseLeftButtonDown\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PopulateCategoryFilter", codeBehind, StringComparison.Ordinal);
        Assert.Contains("CategoryFilterComboBox_SelectionChanged", codeBehind, StringComparison.Ordinal);
        Assert.Contains("CategoryFilterComboBox_PreviewMouseLeftButtonDown", codeBehind, StringComparison.Ordinal);
        Assert.Contains("_currentCategoryFilter", codeBehind, StringComparison.Ordinal);
        Assert.Contains("string.Equals(t.Category, _currentCategoryFilter", codeBehind, StringComparison.Ordinal);
    }

    private static OptimizationToggleModel CreateTyped(
        bool requiresAdmin,
        bool requiresSnapshotForRestore = false) =>
        CreateModel(
            OptimizationOperationRoute.TypedExecutable,
            requiresAdmin,
            requiresSnapshotForRestore);

    private static OptimizationToggleModel CreateModel(
        OptimizationOperationRoute route,
        bool requiresAdmin = false,
        bool requiresSnapshotForRestore = false) =>
        new(
            "Contract operation",
            operationId: "test.contract.operation",
            requiresAdmin: requiresAdmin,
            operationRoute: route,
            requiresSnapshotForRestore: requiresSnapshotForRestore,
            isAdministrator: true);

    private static GameOptimizer.Core.Optimization.OperationStatus Status(bool isApplied, string details) =>
        new(isApplied, details);

    private static OperationExecutionSummary Summary(
        bool success,
        bool changed,
        bool beforeApplied,
        bool afterApplied,
        string message) =>
        new(
            "test.contract.operation",
            "Contract operation",
            success,
            changed,
            message,
            Status(beforeApplied, $"before={beforeApplied}"),
            Status(afterApplied, $"after={afterApplied}"));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameOptimizer.Main.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test base directory.");
    }
}
