// File: GameOptimizer.Tests/Changes/AppxUserRemovalServiceTests.cs
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.Appx;

namespace GameOptimizer.Tests.Changes;

public sealed class AppxUserRemovalServiceTests
{
    private const string FakeInstallLocation = @"C:\Program Files\WindowsApps\Fake";

    private static AppxCatalogEntry Entry(string familyName, AppxRecoveryClass recoveryClass) =>
        new(familyName, familyName, familyName, "1.0.0.0", "测试发布者", recoveryClass, Removable: true, NotRemovableReason: string.Empty);

    private static AppxUserRemovalService CreateService(FakeAppxRemovalOwner owner) => new(owner);

    [Fact]
    public async Task AllStagedPackagesRemoved_NoCompensationIsTriggered()
    {
        var owner = new FakeAppxRemovalOwner();
        owner.AddPackage("A", staged: true);
        owner.AddPackage("B", staged: true);
        var service = CreateService(owner);

        var result = await service.RemoveAsync(
            [Entry("A", AppxRecoveryClass.StagedRegister), Entry("B", AppxRecoveryClass.StagedRegister)],
            progress: null,
            CancellationToken.None);

        Assert.False(result.Interrupted);
        Assert.False(result.CompensatedAny);
        Assert.All(result.Outcomes, item => Assert.True(item.Success));
        Assert.All(result.Outcomes, item => Assert.Null(item.CompensationSucceeded));
        Assert.Equal(0, owner.RegisterCallCount);
    }

    [Fact]
    public async Task SecondItemFails_FirstItemIsCompensatedInReverse()
    {
        var owner = new FakeAppxRemovalOwner();
        owner.AddPackage("A", staged: true);
        owner.AddPackage("B", staged: true);
        owner.FailOnRemove.Add("B");
        var service = CreateService(owner);

        var result = await service.RemoveAsync(
            [Entry("A", AppxRecoveryClass.StagedRegister), Entry("B", AppxRecoveryClass.StagedRegister)],
            progress: null,
            CancellationToken.None);

        Assert.True(result.Interrupted);
        Assert.True(result.CompensatedAny);

        var a = result.Outcomes.Single(item => item.FamilyName == "A");
        var b = result.Outcomes.Single(item => item.FamilyName == "B");
        Assert.True(a.Success);
        Assert.True(a.CompensationSucceeded);
        Assert.False(b.Success);
        Assert.Null(b.CompensationSucceeded);
        Assert.Equal(1, owner.RegisterCallCount);
        Assert.True(owner.Packages["A"].Registered);
    }

    [Fact]
    public async Task StoreOnlyCompensation_RecordsManualRequirementWithoutFakingSuccess()
    {
        var owner = new FakeAppxRemovalOwner();
        owner.AddPackage("A", staged: false);
        owner.AddPackage("B", staged: true);
        owner.FailOnRemove.Add("B");
        var service = CreateService(owner);

        var result = await service.RemoveAsync(
            [Entry("A", AppxRecoveryClass.StoreOnly), Entry("B", AppxRecoveryClass.StagedRegister)],
            progress: null,
            CancellationToken.None);

        Assert.True(result.Interrupted);
        Assert.False(result.CompensatedAny);

        var a = result.Outcomes.Single(item => item.FamilyName == "A");
        Assert.True(a.Success);
        Assert.False(a.CompensationSucceeded);
        Assert.Contains("manual-compensation-required", a.CompensationMessage);
        Assert.Equal(0, owner.RegisterCallCount);
    }

    [Fact]
    public async Task ProbeDenial_StopsBatchWithoutMutationOrCompensation()
    {
        var owner = new FakeAppxRemovalOwner { PermissionGranted = false };
        owner.AddPackage("A", staged: true);
        owner.AddPackage("B", staged: true);
        var service = CreateService(owner);

        var result = await service.RemoveAsync(
            [Entry("A", AppxRecoveryClass.StagedRegister), Entry("B", AppxRecoveryClass.StagedRegister)],
            progress: null,
            CancellationToken.None);

        var a = result.Outcomes.Single(item => item.FamilyName == "A");
        Assert.True(result.Interrupted);
        Assert.False(a.Success);
        Assert.Contains("permission", a.ReasonCode);
        Assert.Null(a.CompensationSucceeded);
        Assert.Single(result.Outcomes);
        Assert.All(owner.Packages.Values, item => Assert.True(item.Registered));
        Assert.Equal(0, owner.RegisterCallCount);
    }

    [Fact]
    public async Task EmptySelection_ReturnsEmptyResultWithoutTouchingPlatform()
    {
        var owner = new FakeAppxRemovalOwner();
        var service = CreateService(owner);

        var result = await service.RemoveAsync([], progress: null, CancellationToken.None);

        Assert.Empty(result.Outcomes);
        Assert.False(result.Interrupted);
        Assert.Equal(0, owner.RemoveCallCount);
    }

    [Fact]
    public async Task PreCancelledToken_ReturnsInterruptedEmptyResultAndLeavesPackagesUntouched()
    {
        var owner = new FakeAppxRemovalOwner();
        owner.AddPackage("A", staged: true);
        var service = CreateService(owner);

        var result = await service.RemoveAsync(
            [Entry("A", AppxRecoveryClass.StagedRegister)],
            progress: null,
            new CancellationToken(canceled: true));

        Assert.True(result.Interrupted);
        Assert.Empty(result.Outcomes);
        Assert.True(owner.Packages["A"].Registered);
        Assert.Equal(0, owner.RemoveCallCount);
        Assert.Equal(0, owner.RegisterCallCount);
    }

