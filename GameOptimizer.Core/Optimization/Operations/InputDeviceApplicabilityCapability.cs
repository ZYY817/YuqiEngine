using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Core.Optimization.Operations;

public enum InputDeviceKind
{
    Pen,
    Touch
}

public enum InputDevicePresence
{
    Present,
    Absent,
    Unknown
}

public sealed record InputDeviceApplicabilityResult(
    InputDevicePresence Presence,
    string ReasonCode,
    string Details);

public interface IInputDeviceApplicabilityProbe
{
    Task<InputDeviceApplicabilityResult> ProbeAsync(
        InputDeviceKind deviceKind,
        CancellationToken cancellationToken);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsInputDeviceApplicabilityProbe : IInputDeviceApplicabilityProbe
{
    private const int SmDigitizer = 94;
    private const int SmMaximumTouches = 95;
    private const int NidIntegratedTouch = 0x01;
    private const int NidExternalTouch = 0x02;
    private const int NidIntegratedPen = 0x04;
    private const int NidExternalPen = 0x08;
    private const int NidReady = 0x80;

    public Task<InputDeviceApplicabilityResult> ProbeAsync(
        InputDeviceKind deviceKind,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            return Task.FromResult(Unknown("os.unsupported", "设备适用性探测要求 Windows 10 2004 或更高版本。"));
        }

        try
        {
            var digitizer = GetSystemMetrics(SmDigitizer);
            var maximumTouches = GetSystemMetrics(SmMaximumTouches);
            return Task.FromResult(Evaluate(deviceKind, digitizer, maximumTouches));
        }
        catch (Exception exception)
        {
            return Task.FromResult(Unknown(
                "input-device.probe-exception",
                $"输入设备适用性探测失败：{exception.GetType().Name}。"));
        }
    }

    internal static InputDeviceApplicabilityResult Evaluate(
        InputDeviceKind deviceKind,
        int digitizerCapabilities,
        int maximumTouches)
    {
        if (digitizerCapabilities < 0 || maximumTouches < 0)
        {
            return Unknown("input-device.probe-invalid", "系统返回了无效的输入设备指标。");
        }

        var ready = (digitizerCapabilities & NidReady) != 0;
        var penPresent = (digitizerCapabilities & (NidIntegratedPen | NidExternalPen)) != 0;
        var touchPresent = (digitizerCapabilities & (NidIntegratedTouch | NidExternalTouch)) != 0;

        if (digitizerCapabilities == 0 && maximumTouches == 0)
        {
            return Absent(deviceKind, "input-device.absent", "系统未报告笔或触控数字化设备。");
        }

        if (!ready)
        {
            return Unknown("input-device.not-ready", "系统报告了数字化能力，但设备未处于 ready 状态。");
        }

        return deviceKind switch
        {
            InputDeviceKind.Pen when penPresent => Present("input-device.pen-present", "检测到可用手写笔数字化设备。"),
            InputDeviceKind.Pen => Absent(deviceKind, "input-device.pen-absent", "系统未报告手写笔数字化能力。"),
            InputDeviceKind.Touch when touchPresent && maximumTouches > 0 =>
                Present("input-device.touch-present", $"检测到触控设备，最大触点数 {maximumTouches}。"),
            InputDeviceKind.Touch when touchPresent =>
                Unknown("input-device.touch-inconsistent", "系统报告触控能力，但最大触点数为 0。"),
            InputDeviceKind.Touch when maximumTouches > 0 =>
                Unknown("input-device.touch-inconsistent", "系统报告了触点数，但未报告触控能力位。"),
            InputDeviceKind.Touch => Absent(deviceKind, "input-device.touch-absent", "系统未报告触控数字化能力。"),
            _ => Unknown("input-device.kind-unknown", "无法识别输入设备类型。")
        };
    }

    private static InputDeviceApplicabilityResult Present(string reasonCode, string details) =>
        new(InputDevicePresence.Present, reasonCode, details);

    private static InputDeviceApplicabilityResult Absent(
        InputDeviceKind deviceKind,
        string reasonCode,
        string details) =>
        new(InputDevicePresence.Absent, reasonCode, $"{deviceKind}: {details}");

    private static InputDeviceApplicabilityResult Unknown(string reasonCode, string details) =>
        new(InputDevicePresence.Unknown, reasonCode, details);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}

public static class InputDeviceApplicabilityCapabilities
{
    public static IChangeCapability ForPen(
        IChangeCapability inner,
        IInputDeviceApplicabilityProbe? probe = null) =>
        Create(
            inner,
            "input.disable_pen_feedback",
            InputDeviceKind.Pen,
            probe);

