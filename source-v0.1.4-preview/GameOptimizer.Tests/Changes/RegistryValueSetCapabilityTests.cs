using System.Text.Json;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Infrastructure.Windows.Registry;

namespace GameOptimizer.Tests.Changes;

public sealed class RegistryValueSetCapabilityTests
{
    [Fact]
    public async Task Execute_UsesFrozenTargetPayloadAndVerifiesWholeSet()
    {
        var mutableTargets = new List<RegistryTypedTargetValue>
        {
            RegistryTypedTargetValue.String("KeyboardDelay", "0"),
            RegistryTypedTargetValue.String("KeyboardSpeed", "31")
        };
        var accessor = new FakeValueSetAccessor(KeyboardState("1", "20"));
        var capability = CreateCapability(KeyboardDefinition(mutableTargets), accessor);
        mutableTargets[0] = RegistryTypedTargetValue.String("KeyboardDelay", "3");
        var scope = RegistryValueSetCapability.CreateCurrentUserScope();
        var before = await capability.ReadAsync(scope, CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);

        var result = await capability.ExecuteAsync(change, CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.MutationOccurred);
        Assert.Equal("0", accessor.Get("KeyboardDelay").StringValue);
        Assert.Equal("31", accessor.Get("KeyboardSpeed").StringValue);
        Assert.Equal(2, accessor.WriteCount);
    }

    [Fact]
    public async Task Prepare_WhenWholeSetAlreadyMatches_ProducesNoChangeFingerprint()
    {
        var accessor = new FakeValueSetAccessor(KeyboardState("0", "31"));
        var capability = CreateCapability(RegistryValueSetCapabilityDefinitions.KeyboardResponse, accessor);
        var before = await capability.ReadAsync(RegistryValueSetCapability.CreateCurrentUserScope(), CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);

        Assert.Equal(change.BeforeFingerprint, change.TargetFingerprint);
        var directResult = await capability.ExecuteAsync(change, CancellationToken.None);
        Assert.True(directResult.Success);
        Assert.False(directResult.MutationOccurred);
        Assert.Equal(0, accessor.WriteCount);
    }

    [Fact]
    public async Task Prepare_DynamicLegalTarget_IsFrozenAndExecutedWithoutReadingConfigurationAgain()
    {
        var accessor = new FakeValueSetAccessor(KeyboardState("0", "31"));
        var capability = CreateCapability(RegistryValueSetCapabilityDefinitions.KeyboardResponse, accessor);
        var scope = RegistryValueSetCapability.CreateCurrentUserScope();
        var before = await capability.ReadAsync(scope, CancellationToken.None);
        var request = DynamicRequest(
            RegistryTypedTargetValue.String("KeyboardSpeed", "15"),
            RegistryTypedTargetValue.String("KeyboardDelay", "2"));

        var change = await capability.PrepareAsync(before, request, CancellationToken.None);
        var result = await capability.ExecuteAsync(change, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("2", accessor.Get("KeyboardDelay").StringValue);
        Assert.Equal("15", accessor.Get("KeyboardSpeed").StringValue);
        var frozen = JsonSerializer.Deserialize<RegistryValueSetPayload>(change.TargetPayload);
        Assert.Equal(new[] { "KeyboardDelay", "KeyboardSpeed" }, frozen!.Values.Select(value => value.ValueName));
    }

    [Fact]
    public async Task Prepare_DynamicTargetRejectsMissingExtraWrongKindAndOutOfRange()
    {
        var accessor = new FakeValueSetAccessor(KeyboardState("1", "20"));
        var capability = CreateCapability(RegistryValueSetCapabilityDefinitions.KeyboardResponse, accessor);
        var before = await capability.ReadAsync(RegistryValueSetCapability.CreateCurrentUserScope(), CancellationToken.None);
        var invalidRequests = new[]
        {
            DynamicRequest(RegistryTypedTargetValue.String("KeyboardDelay", "0")),
            DynamicRequest(
                RegistryTypedTargetValue.String("KeyboardDelay", "0"),
                RegistryTypedTargetValue.String("KeyboardSpeed", "31"),
                RegistryTypedTargetValue.String("Unexpected", "1")),
            DynamicRequest(
                RegistryTypedTargetValue.DWord("KeyboardDelay", 0),
                RegistryTypedTargetValue.String("KeyboardSpeed", "31")),
            DynamicRequest(
                RegistryTypedTargetValue.String("KeyboardDelay", "4"),
                RegistryTypedTargetValue.String("KeyboardSpeed", "31")),
            DynamicRequest(
                RegistryTypedTargetValue.String("KeyboardDelay", "0"),
                RegistryTypedTargetValue.String("KeyboardSpeed", "32")),
            DynamicRequest(
                RegistryTypedTargetValue.String("KeyboardDelay", " 0"),
                RegistryTypedTargetValue.String("KeyboardSpeed", "31"))
        };

        foreach (var request in invalidRequests)
        {
            await Assert.ThrowsAsync<InvalidDataException>(
                () => capability.PrepareAsync(before, request, CancellationToken.None));
        }

        Assert.Equal(0, accessor.WriteCount);
    }

    [Fact]
    public async Task Prepare_WrongKindBlocksEntireSetWithoutWrite()
    {
        var state = KeyboardState("1", "20");
        state[0] = new RegistryValueSetEntryState("KeyboardDelay", true, true, "DWord", null, 1);
        var accessor = new FakeValueSetAccessor(state);
        var capability = CreateCapability(RegistryValueSetCapabilityDefinitions.KeyboardResponse, accessor);
        var before = await capability.ReadAsync(RegistryValueSetCapability.CreateCurrentUserScope(), CancellationToken.None);

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => capability.PrepareAsync(before, "apply", CancellationToken.None));

        Assert.Contains("目标要求 String", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, accessor.WriteCount);
    }

