using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Optimization.Operations;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameOptimizer.Tests.Changes;

public sealed class AccessibilityToggleOperationFailureTests
{
    public static TheoryData<int> Settings => new()
    {
        (int)AccessibilitySetting.StickyKeys,
        (int)AccessibilitySetting.FilterKeys,
        (int)AccessibilitySetting.ToggleKeys
    };

    [Theory]
    [MemberData(nameof(Settings))]
    public async Task GetStatusAndApply_FailClosed_WhenNativeGetFails(int settingValue)
    {
        var setting = (AccessibilitySetting)settingValue;
        var platform = new FakeAccessibilityPort
        {
            ReadFailure = AccessibilityParametersReadResult.Failed(5)
        };
        var operation = CreateOperation(setting, platform);
        var context = CreateContext();

        var status = await operation.GetStatusAsync(CancellationToken.None);
        var apply = await operation.ApplyAsync(context, CancellationToken.None);

        Assert.False(status.IsCompliant);
        Assert.Contains("accessibility.spi_get_failed", status.Details, StringComparison.Ordinal);
        Assert.Contains("Win32=5", status.Details, StringComparison.Ordinal);
        Assert.False(apply.Success);
        Assert.False(apply.Changed);
        Assert.Contains("accessibility.spi_get_failed", apply.Message, StringComparison.Ordinal);
        Assert.Equal(0, platform.WriteCount);
    }

