// File: GameOptimizer.Tests/Shell/AppxCleanupViewModelTests.cs
using GameOptimizer.Core.Services.Appx;
using GameOptimizer.Shell.ViewModels;

namespace GameOptimizer.Tests.Shell;

public sealed class AppxCleanupViewModelTests
{
    private static AppxCatalogEntry Entry(
        string familyName,
        string displayName,
        AppxRecoveryClass recoveryClass = AppxRecoveryClass.StagedRegister,
        bool removable = true,
        string notRemovableReason = "") =>
        new(displayName, familyName, familyName, "1.0.0.0", "测试发布者", recoveryClass, removable, notRemovableReason);

    private sealed class FakeOwner : IAppxRemovalOwner
    {
        internal bool PermissionGranted = true;
        internal IReadOnlyList<AppxCatalogEntry> Catalog = [];
        internal Exception? EnumerateError;

        public Task<AppxPlatformAssessment> ProbeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PermissionGranted
                ? new AppxPlatformAssessment(true, "applicable", "fake")
                : new AppxPlatformAssessment(false, "permission.administrator-required", "需要管理员"));

        public Task<IReadOnlyList<AppxCatalogEntry>> EnumerateInstalledAsync(CancellationToken cancellationToken)
        {
            if (EnumerateError is not null)
                throw EnumerateError;
            return Task.FromResult(Catalog);
        }

        public Task<AppxPackageState> ReadStateAsync(string packageFamilyName, CancellationToken cancellationToken) =>
            throw new NotSupportedException("VM 不应直接读包状态。");

        public Task<AppxMutationResult> RemoveForCurrentUserAsync(string packageFullName, CancellationToken cancellationToken) =>
            throw new NotSupportedException("VM 必须经 IAppxUserRemovalService 执行移除。");

