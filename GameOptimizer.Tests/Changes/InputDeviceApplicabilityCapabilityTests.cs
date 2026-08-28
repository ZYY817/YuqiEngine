using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Optimization.Operations;

namespace GameOptimizer.Tests.Changes;

public sealed class InputDeviceApplicabilityCapabilityTests
{
    private static readonly TargetScope Scope = TargetScope.CurrentUser("S-1-5-21-input-test");

    [Theory]
    [InlineData(0, 0, InputDeviceKind.Pen, InputDevicePresence.Absent)]
    [InlineData(0, 0, InputDeviceKind.Touch, InputDevicePresence.Absent)]
    [InlineData(0x84, 0, InputDeviceKind.Pen, InputDevicePresence.Present)]
    [InlineData(0x88, 0, InputDeviceKind.Pen, InputDevicePresence.Present)]
    [InlineData(0x81, 10, InputDeviceKind.Touch, InputDevicePresence.Present)]
    [InlineData(0x82, 5, InputDeviceKind.Touch, InputDevicePresence.Present)]
    [InlineData(0x80, 0, InputDeviceKind.Pen, InputDevicePresence.Absent)]
    [InlineData(0x80, 0, InputDeviceKind.Touch, InputDevicePresence.Absent)]
    [InlineData(0x04, 0, InputDeviceKind.Pen, InputDevicePresence.Unknown)]
    [InlineData(0x81, 0, InputDeviceKind.Touch, InputDevicePresence.Unknown)]
    [InlineData(0x80, 2, InputDeviceKind.Touch, InputDevicePresence.Unknown)]
    public void WindowsMetricEvaluation_ClassifiesPresenceWithoutSystemAccess(
        int digitizer,
        int maximumTouches,
        InputDeviceKind kind,
        InputDevicePresence expected)
    {
        var result = WindowsInputDeviceApplicabilityProbe.Evaluate(kind, digitizer, maximumTouches);

        Assert.Equal(expected, result.Presence);
    }

    [Fact]
    public async Task Probe_PenAbsentReturnsNotApplicableAndDoesNotReadOrWriteInner()
    {
        var inner = new FakeCapability("input.disable_pen_feedback");
        var wrapper = InputDeviceApplicabilityCapabilities.ForPen(
            inner,
            new FakeProbe(InputDevicePresence.Absent));

        var result = await wrapper.ProbeAsync(Scope, CancellationToken.None);

        Assert.Equal(CapabilityApplicability.NotApplicable, result.Applicability);
        Assert.False(result.HasRequiredPermission);
        Assert.Equal(0, inner.ReadCount);
        Assert.Equal(0, inner.ExecuteCount);
    }

    [Fact]
    public async Task Probe_TouchAbsentReturnsNotApplicable()
    {
        var inner = new FakeCapability("input.disable_touch_feedback");
        var wrapper = InputDeviceApplicabilityCapabilities.ForTouch(
            inner,
            new FakeProbe(InputDevicePresence.Absent));

        var result = await wrapper.ProbeAsync(Scope, CancellationToken.None);

        Assert.Equal(CapabilityApplicability.NotApplicable, result.Applicability);
        Assert.Equal("fake.absent", result.ReasonCode);
    }

    [Fact]
    public async Task Probe_UnknownFailsClosedAndDoesNotClaimDeviceExists()
    {
        var inner = new FakeCapability("input.disable_pen_feedback");
        var wrapper = InputDeviceApplicabilityCapabilities.ForPen(
            inner,
            new FakeProbe(InputDevicePresence.Unknown));

        var result = await wrapper.ProbeAsync(Scope, CancellationToken.None);

        Assert.Equal(CapabilityApplicability.Unknown, result.Applicability);
        Assert.False(result.HasRequiredPermission);
        Assert.Equal("fake.unknown", result.ReasonCode);
    }

    [Fact]
    public async Task Probe_ExceptionFailsClosedAsUnknown()
    {
        var inner = new FakeCapability("input.disable_touch_feedback");
        var wrapper = InputDeviceApplicabilityCapabilities.ForTouch(inner, new ThrowingProbe());

        var result = await wrapper.ProbeAsync(Scope, CancellationToken.None);

        Assert.Equal(CapabilityApplicability.Unknown, result.Applicability);
        Assert.False(result.HasRequiredPermission);
        Assert.Equal("input-device.probe-exception", result.ReasonCode);
    }

