using GameOptimizer.Core.Models;
using GameOptimizer.Core.PowerManagement;
using GameOptimizer.Core.Services;
using GameOptimizer.Infrastructure.Windows.PowerManagement;

namespace GameOptimizer.Tests.PowerManagement;

public sealed class ManagedPowerPlanServiceTests
{
    [Fact]
    public async Task Create_ExportsBeforeDuplicate_WritesOnlyManagedClone_AndPersistsVerifiedBackup()
    {
        await using var fixture = new Fixture(CpuVendor.Intel, CpuArchitecture.IntelHybrid);
        var draft = await fixture.Service.PrepareCreateAsync(fixture.SourceId, ManagedPowerProfileId.IntelGaming, default);

        var result = await fixture.Service.ExecuteAsync(draft, default);

        Assert.True(result.Success, result.Message);
        Assert.Equal("export", fixture.Platform.Events[0]);
        Assert.Equal("duplicate", fixture.Platform.Events[1]);
        Assert.DoesNotContain(fixture.Settings.Writes, write => write.Scheme == fixture.SourceId);
        Assert.All(fixture.Settings.Writes, write => Assert.Equal(draft.TargetPlanId, write.Scheme));
        Assert.Equal(draft.TargetPlanId, fixture.Schemes.Active);
        Assert.NotNull(result.Backup);
        Assert.True(File.Exists(result.Backup!.FilePath));
        Assert.NotEmpty(result.Backup.Sha256);
        var stored = await fixture.Store.LoadAsync(default);
        Assert.Contains(stored.Plans, plan => plan.PlanId == draft.TargetPlanId && plan.SourcePlanId == fixture.SourceId);
        Assert.Contains(stored.Backups, backup => backup.BackupId == result.Backup.BackupId);
    }

    [Fact]
    public async Task Create_UsesValidatedCustomName_AndStillCreatesOnlyManagedClone()
    {
        await using var fixture = new Fixture(CpuVendor.Intel, CpuArchitecture.IntelStandard);

        var draft = await fixture.Service.PrepareCreateAsync(
            fixture.SourceId, ManagedPowerProfileId.IntelGaming, "我的 Intel 游戏计划", default);
        var result = await fixture.Service.ExecuteAsync(draft, default);

        Assert.True(result.Success, result.Message);
        Assert.Equal("我的 Intel 游戏计划", draft.TargetName);
        Assert.Equal("我的 Intel 游戏计划", result.Plan?.Name);
        Assert.DoesNotContain(fixture.Settings.Writes, write => write.Scheme == fixture.SourceId);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("\u0001bad")]
    public async Task Create_RejectsInvalidCustomNameBeforeAnyMutation(string name)
    {
        await using var fixture = new Fixture(CpuVendor.Intel, CpuArchitecture.IntelStandard);

        if (string.IsNullOrWhiteSpace(name))
        {
            var draft = await fixture.Service.PrepareCreateAsync(
                fixture.SourceId, ManagedPowerProfileId.IntelGaming, name, default);
            Assert.StartsWith("宇奇 · Intel 游戏性能", draft.TargetName, StringComparison.Ordinal);
            return;
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.PrepareCreateAsync(
            fixture.SourceId, ManagedPowerProfileId.IntelGaming, name, default));
        Assert.Empty(fixture.Platform.Events);
    }

    [Fact]
    public async Task Create_WhenWriteFails_RemovesCloneAndRestoresOriginalActivePlan()
    {
        await using var fixture = new Fixture(CpuVendor.AMD, CpuArchitecture.AmdX3D);
        var otherActive = Guid.NewGuid();
        fixture.Platform.SchemeIds.Add(otherActive);
        fixture.Schemes.Active = otherActive;
        fixture.Settings.ThrowOnWriteNumber = 2;
        var draft = await fixture.Service.PrepareCreateAsync(fixture.SourceId, ManagedPowerProfileId.AmdGaming, default);

        var result = await fixture.Service.ExecuteAsync(draft, default);

        Assert.False(result.Success);
        Assert.Equal("create.compensated", result.Status);
        Assert.Equal(otherActive, fixture.Schemes.Active);
        Assert.DoesNotContain(draft.TargetPlanId, fixture.Platform.SchemeIds);
        Assert.Contains("delete", fixture.Platform.Events);
        Assert.NotNull(result.Backup);
    }

