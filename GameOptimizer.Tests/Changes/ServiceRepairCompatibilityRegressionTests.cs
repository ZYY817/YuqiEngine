using System.Collections.Concurrent;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.Changes;
using GameOptimizer.Core.Services.Repair;
using GameOptimizer.Core.Services.SystemServices;

namespace GameOptimizer.Tests.Changes;

/// <summary>
/// Compatibility regression for the real RepairService -> typed service owner entry.
/// All platform calls are fake; this suite never opens SCM or mutates the host.
/// </summary>
public sealed class ServiceRepairCompatibilityRegressionTests
{
    [Theory]
    [InlineData("Windows 10", 19041)]
    [InlineData("Windows 11", 22000)]
    public async Task AppInfo_RealRepairEntry_IsRepeatableAndRestoresExactState(
        string osProfile,
        int build)
    {
        var harness = Harness.Create(osProfile, build);
        var item = harness.Item;

        var applied = await harness.Service.RepairAsync(item);

        Assert.True(applied.Success, applied.Message);
        Assert.Equal((osProfile, build), harness.Profile);
        Assert.Equal(WindowsServiceRunState.Running, harness.Accessor.Current.RunState);
        Assert.Equal(WindowsServiceStartType.Manual, harness.Accessor.Current.StartType);
        Assert.Equal(1, harness.Accessor.ApplyCount);
        var receipt = Assert.IsType<RepairChangeReceipt>(applied.ChangeReceipt);
        Assert.Single(receipt.Operations);
        Assert.Equal(RepairChangeCapabilityIds.AppInfoEnsureRunning, receipt.Operations[0].CapabilityId);

        var repeated = await harness.Service.RepairAsync(item);

        Assert.True(repeated.Success, repeated.Message);
        Assert.Equal(1, harness.Accessor.ApplyCount);
        Assert.Empty(Assert.IsType<RepairChangeReceipt>(repeated.ChangeReceipt).Operations);

        var restored = await harness.Service.RestoreAsync(
            new RepairRecoveryRequest(item.Id, receipt));

        Assert.True(restored.Success, restored.Message);
        Assert.Equal(WindowsServiceRunState.Stopped, harness.Accessor.Current.RunState);
        Assert.Equal(WindowsServiceStartType.Manual, harness.Accessor.Current.StartType);
        Assert.Equal(1, harness.Accessor.RestoreCount);

        var replayedRestore = await harness.Service.RestoreAsync(
            new RepairRecoveryRequest(item.Id, receipt));

        Assert.False(replayedRestore.Success);
        Assert.Contains("恢复材料不存在", replayedRestore.Message, StringComparison.Ordinal);
        Assert.Equal(1, harness.Accessor.RestoreCount);
    }

    [Theory]
    [InlineData(false, false, "permission")]
    [InlineData(true, true, "policy")]
    public async Task AppInfo_RealRepairEntry_ClassifiesPermissionAndPolicyWithoutWrite(
        bool hasPermission,
        bool managedByPolicy,
        string expectedMarker)
    {
        var harness = Harness.Create("Windows 11", 22000);
        harness.Accessor.Assessment = new ServicePlatformAssessment(
            Exists: true,
            HasRequiredPermission: hasPermission,
            ManagedByPolicy: managedByPolicy,
            ProtectedService: false,
            ReasonCode: managedByPolicy ? "service.policy-managed" : "service.permission-denied",
            Details: expectedMarker);

        var result = await harness.Service.RepairAsync(harness.Item);

        Assert.False(result.Success);
        Assert.Contains(expectedMarker, result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, harness.Accessor.ApplyCount);
        Assert.Equal(1, harness.Accessor.ProbeCount);
    }

    [Fact]
    public async Task AppInfo_RealRepairEntry_CancellationIsZeroWrite()
    {
        var harness = Harness.Create("Windows 10", 19041);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await harness.Service.RepairAsync(
            harness.Item,
            cancellationToken: cancellation.Token);

        Assert.False(result.Success);
        Assert.Equal(0, harness.Accessor.ProbeCount);
        Assert.Equal(0, harness.Accessor.ApplyCount);
        Assert.Equal(0, harness.Accessor.RestoreCount);
    }