    [Fact]
    public async Task Probe_PresentPreservesInnerPermissionAndDescriptor()
    {
        var inner = new FakeCapability("input.disable_pen_feedback");
        var wrapper = InputDeviceApplicabilityCapabilities.ForPen(
            inner,
            new FakeProbe(InputDevicePresence.Present));

        var result = await wrapper.ProbeAsync(Scope, CancellationToken.None);

        Assert.Equal(CapabilityApplicability.Applicable, result.Applicability);
        Assert.True(result.HasRequiredPermission);
        Assert.Same(inner.Descriptor, wrapper.Descriptor);
        Assert.Equal(1, inner.ProbeCount);
    }

    [Fact]
    public async Task Probe_DoesNotMaskInvalidInnerScope()
    {
        var inner = new FakeCapability("input.disable_pen_feedback")
        {
            Assessment = new CapabilityAssessment(
                CapabilityApplicability.NotApplicable,
                false,
                "scope.invalid",
                "invalid scope")
        };
        var probe = new FakeProbe(InputDevicePresence.Present);
        var wrapper = InputDeviceApplicabilityCapabilities.ForPen(inner, probe);

        var result = await wrapper.ProbeAsync(Scope, CancellationToken.None);

        Assert.Equal("scope.invalid", result.ReasonCode);
        Assert.Equal(0, probe.CallCount);
    }

    [Fact]
    public async Task Execute_RechecksDeviceAndDoesNotWriteWhenDeviceDisappears()
    {
        var inner = new FakeCapability("input.disable_touch_feedback");
        var probe = new FakeProbe(InputDevicePresence.Present);
        var wrapper = InputDeviceApplicabilityCapabilities.ForTouch(inner, probe);
        Assert.Equal(CapabilityApplicability.Applicable, (await wrapper.ProbeAsync(Scope, CancellationToken.None)).Applicability);
        probe.Presence = InputDevicePresence.Absent;

        var result = await wrapper.ExecuteAsync(inner.PreparedChange, CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.MutationOccurred);
        Assert.Equal("fake.absent", result.ReasonCode);
        Assert.Equal(0, inner.ExecuteCount);
    }

    [Theory]
    [InlineData(ChangeIntent.Prepare)]
    [InlineData(ChangeIntent.Execute)]
    public async Task PrepareAndExecuteProbe_RequirePresentDevice(ChangeIntent intent)
    {
        var inner = new FakeCapability("input.disable_pen_feedback");
        var probe = new FakeProbe(InputDevicePresence.Absent);
        var wrapper = InputDeviceApplicabilityCapabilities.ForPen(inner, probe);

        var assessment = await wrapper.ProbeAsync(Scope, intent, CancellationToken.None);

        Assert.Equal(CapabilityApplicability.NotApplicable, assessment.Applicability);
        Assert.False(assessment.HasRequiredPermission);
        Assert.Equal(intent, inner.LastProbeIntent);
        Assert.Equal(1, probe.CallCount);
    }

    [Fact]
    public async Task Execute_DelegatesOnlyWhenDeviceIsPresent()
    {
        var inner = new FakeCapability("input.disable_pen_feedback");
        var wrapper = InputDeviceApplicabilityCapabilities.ForPen(
            inner,
            new FakeProbe(InputDevicePresence.Present));

        var result = await wrapper.ExecuteAsync(inner.PreparedChange, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, inner.ExecuteCount);
    }

    [Fact]
    public async Task Restore_RemainsAvailableWhenDeviceWasRemoved()
    {
        var inner = new FakeCapability("input.disable_pen_feedback");
        var probe = new FakeProbe(InputDevicePresence.Absent);
        var wrapper = InputDeviceApplicabilityCapabilities.ForPen(inner, probe);

        var result = await wrapper.RestoreAsync(inner.RecoveryMaterial, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, inner.RestoreCount);
        Assert.Equal(0, probe.CallCount);
    }

    [Fact]
    public async Task RestoreProbe_DeviceRemovedStillDelegatesScopePermissionAndIdentityToInner()
    {
        var inner = new FakeCapability("input.disable_pen_feedback");
        var probe = new FakeProbe(InputDevicePresence.Absent);
        var wrapper = InputDeviceApplicabilityCapabilities.ForPen(inner, probe);

        var assessment = await wrapper.ProbeAsync(Scope, ChangeIntent.Restore, CancellationToken.None);

        Assert.Equal(CapabilityApplicability.Applicable, assessment.Applicability);
        Assert.True(assessment.HasRequiredPermission);
        Assert.Equal(ChangeIntent.Restore, inner.LastProbeIntent);
        Assert.Equal(0, probe.CallCount);
    }

