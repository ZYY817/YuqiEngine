// File: GameOptimizer.Tests/Changes/AppxRemovalChangeCapabilityTests.cs
using System.Text.Json;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.Appx;

namespace GameOptimizer.Tests.Changes;

public sealed class AppxRemovalChangeCapabilityTests
{
    private const string CapabilityId = "appx.remove_current_user";
    private const string DefinitionVersion = "1";
    private static readonly string EntryFamilyName = "Contoso.SampleApp_0000000000000";
    private static readonly string EntryFullName = "Contoso.SampleApp_0000000000000.1.0.0.0_x64__0000000000000";
    private static readonly string EntryInstallLocation = @"C:\Program Files\WindowsApps\Contoso.SampleApp_1.0.0.0_x64__0000000000000";

    private static readonly TargetScope MachineScope = TargetScope.Machine(Environment.MachineName);

    private static AppxRemovalEntry CreateEntry(AppxRecoveryClass recoveryClass = AppxRecoveryClass.StagedRegister, bool allowed = true) =>
        new(EntryFamilyName, "示例应用", "9NBLGGH4NNS1", recoveryClass, allowed);

    private static AppxRemovalChangeCapability CreateCapability(FakeAppxRemovalOwner owner, AppxRemovalEntry? entry = null) =>
        new(
            new AppxRemovalChangeCapabilityDefinition(CapabilityId, DefinitionVersion, entry ?? CreateEntry()),
            owner);

    private static RecoveryMaterial CreateMaterial(
        AppxRemovalChangeCapability capability,
        PreparedCapabilityChange change,
        string? beforePayloadOverride = null)
    {
        var beforePayload = beforePayloadOverride ?? change.BeforePayload;
        return new RecoveryMaterial(
            Guid.NewGuid(),
            Guid.NewGuid(),
            capability.Descriptor.CapabilityId,
            capability.Descriptor.DefinitionVersion,
            change.Scope,
            beforePayload,
            change.BeforeFingerprint,
            change.TargetFingerprint,
            change.ResourceKey,
            DateTimeOffset.UtcNow,
            "test");
    }

    [Fact]
    public async Task StagedRegisterRemoveAndRestore_ReRegistersExactPayload()
    {
        var owner = new FakeAppxRemovalOwner();
        var capability = CreateCapability(owner);

        var before = await capability.ReadAsync(MachineScope, CancellationToken.None);
        var change = await capability.PrepareAsync(before, "remove", CancellationToken.None);
        var applied = await capability.ExecuteAsync(change, CancellationToken.None);

        Assert.True(applied.Success);
        Assert.Equal("execution.verified", applied.ReasonCode);
        Assert.False(owner.RegisteredForUser);
        Assert.True(owner.PayloadPresent);

        var restored = await capability.RestoreAsync(CreateMaterial(capability, change), CancellationToken.None);

        Assert.True(restored.Success);
        Assert.Equal("restore.verified", restored.ReasonCode);
        Assert.True(restored.MutationOccurred);
        Assert.Equal(1, owner.RegisterCallCount);
        Assert.Equal(EntryInstallLocation, owner.LastRegisteredInstallLocation);
        Assert.True(owner.RegisteredForUser);
    }

    [Fact]
    public async Task NonStagedPackage_DegradesToStoreOnly_AndRestoreNeverFakesSuccess()
    {
        var owner = new FakeAppxRemovalOwner { PayloadPresent = false };
        var capability = CreateCapability(owner);

        var before = await capability.ReadAsync(MachineScope, CancellationToken.None);
        using (var document = JsonDocument.Parse(before.Payload))
        {
            Assert.Equal((int)AppxRecoveryClass.StoreOnly, document.RootElement.GetProperty("RecoveryClass").GetInt32());
        }

        var change = await capability.PrepareAsync(before, "remove", CancellationToken.None);
        var applied = await capability.ExecuteAsync(change, CancellationToken.None);
        Assert.True(applied.Success);

        var restored = await capability.RestoreAsync(CreateMaterial(capability, change), CancellationToken.None);

        Assert.False(restored.Success);
        Assert.False(restored.MutationOccurred);
        Assert.Equal("manual-compensation-required", restored.ReasonCode);
        Assert.Contains("手动重装", restored.Message);
        Assert.Contains("9NBLGGH4NNS1", restored.Message);
        Assert.Equal(0, owner.RegisterCallCount);
    }

