using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Infrastructure.Windows.Registry;

namespace GameOptimizer.Tests.Changes;

public sealed class RegistryValueSetAdvertisingIdTests
{
    private const string ZeroId = "00000000-0000-0000-0000-000000000000";

    [Fact]
    public void Definition_PreservesLegacyMixedTypedSemanticsAndRequiresConfirmation()
    {
        var definition = RegistryValueSetCapabilityDefinitions.DisableAdvertisingId;

        Assert.Equal("privacy.disable_advertising_id", definition.CapabilityId);
        Assert.Equal(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo", definition.SubKeyPath);
        Assert.Equal(ChangeRisk.MediumRebootOrExperience, definition.Risk);
        Assert.True(definition.RequiresExplicitConfirmation);
        Assert.Equal(
            new[]
            {
                RegistryTypedTargetValue.DWord("Enabled", 0),
                RegistryTypedTargetValue.String("Id", ZeroId)
            },
            definition.Targets);
        Assert.Equal(RegistryScalarValueKind.DWord, definition.Constraints![0].Kind);
        Assert.Equal(0, definition.Constraints[0].MinimumNumericValue);
        Assert.Equal(1, definition.Constraints[0].MaximumNumericValue);
        Assert.Equal(RegistryScalarValueKind.String, definition.Constraints[1].Kind);
    }

    [Fact]
    public async Task MissingValues_ApplyAndRestoreExactMissingState()
    {
        var original = new[] { Missing("Enabled"), Missing("Id") };
        var accessor = new FakeAccessor(original);
        var capability = Create(accessor);

        var (change, applied) = await PrepareAndExecuteAsync(capability);

        Assert.True(applied.Success);
        Assert.Equal(Dword("Enabled", 0), accessor.Get("Enabled"));
        Assert.Equal(String("Id", ZeroId), accessor.Get("Id"));

        var restored = await capability.RestoreAsync(Material(capability, change), CancellationToken.None);
        Assert.True(restored.Success);
        Assert.Equal(Sort(original), accessor.Snapshot());
    }

    [Theory]
    [InlineData("Enabled")]
    [InlineData("Id")]
    public async Task WrongKind_BlocksWholeSetBeforeWrite(string wrongValueName)
    {
        var state = AdvertisingState(1, "existing-id");
        state = state.Select(value => value.ValueName == wrongValueName
            ? wrongValueName == "Enabled" ? String("Enabled", "0") : Dword("Id", 0)
            : value).ToArray();
        var accessor = new FakeAccessor(state);
        var capability = Create(accessor);
        var before = await capability.ReadAsync(RegistryValueSetCapability.CreateCurrentUserScope(), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => capability.PrepareAsync(before, "apply", CancellationToken.None));

        Assert.Equal(0, accessor.WriteCount);
    }

    [Fact]
    public async Task FrozenTargetPayloadTampering_IsRejectedBeforeRegistryReadOrWrite()
    {
        var accessor = new FakeAccessor(AdvertisingState(1, "existing-id"));
        var capability = Create(accessor);
        var scope = RegistryValueSetCapability.CreateCurrentUserScope();
        var before = await capability.ReadAsync(scope, CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);
        var readsBeforeExecute = accessor.ReadCount;
        var tampered = change with
        {
            TargetPayload = change.TargetPayload.Replace(ZeroId, Guid.NewGuid().ToString(), StringComparison.Ordinal)
        };

        await Assert.ThrowsAsync<InvalidDataException>(
            () => capability.ExecuteAsync(tampered, CancellationToken.None));

        Assert.Equal(readsBeforeExecute, accessor.ReadCount);
        Assert.Equal(0, accessor.WriteCount);
    }

    [Fact]
    public async Task ExternalDrift_BlocksWithoutWrite()
    {
        var accessor = new FakeAccessor(AdvertisingState(1, "existing-id"));
        var capability = Create(accessor);
        var scope = RegistryValueSetCapability.CreateCurrentUserScope();
        var before = await capability.ReadAsync(scope, CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);
        accessor.Set(String("Id", "externally-changed"));

        var result = await capability.ExecuteAsync(change, CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.MutationOccurred);
        Assert.Equal("state.drift", result.ReasonCode);
        Assert.Equal(0, accessor.WriteCount);
    }

    [Fact]
    public async Task SecondWriteFailure_CompensatesMixedValuesInReverse()
    {
        var original = AdvertisingState(1, "existing-id");
        var accessor = new FakeAccessor(original) { ThrowOnWriteCall = 2 };
        var capability = Create(accessor);

        var (_, result) = await PrepareAndExecuteAsync(capability);

        Assert.False(result.Success);
        Assert.False(result.MutationOccurred);
        Assert.EndsWith(".compensated", result.ReasonCode, StringComparison.Ordinal);
        Assert.Equal(Sort(original), accessor.Snapshot());
        Assert.Equal(new[] { "Id", "Enabled" }, accessor.RestoreOrder);
    }

    [Fact]
    public async Task SuccessfulApply_RestoresOriginalMixedValues()
    {
        var original = AdvertisingState(1, "existing-id");
        var accessor = new FakeAccessor(original);
        var capability = Create(accessor);
        var (change, applied) = await PrepareAndExecuteAsync(capability);
        Assert.True(applied.Success);

        var restored = await capability.RestoreAsync(Material(capability, change), CancellationToken.None);

        Assert.True(restored.Success);
        Assert.Equal(Sort(original), accessor.Snapshot());
        Assert.Equal(new[] { "Id", "Enabled" }, accessor.RestoreOrder);
    }

    [Fact]
    public async Task CancellationDuringSecondWrite_UsesBoundedSafetyTokenForCompensation()
    {
        var original = AdvertisingState(1, "existing-id");
        var accessor = new FakeAccessor(original) { CancelOnWriteCall = 2 };
        var capability = Create(accessor);

        var (_, result) = await PrepareAndExecuteAsync(capability);

        Assert.False(result.Success);
        Assert.False(result.MutationOccurred);
        Assert.Equal("execution.cancelled.compensated", result.ReasonCode);
        Assert.Equal(Sort(original), accessor.Snapshot());
        Assert.All(accessor.CompensationTokens, token => Assert.True(token.CanBeCanceled));
    }

    [Fact]
    public async Task CompensationTimeout_FailsClosedAfterAccessorObservesCancellation()
    {
        var accessor = new FakeAccessor(AdvertisingState(1, "existing-id"))
        {
            ThrowOnWriteCall = 2,
            BlockRestoreUntilCancelled = true
        };
        var capability = Create(accessor, TimeSpan.FromMilliseconds(25));

        var (_, result) = await PrepareAndExecuteAsync(capability);

        Assert.False(result.Success);
        Assert.True(result.MutationOccurred);
        Assert.EndsWith(".compensation-failed", result.ReasonCode, StringComparison.Ordinal);
        Assert.True(accessor.SafetyCancellationObserved);
    }

    [Fact]
    public async Task ExplicitRepairTarget_SetsEnabledAndDeletesId_ThenRestoresExactOriginal()
    {
        var original = AdvertisingState(0, "existing-id");
        var accessor = new FakeAccessor(original);
        var capability = Create(accessor);
        var scope = RegistryValueSetCapability.CreateCurrentUserScope();
        var before = await capability.ReadAsync(scope, CancellationToken.None);
        var change = await capability.PrepareAsync(before, RepairTarget(), CancellationToken.None);

        var applied = await capability.ExecuteAsync(change, CancellationToken.None);
        var target = System.Text.Json.JsonSerializer.Deserialize<RegistryValueSetPayload>(change.TargetPayload)!;
        var restored = await capability.RestoreAsync(Material(capability, change), CancellationToken.None);

        Assert.True(applied.Success);
        Assert.Contains(target.Values, value => value.ValueName == "Id" && !value.ValueExists);
        Assert.True(restored.Success);
        Assert.Equal(Sort(original), accessor.Snapshot());
    }

    [Fact]
    public async Task SetAndDeleteTargets_HaveDistinctCanonicalPayloadAndHash()
    {
        var accessor = new FakeAccessor(AdvertisingState(1, "existing-id"));
        var capability = Create(accessor);
        var scope = RegistryValueSetCapability.CreateCurrentUserScope();
        var before = await capability.ReadAsync(scope, CancellationToken.None);

        var setTarget = await capability.PrepareAsync(before, LegacySetTargetWithoutAction(), CancellationToken.None);
        var deleteTarget = await capability.PrepareAsync(before, RepairTarget(), CancellationToken.None);

        Assert.NotEqual(setTarget.TargetPayload, deleteTarget.TargetPayload);
        Assert.NotEqual(setTarget.TargetFingerprint, deleteTarget.TargetFingerprint);
    }

    [Fact]
    public async Task LegacyTargetWithoutAction_RemainsSetCompatible()
    {
        var accessor = new FakeAccessor(AdvertisingState(1, "existing-id"));
        var capability = Create(accessor);
        var before = await capability.ReadAsync(RegistryValueSetCapability.CreateCurrentUserScope(), CancellationToken.None);

        var change = await capability.PrepareAsync(before, LegacySetTargetWithoutAction(), CancellationToken.None);
        var result = await capability.ExecuteAsync(change, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(ZeroId, accessor.Get("Id").StringValue);
    }

    [Fact]
    public async Task DeleteFailure_CompensatesEarlierSetInReverse()
    {
        var original = AdvertisingState(0, "existing-id");
        var accessor = new FakeAccessor(original) { ThrowOnDeleteCall = 1 };
        var capability = Create(accessor);
        var scope = RegistryValueSetCapability.CreateCurrentUserScope();
        var before = await capability.ReadAsync(scope, CancellationToken.None);
        var change = await capability.PrepareAsync(before, RepairTarget(), CancellationToken.None);

        var result = await capability.ExecuteAsync(change, CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.MutationOccurred);
        Assert.EndsWith(".compensated", result.ReasonCode, StringComparison.Ordinal);
        Assert.Equal(Sort(original), accessor.Snapshot());
    }

    [Fact]
    public async Task DeleteActionWithPayload_IsRejectedBeforeWrite()
    {
        var accessor = new FakeAccessor(AdvertisingState(0, "existing-id"));
        var capability = Create(accessor);
        var before = await capability.ReadAsync(RegistryValueSetCapability.CreateCurrentUserScope(), CancellationToken.None);
        var invalid = System.Text.Json.JsonSerializer.Serialize(new
        {
            Values = new object[]
            {
                new { ValueName = "Enabled", Kind = 1, StringValue = (string?)null, DwordValue = (int?)1, Action = 0 },
                new { ValueName = "Id", Kind = 0, StringValue = "must-not-exist", DwordValue = (int?)null, Action = 1 }
            }
        });

        await Assert.ThrowsAsync<ArgumentException>(() =>
            capability.PrepareAsync(before, invalid, CancellationToken.None));
        Assert.Equal(0, accessor.WriteCount);
    }

    private static RegistryValueSetCapability Create(
        IRegistryValueSetAccessor accessor,
        TimeSpan? safetyTimeout = null) => new(
        RegistryValueSetCapabilityDefinitions.DisableAdvertisingId,
        accessor,
        safetyCompensationTimeout: safetyTimeout);

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
        "advertising-id-fake-test");

    private static RegistryValueSetEntryState Missing(string name) =>
        new(name, false, false, null, null, null);

    private static RegistryValueSetEntryState Dword(string name, int value) =>
        new(name, true, true, "DWord", null, value);

    private static RegistryValueSetEntryState String(string name, string value) =>
        new(name, true, true, "String", value, null);

    private static RegistryValueSetEntryState[] AdvertisingState(int enabled, string id) =>
        [Dword("Enabled", enabled), String("Id", id)];

    private static string RepairTarget() => System.Text.Json.JsonSerializer.Serialize(new
    {
        Values = new object[]
        {
            new { ValueName = "Enabled", Kind = 1, StringValue = (string?)null, DwordValue = (int?)1, Action = 0 },
            new { ValueName = "Id", Kind = 0, StringValue = (string?)null, DwordValue = (int?)null, Action = 1 }
        }
    });

    private static string LegacySetTargetWithoutAction() => System.Text.Json.JsonSerializer.Serialize(new
    {
        Values = new object[]
        {
            new { ValueName = "Enabled", Kind = 1, StringValue = (string?)null, DwordValue = (int?)1 },
            new { ValueName = "Id", Kind = 0, StringValue = ZeroId, DwordValue = (int?)null }
        }
    });

    private static RegistryValueSetEntryState[] Sort(IEnumerable<RegistryValueSetEntryState> values) =>
        values.OrderBy(value => value.ValueName, StringComparer.OrdinalIgnoreCase).ToArray();

    private sealed class FakeAccessor(IEnumerable<RegistryValueSetEntryState> initial) : IRegistryValueSetAccessor
    {
        private readonly Dictionary<string, RegistryValueSetEntryState> _state = initial.ToDictionary(
            state => state.ValueName,
            StringComparer.OrdinalIgnoreCase);

        public int? ThrowOnWriteCall { get; set; }
        public int? CancelOnWriteCall { get; set; }
        public bool BlockRestoreUntilCancelled { get; set; }
        public int? ThrowOnDeleteCall { get; set; }
        public bool SafetyCancellationObserved { get; private set; }
        public int ReadCount { get; private set; }
        public int WriteCount { get; private set; }
        public List<string> RestoreOrder { get; } = [];
        public List<CancellationToken> CompensationTokens { get; } = [];
        private int _deleteCount;

        public RegistryValueSetEntryState Get(string name) => _state[name];

        public void Set(RegistryValueSetEntryState state) => _state[state.ValueName] = state;

        public RegistryValueSetEntryState[] Snapshot() => Sort(_state.Values);

        public Task<IReadOnlyList<RegistryValueSetEntryState>> ReadCurrentUserAsync(
            string subKeyPath,
            IReadOnlyList<string> valueNames,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
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
                throw new IOException("fake mixed write failure");
            }

            _state[target.ValueName] = target.Kind switch
            {
                RegistryScalarValueKind.DWord => Dword(target.ValueName, target.DwordValue!.Value),
                RegistryScalarValueKind.String => String(target.ValueName, target.StringValue!),
                _ => throw new InvalidOperationException("unsupported fake kind")
            };
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
            if (!original.ValueExists && ThrowOnDeleteCall == ++_deleteCount)
            {
                throw new IOException("fake delete failure");
            }
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
