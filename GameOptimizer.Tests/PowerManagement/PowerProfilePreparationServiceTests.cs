// File: GameOptimizer.Tests/PowerManagement/PowerProfilePreparationServiceTests.cs
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Models;
using GameOptimizer.Core.Services;
using GameOptimizer.Infrastructure.Windows.PowerManagement;

namespace GameOptimizer.Tests.PowerManagement;

public sealed class PowerProfilePreparationServiceTests
{
    [Fact]
    public async Task ProbeAndPrepare_CreateOnlyPendingPlans_AndNeverExecutePlatformWrites()
    {
        var orchestrator = new RecordingOrchestrator();
        var service = CreateService(orchestrator);

        var preflight = await service.ProbeAsync(CancellationToken.None);
        var gaming = Assert.Single(preflight.Candidates, item => item.Candidate.Id == PowerProfileId.GamingConservative);
        Assert.True(gaming.CanPrepare);
        Assert.All(gaming.Capabilities, item => Assert.Equal("可准备", item.Status));

        var prepared = await service.PrepareAsync(PowerProfileId.GamingConservative, CancellationToken.None);

        Assert.True(prepared.HasPreparedPlans);
        Assert.Equal(gaming.Capabilities.Count, orchestrator.PrepareCalls);
        Assert.Equal(0, orchestrator.ExecuteCalls);
        Assert.Contains("不会切换计划或写入", prepared.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnavailableProfile_DoesNotPrepareAnyCapability()
    {
        var orchestrator = new RecordingOrchestrator();
        var service = CreateService(orchestrator, onBattery: true);

        var prepared = await service.PrepareAsync(PowerProfileId.GamingConservative, CancellationToken.None);

        Assert.True(prepared.IsBlocked);
        Assert.Empty(prepared.Items);
        Assert.Equal(0, orchestrator.PrepareCalls);
        Assert.Equal(0, orchestrator.ExecuteCalls);
    }

    private static PowerProfilePreparationService CreateService(RecordingOrchestrator orchestrator, bool onBattery = false)
    {
        var capabilities = PowerSettingCapabilityDefinitions.All
            .Select(definition => (IChangeCapability)new ApplicableCapability(definition.CapabilityId, definition.ResourceKey))
            .ToArray();
        var facts = new FakeFacts(onBattery);
        return new PowerProfilePreparationService(
            capabilities,
            orchestrator,
            facts,
            new FakeSchemeAccessor(),
            new CpuTopologyService(),
            topologyReader: () => new CpuTopology
            {
                CpuName = "Test CPU",
                Vendor = CpuVendor.Intel,
                Architecture = CpuArchitecture.IntelStandard,
                LogicalCores = 8,
                ProcessorGroupCount = 1
            });
    }

    private sealed class FakeFacts(bool onBattery) : IPowerPlatformFactsProvider
    {
        public PowerPlatformFacts GetCurrent() => new(
            true, 22631, "test-machine", true, onBattery, !onBattery, false, false, false, 1, true);
    }

    private sealed class FakeSchemeAccessor : IPowerSchemeAccessor
    {
        public Task<Guid> GetActiveSchemeAsync(CancellationToken cancellationToken) => Task.FromResult(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        public Task SetActiveSchemeAsync(Guid scheme, CancellationToken cancellationToken) => throw new InvalidOperationException("This test must not write a scheme.");
    }

    private sealed class ApplicableCapability(string id, string resourceKey) : IChangeCapability
    {
        public CapabilityDescriptor Descriptor { get; } = new(id, "test", ChangeRisk.LowReversible,
            [TargetScopeKind.Machine], true, true, true, false, resourceKey);

        public Task<CapabilityAssessment> ProbeAsync(TargetScope scope, CancellationToken cancellationToken) =>
            Task.FromResult(new CapabilityAssessment(CapabilityApplicability.Applicable, true, "applicable", "safe to prepare"));

        public Task<StateObservation> ReadAsync(TargetScope scope, CancellationToken cancellationToken) =>
            Task.FromResult(new StateObservation(scope, "before", "before", DateTimeOffset.UtcNow, "test", "authoritative"));

        public Task<PreparedCapabilityChange> PrepareAsync(StateObservation current, string targetPayload, CancellationToken cancellationToken) =>
            Task.FromResult(new PreparedCapabilityChange(Descriptor.CapabilityId, Descriptor.DefinitionVersion, current.Scope,
                current.Payload, current.Fingerprint, targetPayload, "after", Descriptor.ResourceKey));

        public Task<PlatformExecutionResult> ExecuteAsync(PreparedCapabilityChange change, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("This test must not execute a platform write.");

        public Task<PlatformExecutionResult> RestoreAsync(RecoveryMaterial material, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("This test must not restore a platform write.");
    }

    private sealed class RecordingOrchestrator : IChangeOrchestrator
    {
        public int PrepareCalls { get; private set; }
        public int ExecuteCalls { get; private set; }

        public Task<StateObservation?> ObserveAsync(string capabilityId, TargetScope scope, CancellationToken cancellationToken) =>
            Task.FromResult<StateObservation?>(null);

        public Task<PreparationResult> PrepareAsync(ChangeRequest request, CancellationToken cancellationToken)
        {
            PrepareCalls++;
            var change = new PreparedCapabilityChange(request.CapabilityId, "test", request.Scope, "before", "before", "apply", "after", "test");
            var plan = new PreparedChangePlan(Guid.NewGuid(), change, "test", DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddMinutes(5), true, true, true, ChangeRisk.LowReversible);
            return Task.FromResult(new PreparationResult(ChangePreparationStatus.Ready, plan, "ready", "prepared", null));
        }

        public Task<BatchReceipt> ExecuteAsync(PreparedChangePlan plan, ConfirmationToken confirmation, CancellationToken cancellationToken)
        {
            ExecuteCalls++;
            throw new InvalidOperationException("This test must not execute a platform write.");
        }

        public Task<BatchReceipt> RestoreAsync(RestoreRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("This test must not restore a platform write.");
    }
}