    [Fact]
    public async Task RestoreProbe_ObjectMissingFailsClosedWithoutDeviceProbe()
    {
        var inner = new FakeCapability("input.disable_touch_feedback");
        inner.SetIntentAssessment(ChangeIntent.Restore, new(
            CapabilityApplicability.NotApplicable,
            false,
            "registry.target-missing",
            "target disappeared"));
        var probe = new FakeProbe(InputDevicePresence.Present);
        var wrapper = InputDeviceApplicabilityCapabilities.ForTouch(inner, probe);

        var assessment = await wrapper.ProbeAsync(Scope, ChangeIntent.Restore, CancellationToken.None);

        Assert.Equal(CapabilityApplicability.NotApplicable, assessment.Applicability);
        Assert.Equal("registry.target-missing", assessment.ReasonCode);
        Assert.Equal(0, probe.CallCount);
    }

    [Fact]
    public async Task RestoreProbe_PermissionRevokedFailsClosedWithoutDeviceProbe()
    {
        var inner = new FakeCapability("input.disable_pen_feedback");
        inner.SetIntentAssessment(ChangeIntent.Restore, new(
            CapabilityApplicability.Applicable,
            false,
            "permission.denied",
            "SID no longer has permission"));
        var probe = new FakeProbe(InputDevicePresence.Absent);
        var wrapper = InputDeviceApplicabilityCapabilities.ForPen(inner, probe);

        var assessment = await wrapper.ProbeAsync(Scope, ChangeIntent.Restore, CancellationToken.None);

        Assert.False(assessment.HasRequiredPermission);
        Assert.Equal("permission.denied", assessment.ReasonCode);
        Assert.Equal(0, probe.CallCount);
    }

    [Fact]
    public async Task RestoreProbe_ResourceIdentityDriftFailsClosedWithoutDeviceProbe()
    {
        var inner = new FakeCapability("input.disable_touch_feedback");
        inner.SetIntentAssessment(ChangeIntent.Restore, new(
            CapabilityApplicability.Conflict,
            false,
            "resource.identity-drift",
            "resource identity changed"));
        var probe = new FakeProbe(InputDevicePresence.Present);
        var wrapper = InputDeviceApplicabilityCapabilities.ForTouch(inner, probe);

        var assessment = await wrapper.ProbeAsync(Scope, ChangeIntent.Restore, CancellationToken.None);

        Assert.Equal(CapabilityApplicability.Conflict, assessment.Applicability);
        Assert.Equal("resource.identity-drift", assessment.ReasonCode);
        Assert.Equal(0, probe.CallCount);
    }

    [Fact]
    public async Task Restore_InnerRejectsMismatchedRecoveryMaterial()
    {
        var inner = new FakeCapability("input.disable_pen_feedback") { RejectRecoveryMaterial = true };
        var wrapper = InputDeviceApplicabilityCapabilities.ForPen(inner, new FakeProbe(InputDevicePresence.Absent));

        var result = await wrapper.RestoreAsync(inner.RecoveryMaterial, CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.MutationOccurred);
        Assert.Equal("restore.material-invalid", result.ReasonCode);
    }

    [Theory]
    [InlineData(InputDevicePresence.Absent)]
    [InlineData(InputDevicePresence.Present)]
    public async Task Restore_WorksWhetherDeviceRemainsDisconnectedOrReconnects(InputDevicePresence presence)
    {
        var inner = new FakeCapability("input.disable_touch_feedback");
        var wrapper = InputDeviceApplicabilityCapabilities.ForTouch(inner, new FakeProbe(presence));

        var assessment = await wrapper.ProbeAsync(Scope, ChangeIntent.Restore, CancellationToken.None);
        var result = await wrapper.RestoreAsync(inner.RecoveryMaterial, CancellationToken.None);

        Assert.Equal(CapabilityApplicability.Applicable, assessment.Applicability);
        Assert.True(result.Success);
        Assert.Equal(1, inner.RestoreCount);
    }

    [Fact]
    public void FactoryRejectsWrongInnerCapabilityId()
    {
        var inner = new FakeCapability("registry.some-other-capability");

        Assert.Throws<ArgumentException>(
            () => InputDeviceApplicabilityCapabilities.ForPen(inner, new FakeProbe(InputDevicePresence.Present)));
    }

