using System.Collections.Concurrent;
using System.Security.Principal;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.Changes;
using GameOptimizer.Core.Services.Repair;
using GameOptimizer.Infrastructure.Windows.Registry;

namespace GameOptimizer.Tests.Changes;

/// <summary>
/// Fake end-to-end regression for the two-value current-user Game DVR Repair entry.
/// The fake is deliberately limited to HKCU; no host registry or legacy writer is reachable.
/// </summary>
public sealed class GameDvrRepairCompatibilityRegressionTests
{
    [Theory]
    [InlineData("Windows 10", 19041, false)]
    [InlineData("Windows 10", 19041, true)]
    [InlineData("Windows 11", 22631, false)]
    [InlineData("Windows 11", 22631, true)]
    public async Task GameDvr_RealRepairEntry_AppliesRepeatsAndRestoresBothValues(
        string edition,
        int build,
        bool isAdministrator)
    {
        var harness = Harness.Create(build, edition, isWindows: true, isAdministrator);

        Assert.False(harness.GameDvrEnabled.Descriptor.RequiresAdministrator);
        Assert.False(harness.GameDvrAppCapture.Descriptor.RequiresAdministrator);

        var applied = await harness.Service.RepairAsync(harness.Item);

        Assert.True(applied.Success, applied.Message);
        Assert.False(applied.RequiresReboot);
        Assert.Equal(2, harness.Registry.CurrentUserWriteCount);
        Assert.Equal(1, harness.Registry.Value("System\\GameConfigStore", "GameDVR_Enabled").Value);
        Assert.Equal(1, harness.Registry.Value(
            "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\GameDVR",
            "AppCaptureEnabled").Value);
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
        Assert.Equal(0, harness.Registry.Value("System\\GameConfigStore", "GameDVR_Enabled").Value);
        Assert.Equal(0, harness.Registry.Value(
            "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\GameDVR",
            "AppCaptureEnabled").Value);
        Assert.Equal(2, harness.Registry.CurrentUserRestoreCount);

        var replayed = await harness.Service.RestoreAsync(
            new RepairRecoveryRequest(harness.Item.Id, receipt));

        Assert.False(replayed.Success);
        Assert.Contains("恢复材料不存在", replayed.Message, StringComparison.Ordinal);
        Assert.Equal(2, harness.Registry.CurrentUserRestoreCount);
    }

    [Fact]
    public async Task GameDvr_UnsupportedWindowsBuild_BlocksBeforeRegistryProbe()
    {
        var harness = Harness.Create(18363, "Windows 10 1909", isWindows: true, isAdministrator: false);

        var result = await harness.Service.RepairAsync(harness.Item);

        Assert.False(result.Success);
        Assert.Contains("Windows 10 2004", result.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.Registry.CurrentUserReadCount);
        Assert.Equal(0, harness.Registry.CurrentUserWriteCount);
    }

    [Fact]
    public async Task GameDvr_OuterPolicyGate_BlocksBeforeRegistryProbe()
    {
        var harness = Harness.Create(
            22631,
            "Windows 11 Pro",
            isWindows: true,
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
    public async Task GameDvr_CancellationBeforeEntry_IsZeroRegistryWrite()
    {
        var harness = Harness.Create(22631, "Windows 11", isWindows: true, isAdministrator: false);
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
            RegistryDwordCapability gameDvrEnabled,
            RegistryDwordCapability gameDvrAppCapture,
            RepairService service,
            RepairItem item)
        {
            Registry = registry;
            GameDvrEnabled = gameDvrEnabled;
            GameDvrAppCapture = gameDvrAppCapture;
            Service = service;
            Item = item;
        }

        public FakeRegistryAccessor Registry { get; }
        public RegistryDwordCapability GameDvrEnabled { get; }
        public RegistryDwordCapability GameDvrAppCapture { get; }
        public RepairService Service { get; }
        public RepairItem Item { get; }

        public static Harness Create(
            int build,
            string edition,
            bool isWindows,
            bool isAdministrator,
            IRepairExecutionGate? repairGate = null)
        {
            var registry = new FakeRegistryAccessor();
            var environment = new FakeEnvironmentProvider(
                isWindows,
                build,
                edition,
                isAdministrator);
            var gameDvrEnabled = new RegistryDwordCapability(
                RepairRegistryCapabilityDefinitions.GameDvrEnabled,
                registry,
                environmentProvider: environment);
            var gameDvrAppCapture = new RegistryDwordCapability(
                RepairRegistryCapabilityDefinitions.GameDvrAppCapture,
                registry,
                environmentProvider: environment);
            var confirmation = new ChangeConfirmationAuthority(new AllowPrompt());
            var orchestrator = new ChangeOrchestrator(
                [gameDvrEnabled, gameDvrAppCapture],
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
                candidate => candidate.Id == "repair.game_dvr");
            return new Harness(registry, gameDvrEnabled, gameDvrAppCapture, service, item);
        }
    }

    private sealed class FakeEnvironmentProvider(
        bool isWindows,
        int build,
        string edition,
        bool isAdministrator) : IRegistryDwordCapabilityEnvironmentProvider
    {
        public RegistryDwordCapabilityEnvironment GetCurrent() => new(
            isWindows,
            build,
            edition,
            Environment.MachineName,
            WindowsIdentity.GetCurrent().User?.Value
                ?? throw new InvalidOperationException("Fake current-user scope requires a readable host SID."),
            isAdministrator);
    }

    internal sealed class FakeRegistryAccessor : IRegistryDwordAccessor
    {
        private readonly Dictionary<string, RegistryDwordState> _states = new(StringComparer.OrdinalIgnoreCase)
        {
            [Key("System\\GameConfigStore", "GameDVR_Enabled")] = new(true, true, 0),
            [Key("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\GameDVR", "AppCaptureEnabled")] = new(true, true, 0)
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

        public Task<RegistryDwordState> ReadLocalMachineAsync(
            string subKeyPath,
            string valueName,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Game DVR must use HKCU, not HKLM.");

        public Task WriteLocalMachineAsync(
            string subKeyPath,
            string valueName,
            int value,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Game DVR must use HKCU, not HKLM.");

        public Task RestoreLocalMachineAsync(
            string subKeyPath,
            string valueName,
            RegistryDwordState original,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Game DVR must use HKCU, not HKLM.");

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
