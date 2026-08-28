using System.Collections.Concurrent;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.Changes;
using GameOptimizer.Core.Services.Repair;
using GameOptimizer.Infrastructure.Windows.Registry;

namespace GameOptimizer.Tests.Changes;

/// <summary>
/// End-to-end fake regression for the existing bidirectional fast-startup Repair entry.
/// The registry accessor is in-memory; the host registry is never opened.
/// </summary>
public sealed class FastStartupRepairCompatibilityRegressionTests
{
    [Theory]
    [InlineData("Windows 10", 19041)]
    [InlineData("Windows 11", 22631)]
    public async Task DiagnosticData_RealRepairEntry_AppliesRepeatsAndRestoresExactly(
        string edition,
        int build)
    {
        var harness = Harness.Create(
            build,
            edition,
            isAdministrator: true,
            definition: RepairRegistryCapabilityDefinitions.DiagnosticDataReset,
            repairItemId: "repair.diagnostic_data");

        var applied = await harness.Service.RepairAsync(harness.Item);

        Assert.True(applied.Success, applied.Message);
        Assert.Equal(1, harness.Registry.WriteCount);
        Assert.Equal(3, harness.Registry.State.Value);
        var receipt = Assert.IsType<RepairChangeReceipt>(applied.ChangeReceipt);

        var repeated = await harness.Service.RepairAsync(harness.Item);

        Assert.True(repeated.Success, repeated.Message);
        Assert.Equal(1, harness.Registry.WriteCount);

        var restored = await harness.Service.RestoreAsync(
            new RepairRecoveryRequest(harness.Item.Id, receipt));

        Assert.True(restored.Success, restored.Message);
        Assert.Equal(new RegistryDwordState(true, true, 0), harness.Registry.State);
        Assert.Equal(1, harness.Registry.RestoreCount);
    }

    [Fact]
    public async Task DiagnosticData_NonAdministrator_BlocksBeforeRegistryWrite()
    {
        var harness = Harness.Create(
            22631,
            "Windows 11 Pro",
            isAdministrator: false,
            definition: RepairRegistryCapabilityDefinitions.DiagnosticDataReset,
            repairItemId: "repair.diagnostic_data");

        var result = await harness.Service.RepairAsync(harness.Item);

        Assert.False(result.Success);
        Assert.Equal(0, harness.Registry.WriteCount);
        Assert.Equal(0, harness.Registry.RestoreCount);
    }

    [Fact]
    public async Task DiagnosticData_PolicyGate_BlocksBeforeRegistryProbe()
    {
        var harness = Harness.Create(
            22631,
            "Windows 11 Pro",
            isAdministrator: true,
            repairGate: new FixedRepairGate(
                new RepairExecutionGateDecision(
                    RepairExecutionGateDecisionKind.BlockedNeedBaseline,
                    "policy-managed")),
            definition: RepairRegistryCapabilityDefinitions.DiagnosticDataReset,
            repairItemId: "repair.diagnostic_data");

        var result = await harness.Service.RepairAsync(harness.Item);

        Assert.False(result.Success);
        Assert.Equal("policy-managed", result.Message);
        Assert.Equal(0, harness.Registry.ReadCount);
        Assert.Equal(0, harness.Registry.WriteCount);
    }

    [Fact]
    public async Task DiagnosticData_CancellationBeforeEntry_IsZeroRegistryWrite()
    {
        var harness = Harness.Create(
            19041,
            "Windows 10",
            isAdministrator: true,
            definition: RepairRegistryCapabilityDefinitions.DiagnosticDataReset,
            repairItemId: "repair.diagnostic_data");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await harness.Service.RepairAsync(
            harness.Item,
            cancellationToken: cancellation.Token);

        Assert.False(result.Success);
        Assert.Equal(0, harness.Registry.ReadCount);
        Assert.Equal(0, harness.Registry.WriteCount);
        Assert.Equal(0, harness.Registry.RestoreCount);
    }

    [Theory]
    [InlineData("Windows 10", 19041)]
    [InlineData("Windows 11", 22631)]
    public async Task FastStartup_RealRepairEntry_AppliesRepeatsAndRestoresExactly(
        string edition,
        int build)
    {
        var harness = Harness.Create(build, edition, isAdministrator: true);

        var applied = await harness.Service.RepairAsync(harness.Item);

        Assert.True(applied.Success, applied.Message);
        Assert.Equal(1, harness.Registry.WriteCount);
        Assert.Equal(1, harness.Registry.State.Value);
        var receipt = Assert.IsType<RepairChangeReceipt>(applied.ChangeReceipt);
        Assert.Single(receipt.Operations);
        Assert.Equal(RepairChangeCapabilityIds.FastStartupEnable, receipt.Operations[0].CapabilityId);

        var repeated = await harness.Service.RepairAsync(harness.Item);

        Assert.True(repeated.Success, repeated.Message);
        Assert.Equal(1, harness.Registry.WriteCount);
        Assert.Empty(Assert.IsType<RepairChangeReceipt>(repeated.ChangeReceipt).Operations);

        var restored = await harness.Service.RestoreAsync(
            new RepairRecoveryRequest(harness.Item.Id, receipt));

        Assert.True(restored.Success, restored.Message);
        Assert.Equal(new RegistryDwordState(true, true, 0), harness.Registry.State);
        Assert.Equal(1, harness.Registry.RestoreCount);

        var replayed = await harness.Service.RestoreAsync(
            new RepairRecoveryRequest(harness.Item.Id, receipt));

        Assert.False(replayed.Success);
        Assert.Contains("恢复材料不存在", replayed.Message, StringComparison.Ordinal);
        Assert.Equal(1, harness.Registry.RestoreCount);
    }

