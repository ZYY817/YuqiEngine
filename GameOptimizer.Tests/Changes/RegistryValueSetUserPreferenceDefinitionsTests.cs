using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Infrastructure.Windows.Registry;

namespace GameOptimizer.Tests.Changes;

public sealed class RegistryValueSetUserPreferenceDefinitionsTests
{
    public static IEnumerable<object[]> Definitions()
    {
        yield return [RegistryValueSetCapabilityDefinitions.DisableWindowsTips];
        yield return [RegistryValueSetCapabilityDefinitions.DisableSuggestedApps];
        yield return [RegistryValueSetCapabilityDefinitions.DisableFullscreenOptimizations];
    }

    [Fact]
    public void Definitions_PreserveExactLegacyHkcuSemanticsAndCorrectRisk()
    {
        AssertDefinition(
            RegistryValueSetCapabilityDefinitions.DisableWindowsTips,
            "windows.disable_tips",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
            ("SoftLandingEnabled", 0),
            ("SubscribedContent-338389Enabled", 0));
        AssertDefinition(
            RegistryValueSetCapabilityDefinitions.DisableSuggestedApps,
            "windows.disable_suggested_apps",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
            ("OemPreInstalledAppsEnabled", 0),
            ("PreInstalledAppsEnabled", 0),
            ("PreInstalledAppsEverEnabled", 0),
            ("SilentInstalledAppsEnabled", 0));
        AssertDefinition(
            RegistryValueSetCapabilityDefinitions.DisableFullscreenOptimizations,
            "fps.disable_fullscreen_optimizations",
            @"System\GameConfigStore",
            ("GameDVR_FSEBehavior", 2),
            ("GameDVR_FSEBehaviorMode", 2));
    }

    [Theory]
    [MemberData(nameof(Definitions))]
    public async Task MissingValues_ApplyAndRestoreExactMissingState(
        RegistryValueSetCapabilityDefinition definition)
    {
        var original = definition.Targets.Select(target => Missing(target.ValueName)).ToArray();
        var accessor = new FakeAccessor(original);
        var capability = new RegistryValueSetCapability(definition, accessor);

        var (change, applied) = await PrepareAndExecuteAsync(capability);

        Assert.True(applied.Success);
        Assert.Equal(ExpectedTargetState(definition), accessor.Snapshot());
        var restored = await capability.RestoreAsync(Material(capability, change), CancellationToken.None);
        Assert.True(restored.Success);
        Assert.Equal(Sort(original), accessor.Snapshot());
    }

    [Theory]
    [MemberData(nameof(Definitions))]
    public async Task WrongKind_BlocksWholeSetBeforeWrite(
        RegistryValueSetCapabilityDefinition definition)
    {
        var original = ExpectedTargetState(definition);
        original[0] = String(original[0].ValueName, "0");
        var accessor = new FakeAccessor(original);
        var capability = new RegistryValueSetCapability(definition, accessor);
        var before = await capability.ReadAsync(RegistryValueSetCapability.CreateCurrentUserScope(), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => capability.PrepareAsync(before, "apply", CancellationToken.None));

        Assert.Equal(0, accessor.WriteCount);
    }

    [Theory]
    [MemberData(nameof(Definitions))]
    public async Task ExternalDriftInAnyValue_BlocksWithoutWrite(
        RegistryValueSetCapabilityDefinition definition)
    {
        var original = definition.Targets.Select(target => Dword(target.ValueName, 1)).ToArray();
        var accessor = new FakeAccessor(original);
        var capability = new RegistryValueSetCapability(definition, accessor);
        var scope = RegistryValueSetCapability.CreateCurrentUserScope();
        var before = await capability.ReadAsync(scope, CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);
        accessor.Set(Dword(original[^1].ValueName, original[^1].DwordValue!.Value == 0 ? 1 : 0));

        var result = await capability.ExecuteAsync(change, CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.MutationOccurred);
        Assert.Equal("state.drift", result.ReasonCode);
        Assert.Equal(0, accessor.WriteCount);
    }

    [Theory]
    [MemberData(nameof(Definitions))]
    public async Task SecondWriteFailure_CompensatesAttemptedValuesInReverse(
        RegistryValueSetCapabilityDefinition definition)
    {
        var original = definition.Targets.Select(target => Dword(target.ValueName, 1)).ToArray();
        var accessor = new FakeAccessor(original) { ThrowOnWriteCall = 2 };
        var capability = new RegistryValueSetCapability(definition, accessor);

        var (_, result) = await PrepareAndExecuteAsync(capability);

        Assert.False(result.Success);
        Assert.False(result.MutationOccurred);
        Assert.EndsWith(".compensated", result.ReasonCode, StringComparison.Ordinal);
        Assert.Equal(Sort(original), accessor.Snapshot());
        Assert.Equal(definition.Targets.Take(2).Reverse().Select(target => target.ValueName), accessor.RestoreOrder);
    }

