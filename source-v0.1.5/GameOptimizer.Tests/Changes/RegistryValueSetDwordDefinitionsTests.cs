using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Infrastructure.Windows.Registry;

namespace GameOptimizer.Tests.Changes;

public sealed class RegistryValueSetDwordDefinitionsTests
{
    [Fact]
    public void Definitions_FreezeExactDwordSemanticsAndConfirmationRisk()
    {
        var touch = RegistryValueSetCapabilityDefinitions.DisableTouchFeedback;
        var handwriting = RegistryValueSetCapabilityDefinitions.DisableHandwritingCollection;

        Assert.Equal("input.disable_touch_feedback", touch.CapabilityId);
        Assert.Equal(@"Control Panel\Cursors", touch.SubKeyPath);
        Assert.All(touch.Targets, target => Assert.Equal(RegistryScalarValueKind.DWord, target.Kind));
        Assert.Equal(
            new[] { ("ContactVisualization", 0), ("GestureVisualization", 0) },
            touch.Targets.Select(target => (target.ValueName, target.DwordValue!.Value)));

        Assert.Equal("privacy.disable_handwriting_collection", handwriting.CapabilityId);
        Assert.Equal(@"SOFTWARE\Microsoft\InputPersonalization", handwriting.SubKeyPath);
        Assert.All(handwriting.Targets, target => Assert.Equal(RegistryScalarValueKind.DWord, target.Kind));
        Assert.Equal(
            new[] { ("RestrictImplicitInkCollection", 1), ("RestrictImplicitTextCollection", 1) },
            handwriting.Targets.Select(target => (target.ValueName, target.DwordValue!.Value)));

        Assert.All(new[] { touch, handwriting }, definition =>
        {
            Assert.True(definition.RequiresExplicitConfirmation);
            Assert.Equal(ChangeRisk.MediumRebootOrExperience, definition.Risk);
            Assert.All(definition.Constraints!, constraint =>
            {
                Assert.Equal(RegistryScalarValueKind.DWord, constraint.Kind);
                Assert.Equal(0, constraint.MinimumNumericValue);
                Assert.Equal(1, constraint.MaximumNumericValue);
            });
        });
    }

    [Fact]
    public async Task MissingDwords_ApplyAndRestoreExactMissingState()
    {
        var original = MissingState("ContactVisualization", "GestureVisualization");
        var accessor = new FakeAccessor(original);
        var capability = Create(RegistryValueSetCapabilityDefinitions.DisableTouchFeedback, accessor);
        var (change, applied) = await PrepareAndExecuteAsync(capability);

        Assert.True(applied.Success);
        Assert.All(accessor.Snapshot(), state =>
        {
            Assert.Equal("DWord", state.ValueKind);
            Assert.Equal(0, state.DwordValue);
        });

        var restored = await capability.RestoreAsync(Material(capability, change), CancellationToken.None);
        Assert.True(restored.Success);
        Assert.Equal(original, accessor.Snapshot());
    }

    [Fact]
    public async Task WrongKind_BlocksWholeHandwritingSetBeforeWrite()
    {
        var accessor = new FakeAccessor(
        [
            StringState("RestrictImplicitInkCollection", "1"),
            DwordState("RestrictImplicitTextCollection", 0)
        ]);
        var capability = Create(RegistryValueSetCapabilityDefinitions.DisableHandwritingCollection, accessor);
        var before = await capability.ReadAsync(RegistryValueSetCapability.CreateCurrentUserScope(), CancellationToken.None);

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => capability.PrepareAsync(before, "apply", CancellationToken.None));