    [Fact]
    public async Task AudioService_RealRepairEntry_OnlyEnsuresRunningAndRestoresExactState()
    {
        var harness = Harness.Create(
            "Windows 11",
            22000,
            "Audiosrv",
            RepairChangeCapabilityIds.AudioServiceEnsureRunning,
            "repair.audio_service");

        var applied = await harness.Service.RepairAsync(harness.Item);

        Assert.True(applied.Success, applied.Message);
        Assert.Equal(WindowsServiceRunState.Running, harness.Accessor.Current.RunState);
        Assert.Equal(WindowsServiceStartType.Manual, harness.Accessor.Current.StartType);
        Assert.Single(harness.Accessor.AppliedTargets);
        Assert.Equal(ServiceChangeAction.EnsureRunning, harness.Accessor.AppliedTargets[0].Action);
        var receipt = Assert.IsType<RepairChangeReceipt>(applied.ChangeReceipt);
        Assert.Equal(RepairChangeCapabilityIds.AudioServiceEnsureRunning, Assert.Single(receipt.Operations).CapabilityId);

        var restored = await harness.Service.RestoreAsync(new RepairRecoveryRequest(harness.Item.Id, receipt));

        Assert.True(restored.Success, restored.Message);
        Assert.Equal(WindowsServiceRunState.Stopped, harness.Accessor.Current.RunState);
        Assert.Equal(WindowsServiceStartType.Manual, harness.Accessor.Current.StartType);
    }

    private sealed class Harness
    {
        private Harness(
            string osProfile,
            int build,
            FakeServiceAccessor accessor,
            RepairService service,
            RepairItem item)
        {
            Profile = (osProfile, build);
            Accessor = accessor;
            Service = service;
            Item = item;
        }

        public (string Os, int Build) Profile { get; }
        public FakeServiceAccessor Accessor { get; }
        public RepairService Service { get; }
        public RepairItem Item { get; }

        public static Harness Create(
            string osProfile,
            int build,
            string serviceName = "Appinfo",
            string capabilityId = RepairChangeCapabilityIds.AppInfoEnsureRunning,
            string repairItemId = "repair.appinfo")
        {
            var accessor = new FakeServiceAccessor(
                new ServiceStateSnapshot(
                    serviceName,
                    Exists: true,
                    WindowsServiceRunState.Stopped,
                    WindowsServiceStartType.Manual));
            var capability = new ServiceChangeCapability(
                new ServiceChangeCapabilityDefinition(
                    capabilityId,
                    "1.0.0",
                    serviceName,
                    ServiceChangeAction.EnsureRunning),
                accessor);
            var confirmation = new ChangeConfirmationAuthority(new AllowPrompt());
            var orchestrator = new ChangeOrchestrator(
                [capability],
                new MemoryRecoveryRepository(),
                new MemoryJournal(),
                new MemoryAuditStore(),
                confirmationAuthority: confirmation,
                recoveryClaimStore: new MemoryRecoveryClaimStore(),
                executionGate: ChangeExecutionGateTestDouble.Allowed());
            var executor = new OrchestratedRepairChangeExecutor(orchestrator, confirmation);
            var service = new RepairService(new RepairItemRegistry(), new AllowRepairGate(), executor);
            var item = Assert.Single(service.GetAllRepairItems(), candidate => candidate.Id == repairItemId);
            return new Harness(osProfile, build, accessor, service, item);
        }
    }

    private sealed class AllowRepairGate : IRepairExecutionGate
    {
        public Task<RepairExecutionGateDecision> EnsureCanExecuteAsync(
            string repairItemId,
            RepairExecutionIntent intent,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(RepairExecutionGateDecision.Allowed());
        }
    }