    [Fact]
    public async Task GetStatus_UsesStableUnknownCategory_WhenNativeGetFailsWithoutLastError()
    {
        var platform = new FakeAccessibilityPort
        {
            ReadFailure = AccessibilityParametersReadResult.Failed(0)
        };
        var operation = new StickyKeysDisableOperation(platform);

        var status = await operation.GetStatusAsync(CancellationToken.None);

        Assert.False(status.IsCompliant);
        Assert.Contains("accessibility.spi_get_failed_unknown", status.Details, StringComparison.Ordinal);
        Assert.Contains("Win32=0", status.Details, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Settings))]
    public async Task GetStatus_ReportsRealFlags(int settingValue)
    {
        var setting = (AccessibilitySetting)settingValue;
        var platform = new FakeAccessibilityPort { Flags = 0x04 };
        var operation = CreateOperation(setting, platform);

        var enabledStatus = await operation.GetStatusAsync(CancellationToken.None);
        platform.Flags = 0;
        var disabledStatus = await operation.GetStatusAsync(CancellationToken.None);

        Assert.False(enabledStatus.IsCompliant);
        Assert.True(disabledStatus.IsCompliant);
        Assert.Equal(0, platform.WriteCount);
    }

    [Fact]
    public async Task StickyKeysStatus_PreservesLegacyHotkeyComplianceMeaning()
    {
        var platform = new FakeAccessibilityPort { Flags = 0x08 };
        var operation = new StickyKeysDisableOperation(platform);

        var status = await operation.GetStatusAsync(CancellationToken.None);

        Assert.True(status.IsCompliant);
        Assert.Equal(0, platform.WriteCount);
    }

    [Fact]
    public void NativeStructures_HaveDocumentedWin32Layouts()
    {
        Assert.Equal(8, NativeAccessibilitySystemParametersPort.GetNativeStructureSize(AccessibilitySetting.StickyKeys));
        Assert.Equal(24, NativeAccessibilitySystemParametersPort.GetNativeStructureSize(AccessibilitySetting.FilterKeys));
        Assert.Equal(8, NativeAccessibilitySystemParametersPort.GetNativeStructureSize(AccessibilitySetting.ToggleKeys));
    }

    [Fact]
    public async Task FilterKeysApply_PreservesAllTimingFields()
    {
        var original = new AccessibilityParameters(0x04, 1200, 300, 75, 40);
        var platform = new FakeAccessibilityPort { Parameters = original };
        var operation = new FilterKeysDisableOperation(platform);

        var result = await operation.ApplyAsync(CreateContext(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(original with { Flags = 0 }, platform.Parameters);
    }

    [Fact]
    public async Task Apply_ReturnsFailureAndRestoresSnapshot_WhenNativeSetFails()
    {
        var platform = new FakeAccessibilityPort
        {
            Flags = 0x04,
            WriteFailure = AccessibilityFlagsWriteResult.Failed(87)
        };
        var operation = new FilterKeysDisableOperation(platform);
        var store = new InMemoryRestoreStore();

        var result = await operation.ApplyAsync(CreateContext(store), CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.Changed);
        Assert.Contains("accessibility.spi_set_failed", result.Message, StringComparison.Ordinal);
        Assert.Contains("Win32=87", result.Message, StringComparison.Ordinal);
        Assert.Equal(1, platform.WriteCount);
        Assert.Equal(0x04u, platform.Flags);
        Assert.False(await store.HasSnapshotAsync(operation.Id, CancellationToken.None));
    }

    [Fact]
    public async Task FilterKeysApply_RestoresCompleteOriginal_WhenFailedSetMayHaveMutated()
    {
        var original = new AccessibilityParameters(0x04, 1050, 225, 70, 25);
        var platform = new FakeAccessibilityPort
        {
            Parameters = original,
            WriteFailure = AccessibilityFlagsWriteResult.Failed(31),
            MutateBeforeWriteFailure = true,
            ClearWriteFailureAfterFirstFailure = true
        };
        var operation = new FilterKeysDisableOperation(platform);

        var result = await operation.ApplyAsync(CreateContext(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.Changed);
        Assert.Contains("已自动回滚", result.Message, StringComparison.Ordinal);
        Assert.Equal(original, platform.Parameters);
        Assert.Equal(2, platform.WriteCount);
    }

    [Fact]
    public async Task Apply_FailsAndRollsBack_WhenWriteReadbackDoesNotMatch()
    {
        var platform = new FakeAccessibilityPort
        {
            Flags = 0x04,
            MutateOnWrite = false
        };
        var operation = new ToggleKeysDisableOperation(platform);
        var store = new InMemoryRestoreStore();

        var result = await operation.ApplyAsync(CreateContext(store), CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.Changed);
        Assert.Contains("回读值与目标值不一致", result.Message, StringComparison.Ordinal);
        Assert.Equal(1, platform.WriteCount);
        Assert.Equal(0x04u, platform.Flags);
        Assert.False(await store.HasSnapshotAsync(operation.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Apply_FailsAndRestoresOriginal_WhenPostWriteGetFails()
    {
        var platform = new FakeAccessibilityPort
        {
            Flags = 0x04,
            FailOnReadNumber = 2,
            ScheduledReadFailure = AccessibilityParametersReadResult.Failed(31)
        };
        var operation = new FilterKeysDisableOperation(platform);

        var result = await operation.ApplyAsync(CreateContext(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("accessibility.spi_get_failed", result.Message, StringComparison.Ordinal);
        Assert.Contains("已自动回滚", result.Message, StringComparison.Ordinal);
        Assert.Equal(0x04u, platform.Flags);
        Assert.Equal(2, platform.WriteCount);
    }

    [Fact]
    public async Task FilterKeysApplyFailure_RestoresFlagsAndAllTimingFields()
    {
        var original = new AccessibilityParameters(0x04, 1100, 250, 80, 35);
        var platform = new FakeAccessibilityPort
        {
            Parameters = original,
            FailOnReadNumber = 2,
            ScheduledReadFailure = AccessibilityParametersReadResult.Failed(31)
        };
        var operation = new FilterKeysDisableOperation(platform);

        var result = await operation.ApplyAsync(CreateContext(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(original, platform.Parameters);
        Assert.Equal(2, platform.WriteCount);
    }

    [Fact]
    public async Task Restore_ReturnsFailure_WhenNativeSetFails()
    {
        var platform = new FakeAccessibilityPort { Flags = 0x04 };
        var operation = new StickyKeysDisableOperation(platform);
        var store = new InMemoryRestoreStore();
        var context = CreateContext(store);
        var applied = await operation.ApplyAsync(context, CancellationToken.None);
        Assert.True(applied.Success);
        platform.WriteFailure = AccessibilityFlagsWriteResult.Failed(5);

        var restored = await operation.RestoreAsync(context, CancellationToken.None);

        Assert.False(restored.Success);
        Assert.False(restored.Changed);
        Assert.Contains("accessibility.spi_set_failed", restored.Message, StringComparison.Ordinal);
        Assert.Equal(0u, platform.Flags);
    }

    [Fact]
    public async Task Restore_WritesAndVerifiesOriginalFlags()
    {
        var platform = new FakeAccessibilityPort { Flags = 0x0Cu };
        var operation = new StickyKeysDisableOperation(platform);
        var context = CreateContext();
        var applied = await operation.ApplyAsync(context, CancellationToken.None);
        Assert.True(applied.Success);
        Assert.Equal(0u, platform.Flags);

        var restored = await operation.RestoreAsync(context, CancellationToken.None);

        Assert.True(restored.Success);
        Assert.True(restored.Changed);
        Assert.Equal(0x0Cu, platform.Flags);
    }

    [Fact]
    public async Task FilterKeysRestore_RestoresFlagsAndAllTimingFieldsExactly()
    {
        var original = new AccessibilityParameters(0x04, 900, 200, 60, 30);
        var platform = new FakeAccessibilityPort { Parameters = original };
        var operation = new FilterKeysDisableOperation(platform);
        var context = CreateContext();
        Assert.True((await operation.ApplyAsync(context, CancellationToken.None)).Success);
        platform.Parameters = new AccessibilityParameters(0, 1, 2, 3, 4);

        var restored = await operation.RestoreAsync(context, CancellationToken.None);

        Assert.True(restored.Success);
        Assert.True(restored.Changed);
        Assert.Equal(original, platform.Parameters);
    }

    [Fact]
    public async Task FilterKeysRestore_RejectsLegacyFlagsOnlySnapshot()
    {
        var platform = new FakeAccessibilityPort { Parameters = new AccessibilityParameters(0) };
        var operation = new FilterKeysDisableOperation(platform);
        var context = CreateContext();
        await context.SaveSnapshotAsync(
            operation.Id,
            new AccessibilityParametersSnapshot(0x04),
            CancellationToken.None);

        var result = await operation.RestoreAsync(context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.Changed);
        Assert.Contains("快照不完整", result.Message, StringComparison.Ordinal);
        Assert.Equal(0, platform.WriteCount);
    }

    [Fact]
    public async Task StickyKeysRestore_AcceptsLegacyFlagsOnlySnapshotBecauseStructureHasNoOtherState()
    {
        var platform = new FakeAccessibilityPort { Parameters = new AccessibilityParameters(0) };
        var operation = new StickyKeysDisableOperation(platform);
        var context = CreateContext();
        await context.SaveSnapshotAsync(
            operation.Id,
            new AccessibilityParametersSnapshot(0x0C),
            CancellationToken.None);

        var result = await operation.RestoreAsync(context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(0x0Cu, platform.Flags);
    }

    [Fact]
    public async Task Restore_FailsClosed_WhenPreRestoreGetFails()
    {
        var platform = new FakeAccessibilityPort { Flags = 0x04 };
        var operation = new ToggleKeysDisableOperation(platform);
        var context = CreateContext();
        Assert.True((await operation.ApplyAsync(context, CancellationToken.None)).Success);
        var writeCountBeforeRestore = platform.WriteCount;
        platform.FailOnReadNumber = platform.ReadCount + 1;
        platform.ScheduledReadFailure = AccessibilityParametersReadResult.Failed(5);

        var restored = await operation.RestoreAsync(context, CancellationToken.None);

        Assert.False(restored.Success);
        Assert.False(restored.Changed);
        Assert.Contains("accessibility.spi_get_failed", restored.Message, StringComparison.Ordinal);
        Assert.Equal(writeCountBeforeRestore, platform.WriteCount);
    }

    private static IOptimizationOperation CreateOperation(
        AccessibilitySetting setting,
        IAccessibilitySystemParametersPort platform) => setting switch
    {
        AccessibilitySetting.StickyKeys => new StickyKeysDisableOperation(platform),
        AccessibilitySetting.FilterKeys => new FilterKeysDisableOperation(platform),
        AccessibilitySetting.ToggleKeys => new ToggleKeysDisableOperation(platform),
        _ => throw new ArgumentOutOfRangeException(nameof(setting), setting, null)
    };

    private static OperationExecutionContext CreateContext(InMemoryRestoreStore? store = null) =>
        new(store ?? new InMemoryRestoreStore(), NullLogger.Instance);

    private sealed class FakeAccessibilityPort : IAccessibilitySystemParametersPort
    {
        public AccessibilityParameters Parameters { get; set; } = new(0);
        public uint Flags
        {
            get => Parameters.Flags;
            set => Parameters = Parameters with { Flags = value };
        }

        public AccessibilityParametersReadResult? ReadFailure { get; set; }
        public AccessibilityFlagsWriteResult? WriteFailure { get; set; }
        public AccessibilityParametersReadResult? ScheduledReadFailure { get; set; }
        public int? FailOnReadNumber { get; set; }
        public bool MutateOnWrite { get; set; } = true;
        public bool MutateBeforeWriteFailure { get; set; }
        public bool ClearWriteFailureAfterFirstFailure { get; set; }
        public int WriteCount { get; private set; }
        public int ReadCount { get; private set; }

        public AccessibilityParametersReadResult ReadParameters(AccessibilitySetting setting)
        {
            ReadCount++;
            if (ReadFailure is { } persistentFailure)
            {
                return persistentFailure;
            }

            if (FailOnReadNumber == ReadCount && ScheduledReadFailure is { } scheduledFailure)
            {
                return scheduledFailure;
            }

            return AccessibilityParametersReadResult.Succeeded(Parameters);
        }

        public AccessibilityFlagsWriteResult WriteParameters(
            AccessibilitySetting setting,
            AccessibilityParameters parameters)
        {
            WriteCount++;
            if (WriteFailure is { } failure)
            {
                if (MutateBeforeWriteFailure)
                {
                    Parameters = parameters;
                }

                if (ClearWriteFailureAfterFirstFailure)
                {
                    WriteFailure = null;
                }

                return failure;
            }

            if (MutateOnWrite)
            {
                Parameters = parameters;
            }

            return AccessibilityFlagsWriteResult.Succeeded();
        }
    }

    private sealed class InMemoryRestoreStore : IOperationRestoreStore
    {
        private readonly Dictionary<string, string> _snapshots = new(StringComparer.Ordinal);

        public Task SaveSnapshotAsync(string operationId, string payload, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _snapshots[operationId] = payload;
            return Task.CompletedTask;
        }

        public Task<string?> LoadSnapshotAsync(string operationId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_snapshots.GetValueOrDefault(operationId));
        }

        public Task DeleteSnapshotAsync(string operationId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _snapshots.Remove(operationId);
            return Task.CompletedTask;
        }

        public Task<bool> HasSnapshotAsync(string operationId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_snapshots.ContainsKey(operationId));
        }
    }
}