    [Fact]
    public async Task Execute_WhenAnyValueDrifts_BlocksBeforeWrite()
    {
        var accessor = new FakeValueSetAccessor(KeyboardState("1", "20"));
        var capability = CreateCapability(RegistryValueSetCapabilityDefinitions.KeyboardResponse, accessor);
        var scope = RegistryValueSetCapability.CreateCurrentUserScope();
        var before = await capability.ReadAsync(scope, CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);
        accessor.Set(RegistryValueSetEntry("KeyboardSpeed", "25"));

        var result = await capability.ExecuteAsync(change, CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.MutationOccurred);
        Assert.Equal("state.drift", result.ReasonCode);
        Assert.Equal(0, accessor.WriteCount);
    }

    [Fact]
    public async Task Execute_WhenSecondWriteFails_CompensatesFirstAndAttemptedValueInReverse()
    {
        var original = KeyboardState("1", "20");
        var accessor = new FakeValueSetAccessor(original) { ThrowOnWriteCall = 2 };
        var capability = CreateCapability(RegistryValueSetCapabilityDefinitions.KeyboardResponse, accessor);
        var result = await PrepareAndExecuteAsync(capability);

        Assert.False(result.Success);
        Assert.False(result.MutationOccurred);
        Assert.EndsWith(".compensated", result.ReasonCode, StringComparison.Ordinal);
        Assert.Equal(original, accessor.Snapshot());
        Assert.Equal(new[] { "KeyboardSpeed", "KeyboardDelay" }, accessor.RestoreOrder);
    }

    [Fact]
    public async Task Execute_WhenWholeSetReadbackMismatches_CompensatesToOriginal()
    {
        var original = KeyboardState("1", "20");
        var accessor = new FakeValueSetAccessor(original) { TamperOnReadCall = 3 };
        var capability = CreateCapability(RegistryValueSetCapabilityDefinitions.KeyboardResponse, accessor);
        var result = await PrepareAndExecuteAsync(capability);

        Assert.False(result.Success);
        Assert.False(result.MutationOccurred);
        Assert.Equal("execution.verification-mismatch.compensated", result.ReasonCode);
        Assert.Equal(original, accessor.Snapshot());
    }