    private sealed class AllowPrompt : IChangeConfirmationPrompt
    {
        public Task<bool> ConfirmAsync(
            ChangeConfirmationPrompt prompt,
            CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class FakeServiceAccessor(ServiceStateSnapshot initial) : IWindowsServiceAccessor
    {
        public ServiceStateSnapshot Current { get; set; } = initial;
        public ServicePlatformAssessment Assessment { get; set; } =
            new(true, true, false, false, "applicable", "fake service");
        public int ProbeCount { get; private set; }
        public int ApplyCount { get; private set; }
        public int RestoreCount { get; private set; }
        public List<ServiceChangeTarget> AppliedTargets { get; } = [];

        public Task<ServicePlatformAssessment> ProbeAsync(
            string serviceName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProbeCount++;
            return Task.FromResult(Assessment);
        }

        public Task<ServiceStateSnapshot> ReadAsync(
            string serviceName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Current);
        }

        public Task<ServicePlatformMutationResult> ApplyAsync(
            string serviceName,
            ServiceChangeTarget target,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApplyCount++;
            AppliedTargets.Add(target);
            var desired = target.Action == ServiceChangeAction.EnsureRunning
                ? WindowsServiceRunState.Running
                : WindowsServiceRunState.Stopped;
            var changed = Current.RunState != desired || Current.StartType != target.StartType;
            Current = Current with { RunState = desired, StartType = target.StartType };
            return Task.FromResult(new ServicePlatformMutationResult(true, changed, "ok", "fake apply"));
        }

        public Task<ServicePlatformMutationResult> RestoreAsync(
            ServiceStateSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RestoreCount++;
            var changed = Current != snapshot;
            Current = snapshot;
            return Task.FromResult(new ServicePlatformMutationResult(true, changed, "ok", "fake restore"));
        }
    }

    private sealed class MemoryRecoveryRepository : IRecoveryRepository
    {
        private readonly ConcurrentDictionary<Guid, RecoveryMaterial> _materials = new();

        public Task SaveAsync(RecoveryMaterial material, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _materials[material.RecoveryId] = material;
            return Task.CompletedTask;
        }

        public Task<RecoveryMaterial?> LoadAsync(Guid recoveryId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_materials.GetValueOrDefault(recoveryId));
        }
    }

    private sealed class MemoryAuditStore : IChangeAuditStore
    {
        private readonly ConcurrentDictionary<Guid, OperationReceipt> _receipts = new();

        public Task SaveAsync(OperationReceipt receipt, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _receipts[receipt.ReceiptId] = receipt;
            return Task.CompletedTask;
        }

        public Task<OperationReceipt?> LoadAsync(Guid receiptId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_receipts.GetValueOrDefault(receiptId));
        }
    }

    private sealed class MemoryJournal : IChangeJournal
    {
        public Task AppendAsync(JournalEntry entry, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryRecoveryClaimStore : IRecoveryClaimStore
    {
        private readonly Dictionary<Guid, (string IntegrityHash, RecoveryClaimStatus Status)> _states = [];
        private readonly object _sync = new();

        public Task RegisterAvailableAsync(
            Guid recoveryId,
            string integrityHash,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                if (!_states.TryGetValue(recoveryId, out var state)
                    || state.Status is RecoveryClaimStatus.Missing)
                {
                    _states[recoveryId] = (integrityHash, RecoveryClaimStatus.Claimed);
                }
            }

            return Task.CompletedTask;
        }

        public Task<RecoveryClaimResult> TryBeginRestoreAsync(
            Guid recoveryId,
            string integrityHash,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                if (!_states.TryGetValue(recoveryId, out var state))
                    return Task.FromResult(new RecoveryClaimResult(RecoveryClaimStatus.Missing, null));
                if (!string.Equals(state.IntegrityHash, integrityHash, StringComparison.Ordinal))
                    return Task.FromResult(new RecoveryClaimResult(RecoveryClaimStatus.IntegrityMismatch, null));
                if (state.Status == RecoveryClaimStatus.AlreadyRestored)
                    return Task.FromResult(new RecoveryClaimResult(RecoveryClaimStatus.AlreadyRestored, null));

                _states[recoveryId] = (state.IntegrityHash, RecoveryClaimStatus.AlreadyRestoring);
                return Task.FromResult(new RecoveryClaimResult(RecoveryClaimStatus.Claimed, Guid.NewGuid()));
            }
        }

        public Task MarkRestoredAsync(
            Guid recoveryId,
            string integrityHash,
            Guid claimId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                if (_states.TryGetValue(recoveryId, out var state)
                    && string.Equals(state.IntegrityHash, integrityHash, StringComparison.Ordinal))
                {
                    _states[recoveryId] = (state.IntegrityHash, RecoveryClaimStatus.AlreadyRestored);
                }
            }

            return Task.CompletedTask;
        }

        public Task ReleaseAsync(
            Guid recoveryId,
            string integrityHash,
            Guid claimId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                if (_states.TryGetValue(recoveryId, out var state)
                    && string.Equals(state.IntegrityHash, integrityHash, StringComparison.Ordinal))
                {
                    _states[recoveryId] = (state.IntegrityHash, RecoveryClaimStatus.Claimed);
                }
            }

            return Task.CompletedTask;
        }
    }
}
