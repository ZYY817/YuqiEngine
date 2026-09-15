using System.Collections.Concurrent;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.Changes;
using GameOptimizer.Core.Services.Repair;
using GameOptimizer.Infrastructure.Windows.Registry;

namespace GameOptimizer.Tests.Changes;

/// <summary>
/// Fake end-to-end regression for the mixed-type Advertising ID Repair entry.
/// </summary>
public sealed class AdvertisingIdRepairCompatibilityRegressionTests
{
    [Theory]
    [InlineData("Windows 10", 19041, false)]
    [InlineData("Windows 10", 19041, true)]
    [InlineData("Windows 11", 22631, false)]
    [InlineData("Windows 11", 22631, true)]
    public async Task AdvertisingId_RealRepairEntry_AppliesRepeatsAndRestoresMixedState(
        string edition,
        int build,
        bool isAdministrator)
    {
        var harness = Harness.Create(edition, build, isAdministrator);

        Assert.False(harness.Capability.Descriptor.RequiresAdministrator);
        Assert.False(harness.Capability.Descriptor.RequiresRestart);

        var applied = await harness.Service.RepairAsync(harness.Item);

        Assert.True(applied.Success, applied.Message);
        Assert.False(applied.RequiresReboot);
        Assert.Equal(2, harness.Registry.MutationCount);
        Assert.Equal(new RegistryValueSetEntryState(
            "Enabled", true, true, "DWord", null, 1), harness.Registry.Value("Enabled"));
        Assert.Equal(new RegistryValueSetEntryState(
            "Id", true, false, null, null, null), harness.Registry.Value("Id"));
        var receipt = Assert.IsType<RepairChangeReceipt>(applied.ChangeReceipt);
        Assert.Single(receipt.Operations);

        var repeated = await harness.Service.RepairAsync(harness.Item);

        Assert.True(repeated.Success, repeated.Message);
        Assert.False(repeated.RequiresReboot);
        Assert.Equal(2, harness.Registry.MutationCount);
        Assert.Empty(Assert.IsType<RepairChangeReceipt>(repeated.ChangeReceipt).Operations);

        var restored = await harness.Service.RestoreAsync(
            new RepairRecoveryRequest(harness.Item.Id, receipt));

        Assert.True(restored.Success, restored.Message);
        Assert.False(restored.RequiresReboot);
        Assert.Equal(new RegistryValueSetEntryState(
            "Enabled", true, true, "DWord", null, 0), harness.Registry.Value("Enabled"));
        Assert.Equal(new RegistryValueSetEntryState(
            "Id", true, true, "String", "existing-id", null), harness.Registry.Value("Id"));
        Assert.Equal(4, harness.Registry.MutationCount);

        var replayed = await harness.Service.RestoreAsync(
            new RepairRecoveryRequest(harness.Item.Id, receipt));

        Assert.False(replayed.Success);
        Assert.Contains("恢复材料不存在", replayed.Message, StringComparison.Ordinal);
        Assert.Equal(4, harness.Registry.MutationCount);
    }

    [Fact]
    public async Task AdvertisingId_OuterPolicyGate_BlocksBeforeRegistryProbe()
    {
        var harness = Harness.Create(
            "Windows 11",
            22631,
            isAdministrator: false,
            repairGate: new FixedRepairGate(
                new RepairExecutionGateDecision(
                    RepairExecutionGateDecisionKind.BlockedNeedBaseline,
                    "policy-managed")));

        var result = await harness.Service.RepairAsync(harness.Item);

        Assert.False(result.Success);
        Assert.Equal("policy-managed", result.Message);
        Assert.Equal(0, harness.Registry.ReadCount);
        Assert.Equal(0, harness.Registry.MutationCount);
    }

    [Fact]
    public async Task AdvertisingId_CancellationBeforeEntry_IsZeroRegistryMutation()
    {
        var harness = Harness.Create("Windows 10", 19041, isAdministrator: false);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await harness.Service.RepairAsync(
            harness.Item,
            cancellationToken: cancellation.Token);

        Assert.False(result.Success);
        Assert.Equal(0, harness.Registry.ReadCount);
        Assert.Equal(0, harness.Registry.MutationCount);
    }

    internal sealed class Harness
    {
        private Harness(
            FakeRegistryAccessor registry,
            RegistryValueSetCapability capability,
            RepairService service,
            RepairItem item)
        {
            Registry = registry;
            Capability = capability;
            Service = service;
            Item = item;
        }