    [Theory]
    [MemberData(nameof(Definitions))]
    public async Task SuccessfulApply_RestoresEveryOriginalValueInReverse(
        RegistryValueSetCapabilityDefinition definition)
    {
        var original = definition.Targets.Select(target => Dword(target.ValueName, 1)).ToArray();
        var accessor = new FakeAccessor(original);
        var capability = new RegistryValueSetCapability(definition, accessor);
        var (change, applied) = await PrepareAndExecuteAsync(capability);
        Assert.True(applied.Success);

        var restored = await capability.RestoreAsync(Material(capability, change), CancellationToken.None);

        Assert.True(restored.Success);
        Assert.Equal(Sort(original), accessor.Snapshot());
        Assert.Equal(definition.Targets.Reverse().Select(target => target.ValueName), accessor.RestoreOrder);
    }

    [Fact]
    public async Task CancelledSecondWrite_UsesIndependentBoundedTokenForCompensation()
    {
        var definition = RegistryValueSetCapabilityDefinitions.DisableWindowsTips;
        var original = definition.Targets.Select(target => Dword(target.ValueName, 1)).ToArray();
        var accessor = new FakeAccessor(original) { CancelOnWriteCall = 2 };
        var capability = new RegistryValueSetCapability(definition, accessor);

        var (_, result) = await PrepareAndExecuteAsync(capability);

        Assert.False(result.Success);
        Assert.False(result.MutationOccurred);
        Assert.Equal("execution.cancelled.compensated", result.ReasonCode);
        Assert.Equal(Sort(original), accessor.Snapshot());
        Assert.All(accessor.CompensationTokens, token => Assert.True(token.CanBeCanceled));
    }

    private static void AssertDefinition(
        RegistryValueSetCapabilityDefinition definition,
        string capabilityId,
        string subKeyPath,
        params (string Name, int Value)[] values)
    {
        Assert.Equal(capabilityId, definition.CapabilityId);
        Assert.Equal(subKeyPath, definition.SubKeyPath);
        Assert.Equal(ChangeRisk.MediumRebootOrExperience, definition.Risk);
        Assert.True(definition.RequiresExplicitConfirmation);
        Assert.Equal(values, definition.Targets.Select(target => (target.ValueName, target.DwordValue!.Value)));
        Assert.All(definition.Targets, target => Assert.Equal(RegistryScalarValueKind.DWord, target.Kind));
        Assert.Equal(values.Length, definition.Constraints!.Count);
    }

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
        Guid.NewGuid(), Guid.NewGuid(), capability.Descriptor.CapabilityId,
        capability.Descriptor.DefinitionVersion, change.Scope, change.BeforePayload,
        change.BeforeFingerprint, change.TargetFingerprint, change.ResourceKey,
        DateTimeOffset.UtcNow, "hkcu-batch-fake-test");

    private static RegistryValueSetEntryState[] ExpectedTargetState(RegistryValueSetCapabilityDefinition definition) =>
        Sort(definition.Targets.Select(target => Dword(target.ValueName, target.DwordValue!.Value)));

    private static RegistryValueSetEntryState Missing(string name) =>
        new(name, false, false, null, null, null);

    private static RegistryValueSetEntryState Dword(string name, int value) =>
        new(name, true, true, "DWord", null, value);

    private static RegistryValueSetEntryState String(string name, string value) =>
        new(name, true, true, "String", value, null);

    private static RegistryValueSetEntryState[] Sort(IEnumerable<RegistryValueSetEntryState> values) =>
        values.OrderBy(value => value.ValueName, StringComparer.OrdinalIgnoreCase).ToArray();

    private sealed class FakeAccessor(IEnumerable<RegistryValueSetEntryState> initial) : IRegistryValueSetAccessor
    {
        private readonly Dictionary<string, RegistryValueSetEntryState> _state = initial.ToDictionary(
            value => value.ValueName, StringComparer.OrdinalIgnoreCase);

        public int? ThrowOnWriteCall { get; set; }
        public int? CancelOnWriteCall { get; set; }
        public int WriteCount { get; private set; }
        public List<string> RestoreOrder { get; } = [];
        public List<CancellationToken> CompensationTokens { get; } = [];

        public void Set(RegistryValueSetEntryState state) => _state[state.ValueName] = state;

        public RegistryValueSetEntryState[] Snapshot() => Sort(_state.Values);

        public Task<IReadOnlyList<RegistryValueSetEntryState>> ReadCurrentUserAsync(
            string subKeyPath, IReadOnlyList<string> valueNames, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<RegistryValueSetEntryState>>(
                valueNames.Select(name => _state[name]).ToArray());
        }

        public Task WriteCurrentUserValueAsync(
            string subKeyPath, RegistryTypedTargetValue target, CancellationToken cancellationToken)
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

            _state[target.ValueName] = Dword(target.ValueName, target.DwordValue!.Value);
            return Task.CompletedTask;
        }

        public Task RestoreCurrentUserValueAsync(
            string subKeyPath, RegistryValueSetEntryState original, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RestoreOrder.Add(original.ValueName);
            CompensationTokens.Add(cancellationToken);
            _state[original.ValueName] = original;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RegistryValueSetEntryState>> ReadLocalMachineAsync(
            string subKeyPath, IReadOnlyList<string> valueNames, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task WriteLocalMachineValueAsync(
            string subKeyPath, RegistryTypedTargetValue target, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RestoreLocalMachineValueAsync(
            string subKeyPath, RegistryValueSetEntryState original, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
