using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Optimization.Operations;
using GameOptimizer.Core.Services.Memory;
using GameOptimizer.Core.Services.Changes;
using GameOptimizer.Infrastructure.Windows.Extensions;
using GameOptimizer.Infrastructure.Windows.Memory;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

namespace GameOptimizer.Tests.Changes;

public sealed class MemoryCleaningBoundaryTests
{
    [Fact]
    public async Task LegacyCleanerMutationMethodsFailClosedWithoutRecoveryIdentity()
    {
        var service = new MemoryCleanerService();

        var smart = await service.SmartBoostAsync();
        var standby = await service.ClearStandbyListAsync();
        var workingSet = await service.FlushWorkingSetsAsync();

        Assert.All(new[] { smart, standby, workingSet }, result =>
        {
            Assert.False(result.Success);
            Assert.Equal(0, result.FreedBytes);
            Assert.Contains("RecoveryId", result.Message, StringComparison.Ordinal);
            Assert.Contains("未执行", result.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task WindowsPlatformKeepsMachineWideStandbyListFailClosed()
    {
        var platform = new WindowsMemoryCleaningPlatform();
        var assessment = await platform.ProbeAsync(
            MemoryCleaningCapabilityKind.StandbyListPurge,
            new TargetScope(TargetScopeKind.Machine, "test-machine"),
            ChangeIntent.Prepare,
            default);

        Assert.Equal(CapabilityApplicability.Unsupported, assessment.Applicability);
        Assert.False(assessment.HasRequiredPermission);
        Assert.Equal("memory.recovery-unavailable", assessment.ReasonCode);
        Assert.Contains("禁用", assessment.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WindowsPlatformRejectsInvalidWorkingSetScopeBeforeMutation()
    {
        var platform = new WindowsMemoryCleaningPlatform();
        var assessment = await platform.ProbeAsync(
            MemoryCleaningCapabilityKind.WorkingSetTrim,
            new TargetScope(TargetScopeKind.Process, "4"),
            ChangeIntent.Prepare,
            default);

        Assert.Equal(CapabilityApplicability.NotApplicable, assessment.Applicability);
        Assert.False(assessment.HasRequiredPermission);
        Assert.Equal("memory.process-scope-invalid", assessment.ReasonCode);
    }

    [Fact]
    public void WorkingSetObservationFingerprintUsesOnlyStableProcessIdentity()
    {
        var identity = new MemoryProcessIdentity(
            ProcessId: 1234,
            CreationTimeUtcTicks: 638000000000000000,
            ExecutableName: "test-process.exe",
            ExecutableHash: "A1B2C3",
            ExecutableHashAvailable: true,
            SessionId: 1,
            UserSecurityIdentifier: "S-1-5-21-test",
            Architecture: "x64",
            TopologyFingerprint: "topology-test");

        var first = MemoryCleaningPayloadCodec.WorkingSetObservedFingerprint(identity);
        var second = MemoryCleaningPayloadCodec.WorkingSetObservedFingerprint(identity);

        Assert.Equal(first, second);
        Assert.NotEqual(first, MemoryCleaningPayloadCodec.WorkingSetTrimFingerprint(identity));
    }

    [Fact]
    public async Task WindowsPlatformTrimsOnlyAnExplicitSelfOwnedTestProcess()
    {
        using var child = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -NonInteractive -Command \"$bytes = New-Object byte[] 67108864; while ($true) { [GC]::KeepAlive($bytes); Start-Sleep -Milliseconds 100 }\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });
        Assert.NotNull(child);

        try
        {
            var platform = new WindowsMemoryCleaningPlatform();
            StateObservation? before = null;
            for (var attempt = 0; attempt < 30; attempt++)
            {
                before = await platform.ReadAsync(
                    MemoryCleaningCapabilityKind.WorkingSetTrim,
                    new TargetScope(TargetScopeKind.Process, child!.Id.ToString()),
                    default);
                var observed = MemoryCleaningPayloadCodec.DeserializeObserved(before.Payload);
                if (observed.WorkingSetBytes >= 8 * 1024 * 1024 && observed.ProcessIdentity?.IsUsable == true)
                    break;
                await Task.Delay(100);
            }

            Assert.NotNull(before);
            var initial = MemoryCleaningPayloadCodec.DeserializeObserved(before!.Payload);
            Assert.NotNull(initial.ProcessIdentity);
            Assert.True(initial.ProcessIdentity!.IsUsable);
            Assert.Equal(
                MemoryCleaningPayloadCodec.WorkingSetObservedFingerprint(initial.ProcessIdentity),
                before.Fingerprint);

            var target = MemoryCleaningRequest.ForWorkingSet(initial.ProcessIdentity);
            var change = new PreparedCapabilityChange(
                MemoryCleaningCapabilityIds.WorkingSetTrim,
                "memory-cleaning.v1",
                before.Scope,
                before.Payload,
                before.Fingerprint,
                MemoryCleaningPayloadCodec.SerializeTarget(target),
                MemoryCleaningPayloadCodec.WorkingSetTrimFingerprint(initial.ProcessIdentity),
                "memory:process-working-set");

            var execution = await platform.ApplyAsync(MemoryCleaningCapabilityKind.WorkingSetTrim, change, default);
            var after = await platform.ReadAsync(MemoryCleaningCapabilityKind.WorkingSetTrim, before.Scope, default);

            Assert.True(execution.Success, execution.Message);
            Assert.True(execution.MutationOccurred);
            Assert.Equal(change.TargetFingerprint, after.Fingerprint);
        }
        finally
        {
            if (child is { HasExited: false })
            {
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync();
            }
        }
    }

    [Fact]
    public async Task OwnerPreservesReadOnlyObservationAndBlockedPrepareWithoutExecution()
    {
        var orchestrator = new FakeOrchestrator();
        var owner = new MemoryCleaningOwner(orchestrator, new NeverConfirmAuthority());
        var request = MemoryCleaningRequest.ForStandbyList("test-machine");

        var read = await owner.ReadStatusAsync(request);
        var prepare = await owner.PrepareAsync(request);

        Assert.Equal(MemoryCleaningResultKind.ReadOnly, read.Kind);
        Assert.NotNull(read.Observation);
        Assert.Equal(MemoryCleaningResultKind.FailClosed, prepare.Kind);
        Assert.Equal("memory.recovery-unavailable", prepare.ReasonCode);
        Assert.Equal(1, orchestrator.ObserveCalls);
        Assert.Equal(1, orchestrator.PrepareCalls);
        Assert.Equal(0, orchestrator.ExecuteCalls);
        Assert.Equal(0, orchestrator.RestoreCalls);
    }

    [Fact]
    public async Task StorageStandbyEntryCannotApplyOrClaimRestoreSuccess()
    {
        var operation = new ClearStandbyListOperation();

        var status = await operation.GetStatusAsync(default);
        var apply = await operation.ApplyAsync(null!, default);
        var restore = await operation.RestoreAsync(null!, default);

        Assert.False(status.IsCompliant);
        Assert.False(apply.Success);
        Assert.False(apply.Changed);
        Assert.False(restore.Success);
        Assert.False(restore.Changed);
        Assert.Contains("RecoveryId", apply.Message, StringComparison.Ordinal);
        Assert.Contains("拒绝伪造", restore.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CompositionDefinesOneOwnerOnePlatformAndTwoUserFacingCapabilities()
    {
        var services = new ServiceCollection();
        services.AddMemoryCleaningWindowsInfrastructure();

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IMemoryCleaningOwner));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(MemoryCleaningOwner));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IMemoryCleaningPlatform));
        Assert.Equal(2, services.Count(descriptor => descriptor.ServiceType == typeof(IChangeCapability)));

        using var provider = services.BuildServiceProvider();
        Assert.IsType<WindowsMemoryCleaningPlatform>(provider.GetRequiredService<IMemoryCleaningPlatform>());
    }

    [Fact]
    public void ReachableMemorySourcesContainNoLegacyWriterAndExposeBlockedUiState()
    {
        var root = FindRepositoryRoot();
        var cleaner = Read(root, @"GameOptimizer.Core\Services\Memory\MemoryCleanerService.cs");
        var processCleaner = Read(root, @"GameOptimizer.Core\Services\Memory\ProcessCleanerService.cs");
        var dialog = Read(root, @"GameOptimizer.Shell\Views\MemoryCleanDialog.xaml.cs");
        var dialogXaml = Read(root, @"GameOptimizer.Shell\Views\MemoryCleanDialog.xaml");
        var panelXaml = Read(root, @"GameOptimizer.Shell\Views\MemoryControlPanel.xaml");
        var storage = Read(root, @"GameOptimizer.Core\Optimization\Operations\StorageOptimizationOperation.cs");

        Assert.DoesNotContain("DllImport(\"psapi.dll\")", cleaner, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DllImport(\"ntdll.dll\")", cleaner, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EmptyWorkingSet", processCleaner, StringComparison.Ordinal);
        Assert.DoesNotContain("EmptyWorkingSet", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.GetProcessById", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("NtSetSystemInformation", storage, StringComparison.Ordinal);
        Assert.DoesNotContain("FileName = \"powershell\"", storage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IMemoryCleaningOwner", dialog, StringComparison.Ordinal);
        Assert.Contains("CaptureWorkingSetTargetAsync", dialog, StringComparison.Ordinal);
        Assert.Contains("RunInteractiveAsync", dialog, StringComparison.Ordinal);
        Assert.Contains("DangerousOperationDialog.Show", dialog, StringComparison.Ordinal);
        Assert.Contains("目标 PID：", dialog, StringComparison.Ordinal);
        Assert.Contains("不会结束进程", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("SmartSelect_Click", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("智能选择", dialogXaml, StringComparison.Ordinal);
        Assert.Contains("释放所选进程工作集", dialogXaml + dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("TerminateProcesses", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("ProcessTermination", dialog, StringComparison.Ordinal);
        Assert.Contains("MemoryBlockedReasonText", dialogXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ConfirmBtn\"", dialogXaml, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"False\"", dialogXaml, StringComparison.Ordinal);
        Assert.Contains("MemoryMutationStatusText", panelXaml, StringComparison.Ordinal);
    }

    private static string Read(string root, string relativePath) =>
        File.ReadAllText(Path.Combine(root, relativePath));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GameOptimizer.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }

    private sealed class FakeOrchestrator : IChangeOrchestrator
    {
        public int ObserveCalls { get; private set; }
        public int PrepareCalls { get; private set; }
        public int ExecuteCalls { get; private set; }
        public int RestoreCalls { get; private set; }

        public Task<StateObservation?> ObserveAsync(
            string capabilityId,
            TargetScope scope,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObserveCalls++;
            return Task.FromResult<StateObservation?>(new StateObservation(
                scope,
                "read-only",
                "READ-ONLY",
                DateTimeOffset.UtcNow,
                "fake",
                "fake-read-only"));
        }

        public Task<PreparationResult> PrepareAsync(
            ChangeRequest request,
            CancellationToken cancellationToken,
            bool registerPlan = true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PrepareCalls++;
            return Task.FromResult(new PreparationResult(
                ChangePreparationStatus.Blocked,
                null,
                "memory.recovery-unavailable",
                "没有真实恢复材料，未生成计划。",
                null));
        }

        public Task<BatchReceipt> ExecuteAsync(
            PreparedChangePlan plan,
            ConfirmationToken confirmation,
            CancellationToken cancellationToken)
        {
            ExecuteCalls++;
            throw new InvalidOperationException("Execute must not be reached.");
        }

        public Task<BatchReceipt> RestoreAsync(
            RestoreRequest request,
            CancellationToken cancellationToken)
        {
            RestoreCalls++;
            throw new InvalidOperationException("Restore must not be reached.");
        }
    }

    private sealed class NeverConfirmAuthority : IChangeConfirmationAuthority
    {
        public Task<ConfirmationToken?> RequestAsync(
            PreparedChangePlan plan,
            string title,
            string summary,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Confirmation must not be reached.");

        public bool TryConsume(PreparedChangePlan plan, ConfirmationToken token) => false;
    }

    private sealed class AlwaysConfirmPrompt : IChangeConfirmationPrompt
    {
        public Task<bool> ConfirmAsync(ChangeConfirmationPrompt prompt, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class AllowExecutionGate : IChangeExecutionGate
    {
        public Task<ChangeExecutionGateDecision> EnsureCanExecuteAsync(
            string capabilityId,
            ChangeExecutionIntent intent,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ChangeExecutionGateDecision.Allowed("controlled test"));
    }

    private sealed class TestChangeStore : IRecoveryRepository, IChangeJournal, IChangeAuditStore
    {
        private readonly Dictionary<Guid, RecoveryMaterial> _recovery = [];
        private readonly Dictionary<Guid, OperationReceipt> _receipts = [];

        public Task SaveAsync(RecoveryMaterial material, CancellationToken cancellationToken)
        {
            _recovery[material.RecoveryId] = material;
            return Task.CompletedTask;
        }

        Task<RecoveryMaterial?> IRecoveryRepository.LoadAsync(Guid recoveryId, CancellationToken cancellationToken) =>
            Task.FromResult(_recovery.GetValueOrDefault(recoveryId));

        public Task AppendAsync(JournalEntry entry, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveAsync(OperationReceipt receipt, CancellationToken cancellationToken)
        {
            _receipts[receipt.ReceiptId] = receipt;
            return Task.CompletedTask;
        }

        Task<OperationReceipt?> IChangeAuditStore.LoadAsync(Guid receiptId, CancellationToken cancellationToken) =>
            Task.FromResult(_receipts.GetValueOrDefault(receiptId));
    }
}