    [Fact]
    public async Task HardDenyList_IsRejectedAtProbeAndPrepare()
    {
        var scenarios = new (Action<FakeAppxRemovalOwner> Configure, string ExpectedReason)[]
        {
            (static owner =>
                {
                    owner.Name = "Microsoft.WindowsStore";
                    owner.FamilyName = "Microsoft.WindowsStore_8wekyb3d8bbwe";
                }, "appx.package-protected"),
            (static owner => owner.NonRemovable = true, "appx.package-nonremovable"),
            (static owner => owner.IsFramework = true, "appx.package-framework"),
            (static owner => owner.DependencyCount = 3, "appx.package-has-dependents")
        };

        foreach (var (configure, expectedReason) in scenarios)
        {
            var owner = new FakeAppxRemovalOwner();
            configure(owner);
            var capability = CreateCapability(owner);

            var probe = await capability.ProbeAsync(MachineScope, CancellationToken.None);
            Assert.Equal(CapabilityApplicability.NotApplicable, probe.Applicability);
            Assert.Equal(expectedReason, probe.ReasonCode);

            var observation = await capability.ReadAsync(MachineScope, CancellationToken.None);
            await Assert.ThrowsAsync<InvalidDataException>(
                () => capability.PrepareAsync(observation, "remove", CancellationToken.None));
        }
    }

    [Fact]
    public async Task EntryOutsideAllowList_IsRejected()
    {
        var owner = new FakeAppxRemovalOwner();
        var capability = CreateCapability(owner, CreateEntry(allowed: false));

        var probe = await capability.ProbeAsync(MachineScope, CancellationToken.None);

        Assert.Equal(CapabilityApplicability.NotApplicable, probe.Applicability);
        Assert.Equal("appx.entry-not-allowed", probe.ReasonCode);
        var beforeRead = await capability.ReadAsync(MachineScope, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidDataException>(
            () => capability.PrepareAsync(beforeRead, "remove", CancellationToken.None));
    }

    [Fact]
    public async Task DriftBetweenPrepareAndWrite_IsRejectedWithoutMutation()
    {
        var owner = new FakeAppxRemovalOwner();
        var capability = CreateCapability(owner);

        var before = await capability.ReadAsync(MachineScope, CancellationToken.None);
        var change = await capability.PrepareAsync(before, "remove", CancellationToken.None);
        owner.RegisteredForUser = false;

        var result = await capability.ExecuteAsync(change, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("state.drift", result.ReasonCode);
        Assert.False(result.MutationOccurred);
        Assert.Equal(0, owner.RemoveCallCount);
    }

    [Fact]
    public async Task MissingPermission_ProbeBlocksCapability()
    {
        var owner = new FakeAppxRemovalOwner { PermissionGranted = false };
        var capability = CreateCapability(owner);

        var probe = await capability.ProbeAsync(MachineScope, CancellationToken.None);

        Assert.Equal(CapabilityApplicability.NotApplicable, probe.Applicability);
        Assert.False(probe.HasRequiredPermission);
        Assert.Equal("permission.administrator-required", probe.ReasonCode);
    }

    [Fact]
    public async Task RemoveSucceedsButReadbackStillVisible_ReportsAppliedButUnverified()
    {
        var owner = new FakeAppxRemovalOwner
        {
            RemoveHook = () => new AppxMutationResult(true, true, "ok", "假象成功")
        };
        var capability = CreateCapability(owner);

        var before = await capability.ReadAsync(MachineScope, CancellationToken.None);
        var change = await capability.PrepareAsync(before, "remove", CancellationToken.None);
        var applied = await capability.ExecuteAsync(change, CancellationToken.None);

        Assert.False(applied.Success);
        Assert.True(applied.MutationOccurred);
        Assert.Equal("applied-but-unverified", applied.ReasonCode);
        Assert.Equal(2, owner.RemoveCallCount);
    }

    [Fact]
    public async Task CrossUserRecoveryMaterial_IsRejected()
    {
        var owner = new FakeAppxRemovalOwner();
        var capability = CreateCapability(owner);
        var before = await capability.ReadAsync(MachineScope, CancellationToken.None);
        var change = await capability.PrepareAsync(before, "remove", CancellationToken.None);

        var foreignPayload = TamperScopeSid(change.BeforePayload, "S-1-12-ARBITRARY-FOREIGN-SID");
        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => capability.RestoreAsync(CreateMaterial(capability, change, foreignPayload), CancellationToken.None));

        Assert.Contains("跨用户", exception.Message);
        Assert.Equal(0, owner.RegisterCallCount);
    }

    private static string TamperScopeSid(string payload, string sid)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(payload)!;
        node["ScopeSid"] = sid;
        return node.ToJsonString();
    }