    [Fact]
    public async Task FastStartup_NonAdministrator_BlocksBeforeRegistryWrite()
    {
        var harness = Harness.Create(22631, "Windows 11 Pro", isAdministrator: false);

        var result = await harness.Service.RepairAsync(harness.Item);

        Assert.False(result.Success);
        Assert.Equal(0, harness.Registry.WriteCount);
        Assert.Equal(0, harness.Registry.RestoreCount);
    }

    [Fact]
    public async Task FastStartup_PolicyGate_BlocksBeforeRegistryProbe()
    {
        var harness = Harness.Create(
            22631,
            "Windows 11 Pro",
            isAdministrator: true,
            repairGate: new FixedRepairGate(
                new RepairExecutionGateDecision(
                    RepairExecutionGateDecisionKind.BlockedNeedBaseline,
                    "policy-managed")));

        var result = await harness.Service.RepairAsync(harness.Item);

        Assert.False(result.Success);
        Assert.Equal("policy-managed", result.Message);
        Assert.Equal(0, harness.Registry.ReadCount);
        Assert.Equal(0, harness.Registry.WriteCount);
    }

    [Fact]
    public async Task FastStartup_CancellationBeforeEntry_IsZeroRegistryWrite()
    {
        var harness = Harness.Create(19041, "Windows 10", isAdministrator: true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await harness.Service.RepairAsync(
            harness.Item,
            cancellationToken: cancellation.Token);

        Assert.False(result.Success);
        Assert.Equal(0, harness.Registry.ReadCount);
        Assert.Equal(0, harness.Registry.WriteCount);
        Assert.Equal(0, harness.Registry.RestoreCount);
    }

    private sealed class Harness
    {
        private Harness(
            FakeRegistryAccessor registry,
            RepairService service,
            RepairItem item)
        {
            Registry = registry;
            Service = service;
            Item = item;
        }

        public FakeRegistryAccessor Registry { get; }
        public RepairService Service { get; }
        public RepairItem Item { get; }

        public static Harness Create(
            int build,
            string edition,
            bool isAdministrator,
            IRepairExecutionGate? repairGate = null,
            RegistryDwordCapabilityDefinition? definition = null,
            string repairItemId = "repair.fast_startup")
        {
            var registry = new FakeRegistryAccessor(new RegistryDwordState(true, true, 0));
            var environment = new FakeEnvironmentProvider(build, edition, isAdministrator);
            var capability = new RegistryDwordCapability(
                definition ?? RepairRegistryCapabilityDefinitions.FastStartupEnable,
                registry,
                environmentProvider: environment);
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
                candidate => candidate.Id == repairItemId);
            return new Harness(registry, service, item);
        }
    }

    private sealed class FakeEnvironmentProvider(
        int build,
        string edition,
        bool isAdministrator) : IRegistryDwordCapabilityEnvironmentProvider
    {
        public RegistryDwordCapabilityEnvironment GetCurrent() => new(
            true,
            build,
            edition,
            Environment.MachineName,
            "S-1-5-21-fake",
            isAdministrator);
    }

    private sealed class FakeRegistryAccessor(RegistryDwordState initial) : IRegistryDwordAccessor
    {
        public RegistryDwordState State { get; private set; } = initial;
        public int ReadCount { get; private set; }
        public int WriteCount { get; private set; }
        public int RestoreCount { get; private set; }

        public Task<RegistryDwordState> ReadCurrentUserAsync(
            string subKeyPath,
            string valueName,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Fast startup must use HKLM, not HKCU.");

        public Task WriteCurrentUserAsync(
            string subKeyPath,
            string valueName,
            int value,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Fast startup must use HKLM, not HKCU.");

        public Task RestoreCurrentUserAsync(
            string subKeyPath,
            string valueName,
            RegistryDwordState original,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Fast startup must use HKLM, not HKCU.");

        public Task<RegistryDwordState> ReadLocalMachineAsync(
            string subKeyPath,
            string valueName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            return Task.FromResult(State);
        }

        public Task WriteLocalMachineAsync(
            string subKeyPath,
            string valueName,
            int value,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteCount++;
            State = new RegistryDwordState(true, true, value);
            return Task.CompletedTask;
        }

        public Task RestoreLocalMachineAsync(
            string subKeyPath,
            string valueName,
            RegistryDwordState original,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RestoreCount++;
            State = original;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedRepairGate(RepairExecutionGateDecision decision) : IRepairExecutionGate
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

        public Task RegisterAvailableAsync(
            Guid recoveryId,
            string integrityHash,
            CancellationToken cancellationToken)
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
