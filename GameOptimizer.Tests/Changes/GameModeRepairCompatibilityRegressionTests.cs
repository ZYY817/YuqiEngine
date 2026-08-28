using System.Collections.Concurrent;
using System.Security.Principal;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.Changes;
using GameOptimizer.Core.Services.Repair;
using GameOptimizer.Infrastructure.Windows.Extensions;
using GameOptimizer.Infrastructure.Windows.Registry;
using Microsoft.Extensions.DependencyInjection;

namespace GameOptimizer.Tests.Changes;

/// <summary>
/// Compatibility regression for the existing two-owner Game Mode Repair entry.
/// The capabilities come from the production DI registration; only the registry accessor is fake.
/// </summary>
public sealed class GameModeRepairCompatibilityRegressionTests
{
    [Theory]
    [InlineData("Windows 10", 19041, false)]
    [InlineData("Windows 10", 19041, true)]
    [InlineData("Windows 11", 22631, false)]
    [InlineData("Windows 11", 22631, true)]
    public async Task GameMode_RealRepairEntry_AppliesRepeatsAndRestoresBothOwners(
        string edition,
        int build,
        bool isAdministrator)
    {
        var harness = Harness.Create(edition, build, isAdministrator);

        Assert.False(harness.ExistingGameModeEnable.Descriptor.RequiresAdministrator);
        Assert.False(harness.GameModeAutoEnable.Descriptor.RequiresAdministrator);

        var applied = await harness.Service.RepairAsync(harness.Item);

        Assert.True(applied.Success, applied.Message);
        Assert.False(applied.RequiresReboot);
        Assert.Equal(2, harness.Registry.CurrentUserWriteCount);
        Assert.Equal(1, harness.Registry.Value(
            "SOFTWARE\\Microsoft\\GameBar", "AllowAutoGameMode").Value);
        Assert.Equal(1, harness.Registry.Value(
            "SOFTWARE\\Microsoft\\GameBar", "AutoGameModeEnabled").Value);
        var receipt = Assert.IsType<RepairChangeReceipt>(applied.ChangeReceipt);
        Assert.Equal(2, receipt.Operations.Count);

        var repeated = await harness.Service.RepairAsync(harness.Item);

        Assert.True(repeated.Success, repeated.Message);
        Assert.False(repeated.RequiresReboot);
        Assert.Equal(2, harness.Registry.CurrentUserWriteCount);
        Assert.Empty(Assert.IsType<RepairChangeReceipt>(repeated.ChangeReceipt).Operations);

        var restored = await harness.Service.RestoreAsync(
            new RepairRecoveryRequest(harness.Item.Id, receipt));

        Assert.True(restored.Success, restored.Message);
        Assert.False(restored.RequiresReboot);
        Assert.Equal(0, harness.Registry.Value(
            "SOFTWARE\\Microsoft\\GameBar", "AllowAutoGameMode").Value);
        Assert.Equal(0, harness.Registry.Value(
            "SOFTWARE\\Microsoft\\GameBar", "AutoGameModeEnabled").Value);
        Assert.Equal(2, harness.Registry.CurrentUserRestoreCount);

        var replayed = await harness.Service.RestoreAsync(
            new RepairRecoveryRequest(harness.Item.Id, receipt));

        Assert.False(replayed.Success);
        Assert.Contains("恢复材料不存在", replayed.Message, StringComparison.Ordinal);
        Assert.Equal(2, harness.Registry.CurrentUserRestoreCount);
    }

    [Fact]
    public async Task GameMode_OuterPolicyGate_BlocksBeforeRegistryProbe()
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
    public async Task GameMode_CancellationBeforeEntry_IsZeroRegistryWrite()
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
            IChangeCapability existingGameModeEnable,
            IChangeCapability gameModeAutoEnable,
            RepairService service,
            RepairItem item)
        {
            Registry = registry;
            ExistingGameModeEnable = existingGameModeEnable;
            GameModeAutoEnable = gameModeAutoEnable;
            Service = service;
            Item = item;
        }

        public FakeRegistryAccessor Registry { get; }
        public IChangeCapability ExistingGameModeEnable { get; }
        public IChangeCapability GameModeAutoEnable { get; }
        public RepairService Service { get; }
        public RepairItem Item { get; }

        public static Harness Create(
            string edition,
            int build,
            bool isAdministrator,
            IRepairExecutionGate? repairGate = null)
        {
            var registry = new FakeRegistryAccessor();
            var services = new ServiceCollection();
            services.AddGameOptimizerWindowsInfrastructure();
            services.AddSingleton<IRegistryDwordAccessor>(registry);
            using var provider = services.BuildServiceProvider();
            var capabilities = provider.GetServices<IChangeCapability>().ToArray();
            var existingGameModeEnable = Assert.Single(
                capabilities,
                candidate => candidate.Descriptor.CapabilityId == RepairChangeCapabilityIds.ExistingGameModeEnable);
            var gameModeAutoEnable = Assert.Single(
                capabilities,
                candidate => candidate.Descriptor.CapabilityId == RepairChangeCapabilityIds.GameModeAutoEnable);
            var confirmation = new ChangeConfirmationAuthority(new AllowPrompt());
            var orchestrator = new ChangeOrchestrator(
                [existingGameModeEnable, gameModeAutoEnable],
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
                candidate => candidate.Id == "repair.game_mode");
            return new Harness(registry, existingGameModeEnable, gameModeAutoEnable, service, item);
        }
    }

    internal sealed class FakeRegistryAccessor : IRegistryDwordAccessor
    {
        private readonly Dictionary<string, RegistryDwordState> _states = new(StringComparer.OrdinalIgnoreCase)
        {
            [Key("SOFTWARE\\Microsoft\\GameBar", "AllowAutoGameMode")] = new(true, true, 0),
            [Key("SOFTWARE\\Microsoft\\GameBar", "AutoGameModeEnabled")] = new(true, true, 0)
        };

        public int CurrentUserReadCount { get; private set; }
        public int CurrentUserWriteCount { get; private set; }
        public int CurrentUserRestoreCount { get; private set; }

        public RegistryDwordState Value(string subKeyPath, string valueName) =>
            _states[Key(subKeyPath, valueName)];

        public Task<RegistryDwordState> ReadCurrentUserAsync(
            string subKeyPath,
            string valueName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CurrentUserReadCount++;
            return Task.FromResult(Value(subKeyPath, valueName));
        }

        public Task WriteCurrentUserAsync(
            string subKeyPath,
            string valueName,
            int value,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CurrentUserWriteCount++;
            _states[Key(subKeyPath, valueName)] = new RegistryDwordState(true, true, value);
            return Task.CompletedTask;
        }

        public Task RestoreCurrentUserAsync(
            string subKeyPath,
            string valueName,
            RegistryDwordState original,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CurrentUserRestoreCount++;
            _states[Key(subKeyPath, valueName)] = original;
            return Task.CompletedTask;
        }

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