    [Fact]
    public void Descriptor_DeclaresFixedIdentityAndSafetyFlags()
    {
        var descriptor = CreateCapability(new FakeAppxRemovalOwner()).Descriptor;

        Assert.Equal(CapabilityId, descriptor.CapabilityId);
        Assert.Equal(DefinitionVersion, descriptor.DefinitionVersion);
        Assert.Equal($"appx:{EntryFamilyName}", descriptor.ResourceKey);
        Assert.Single(descriptor.SupportedScopes, TargetScopeKind.Machine);
        Assert.Equal(ChangeRisk.HighSystem, descriptor.Risk);
        Assert.True(descriptor.RequiresAdministrator);
        Assert.True(descriptor.RequiresExplicitConfirmation);
        Assert.True(descriptor.RequiresRecoveryMaterial);
        Assert.False(descriptor.RequiresRestart);
    }

    private sealed class FakeAppxRemovalOwner : IAppxRemovalOwner
    {
        internal bool PermissionGranted = true;
        internal bool RegisteredForUser = true;
        internal bool PayloadPresent = true;
        internal bool NonRemovable;
        internal bool IsFramework;
        internal int DependencyCount;
        internal string Name = "Contoso.SampleApp";
        internal string FamilyName = EntryFamilyName;
        internal Func<AppxMutationResult>? RemoveHook;

        public Task<IReadOnlyList<AppxCatalogEntry>> EnumerateInstalledAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AppxCatalogEntry>>([]);

        internal int RemoveCallCount { get; private set; }
        internal int RegisterCallCount { get; private set; }
        internal string? LastRegisteredInstallLocation { get; private set; }

        public Task<AppxPlatformAssessment> ProbeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(PermissionGranted
                ? new AppxPlatformAssessment(true, "applicable", "fake")
                : new AppxPlatformAssessment(false, "permission.administrator-required", "fake"));
        }

        public Task<AppxPackageState> ReadStateAsync(string packageFamilyName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var known = RegisteredForUser || PayloadPresent;
            return Task.FromResult(new AppxPackageState(
                Name: known ? Name : string.Empty,
                FullName: known ? EntryFullName : string.Empty,
                FamilyName: packageFamilyName,
                Version: "1.0.0.0",
                InstallLocation: known ? EntryInstallLocation : string.Empty,
                Installed: RegisteredForUser,
                IsStaged: PayloadPresent,
                NonRemovable,
                IsFramework,
                DependencyCount,
                SignatureKind: "Store",
                ErrorCode: null));
        }

        public Task<AppxMutationResult> RemoveForCurrentUserAsync(string packageFullName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RemoveCallCount++;
            if (RemoveHook is not null)
                return Task.FromResult(RemoveHook());
            if (!RegisteredForUser)
                return Task.FromResult(new AppxMutationResult(false, false, "appx.deployment-failed", "fake：目标包未注册。"));

            RegisteredForUser = false;
            return Task.FromResult(new AppxMutationResult(true, true, "ok", "fake：已解除当前用户注册。"));
        }

        public Task<AppxMutationResult> RegisterFromInstallLocationAsync(string installLocation, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RegisterCallCount++;
            LastRegisteredInstallLocation = installLocation;
            if (!PayloadPresent || !string.Equals(installLocation, EntryInstallLocation, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(new AppxMutationResult(false, false, "appx.manifest-missing", "fake：Staged 载荷清单缺失。"));

            RegisteredForUser = true;
            return Task.FromResult(new AppxMutationResult(true, true, "ok", "fake：已重新注册。"));
        }
    }
}