        public FakeRegistryAccessor Registry { get; }
        public RegistryValueSetCapability Capability { get; }
        public RepairService Service { get; }
        public RepairItem Item { get; }

        public static Harness Create(
            string edition,
            int build,
            bool isAdministrator,
            IRepairExecutionGate? repairGate = null)
        {
            var registry = new FakeRegistryAccessor();
            var capability = new RegistryValueSetCapability(
                RegistryValueSetCapabilityDefinitions.DisableAdvertisingId,
                registry);
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
            var service = new RepairService(
                new RepairItemRegistry(),
                repairGate ?? new FixedRepairGate(RepairExecutionGateDecision.Allowed()),
                executor);
            var item = Assert.Single(
                service.GetAllRepairItems(),
                candidate => candidate.Id == "repair.advertising_id");
            return new Harness(registry, capability, service, item);
        }
    }

    internal sealed class FakeRegistryAccessor : IRegistryValueSetAccessor
    {
        private readonly Dictionary<string, RegistryValueSetEntryState> _states = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Enabled"] = new("Enabled", true, true, "DWord", null, 0),
            ["Id"] = new("Id", true, true, "String", "existing-id", null)
        };

        public int ReadCount { get; private set; }
        public int MutationCount { get; private set; }

        public RegistryValueSetEntryState Value(string valueName) => _states[valueName];

        public Task<IReadOnlyList<RegistryValueSetEntryState>> ReadCurrentUserAsync(
            string subKeyPath,
            IReadOnlyList<string> valueNames,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            return Task.FromResult<IReadOnlyList<RegistryValueSetEntryState>>(
                valueNames.Select(valueName => _states[valueName]).ToArray());
        }

        public Task WriteCurrentUserValueAsync(
            string subKeyPath,
            RegistryTypedTargetValue target,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MutationCount++;
            _states[target.ValueName] = target.Kind == RegistryScalarValueKind.DWord
                ? new RegistryValueSetEntryState(target.ValueName, true, true, "DWord", null, target.DwordValue)
                : new RegistryValueSetEntryState(target.ValueName, true, true, "String", target.StringValue, null);
            return Task.CompletedTask;
        }

        public Task RestoreCurrentUserValueAsync(
            string subKeyPath,
            RegistryValueSetEntryState original,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MutationCount++;
            _states[original.ValueName] = original;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RegistryValueSetEntryState>> ReadLocalMachineAsync(
            string subKeyPath,
            IReadOnlyList<string> valueNames,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task WriteLocalMachineValueAsync(
            string subKeyPath,
            RegistryTypedTargetValue target,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RestoreLocalMachineValueAsync(
            string subKeyPath,
            RegistryValueSetEntryState original,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    internal sealed class FixedRepairGate(RepairExecutionGateDecision decision) : IRepairExecutionGate
    {
        public Task<RepairExecutionGateDecision> EnsureCanExecuteAsync(
            string repairItemId,
            RepairExecutionIntent intent,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(decision);
        }
    }

    private sealed class AllowPrompt : IChangeConfirmationPrompt
    {
        public Task<bool> ConfirmAsync(
            ChangeConfirmationPrompt prompt,
            CancellationToken cancellationToken) =>
            Task.FromResult(true);
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

    private sealed class MemoryJournal : IChangeJournal
    {
        public Task AppendAsync(JournalEntry entry, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
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

    private sealed class MemoryRecoveryClaimStore : IRecoveryClaimStore
    {
        private readonly Dictionary<Guid, (string IntegrityHash, RecoveryClaimStatus Status)> _states = [];
        private readonly object _sync = new();

        public Task RegisterAvailableAsync(Guid recoveryId, string integrityHash, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                if (!_states.TryGetValue(recoveryId, out var state)
                    || state.Status == RecoveryClaimStatus.Missing)
                {
                    _states[recoveryId] = (integrityHash, RecoveryClaimStatus.Claimed);
                }
            }
            return Task.CompletedTask;
        }

        public Task<RecoveryClaimResult> TryBeginRestoreAsync(Guid recoveryId, string integrityHash, CancellationToken cancellationToken)
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

        public Task MarkRestoredAsync(Guid recoveryId, string integrityHash, Guid claimId, CancellationToken cancellationToken)
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

        public Task ReleaseAsync(Guid recoveryId, string integrityHash, Guid claimId, CancellationToken cancellationToken)
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