        public Task<AppxMutationResult> RegisterFromInstallLocationAsync(string installLocation, CancellationToken cancellationToken) =>
            throw new NotSupportedException("恢复由能力层负责。");
    }

    private sealed class FakeRemovalService : IAppxUserRemovalService
    {
        internal IReadOnlyList<AppxCatalogEntry>? LastEntries;
        internal int CallCount;
        internal AppxUserRemovalBatchResult Result = new(false, false, []);

        public Task<AppxUserRemovalBatchResult> RemoveAsync(
            IReadOnlyList<AppxCatalogEntry> entries,
            Action<int, int, string>? progress,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastEntries = entries;
            return Task.FromResult(Result);
        }
    }

    private sealed class CancellableEnumerationOwner : IAppxRemovalOwner
    {
        internal TaskCompletionSource EnumerationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<AppxPlatformAssessment> ProbeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new AppxPlatformAssessment(true, "applicable", "fake"));

        public async Task<IReadOnlyList<AppxCatalogEntry>> EnumerateInstalledAsync(CancellationToken cancellationToken)
        {
            EnumerationStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return [];
        }

        public Task<AppxPackageState> ReadStateAsync(string packageFamilyName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AppxMutationResult> RemoveForCurrentUserAsync(string packageFullName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AppxMutationResult> RegisterFromInstallLocationAsync(string installLocation, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Load_MapsEntriesWithBadges_AndSortsRemovableFirst()
    {
        var owner = new FakeOwner
        {
            Catalog =
            [
                Entry("B", "AppB", AppxRecoveryClass.StoreOnly),
                Entry("C", "InboxApp", removable: false, notRemovableReason: "系统关键组件，禁止移除"),
                Entry("A", "AppA")
            ]
        };
        var vm = new AppxCleanupViewModel(owner, new FakeRemovalService());

        await vm.LoadAsync();

        Assert.Equal(AppxCleanupPageState.Ready, vm.PageState);
        Assert.Equal(3, vm.Packages.Count);
        Assert.Equal(["A", "B", "C"], vm.Packages.Select(item => item.FamilyName));

        var staged = vm.Packages[0];
        Assert.True(staged.IsStagedRegister);
        Assert.False(staged.IsStoreOnly);
        Assert.Contains("自动恢复", staged.RecoveryBadgeText);

        var storeOnly = vm.Packages[1];
        Assert.True(storeOnly.IsStoreOnly);
        Assert.Contains("Store", storeOnly.RecoveryBadgeText);

        Assert.Equal(2, vm.RemovableCount);
    }

    [Fact]
    public async Task Load_WithoutAdministratorPermission_EntersUnauthorizedStateFailClosed()
    {
        var owner = new FakeOwner { PermissionGranted = false };
        var vm = new AppxCleanupViewModel(owner, new FakeRemovalService());

        await vm.LoadAsync();

        Assert.Equal(AppxCleanupPageState.Unauthorized, vm.PageState);
        Assert.Empty(vm.Packages);
        Assert.Contains("管理员", vm.StatusText);
    }

    [Fact]
    public async Task Load_EnumerationFailure_EntersErrorStateWithMessage()
    {
        var owner = new FakeOwner { EnumerateError = new InvalidOperationException("目录查询失败") };
        var vm = new AppxCleanupViewModel(owner, new FakeRemovalService());

        await vm.LoadAsync();

        Assert.Equal(AppxCleanupPageState.Error, vm.PageState);
        Assert.Contains("失败", vm.StatusText);
        Assert.Contains("目录查询失败", vm.StatusText);
    }

    [Fact]
    public async Task Load_EmptyCatalog_EntersEmptyState()
    {
        var owner = new FakeOwner { Catalog = [] };
        var vm = new AppxCleanupViewModel(owner, new FakeRemovalService());

        await vm.LoadAsync();

        Assert.Equal(AppxCleanupPageState.Empty, vm.PageState);
        Assert.True(vm.IsEmpty);
    }

    [Fact]
    public async Task Load_CancelledEnumeration_ReturnsIdleStateAndReportsNoUninstall()
    {
        var owner = new CancellableEnumerationOwner();
        var vm = new AppxCleanupViewModel(owner, new FakeRemovalService());

        var load = vm.LoadAsync();
        await owner.EnumerationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(vm.IsEnumerating);

        vm.CancelCommand.Execute(null);
        await load.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(vm.IsEnumerating);
        Assert.False(vm.IsBusy);
        Assert.Equal(AppxCleanupPageState.Empty, vm.PageState);
        Assert.Contains("未执行任何卸载", vm.StatusText);
    }

    [Fact]
    public void NonRemovableItem_SelectionIsForcedBackOff_AndExcludedFromCount()
    {
        var owner = new FakeOwner();
        var vm = new AppxCleanupViewModel(owner, new FakeRemovalService());
        vm.Packages.Add(new AppxCleanupItemViewModel(
            Entry("C", "收件箱应用", removable: false, notRemovableReason: "收件箱系统应用")));

        var item = vm.Packages[0];
        item.IsSelected = true;

        Assert.False(item.IsSelected);
        Assert.Equal(0, vm.SelectedCount);
        Assert.False(vm.HasSelection);
    }

    [Fact]
    public void SelectionChanges_UpdateCountAndButtonText()
    {
        var owner = new FakeOwner();
        var vm = new AppxCleanupViewModel(owner, new FakeRemovalService());
        vm.Packages.Add(new AppxCleanupItemViewModel(Entry("A", "应用一")));
        vm.Packages.Add(new AppxCleanupItemViewModel(Entry("B", "应用二", AppxRecoveryClass.StoreOnly)));

        Assert.DoesNotContain("1", vm.RemoveButtonText);

        vm.Packages[0].IsSelected = true;

        Assert.Equal(1, vm.SelectedCount);
        Assert.True(vm.HasStoreOnlySelected is false);
        Assert.Contains("(1)", vm.RemoveButtonText);

        vm.Packages[1].IsSelected = true;

        Assert.Equal(2, vm.SelectedCount);
        Assert.True(vm.HasStoreOnlySelected);
        Assert.Contains("(2)", vm.RemoveButtonText);
    }

    [Fact]
    public async Task Uninstall_WithoutConfirmationPrompt_IsRejectedWithoutCallingService()
    {
        var owner = new FakeOwner();
        var service = new FakeRemovalService();
        var vm = new AppxCleanupViewModel(owner, service);
        vm.Packages.Add(new AppxCleanupItemViewModel(Entry("A", "应用一")));
        vm.Packages[0].IsSelected = true;

        await vm.UninstallSelectedAsync();

        Assert.Equal(0, service.CallCount);
        Assert.False(vm.IsRemoving);
    }

    [Fact]
    public async Task Uninstall_Confirmed_PassesOnlySelectedRemovableEntriesToService()
    {
        var owner = new FakeOwner
        {
            Catalog =
            [
                Entry("A", "应用一"),
                Entry("B", "商店应用", AppxRecoveryClass.StoreOnly),
                Entry("C", "收件箱", removable: false, notRemovableReason: "禁止移除")
            ]
        };
        var service = new FakeRemovalService
        {
            Result = new AppxUserRemovalBatchResult(
                Interrupted: false,
                CompensatedAny: false,
                [new AppxUserRemovalOutcome("B", "商店应用", AppxRecoveryClass.StoreOnly, true, true, "execution.verified", "已移除")])
        };
        var requests = new List<AppxCleanupConfirmationRequest>();
        var vm = new AppxCleanupViewModel(owner, service, request =>
        {
            requests.Add(request);
            return true;
        });

        await vm.LoadAsync();
        vm.Packages.First(item => item.FamilyName == "A").IsSelected = false;
        vm.Packages.First(item => item.FamilyName == "B").IsSelected = true;
        vm.Packages.First(item => item.FamilyName == "C").IsSelected = true; // 禁勾项应被强制回退

        await vm.UninstallSelectedAsync();

        Assert.Equal(1, service.CallCount);
        Assert.NotNull(service.LastEntries);
        Assert.Single(service.LastEntries!, item => item.FamilyName == "B");

        var request = Assert.Single(requests);
        Assert.Single(request.Consequences, line => line.Contains("无法自动恢复"));
        Assert.Contains("无法通过本机载荷自动恢复", request.Message);
        Assert.Contains("卸载完成：成功 1 项", vm.StatusText);
        Assert.False(vm.IsRemoving);
    }

    [Fact]
    public async Task Uninstall_Declined_DoesNotCallService()
    {
        var owner = new FakeOwner();
        var service = new FakeRemovalService();
        var vm = new AppxCleanupViewModel(owner, service, _ => false);
        vm.Packages.Add(new AppxCleanupItemViewModel(Entry("A", "应用一")));
        vm.Packages[0].IsSelected = true;

        await vm.UninstallSelectedAsync();

        Assert.Equal(0, service.CallCount);
    }

    [Fact]
    public async Task Uninstall_PartialFailureSummary_ReportsCompensationAndManualRestoreHints()
    {
        var owner = new FakeOwner();
        var service = new FakeRemovalService
        {
            Result = new AppxUserRemovalBatchResult(
                Interrupted: true,
                CompensatedAny: false,
                [
                    new AppxUserRemovalOutcome("A", "应用一", AppxRecoveryClass.StagedRegister, true, true, "execution.verified", "已移除")
                    {
                        CompensationSucceeded = true,
                        CompensationMessage = "restored"
                    },
                    new AppxUserRemovalOutcome("B", "应用二", AppxRecoveryClass.StoreOnly, true, true, "execution.verified", "已移除")
                    {
                        CompensationSucceeded = false,
                        CompensationMessage = "manual-compensation-required"
                    },
                    new AppxUserRemovalOutcome("C", "应用三", AppxRecoveryClass.StagedRegister, false, false, "appx.deployment-failed", "部署失败")
                ])
        };
        var vm = new AppxCleanupViewModel(owner, service, _ => true);
        owner.Catalog = [Entry("A", "应用一"), Entry("B", "应用二", AppxRecoveryClass.StoreOnly)];

        await vm.LoadAsync();
        foreach (var item in vm.Packages)
            item.IsSelected = true;

        await vm.UninstallSelectedAsync();

        Assert.Contains("当前仍移除 1 项", vm.StatusText);
        Assert.Contains("失败/阻断 1 项", vm.StatusText);
        Assert.Contains("已自动恢复 1 项", vm.StatusText);
        Assert.Contains("Microsoft Store 手动重装", vm.StatusText);
        Assert.Contains("appx.deployment-failed", vm.StatusText);
    }
}
