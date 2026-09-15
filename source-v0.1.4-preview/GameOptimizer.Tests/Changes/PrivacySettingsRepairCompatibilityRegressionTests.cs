using System.Collections.Concurrent;
using System.Security.Principal;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.Changes;
using GameOptimizer.Core.Services.Repair;
using GameOptimizer.Infrastructure.Windows.Registry;

namespace GameOptimizer.Tests.Changes;

/// <summary>
/// Compatibility regression for the existing three-owner current-user privacy Repair entry.
/// The fake stores only HKCU REG_SZ state; the legacy PowerShell writer is never called.
/// </summary>
public sealed class PrivacySettingsRepairCompatibilityRegressionTests
{
    [Theory]
    [InlineData("Windows 10", 19041, false)]
    [InlineData("Windows 10", 19041, true)]
    [InlineData("Windows 11", 22631, false)]
    [InlineData("Windows 11", 22631, true)]
    public async Task PrivacySettings_RealRepairEntry_AppliesRepeatsAndRestoresAllConsents(
        string edition,
        int build,
        bool isAdministrator)
    {
        var harness = Harness.Create(edition, build, isAdministrator);

        Assert.All(harness.Capabilities, capability =>
            Assert.False(capability.Descriptor.RequiresAdministrator));

        var applied = await harness.Service.RepairAsync(harness.Item);

        Assert.True(applied.Success, applied.Message);
        Assert.False(applied.RequiresReboot);
        Assert.Equal(3, harness.Registry.CurrentUserWriteCount);
        Assert.All(harness.Registry.States.Values, state => Assert.Equal("Allow", state.Value));
        var receipt = Assert.IsType<RepairChangeReceipt>(applied.ChangeReceipt);
        Assert.Equal(3, receipt.Operations.Count);

        var repeated = await harness.Service.RepairAsync(harness.Item);

        Assert.True(repeated.Success, repeated.Message);
        Assert.False(repeated.RequiresReboot);
        Assert.Equal(3, harness.Registry.CurrentUserWriteCount);
        Assert.Empty(Assert.IsType<RepairChangeReceipt>(repeated.ChangeReceipt).Operations);

        var restored = await harness.Service.RestoreAsync(
            new RepairRecoveryRequest(harness.Item.Id, receipt));

        Assert.True(restored.Success, restored.Message);
        Assert.False(restored.RequiresReboot);
        Assert.All(harness.Registry.States.Values, state => Assert.Equal("Deny", state.Value));
        Assert.Equal(3, harness.Registry.CurrentUserRestoreCount);

        var replayed = await harness.Service.RestoreAsync(
            new RepairRecoveryRequest(harness.Item.Id, receipt));

        Assert.False(replayed.Success);
        Assert.Contains("恢复材料不存在", replayed.Message, StringComparison.Ordinal);
        Assert.Equal(3, harness.Registry.CurrentUserRestoreCount);
    }

    [Fact]
    public async Task PrivacySettings_OuterPolicyGate_BlocksBeforeRegistryProbe()
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
        Assert.Equal(0, harness.Registry.CurrentUserReadCount);
        Assert.Equal(0, harness.Registry.CurrentUserWriteCount);
    }

    [Fact]
    public async Task PrivacySettings_CancellationBeforeEntry_IsZeroRegistryWrite()
    {
        var harness = Harness.Create("Windows 10", 19041, isAdministrator: false);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await harness.Service.RepairAsync(
            harness.Item,
            cancellationToken: cancellation.Token);

        Assert.False(result.Success);
        Assert.Equal(0, harness.Registry.CurrentUserReadCount);
        Assert.Equal(0, harness.Registry.CurrentUserWriteCount);
        Assert.Equal(0, harness.Registry.CurrentUserRestoreCount);
    }

    internal sealed class Harness
    {
        private Harness(
            FakeRegistryAccessor registry,
            RegistryStringCapability[] capabilities,
            RepairService service,
            RepairItem item)
        {
            Registry = registry;
            Capabilities = capabilities;
            Service = service;
            Item = item;
        }

        public FakeRegistryAccessor Registry { get; }
        public RegistryStringCapability[] Capabilities { get; }
        public RepairService Service { get; }
        public RepairItem Item { get; }

        public static Harness Create(
            string edition,
            int build,
            bool isAdministrator,
            IRepairExecutionGate? repairGate = null)
        {
            var registry = new FakeRegistryAccessor();
            var capabilities = new[]
            {
                new RegistryStringCapability(RegistryStringCapabilityDefinitions.LocationConsent, registry),
                new RegistryStringCapability(RegistryStringCapabilityDefinitions.WebcamConsent, registry),
                new RegistryStringCapability(RegistryStringCapabilityDefinitions.MicrophoneConsent, registry)
            };
            var confirmation = new ChangeConfirmationAuthority(new AllowPrompt());
            var orchestrator = new ChangeOrchestrator(
                capabilities,
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
                candidate => candidate.Id == "repair.privacy_settings");
            return new Harness(registry, capabilities, service, item);
        }
    }

    internal sealed class FakeRegistryAccessor : IRegistryStringAccessor
    {
        private readonly Dictionary<string, RegistryStringState> _states = new(StringComparer.OrdinalIgnoreCase)
        {
            [Key(RegistryStringCapabilityDefinitions.LocationConsent)] = new(true, true, "String", "Deny"),
            [Key(RegistryStringCapabilityDefinitions.WebcamConsent)] = new(true, true, "String", "Deny"),
            [Key(RegistryStringCapabilityDefinitions.MicrophoneConsent)] = new(true, true, "String", "Deny")
        };

        public IReadOnlyDictionary<string, RegistryStringState> States => _states;
        public int CurrentUserReadCount { get; private set; }
        public int CurrentUserWriteCount { get; private set; }
        public int CurrentUserRestoreCount { get; private set; }

        public Task<RegistryStringState> ReadCurrentUserAsync(
            string subKeyPath,
            string valueName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CurrentUserReadCount++;
            return Task.FromResult(_states[Key(subKeyPath, valueName)]);
        }

        public Task WriteCurrentUserAsync(
            string subKeyPath,
            string valueName,
            string value,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CurrentUserWriteCount++;
            _states[Key(subKeyPath, valueName)] = new RegistryStringState(true, true, "String", value);
            return Task.CompletedTask;
        }

        public Task RestoreCurrentUserAsync(
            string subKeyPath,
            string valueName,
            RegistryStringState original,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CurrentUserRestoreCount++;
            _states[Key(subKeyPath, valueName)] = original;
            return Task.CompletedTask;
        }

        public Task<RegistryStringState> ReadLocalMachineAsync(
            string subKeyPath, string valueName, CancellationToken cancellationToken) =>
            throw new NotSupportedException("HKLM must not be used by this HKCU-only test fake.");

        public Task WriteLocalMachineAsync(
            string subKeyPath, string valueName, string value, CancellationToken cancellationToken) =>
            throw new NotSupportedException("HKLM must not be used by this HKCU-only test fake.");

        public Task RestoreLocalMachineAsync(
            string subKeyPath, string valueName, RegistryStringState original, CancellationToken cancellationToken) =>
            throw new NotSupportedException("HKLM must not be used by this HKCU-only test fake.");

        private static string Key(RegistryStringCapabilityDefinition definition) =>
            Key(definition.SubKeyPath, definition.ValueName);

        private static string Key(string subKeyPath, string valueName) => $"{subKeyPath}|{valueName}";
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