    [Fact]
    public async Task CancellationAfterFirstItem_ReturnsCompletedOutcomeWithoutStartingSecondWrite()
    {
        var owner = new FakeAppxRemovalOwner();
        owner.AddPackage("A", staged: true);
        owner.AddPackage("B", staged: true);
        using var cancellation = new CancellationTokenSource();
        var service = CreateService(owner);

        var result = await service.RemoveAsync(
            [Entry("A", AppxRecoveryClass.StagedRegister), Entry("B", AppxRecoveryClass.StagedRegister)],
            (current, _, _) => { if (current == 2) cancellation.Cancel(); },
            cancellation.Token);

        Assert.True(result.Interrupted);
        Assert.Equal("A", Assert.Single(result.Outcomes).FamilyName);
        Assert.Equal(1, owner.RemoveCallCount);
        Assert.False(owner.Packages["A"].Registered);
        Assert.True(owner.Packages["B"].Registered);
    }

    [Fact]
    public async Task ProgressCallback_ReceivesSequentialItemReports()
    {
        var owner = new FakeAppxRemovalOwner();
        owner.AddPackage("A", staged: true);
        owner.AddPackage("B", staged: true);
        var service = CreateService(owner);

        var reports = new List<(int Current, int Total, string Name)>();
        await service.RemoveAsync(
            [Entry("A", AppxRecoveryClass.StagedRegister), Entry("B", AppxRecoveryClass.StagedRegister)],
            (current, total, name) => reports.Add((current, total, name)),
            CancellationToken.None);

        Assert.Equal([(1, 2, "A"), (2, 2, "B")], reports);
    }

    private sealed class FakeAppxRemovalOwner : IAppxRemovalOwner
    {
        internal bool PermissionGranted = true;

        internal sealed class Pkg
        {
            internal bool Registered = true;
            internal string InstallLocation = FakeInstallLocation;
        }

        internal Dictionary<string, Pkg> Packages = new(StringComparer.OrdinalIgnoreCase);
        internal HashSet<string> FailOnRemove = new(StringComparer.OrdinalIgnoreCase);
        internal int RegisterCallCount;
        internal int RemoveCallCount;

        internal void AddPackage(string familyName, bool staged)
        {
            Packages[familyName] = new Pkg
            {
                InstallLocation = staged ? $"{FakeInstallLocation}\\{familyName}" : string.Empty
            };
        }

        public Task<AppxPlatformAssessment> ProbeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(PermissionGranted
                ? new AppxPlatformAssessment(true, "applicable", "fake")
                : new AppxPlatformAssessment(false, "permission.administrator-required", "fake"));
        }

        public Task<IReadOnlyList<AppxCatalogEntry>> EnumerateInstalledAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AppxCatalogEntry>>([]);

        public Task<AppxPackageState> ReadStateAsync(string packageFamilyName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Packages.TryGetValue(packageFamilyName, out var pkg))
                return Task.FromResult(new AppxPackageState(
                    string.Empty, string.Empty, packageFamilyName, string.Empty, string.Empty,
                    Installed: false, IsStaged: false, NonRemovable: false, IsFramework: false,
                    DependencyCount: 0, SignatureKind: string.Empty, ErrorCode: null));

            return Task.FromResult(new AppxPackageState(
                Name: packageFamilyName,
                FullName: packageFamilyName,
                FamilyName: packageFamilyName,
                Version: "1.0.0.0",
                InstallLocation: pkg.InstallLocation,
                Installed: pkg.Registered,
                IsStaged: !string.IsNullOrWhiteSpace(pkg.InstallLocation),
                NonRemovable: false,
                IsFramework: false,
                DependencyCount: 0,
                SignatureKind: "Store",
                ErrorCode: null));
        }

        public Task<AppxMutationResult> RemoveForCurrentUserAsync(string packageFullName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RemoveCallCount++;

            if (FailOnRemove.Contains(packageFullName))
                return Task.FromResult(new AppxMutationResult(false, false, "appx.deployment-failed", "fake：部署失败。"));

            if (!Packages.TryGetValue(packageFullName, out var pkg) || !pkg.Registered)
                return Task.FromResult(new AppxMutationResult(false, false, "appx.deployment-failed", "fake：目标包未注册。"));

            pkg.Registered = false;
            return Task.FromResult(new AppxMutationResult(true, true, "ok", "fake：已解除当前用户注册。"));
        }

        public Task<AppxMutationResult> RegisterFromInstallLocationAsync(string installLocation, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RegisterCallCount++;

            if (string.IsNullOrWhiteSpace(installLocation))
                return Task.FromResult(new AppxMutationResult(false, false, "appx.manifest-missing", "fake：清单缺失。"));

            var match = Packages.Values.FirstOrDefault(pkg =>
                string.Equals(pkg.InstallLocation, installLocation, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                return Task.FromResult(new AppxMutationResult(false, false, "appx.manifest-missing", "fake：清单缺失。"));

            match.Registered = true;
            return Task.FromResult(new AppxMutationResult(true, true, "ok", "fake：已重新注册。"));
        }
    }
}