    private sealed class FakeProbe : IInputDeviceApplicabilityProbe
    {
        public FakeProbe(InputDevicePresence presence)
        {
            Presence = presence;
        }

        public InputDevicePresence Presence { get; set; }
        public int CallCount { get; private set; }

        public Task<InputDeviceApplicabilityResult> ProbeAsync(
            InputDeviceKind deviceKind,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            var suffix = Presence.ToString().ToLowerInvariant();
            return Task.FromResult(new InputDeviceApplicabilityResult(
                Presence,
                $"fake.{suffix}",
                $"{deviceKind} is {suffix}"));
        }
    }

    private sealed class ThrowingProbe : IInputDeviceApplicabilityProbe
    {
        public Task<InputDeviceApplicabilityResult> ProbeAsync(
            InputDeviceKind deviceKind,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("probe failed");
    }

    private sealed class FakeCapability : IChangeCapability
    {
        public FakeCapability(string id)
        {
            Descriptor = new CapabilityDescriptor(
                id,
                "1.0.0",
                ChangeRisk.MediumRebootOrExperience,
                [TargetScopeKind.CurrentUser],
                false,
                true,
                true,
                false,
                $"fake:{id}");
            PreparedChange = new PreparedCapabilityChange(
                id,
                "1.0.0",
                Scope,
                "before",
                "before-hash",
                "target",
                "target-hash",
                $"fake:{id}");
            RecoveryMaterial = new RecoveryMaterial(
                Guid.NewGuid(),
                Guid.NewGuid(),
                id,
                "1.0.0",
                Scope,
                "before",
                "before-hash",
                "target-hash",
                $"fake:{id}",
                DateTimeOffset.UtcNow,
                "integrity");
        }

        public CapabilityDescriptor Descriptor { get; }
        public PreparedCapabilityChange PreparedChange { get; }
        public RecoveryMaterial RecoveryMaterial { get; }
        public int ProbeCount { get; private set; }
        public int ReadCount { get; private set; }
        public int ExecuteCount { get; private set; }
        public int RestoreCount { get; private set; }
        public ChangeIntent? LastProbeIntent { get; private set; }
        public bool RejectRecoveryMaterial { get; set; }
        public CapabilityAssessment Assessment { get; set; } = new(
            CapabilityApplicability.Applicable,
            true,
            "applicable",
            "inner applicable");

        public Task<CapabilityAssessment> ProbeAsync(TargetScope scope, CancellationToken cancellationToken)
        {
            ProbeCount++;
            return Task.FromResult(Assessment);
        }

        private readonly Dictionary<ChangeIntent, CapabilityAssessment> _intentAssessments = [];

        public void SetIntentAssessment(ChangeIntent intent, CapabilityAssessment assessment) =>
            _intentAssessments[intent] = assessment;

        public Task<CapabilityAssessment> ProbeAsync(
            TargetScope scope,
            ChangeIntent intent,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProbeCount++;
            LastProbeIntent = intent;
            return Task.FromResult(_intentAssessments.TryGetValue(intent, out var assessment) ? assessment : Assessment);
        }

        public Task<StateObservation> ReadAsync(TargetScope scope, CancellationToken cancellationToken)
        {
            ReadCount++;
            return Task.FromResult(new StateObservation(scope, "before", "before-hash", DateTimeOffset.UtcNow, "fake", "fake"));
        }

        public Task<PreparedCapabilityChange> PrepareAsync(
            StateObservation current,
            string targetPayload,
            CancellationToken cancellationToken) => Task.FromResult(PreparedChange);

        public Task<PlatformExecutionResult> ExecuteAsync(
            PreparedCapabilityChange change,
            CancellationToken cancellationToken)
        {
            ExecuteCount++;
            return Task.FromResult(new PlatformExecutionResult(true, true, "execution.verified", "executed"));
        }

        public Task<PlatformExecutionResult> RestoreAsync(
            RecoveryMaterial material,
            CancellationToken cancellationToken)
        {
            RestoreCount++;
            if (RejectRecoveryMaterial)
            {
                return Task.FromResult(new PlatformExecutionResult(false, false, "restore.material-invalid", "recovery material does not match resource identity"));
            }
            return Task.FromResult(new PlatformExecutionResult(true, true, "restore.verified", "restored"));
        }
    }
}
