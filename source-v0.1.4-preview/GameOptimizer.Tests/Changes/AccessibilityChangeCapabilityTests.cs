using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Optimization.Operations;

namespace GameOptimizer.Tests.Changes;

public sealed class AccessibilityChangeCapabilityTests
{
    private const string OwnerSid = "S-1-5-21-test-owner";

    public static TheoryData<int> Definitions => new()
    {
        0,
        1,
        2
    };

    [Theory]
    [MemberData(nameof(Definitions))]
    public void Descriptor_RequiresExplicitConfirmationAndRecoveryAtMediumRisk(int definitionIndex)
    {
        var capability = CreateCapability(Definition(definitionIndex), new FakeAccessibilityPort());

        Assert.Equal(ChangeRisk.MediumRebootOrExperience, capability.Descriptor.Risk);
        Assert.True(capability.Descriptor.RequiresExplicitConfirmation);
        Assert.True(capability.Descriptor.RequiresRecoveryMaterial);
        Assert.False(capability.Descriptor.RequiresAdministrator);
        Assert.False(capability.Descriptor.RequiresRestart);
        Assert.Equal([TargetScopeKind.CurrentUser], capability.Descriptor.SupportedScopes);
    }

    [Fact]
    public async Task Probe_RejectsAnotherUserScope()
    {
        var capability = CreateCapability(AccessibilityCapabilityDefinition.StickyKeys, new FakeAccessibilityPort());

        var assessment = await capability.ProbeAsync(
            TargetScope.CurrentUser("S-1-5-21-other"),
            CancellationToken.None);

        Assert.Equal(CapabilityApplicability.NotApplicable, assessment.Applicability);
        Assert.False(assessment.HasRequiredPermission);
        Assert.Equal("scope.invalid", assessment.ReasonCode);
    }

    [Fact]
    public async Task Prepare_FilterKeysPreservesAllTimingFieldsAndOnlyClearsTargetBit()
    {
        var original = new AccessibilityParameters(0xA4, 1100, 250, 80, 35);
        var platform = new FakeAccessibilityPort { Parameters = original };
        var capability = CreateCapability(AccessibilityCapabilityDefinition.FilterKeys, platform);
        var current = await capability.ReadAsync(Scope(), CancellationToken.None);

        var prepared = await capability.PrepareAsync(current, "apply", CancellationToken.None);
        var target = System.Text.Json.JsonSerializer.Deserialize<AccessibilityParameters>(prepared.TargetPayload);

        Assert.NotNull(target);
        Assert.Equal(original with { Flags = 0xA0 }, target);
        Assert.NotEqual(prepared.BeforeFingerprint, prepared.TargetFingerprint);
    }

    [Fact]
    public async Task Execute_BlocksStateDriftWithoutWriting()
    {
        var platform = new FakeAccessibilityPort { Parameters = new AccessibilityParameters(0x04, 1, 2, 3, 4) };
        var capability = CreateCapability(AccessibilityCapabilityDefinition.FilterKeys, platform);
        var prepared = await PrepareAsync(capability);
        platform.Parameters = platform.Parameters with { DelayMilliseconds = 99 };

        var result = await capability.ExecuteAsync(prepared, CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.MutationOccurred);
        Assert.Equal("state.drift", result.ReasonCode);
        Assert.Equal(0, platform.WriteCount);
    }

    [Fact]
    public async Task Execute_WritesAndVerifiesCompleteTarget()
    {
        var original = new AccessibilityParameters(0x04, 1000, 200, 70, 30);
        var platform = new FakeAccessibilityPort { Parameters = original };
        var capability = CreateCapability(AccessibilityCapabilityDefinition.FilterKeys, platform);
        var prepared = await PrepareAsync(capability);

        var result = await capability.ExecuteAsync(prepared, CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.MutationOccurred);
        Assert.Equal("execution.verified", result.ReasonCode);
        Assert.Equal(original with { Flags = 0 }, platform.Parameters);
        Assert.Equal(1, platform.WriteCount);
    }