    [Fact]
    public async Task Execute_WhenCancelledDuringSecondWrite_CompensatesWithNonCancelledToken()
    {
        var original = KeyboardState("1", "20");
        var accessor = new FakeValueSetAccessor(original) { CancelOnWriteCall = 2 };
        var capability = CreateCapability(RegistryValueSetCapabilityDefinitions.KeyboardResponse, accessor);
        var result = await PrepareAndExecuteAsync(capability);

        Assert.False(result.Success);
        Assert.False(result.MutationOccurred);
        Assert.Equal("execution.cancelled.compensated", result.ReasonCode);
        Assert.Equal(original, accessor.Snapshot());
    }

    [Fact]
    public async Task Execute_WhenPermissionFailsDuringSecondWrite_CompensatesToOriginal()
    {
        var original = KeyboardState("1", "20");
        var accessor = new FakeValueSetAccessor(original) { DenyOnWriteCall = 2 };
        var capability = CreateCapability(RegistryValueSetCapabilityDefinitions.KeyboardResponse, accessor);
        var result = await PrepareAndExecuteAsync(capability);

        Assert.False(result.Success);
        Assert.False(result.MutationOccurred);
        Assert.Equal("execution.permission-denied.compensated", result.ReasonCode);
        Assert.Equal(original, accessor.Snapshot());
    }

    [Fact]
    public async Task Restore_SucceedsInReverseAndRestoresMissingAndOriginalValues()
    {
        var original = new[]
        {
            new RegistryValueSetEntryState("MouseSpeed", true, true, "String", "1", null),
            new RegistryValueSetEntryState("MouseThreshold1", true, false, null, null, null),
            new RegistryValueSetEntryState("MouseThreshold2", true, true, "String", "10", null)
        };
        var accessor = new FakeValueSetAccessor(original);
        var capability = CreateCapability(RegistryValueSetCapabilityDefinitions.MousePrecision, accessor);
        var (change, applied) = await PrepareAndExecuteWithChangeAsync(capability);
        Assert.True(applied.Success);

        var restored = await capability.RestoreAsync(CreateRecoveryMaterial(capability, change), CancellationToken.None);

        Assert.True(restored.Success);
        Assert.True(restored.MutationOccurred);
        Assert.Equal(original, accessor.Snapshot());
        Assert.Equal(new[] { "MouseThreshold2", "MouseThreshold1", "MouseSpeed" }, accessor.RestoreOrder);
    }

    [Fact]
    public async Task Restore_WhenAnyValueDrifts_BlocksBeforeRestore()
    {
        var accessor = new FakeValueSetAccessor(KeyboardState("1", "20"));
        var capability = CreateCapability(RegistryValueSetCapabilityDefinitions.KeyboardResponse, accessor);
        var (change, applied) = await PrepareAndExecuteWithChangeAsync(capability);
        Assert.True(applied.Success);
        accessor.Set(RegistryValueSetEntry("KeyboardSpeed", "30"));
        var restoreCount = accessor.RestoreCount;

        var restored = await capability.RestoreAsync(CreateRecoveryMaterial(capability, change), CancellationToken.None);

        Assert.False(restored.Success);
        Assert.False(restored.MutationOccurred);
        Assert.Equal("restore.state-drift", restored.ReasonCode);
        Assert.Equal(restoreCount, accessor.RestoreCount);
    }

    [Fact]
    public async Task Restore_TamperedTargetFingerprint_BlocksBeforeRestore()
    {
        var accessor = new FakeValueSetAccessor(KeyboardState("1", "20"));
        var capability = CreateCapability(RegistryValueSetCapabilityDefinitions.KeyboardResponse, accessor);
        var (change, applied) = await PrepareAndExecuteWithChangeAsync(capability);
        Assert.True(applied.Success);
        var material = CreateRecoveryMaterial(capability, change) with { TargetFingerprint = new string('A', 64) };
        var restoreCount = accessor.RestoreCount;

        var restored = await capability.RestoreAsync(material, CancellationToken.None);

        Assert.False(restored.Success);
        Assert.False(restored.MutationOccurred);
        Assert.Equal("restore.state-drift", restored.ReasonCode);
        Assert.Equal(restoreCount, accessor.RestoreCount);
    }

