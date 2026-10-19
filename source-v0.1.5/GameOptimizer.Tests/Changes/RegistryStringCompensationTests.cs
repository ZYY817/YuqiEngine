using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Infrastructure.Windows.Registry;

namespace GameOptimizer.Tests.Changes;

public sealed class RegistryStringCompensationTests
{
    [Fact]
    public async Task WriteThenThrow_RestoresOriginalStringAndConfirmsReadback()
    {
        var original = Existing("Allow");
        var accessor = new FaultAccessor(original) { WriteThenThrow = true };
        var capability = Create(accessor);

        var result = await PrepareAndExecuteAsync(capability);

        Assert.False(result.Success);
        Assert.False(result.MutationOccurred);
        Assert.Equal("execution.failed.compensated", result.ReasonCode);
        Assert.Equal(original, accessor.State);
        Assert.Equal(1, accessor.RestoreCount);
        Assert.True(accessor.LastRestoreToken.CanBeCanceled);
    }

    [Fact]
    public async Task WriteThenCancel_RestoresOriginalMissingValueWithIndependentToken()
    {
        var original = new RegistryStringState(false, false, null, null);
        var accessor = new FaultAccessor(original) { WriteThenCancel = true };
        var capability = Create(accessor);

        var result = await PrepareAndExecuteAsync(capability);

        Assert.False(result.Success);
        Assert.False(result.MutationOccurred);
        Assert.Equal("execution.cancelled.compensated", result.ReasonCode);
        Assert.Equal(original, accessor.State);
        Assert.True(accessor.LastRestoreToken.CanBeCanceled);
    }

    [Fact]
    public async Task WriteReadbackMismatch_RestoresOriginalState()
    {
        var original = Existing("Allow");
        var accessor = new FaultAccessor(original) { RewriteAfterWrite = Existing("Prompt") };
        var capability = Create(accessor);

        var result = await PrepareAndExecuteAsync(capability);

        Assert.False(result.Success);
        Assert.False(result.MutationOccurred);
        Assert.Equal("verification.mismatch.compensated", result.ReasonCode);
        Assert.Equal(original, accessor.State);
    }

    [Fact]
    public async Task CompensationReadbackMismatch_ReturnsMutationUnknown()
    {
        var original = Existing("Allow");
        var accessor = new FaultAccessor(original)
        {
            WriteThenThrow = true,
            IgnoreRestore = true
        };
        var capability = Create(accessor);

        var result = await PrepareAndExecuteAsync(capability);

        Assert.False(result.Success);
        Assert.True(result.MutationOccurred);
        Assert.Equal("execution.failed.compensation-failed", result.ReasonCode);
        Assert.Equal("Deny", accessor.State.Value);
    }

    [Fact]
    public async Task CompensationTimeout_WaitsForAccessorCancellationAndFailsClosed()
    {
        var accessor = new FaultAccessor(Existing("Allow"))
        {
            WriteThenThrow = true,
            BlockRestoreUntilCancelled = true
        };
        var capability = Create(accessor, TimeSpan.FromMilliseconds(25));

        var result = await PrepareAndExecuteAsync(capability);

        Assert.False(result.Success);
        Assert.True(result.MutationOccurred);
        Assert.Equal("execution.failed.compensation-failed", result.ReasonCode);
        Assert.True(accessor.RestoreCancellationObserved);
        Assert.True(accessor.RestoreTaskExited);
    }

    [Fact]
    public async Task ExistingTarget_ReturnsNoChangeWithoutWrite()
    {
        var accessor = new FaultAccessor(Existing("Deny"));
        var capability = Create(accessor);

        var result = await PrepareAndExecuteAsync(capability);

        Assert.True(result.Success);
        Assert.False(result.MutationOccurred);
        Assert.Equal("execution.no-change", result.ReasonCode);
        Assert.Equal(0, accessor.WriteCount);
    }

    [Fact]
    public async Task TamperedFrozenTarget_IsRejectedBeforeAdditionalReadOrWrite()
    {
        var accessor = new FaultAccessor(Existing("Allow"));
        var capability = Create(accessor);
        var scope = RegistryStringCapability.CreateCurrentUserScope();
        var before = await capability.ReadAsync(scope, CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);
        var readsBefore = accessor.ReadCount;
        var tampered = change with { TargetPayload = change.TargetPayload.Replace("Deny", "Allow", StringComparison.Ordinal) };

        await Assert.ThrowsAsync<InvalidDataException>(
            () => capability.ExecuteAsync(tampered, CancellationToken.None));

        Assert.Equal(readsBefore, accessor.ReadCount);
        Assert.Equal(0, accessor.WriteCount);
    }

    private static RegistryStringCapability Create(
        IRegistryStringAccessor accessor,
        TimeSpan? safetyTimeout = null) => new(
        RegistryStringCapabilityDefinitions.DenyAppCalendarAccess,
        accessor,
        safetyCompensationTimeout: safetyTimeout);

    private static async Task<PlatformExecutionResult> PrepareAndExecuteAsync(RegistryStringCapability capability)
    {
        var scope = RegistryStringCapability.CreateCurrentUserScope();
        var before = await capability.ReadAsync(scope, CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);
        return await capability.ExecuteAsync(change, CancellationToken.None);
    }

    private static RegistryStringState Existing(string value) => new(true, true, "String", value);

    private sealed class FaultAccessor(RegistryStringState initial) : IRegistryStringAccessor
    {
        public RegistryStringState State { get; private set; } = initial;
        public bool WriteThenThrow { get; set; }
        public bool WriteThenCancel { get; set; }
        public RegistryStringState? RewriteAfterWrite { get; set; }
        public bool IgnoreRestore { get; set; }
        public bool BlockRestoreUntilCancelled { get; set; }
        public bool RestoreCancellationObserved { get; private set; }
        public bool RestoreTaskExited { get; private set; }
        public int ReadCount { get; private set; }
        public int WriteCount { get; private set; }
        public int RestoreCount { get; private set; }
        public CancellationToken LastRestoreToken { get; private set; }

        public Task<RegistryStringState> ReadCurrentUserAsync(
            string subKeyPath, string valueName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            return Task.FromResult(State);
        }

        public Task WriteCurrentUserAsync(
            string subKeyPath, string valueName, string value, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteCount++;
            State = Existing(value);
            if (WriteThenCancel)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            if (WriteThenThrow)
            {
                throw new IOException("fake write completed then failed");
            }

            if (RewriteAfterWrite is not null)
            {
                State = RewriteAfterWrite;
            }

            return Task.CompletedTask;
        }

        public async Task RestoreCurrentUserAsync(
            string subKeyPath,
            string valueName,
            RegistryStringState original,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RestoreCount++;
            LastRestoreToken = cancellationToken;
            if (BlockRestoreUntilCancelled)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    RestoreCancellationObserved = true;
                    throw;
                }
                finally
                {
                    RestoreTaskExited = true;
                }
            }

            if (!IgnoreRestore)
            {
                State = original;
            }
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
    }
}