        Assert.Contains("目标要求 DWord", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, accessor.WriteCount);
    }

    [Fact]
    public async Task ExternalDriftInEitherDword_BlocksWithoutWrite()
    {
        var accessor = new FakeAccessor(TouchState(1, 1));
        var capability = Create(RegistryValueSetCapabilityDefinitions.DisableTouchFeedback, accessor);
        var scope = RegistryValueSetCapability.CreateCurrentUserScope();
        var before = await capability.ReadAsync(scope, CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);
        accessor.Set(DwordState("GestureVisualization", 0));

        var result = await capability.ExecuteAsync(change, CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.MutationOccurred);
        Assert.Equal("state.drift", result.ReasonCode);
        Assert.Equal(0, accessor.WriteCount);
    }

    [Fact]
    public async Task SecondDwordWriteFailure_CompensatesBothAttemptedValuesInReverse()
    {
        var original = HandwritingState(0, 0);
        var accessor = new FakeAccessor(original) { ThrowOnWriteCall = 2 };
        var capability = Create(RegistryValueSetCapabilityDefinitions.DisableHandwritingCollection, accessor);

        var (_, result) = await PrepareAndExecuteAsync(capability);

        Assert.False(result.Success);
        Assert.False(result.MutationOccurred);
        Assert.EndsWith(".compensated", result.ReasonCode, StringComparison.Ordinal);
        Assert.Equal(original, accessor.Snapshot());
        Assert.Equal(
            new[] { "RestrictImplicitTextCollection", "RestrictImplicitInkCollection" },
            accessor.RestoreOrder);
    }

    [Fact]
    public async Task SuccessfulHandwritingApply_RestoresOriginalDwordsInReverse()
    {
        var original = HandwritingState(0, 0);
        var accessor = new FakeAccessor(original);
        var capability = Create(RegistryValueSetCapabilityDefinitions.DisableHandwritingCollection, accessor);
        var (change, applied) = await PrepareAndExecuteAsync(capability);
        Assert.True(applied.Success);
        Assert.All(accessor.Snapshot(), value => Assert.Equal(1, value.DwordValue));

        var restored = await capability.RestoreAsync(Material(capability, change), CancellationToken.None);

        Assert.True(restored.Success);
        Assert.Equal(original, accessor.Snapshot());
        Assert.Equal(
            new[] { "RestrictImplicitTextCollection", "RestrictImplicitInkCollection" },
            accessor.RestoreOrder);
    }

    [Fact]
    public async Task CancellationDuringSecondTouchWrite_CompensatesWithoutUsingCancelledToken()
    {
        var original = TouchState(1, 1);
        var accessor = new FakeAccessor(original) { CancelOnWriteCall = 2 };
        var capability = Create(RegistryValueSetCapabilityDefinitions.DisableTouchFeedback, accessor);

        var (_, result) = await PrepareAndExecuteAsync(capability);

        Assert.False(result.Success);
        Assert.False(result.MutationOccurred);
        Assert.Equal("execution.cancelled.compensated", result.ReasonCode);
        Assert.Equal(original, accessor.Snapshot());
        Assert.All(accessor.CompensationTokens, token => Assert.True(token.CanBeCanceled));
    }

    [Fact]
    public async Task CompensationTimeout_ReturnsMutationUnknownOnlyAfterAccessorObservesCancellation()
    {
        var accessor = new FakeAccessor(TouchState(1, 1))
        {
            ThrowOnWriteCall = 2,
            BlockRestoreUntilCancelled = true
        };
        var capability = new RegistryValueSetCapability(
            RegistryValueSetCapabilityDefinitions.DisableTouchFeedback,
            accessor,
            safetyCompensationTimeout: TimeSpan.FromMilliseconds(25));

        var (_, result) = await PrepareAndExecuteAsync(capability);

        Assert.False(result.Success);
        Assert.True(result.MutationOccurred);
        Assert.EndsWith(".compensation-failed", result.ReasonCode, StringComparison.Ordinal);
        Assert.True(accessor.SafetyCancellationObserved);
    }

    private static RegistryValueSetCapability Create(
        RegistryValueSetCapabilityDefinition definition,
        IRegistryValueSetAccessor accessor) => new(definition, accessor);

    private static async Task<(PreparedCapabilityChange Change, PlatformExecutionResult Result)> PrepareAndExecuteAsync(
        RegistryValueSetCapability capability)
    {
        var scope = RegistryValueSetCapability.CreateCurrentUserScope();
        var before = await capability.ReadAsync(scope, CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);
        return (change, await capability.ExecuteAsync(change, CancellationToken.None));
    }

    private static RecoveryMaterial Material(
        RegistryValueSetCapability capability,
        PreparedCapabilityChange change) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        capability.Descriptor.CapabilityId,
        capability.Descriptor.DefinitionVersion,
        change.Scope,
        change.BeforePayload,
        change.BeforeFingerprint,
        change.TargetFingerprint,
        change.ResourceKey,
        DateTimeOffset.UtcNow,
        "fake-test");

    private static RegistryValueSetEntryState[] MissingState(params string[] names) =>
        names.Select(name => new RegistryValueSetEntryState(name, false, false, null, null, null)).ToArray();

    private static RegistryValueSetEntryState DwordState(string name, int value) =>
        new(name, true, true, "DWord", null, value);

    private static RegistryValueSetEntryState StringState(string name, string value) =>
        new(name, true, true, "String", value, null);

    private static RegistryValueSetEntryState[] TouchState(int contact, int gesture) =>
    [
        DwordState("ContactVisualization", contact),
        DwordState("GestureVisualization", gesture)
    ];

    private static RegistryValueSetEntryState[] HandwritingState(int ink, int text) =>
    [
        DwordState("RestrictImplicitInkCollection", ink),
        DwordState("RestrictImplicitTextCollection", text)
    ];

    private sealed class FakeAccessor(IEnumerable<RegistryValueSetEntryState> initial) : IRegistryValueSetAccessor
    {
        private readonly Dictionary<string, RegistryValueSetEntryState> _state = initial.ToDictionary(
            state => state.ValueName,
            StringComparer.OrdinalIgnoreCase);

        public int? ThrowOnWriteCall { get; set; }
        public int? CancelOnWriteCall { get; set; }
        public bool BlockRestoreUntilCancelled { get; set; }
        public bool SafetyCancellationObserved { get; private set; }
        public int WriteCount { get; private set; }
        public List<string> RestoreOrder { get; } = [];
        public List<CancellationToken> CompensationTokens { get; } = [];

        public void Set(RegistryValueSetEntryState state) => _state[state.ValueName] = state;

        public RegistryValueSetEntryState[] Snapshot() => _state.Values
            .OrderBy(state => state.ValueName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        public Task<IReadOnlyList<RegistryValueSetEntryState>> ReadCurrentUserAsync(
            string subKeyPath,
            IReadOnlyList<string> valueNames,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<RegistryValueSetEntryState>>(
                valueNames.Select(name => _state[name]).ToArray());
        }

        public Task WriteCurrentUserValueAsync(
            string subKeyPath,
            RegistryTypedTargetValue target,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteCount++;
            if (CancelOnWriteCall == WriteCount)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            if (ThrowOnWriteCall == WriteCount)
            {
                throw new IOException("fake write failure");
            }

            _state[target.ValueName] = new RegistryValueSetEntryState(
                target.ValueName,
                true,
                true,
                "DWord",
                null,
                target.DwordValue);
            return Task.CompletedTask;
        }

        public async Task RestoreCurrentUserValueAsync(
            string subKeyPath,
            RegistryValueSetEntryState original,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RestoreOrder.Add(original.ValueName);
            CompensationTokens.Add(cancellationToken);
            if (BlockRestoreUntilCancelled)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    SafetyCancellationObserved = true;
                    throw;
                }
            }

            _state[original.ValueName] = original;
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
}