    [Fact]
    public async Task Restore_WhenSecondRestoreFails_CompensatesBackToTarget()
    {
        var accessor = new FakeValueSetAccessor(MouseState("1", "6", "10"));
        var capability = CreateCapability(RegistryValueSetCapabilityDefinitions.MousePrecision, accessor);
        var (change, applied) = await PrepareAndExecuteWithChangeAsync(capability);
        Assert.True(applied.Success);
        var target = accessor.Snapshot();
        accessor.ThrowOnRestoreCall = 2;

        var restored = await capability.RestoreAsync(CreateRecoveryMaterial(capability, change), CancellationToken.None);

        Assert.False(restored.Success);
        Assert.False(restored.MutationOccurred);
        Assert.Equal("restore.failed.compensated", restored.ReasonCode);
        Assert.Equal(target, accessor.Snapshot());
    }

    [Fact]
    public async Task Execute_CompensationUsesBoundedCooperativeSafetyToken()
    {
        var original = KeyboardState("1", "20");
        var accessor = new FakeValueSetAccessor(original)
        {
            ThrowOnWriteCall = 2,
            BlockRestoreUntilCancelled = true
        };
        var capability = new RegistryValueSetCapability(
            RegistryValueSetCapabilityDefinitions.KeyboardResponse,
            accessor,
            safetyCompensationTimeout: TimeSpan.FromMilliseconds(25));
        var started = DateTime.UtcNow;

        var result = await PrepareAndExecuteAsync(capability);

        Assert.False(result.Success);
        Assert.True(result.MutationOccurred);
        Assert.EndsWith(".compensation-failed", result.ReasonCode, StringComparison.Ordinal);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(2));
        Assert.True(accessor.SafetyCancellationObserved);
    }

    [Fact]
    public async Task MixedStringAndDwordDefinition_HasRealApplyAndRestoreSupport()
    {
        var definition = new RegistryValueSetCapabilityDefinition(
            "test.mixed",
            "1.0.0",
            @"Software\Test",
            Array.AsReadOnly(
            [
                RegistryTypedTargetValue.String("Mode", "on"),
                RegistryTypedTargetValue.DWord("Enabled", 1)
            ]),
            ChangeRisk.LowReversible);
        var original = new[]
        {
            new RegistryValueSetEntryState("Enabled", true, true, "DWord", null, 0),
            new RegistryValueSetEntryState("Mode", true, true, "String", "off", null)
        };
        var accessor = new FakeValueSetAccessor(original);
        var capability = CreateCapability(definition, accessor);
        var (change, applied) = await PrepareAndExecuteWithChangeAsync(capability);

        Assert.True(applied.Success);
        Assert.Equal(1, accessor.Get("Enabled").DwordValue);
        Assert.Equal("on", accessor.Get("Mode").StringValue);
        Assert.True((await capability.RestoreAsync(CreateRecoveryMaterial(capability, change), CancellationToken.None)).Success);
        Assert.Equal(original, accessor.Snapshot());
    }

    [Fact]
    public async Task ForeignSid_FailsClosedWithoutAccessorRead()
    {
        var accessor = new FakeValueSetAccessor(KeyboardState("1", "20"));
        var capability = CreateCapability(RegistryValueSetCapabilityDefinitions.KeyboardResponse, accessor);
        var foreign = TargetScope.CurrentUser("S-1-5-21-foreign");

        var assessment = await capability.ProbeAsync(foreign, CancellationToken.None);

        Assert.Equal(CapabilityApplicability.NotApplicable, assessment.Applicability);
        Assert.False(assessment.HasRequiredPermission);
        await Assert.ThrowsAsync<InvalidOperationException>(() => capability.ReadAsync(foreign, CancellationToken.None));
        Assert.Equal(0, accessor.ReadCount);
    }

    private static RegistryValueSetCapabilityDefinition KeyboardDefinition(
        IReadOnlyList<RegistryTypedTargetValue> targets) => new(
        "peripheral.keyboard_response",
        "1.0.0",
        @"Control Panel\Keyboard",
        targets,
        ChangeRisk.MediumRebootOrExperience,
        RequiresExplicitConfirmation: true);

    private static RegistryValueSetCapability CreateCapability(
        RegistryValueSetCapabilityDefinition definition,
        IRegistryValueSetAccessor accessor) => new(definition, accessor);

    private static async Task<PlatformExecutionResult> PrepareAndExecuteAsync(RegistryValueSetCapability capability)
    {
        var (_, result) = await PrepareAndExecuteWithChangeAsync(capability);
        return result;
    }

    private static async Task<(PreparedCapabilityChange Change, PlatformExecutionResult Result)> PrepareAndExecuteWithChangeAsync(
        RegistryValueSetCapability capability)
    {
        var scope = RegistryValueSetCapability.CreateCurrentUserScope();
        var before = await capability.ReadAsync(scope, CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);
        return (change, await capability.ExecuteAsync(change, CancellationToken.None));
    }

    private static RecoveryMaterial CreateRecoveryMaterial(
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
        "direct-capability-test");

    private static string DynamicRequest(params RegistryTypedTargetValue[] values) =>
        JsonSerializer.Serialize(new RegistryValueSetTargetRequest(values));

    private static RegistryValueSetEntryState RegistryValueSetEntry(string name, string value) =>
        new(name, true, true, "String", value, null);

    private static RegistryValueSetEntryState[] KeyboardState(string delay, string speed) =>
    [
        RegistryValueSetEntry("KeyboardDelay", delay),
        RegistryValueSetEntry("KeyboardSpeed", speed)
    ];

    private static RegistryValueSetEntryState[] MouseState(string speed, string threshold1, string threshold2) =>
    [
        RegistryValueSetEntry("MouseSpeed", speed),
        RegistryValueSetEntry("MouseThreshold1", threshold1),
        RegistryValueSetEntry("MouseThreshold2", threshold2)
    ];

    private sealed class FakeValueSetAccessor(IEnumerable<RegistryValueSetEntryState> initial) : IRegistryValueSetAccessor
    {
        private readonly Dictionary<string, RegistryValueSetEntryState> _state = initial.ToDictionary(
            value => value.ValueName,
            StringComparer.OrdinalIgnoreCase);
        private bool _tampered;

        public int? ThrowOnWriteCall { get; set; }
        public int? DenyOnWriteCall { get; set; }
        public int? CancelOnWriteCall { get; set; }
        public int? ThrowOnRestoreCall { get; set; }
        public int? TamperOnReadCall { get; set; }
        public bool BlockRestoreUntilCancelled { get; set; }
        public bool SafetyCancellationObserved { get; private set; }
        public int ReadCount { get; private set; }
        public int WriteCount { get; private set; }
        public int RestoreCount { get; private set; }
        public List<string> RestoreOrder { get; } = [];

        public RegistryValueSetEntryState Get(string valueName) => _state[valueName];

        public void Set(RegistryValueSetEntryState state) => _state[state.ValueName] = state;

        public RegistryValueSetEntryState[] Snapshot() => _state.Values
            .OrderBy(value => value.ValueName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.ValueName, StringComparer.Ordinal)
            .ToArray();

        public Task<IReadOnlyList<RegistryValueSetEntryState>> ReadCurrentUserAsync(
            string subKeyPath,
            IReadOnlyList<string> valueNames,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            if (!_tampered && TamperOnReadCall == ReadCount)
            {
                var name = valueNames[0];
                var current = _state[name];
                _state[name] = current with { StringValue = "tampered" };
                _tampered = true;
            }

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

            if (DenyOnWriteCall == WriteCount)
            {
                throw new UnauthorizedAccessException("fake denied");
            }

            if (ThrowOnWriteCall == WriteCount)
            {
                throw new IOException("fake write failure");
            }

            _state[target.ValueName] = target.Kind == RegistryScalarValueKind.String
                ? new RegistryValueSetEntryState(target.ValueName, true, true, "String", target.StringValue, null)
                : new RegistryValueSetEntryState(target.ValueName, true, true, "DWord", null, target.DwordValue);
            return Task.CompletedTask;
        }

        public async Task RestoreCurrentUserValueAsync(
            string subKeyPath,
            RegistryValueSetEntryState original,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RestoreCount++;
            RestoreOrder.Add(original.ValueName);
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

            if (ThrowOnRestoreCall == RestoreCount)
            {
                throw new IOException("fake restore failure");
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
