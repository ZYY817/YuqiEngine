using System;
using System.Collections.Generic;
using GameOptimizer.Core.Models;

namespace GameOptimizer.Core.Services;

public enum TelemetryAvailability
{
    Live,
    Unavailable,
    Unsupported,
    PermissionDenied,
    Error
}

public sealed record TelemetryFieldStatus(
    TelemetryAvailability Availability,
    string Source,
    string Detail)
{
    public bool IsLive => Availability == TelemetryAvailability.Live;

    public static TelemetryFieldStatus Live(string source) =>
        new(TelemetryAvailability.Live, source, "读取成功");

    public static TelemetryFieldStatus Unavailable(string source, string detail) =>
        new(TelemetryAvailability.Unavailable, source, detail);

    public static TelemetryFieldStatus Unsupported(string source, string detail) =>
        new(TelemetryAvailability.Unsupported, source, detail);

    public static TelemetryFieldStatus PermissionDenied(string source, string detail) =>
        new(TelemetryAvailability.PermissionDenied, source, detail);

    public static TelemetryFieldStatus Error(string source, string detail) =>
        new(TelemetryAvailability.Error, source, detail);
}

public sealed record HomeTelemetrySnapshot(
    SystemResourceSnapshot Snapshot,
    DateTimeOffset CapturedAtUtc,
    TelemetryFieldStatus Cpu,
    TelemetryFieldStatus Gpu,
    TelemetryFieldStatus Memory,
    TelemetryFieldStatus Disk)
{
    /// <summary>
    /// 实时 GPU 负载的硬件归属。此字段与总利用率分离，避免把无法证明的映射显示为某一张卡。
    /// </summary>
    public GpuLoadAttribution GpuLoad { get; init; } = GpuLoadAttribution.Unavailable("未采样", "尚未采样");

    public bool HasRealCpuSample => Cpu.IsLive;

    public bool HasRealGpuSample => Gpu.IsLive;

    public bool HasRealMemorySample => Memory.IsLive;

    public bool HasRealDiskSample => Disk.IsLive;

    public static HomeTelemetrySnapshot FromSnapshot(
        SystemResourceSnapshot snapshot,
        DateTimeOffset capturedAtUtc,
        string source,
        TelemetryAvailability availability = TelemetryAvailability.Live,
        string detail = "读取成功")
    {
        var status = new TelemetryFieldStatus(availability, source, detail);
        return new HomeTelemetrySnapshot(snapshot, capturedAtUtc, status, status, status, status);
    }

    public static HomeTelemetrySnapshot Unavailable(
        DateTimeOffset capturedAtUtc,
        string source,
        TelemetryAvailability availability,
        string detail)
    {
        var snapshot = new SystemResourceSnapshot(0, 0, 0, 0, 0, 0, 0);
        var status = new TelemetryFieldStatus(availability, source, detail);
        return new HomeTelemetrySnapshot(snapshot, capturedAtUtc, status, status, status, status);
    }
}

public interface IHomeTelemetrySource
{
    HomeTelemetrySnapshot GetCurrentSnapshotWithStatus();
}

public sealed class TelemetryTrendBuffer
{
    private readonly int _capacity;
    private readonly Queue<double> _values = new();

    public TelemetryTrendBuffer(int capacity = 48)
    {
        if (capacity < 2)
            throw new ArgumentOutOfRangeException(nameof(capacity), "趋势容量必须至少为 2。");

        _capacity = capacity;
    }

    public int Count => _values.Count;

    public bool HasHistory => _values.Count > 1;

    public IReadOnlyList<double> Values => _values.ToArray();

    public void Clear() => _values.Clear();

    public bool TryAdd(double value, TelemetryFieldStatus status)
    {
        if (!status.IsLive || !double.IsFinite(value))
            return false;

        if (_values.Count >= _capacity)
            _values.Dequeue();

        _values.Enqueue(Math.Clamp(value, 0, 100));
        return true;
    }
}