    public static IChangeCapability ForTouch(
        IChangeCapability inner,
        IInputDeviceApplicabilityProbe? probe = null) =>
        Create(
            inner,
            "input.disable_touch_feedback",
            InputDeviceKind.Touch,
            probe);

    private static IChangeCapability Create(
        IChangeCapability inner,
        string expectedCapabilityId,
        InputDeviceKind deviceKind,
        IInputDeviceApplicabilityProbe? probe)
    {
        ArgumentNullException.ThrowIfNull(inner);
        if (!string.Equals(inner.Descriptor.CapabilityId, expectedCapabilityId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"设备适用性 wrapper 期望 capability '{expectedCapabilityId}'，实际为 '{inner.Descriptor.CapabilityId}'。",
                nameof(inner));
        }

        return new InputDeviceApplicabilityCapability(
            inner,
            probe ?? new WindowsInputDeviceApplicabilityProbe(),
            deviceKind);
    }
}

internal sealed class InputDeviceApplicabilityCapability : IChangeCapability
{
    private readonly IChangeCapability _inner;
    private readonly IInputDeviceApplicabilityProbe _probe;
    private readonly InputDeviceKind _deviceKind;

    public InputDeviceApplicabilityCapability(
        IChangeCapability inner,
        IInputDeviceApplicabilityProbe probe,
        InputDeviceKind deviceKind)
    {
        _inner = inner;
        _probe = probe;
        _deviceKind = deviceKind;
    }

    public CapabilityDescriptor Descriptor => _inner.Descriptor;

    public async Task<CapabilityAssessment> ProbeAsync(
        TargetScope scope,
        CancellationToken cancellationToken) =>
        await ProbeAsync(scope, ChangeIntent.Prepare, cancellationToken).ConfigureAwait(false);

    public async Task<CapabilityAssessment> ProbeAsync(
        TargetScope scope,
        ChangeIntent intent,
        CancellationToken cancellationToken)
    {
        var innerAssessment = await _inner.ProbeAsync(scope, intent, cancellationToken).ConfigureAwait(false);
        if (innerAssessment.Applicability != CapabilityApplicability.Applicable ||
            !innerAssessment.HasRequiredPermission)
        {
            return innerAssessment;
        }

        // Recovery targets the persisted registry resource, not the currently connected
        // digitizer. Keep the inner owner as the authority for scope, SID, permission and
        // resource identity, but do not strand valid recovery material after hot-unplug.
        if (intent == ChangeIntent.Restore)
        {
            return innerAssessment;
        }

        var device = await ProbeDeviceAsync(cancellationToken).ConfigureAwait(false);
        return device.Presence switch
        {
            InputDevicePresence.Present => innerAssessment with
            {
                ReasonCode = device.ReasonCode,
                Details = device.Details
            },
            InputDevicePresence.Absent => new CapabilityAssessment(
                CapabilityApplicability.NotApplicable,
                false,
                device.ReasonCode,
                device.Details),
            _ => new CapabilityAssessment(
                CapabilityApplicability.Unknown,
                false,
                device.ReasonCode,
                device.Details)
        };
    }

    public Task<StateObservation> ReadAsync(TargetScope scope, CancellationToken cancellationToken) =>
        _inner.ReadAsync(scope, cancellationToken);

    public Task<PreparedCapabilityChange> PrepareAsync(
        StateObservation current,
        string targetPayload,
        CancellationToken cancellationToken) =>
        _inner.PrepareAsync(current, targetPayload, cancellationToken);

    public async Task<PlatformExecutionResult> ExecuteAsync(
        PreparedCapabilityChange change,
        CancellationToken cancellationToken)
    {
        var device = await ProbeDeviceAsync(cancellationToken).ConfigureAwait(false);
        if (device.Presence != InputDevicePresence.Present)
        {
            return new PlatformExecutionResult(
                false,
                false,
                device.ReasonCode,
                device.Details);
        }

        return await _inner.ExecuteAsync(change, cancellationToken).ConfigureAwait(false);
    }

    public Task<PlatformExecutionResult> RestoreAsync(
        RecoveryMaterial material,
        CancellationToken cancellationToken) =>
        _inner.RestoreAsync(material, cancellationToken);

    private async Task<InputDeviceApplicabilityResult> ProbeDeviceAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _probe.ProbeAsync(_deviceKind, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new InputDeviceApplicabilityResult(
                InputDevicePresence.Unknown,
                "input-device.probe-exception",
                $"输入设备适用性探测失败：{exception.GetType().Name}。");
        }
    }
}