    [Fact]
    public async Task Update_UnmanagedWindowsPlan_IsRejectedBeforeBackupOrWrite()
    {
        await using var fixture = new Fixture(CpuVendor.Intel, CpuArchitecture.IntelStandard);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.PrepareUpdateAsync(
            fixture.SourceId, [new("cpu.minimum", 20, 20)], default));

        Assert.Contains("不是宇奇", error.Message);
        Assert.Empty(fixture.Platform.Events);
        Assert.Empty(fixture.Settings.Writes);
    }

    [Fact]
    public async Task Update_ManagedPlan_ExportsBeforeWrite_LeavesOriginalPlanUntouched()
    {
        await using var fixture = new Fixture(CpuVendor.Intel, CpuArchitecture.IntelStandard);
        var create = await fixture.Service.PrepareCreateAsync(fixture.SourceId, ManagedPowerProfileId.IntelGaming, default);
        Assert.True((await fixture.Service.ExecuteAsync(create, default)).Success);
        fixture.Platform.Events.Clear();
        fixture.Settings.Writes.Clear();

        var update = await fixture.Service.PrepareUpdateAsync(create.TargetPlanId,
            [new("cpu.minimum", 20, null), new("cpu.maximum", 100, null), new("cpu.epp", 10, null)], default);
        var result = await fixture.Service.ExecuteAsync(update, default);

        Assert.True(result.Success, result.Message);
        Assert.Equal("export", fixture.Platform.Events[0]);
        Assert.All(fixture.Settings.Writes, write => Assert.Equal(create.TargetPlanId, write.Scheme));
        Assert.DoesNotContain(fixture.Settings.Writes, write => write.Scheme == fixture.SourceId);
        Assert.NotNull(result.Backup);
    }

    [Fact]
    public async Task Update_RejectsMinimumAboveMaximumBeforeBackup()
    {
        await using var fixture = new Fixture(CpuVendor.Intel, CpuArchitecture.IntelStandard);
        var create = await fixture.Service.PrepareCreateAsync(fixture.SourceId, ManagedPowerProfileId.IntelGaming, default);
        Assert.True((await fixture.Service.ExecuteAsync(create, default)).Success);
        fixture.Platform.Events.Clear();

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.PrepareUpdateAsync(create.TargetPlanId,
            [new("cpu.minimum", 90, null), new("cpu.maximum", 20, null)], default));

        Assert.Contains("不能高于", error.Message);
        Assert.Empty(fixture.Platform.Events);
    }

    [Fact]
    public async Task Execute_TamperedOrReplayedDraft_IsRejectedBeforeMutation()
    {
        await using var fixture = new Fixture(CpuVendor.Intel, CpuArchitecture.IntelStandard);
        var draft = await fixture.Service.PrepareCreateAsync(fixture.SourceId, ManagedPowerProfileId.IntelGaming, default);
        var tampered = draft with { TargetName = "被修改" };

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.ExecuteAsync(tampered, default));
        Assert.Empty(fixture.Platform.Events);
        var result = await fixture.Service.ExecuteAsync(draft, default);
        Assert.True(result.Success);
        var eventCount = fixture.Platform.Events.Count;
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.ExecuteAsync(draft, default));
        Assert.Equal(eventCount, fixture.Platform.Events.Count);
    }

    [Fact]
    public async Task Inspect_OffersVendorProfileAndBlocksOtherVendorAndLaptopExtreme()
    {
        await using var fixture = new Fixture(CpuVendor.AMD, CpuArchitecture.AmdMultiCcd, hasBattery: true);

        var overview = await fixture.Service.InspectAsync(default);

        Assert.True(overview.Profiles.Single(item => item.Profile.Id == ManagedPowerProfileId.AmdGaming).IsAvailable);
        Assert.False(overview.Profiles.Single(item => item.Profile.Id == ManagedPowerProfileId.IntelGaming).IsAvailable);
        Assert.False(overview.Profiles.Single(item => item.Profile.Id == ManagedPowerProfileId.MaximumPerformance).IsAvailable);
    }

    [Fact]
    public async Task Delete_ActiveManagedPlan_ExportsThenReturnsToSourceAndRemovesOnlyClone()
    {
        await using var fixture = new Fixture(CpuVendor.Intel, CpuArchitecture.IntelStandard);
        var create = await fixture.Service.PrepareCreateAsync(fixture.SourceId, ManagedPowerProfileId.IntelGaming, default);
        Assert.True((await fixture.Service.ExecuteAsync(create, default)).Success);
        fixture.Platform.Events.Clear();

        var draft = await fixture.Service.PrepareDeleteAsync(create.TargetPlanId, default);
        var result = await fixture.Service.ExecuteAsync(draft, default);

        Assert.True(result.Success, result.Message);
        Assert.Equal("deleted.verified", result.Status);
        Assert.Equal("export", fixture.Platform.Events[0]);
        Assert.Contains("delete", fixture.Platform.Events);
        Assert.Equal(fixture.SourceId, fixture.Schemes.Active);
        Assert.Contains(fixture.SourceId, fixture.Platform.SchemeIds);
        Assert.DoesNotContain(create.TargetPlanId, fixture.Platform.SchemeIds);
        var stored = await fixture.Store.LoadAsync(default);
        Assert.DoesNotContain(stored.Plans, item => item.PlanId == create.TargetPlanId);
        Assert.Contains(stored.Backups, item => item.BackupId == result.Backup!.BackupId);
    }

    [Fact]
    public async Task Delete_WhenPlatformRejectsRemoval_RestoresPreviousActivePlanAndKeepsManagedRegistration()
    {
        await using var fixture = new Fixture(CpuVendor.Intel, CpuArchitecture.IntelStandard);
        var create = await fixture.Service.PrepareCreateAsync(fixture.SourceId, ManagedPowerProfileId.IntelGaming, default);
        Assert.True((await fixture.Service.ExecuteAsync(create, default)).Success);
        fixture.Platform.Events.Clear();
        fixture.Platform.FailDelete = true;

        var draft = await fixture.Service.PrepareDeleteAsync(create.TargetPlanId, default);
        var result = await fixture.Service.ExecuteAsync(draft, default);

        Assert.False(result.Success);
        Assert.Equal("delete.compensated", result.Status);
        Assert.Equal(create.TargetPlanId, fixture.Schemes.Active);
        Assert.Contains(create.TargetPlanId, fixture.Platform.SchemeIds);
        var stored = await fixture.Store.LoadAsync(default);
        Assert.Contains(stored.Plans, item => item.PlanId == create.TargetPlanId);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "Yuqi-ManagedPowerTests", Guid.NewGuid().ToString("N"));

        public Fixture(CpuVendor vendor, CpuArchitecture architecture, bool hasBattery = false)
        {
            Directory.CreateDirectory(_root);
            SourceId = Guid.NewGuid();
            Platform = new FakeManagedPlatform(SourceId);
            Settings = new FakeSettings(Platform);
            Schemes = new FakeSchemes(SourceId);
            Store = new FileManagedPowerPlanStore(_root, new PassThroughProtector());
            var topology = new CpuTopology
            {
                Vendor = vendor,
                Architecture = architecture,
                CpuName = vendor + " test CPU",
                ProcessorGroupCount = 1,
                IsDetailedTopologyReliable = true
            };
            Service = new ManagedPowerPlanService(
                Platform,
                Store,
                new FakePowerPlatform(Platform, Schemes),
                Settings,
                new AllowGuard(),
                Schemes,
                new FakeFacts(hasBattery),
                new CpuTopologyService(),
                topologyReader: () => topology);
        }

        public Guid SourceId { get; }
        public FakeManagedPlatform Platform { get; }
        public FakeSettings Settings { get; }
        public FakeSchemes Schemes { get; }
        public FileManagedPowerPlanStore Store { get; }
        public ManagedPowerPlanService Service { get; }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeManagedPlatform : IManagedPowerPlanPlatform
    {
        public FakeManagedPlatform(Guid source) => SchemeIds.Add(source);
        public HashSet<Guid> SchemeIds { get; } = [];
        public List<string> Events { get; } = [];
        public Action<Guid, Guid>? Duplicated { get; set; }
        public bool FailDelete { get; set; }

        public Task<ManagedPowerCommandResult> ExportAsync(Guid schemeId, string destinationPath, CancellationToken cancellationToken)
        {
            Events.Add("export");
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.WriteAllText(destinationPath, $"backup:{schemeId:D}");
            return Ok();
        }

        public Task<ManagedPowerCommandResult> DuplicateAsync(Guid sourceSchemeId, Guid targetSchemeId, CancellationToken cancellationToken)
        {
            Events.Add("duplicate");
            SchemeIds.Add(targetSchemeId);
            Duplicated?.Invoke(sourceSchemeId, targetSchemeId);
            return Ok();
        }

        public Task<ManagedPowerCommandResult> RenameAsync(Guid schemeId, string name, CancellationToken cancellationToken)
        {
            Events.Add("rename");
            return Ok();
        }

        public Task<ManagedPowerCommandResult> DeleteAsync(Guid schemeId, CancellationToken cancellationToken)
        {
            Events.Add("delete");
            if (FailDelete)
                return Task.FromResult(new ManagedPowerCommandResult(false, 1, string.Empty, "simulated delete failure"));
            SchemeIds.Remove(schemeId);
            return Ok();
        }

        public Task<ManagedPowerCommandResult> ImportAsync(string sourcePath, Guid schemeId, CancellationToken cancellationToken)
        {
            Events.Add("import");
            SchemeIds.Add(schemeId);
            return Ok();
        }

        private static Task<ManagedPowerCommandResult> Ok() => Task.FromResult(new ManagedPowerCommandResult(true, 0, string.Empty, string.Empty));
    }

    private sealed class FakeSettings : IPowerSettingAccessor
    {
        private readonly Dictionary<(Guid Scheme, Guid Subgroup, Guid Setting), PowerSettingValueState> _values = [];
        private int _writeCount;

        public FakeSettings(FakeManagedPlatform platform)
        {
            foreach (var definition in ManagedPowerPlanCatalog.Settings)
                _values[(platform.SchemeIds.Single(), definition.SubgroupGuid, definition.SettingGuid)] = new(definition.SubgroupGuid, definition.SettingGuid, 50, 50);
            platform.Duplicated = (source, target) =>
            {
                foreach (var pair in _values.Where(item => item.Key.Scheme == source).ToArray())
                    _values[(target, pair.Key.Subgroup, pair.Key.Setting)] = pair.Value;
            };
        }

        public List<(Guid Scheme, Guid Setting)> Writes { get; } = [];
        public int? ThrowOnWriteNumber { get; set; }
        public Task<Guid> GetActiveSchemeAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PowerSettingValueState> ReadValueAsync(Guid scheme, Guid subgroup, Guid setting, CancellationToken cancellationToken) =>
            Task.FromResult(_values.TryGetValue((scheme, subgroup, setting), out var value)
                ? value
                : new PowerSettingValueState(subgroup, setting, null, null));

        public Task WriteValueAsync(Guid scheme, Guid subgroup, Guid setting, int? acValue, int? dcValue, CancellationToken cancellationToken)
        {
            _writeCount++;
            Writes.Add((scheme, setting));
            if (ThrowOnWriteNumber == _writeCount)
                throw new IOException("simulated write failure");
            _values[(scheme, subgroup, setting)] = new(subgroup, setting, acValue, dcValue);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSchemes(Guid active) : IPowerSchemeAccessor
    {
        public Guid Active { get; set; } = active;
        public Task<Guid> GetActiveSchemeAsync(CancellationToken cancellationToken) => Task.FromResult(Active);
        public Task SetActiveSchemeAsync(Guid scheme, CancellationToken cancellationToken) { Active = scheme; return Task.CompletedTask; }
    }

    private sealed class FakePowerPlatform(FakeManagedPlatform platform, FakeSchemes schemes) : IPowerPlatform
    {
        public Task<PowerEnvironment> GetEnvironmentAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<PowerScheme>> EnumerateSchemesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PowerScheme>>(
            platform.SchemeIds.Select(id => new PowerScheme(id, id.ToString("D"), id == schemes.Active)).ToArray());
        public Task<Guid> GetActiveSchemeAsync(CancellationToken cancellationToken) => Task.FromResult(schemes.Active);
        public Task<PowerSettingCapability> GetDisplayTimeoutCapabilityAsync(Guid schemeId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PowerSettingValues> ReadDisplayTimeoutAsync(Guid schemeId, bool includeDc, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PowerMutationResult> ApplyDisplayTimeoutAsync(Guid schemeId, PowerSettingValues values, Guid schemeToActivate, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class AllowGuard : IPowerSettingCapabilityGuard
    {
        public Task<PowerSettingGuardResult> EvaluateAsync(Guid scheme, Guid subgroup, Guid setting, int? targetAc, int? targetDc,
            string? operationId = null, string? label = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PowerSettingGuardResult { Decision = PowerSettingGuardDecision.Allowed });
    }

    private sealed class FakeFacts(bool hasBattery) : IPowerPlatformFactsProvider
    {
        public PowerPlatformFacts GetCurrent() => new(true, 26100, "test", true, hasBattery, true, false, false, false, 1, true);
    }

    private sealed class PassThroughProtector : IPowerDataProtector
    {
        public byte[] Protect(byte[] plaintext) => plaintext.ToArray();
        public byte[] Unprotect(byte[] protectedData) => protectedData.ToArray();
    }
}