    [Fact]
    public async Task Execute_WriteFailureReportsUnknownMutationAndKeepsRecoveryRequired()
    {
        var platform = new FakeAccessibilityPort
        {
            Parameters = new AccessibilityParameters(0x04),
            WriteFailure = AccessibilityFlagsWriteResult.Failed(5)
        };
        var capability = CreateCapability(AccessibilityCapabilityDefinition.ToggleKeys, platform);
        var prepared = await PrepareAsync(capability);

        var result = await capability.ExecuteAsync(prepared, CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(result.MutationOccurred);
        Assert.Equal("accessibility.write-failed-unknown-mutation", result.ReasonCode);
        Assert.Contains("Win32=5", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Execute_ReadbackMismatchNeverReportsSuccess()
    {
        var platform = new FakeAccessibilityPort
        {
            Parameters = new AccessibilityParameters(0x04),
            MutateOnWrite = false
        };
        var capability = CreateCapability(AccessibilityCapabilityDefinition.ToggleKeys, platform);
        var prepared = await PrepareAsync(capability);

        var result = await capability.ExecuteAsync(prepared, CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(result.MutationOccurred);
        Assert.Equal("verification.mismatch", result.ReasonCode);
    }

    [Fact]
    public async Task Restore_BlocksDriftFromExecutedTarget()
    {
        var platform = new FakeAccessibilityPort { Parameters = new AccessibilityParameters(0x04) };
        var capability = CreateCapability(AccessibilityCapabilityDefinition.StickyKeys, platform);
        var prepared = await PrepareAsync(capability);
        Assert.True((await capability.ExecuteAsync(prepared, CancellationToken.None)).Success);
        var material = Recovery(prepared);
        platform.Parameters = new AccessibilityParameters(0x40);

        var result = await capability.RestoreAsync(material, CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.MutationOccurred);
        Assert.Equal("restore.state-drift", result.ReasonCode);
        Assert.Equal(1, platform.WriteCount);
    }

    [Fact]
    public async Task Restore_RestoresAndVerifiesCompleteFilterKeysState()
    {
        var original = new AccessibilityParameters(0x04, 900, 180, 65, 25);
        var platform = new FakeAccessibilityPort { Parameters = original };
        var capability = CreateCapability(AccessibilityCapabilityDefinition.FilterKeys, platform);
        var prepared = await PrepareAsync(capability);
        Assert.True((await capability.ExecuteAsync(prepared, CancellationToken.None)).Success);

        var result = await capability.RestoreAsync(Recovery(prepared), CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.MutationOccurred);
        Assert.Equal("restore.verified", result.ReasonCode);
        Assert.Equal(original, platform.Parameters);
    }

    [Fact]
    public async Task Restore_RejectsTamperedBeforePayload()
    {
        var platform = new FakeAccessibilityPort { Parameters = new AccessibilityParameters(0) };
        var capability = CreateCapability(AccessibilityCapabilityDefinition.FilterKeys, platform);
        var prepared = await PrepareAsync(capability);
        var material = Recovery(prepared) with { BeforePayload = "{}" };

        await Assert.ThrowsAsync<InvalidDataException>(
            () => capability.RestoreAsync(material, CancellationToken.None));
        Assert.Equal(0, platform.WriteCount);
    }

    [Fact]
    public async Task Execute_CancelledBeforeWriteDoesNotMutate()
    {
        var platform = new FakeAccessibilityPort { Parameters = new AccessibilityParameters(0x04) };
        var capability = CreateCapability(AccessibilityCapabilityDefinition.ToggleKeys, platform);
        var prepared = await PrepareAsync(capability);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => capability.ExecuteAsync(prepared, cancellation.Token));
        Assert.Equal(0, platform.WriteCount);
    }

    private static AccessibilityChangeCapability CreateCapability(
        AccessibilityCapabilityDefinition definition,
        IAccessibilitySystemParametersPort platform) =>
        new(definition, platform, OwnerSid, TimeProvider.System);

    private static async Task<PreparedCapabilityChange> PrepareAsync(AccessibilityChangeCapability capability)
    {
        var current = await capability.ReadAsync(Scope(), CancellationToken.None);
        return await capability.PrepareAsync(current, "apply", CancellationToken.None);
    }

    private static RecoveryMaterial Recovery(PreparedCapabilityChange prepared) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        prepared.CapabilityId,
        prepared.DefinitionVersion,
        prepared.Scope,
        prepared.BeforePayload,
        prepared.BeforeFingerprint,
        prepared.TargetFingerprint,
        prepared.ResourceKey,
        DateTimeOffset.UtcNow,
        "integrity-owned-by-orchestrator");

    private static TargetScope Scope() => TargetScope.CurrentUser(OwnerSid);

    private static AccessibilityCapabilityDefinition Definition(int index) => index switch
    {
        0 => AccessibilityCapabilityDefinition.StickyKeys,
        1 => AccessibilityCapabilityDefinition.FilterKeys,
        2 => AccessibilityCapabilityDefinition.ToggleKeys,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    private sealed class FakeAccessibilityPort : IAccessibilitySystemParametersPort
    {
        public AccessibilityParameters Parameters { get; set; } = new(0);
        public AccessibilityParametersReadResult? ReadFailure { get; set; }
        public AccessibilityFlagsWriteResult? WriteFailure { get; set; }
        public bool MutateOnWrite { get; set; } = true;
        public int WriteCount { get; private set; }

        public AccessibilityParametersReadResult ReadParameters(AccessibilitySetting setting) =>
            ReadFailure ?? AccessibilityParametersReadResult.Succeeded(Parameters);

        public AccessibilityFlagsWriteResult WriteParameters(
            AccessibilitySetting setting,
            AccessibilityParameters parameters)
        {
            WriteCount++;
            if (WriteFailure is { } failure)
            {
                return failure;
            }

            if (MutateOnWrite)
            {
                Parameters = parameters;
            }

            return AccessibilityFlagsWriteResult.Succeeded();
        }
    }
}
