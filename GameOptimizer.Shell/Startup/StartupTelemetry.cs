// File: GameOptimizer.Shell/Startup/StartupTelemetry.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameOptimizer.Shell.Startup;

/// <summary>
/// 单个初始化阶段的遥测记录
/// </summary>
public sealed class StageRecord
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("startTime")]
    public DateTime StartTime { get; init; }

    [JsonPropertyName("durationMs")]
    public long DurationMs { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }
}

/// <summary>
/// 启动遥测数据，记录各初始化阶段耗时。
/// 线程安全：仅在 UI 线程上顺序调用 BeginStage/EndStage。
/// </summary>
public sealed class StartupTelemetry
{
    private readonly Stopwatch _totalStopwatch = new();
    private Stopwatch? _currentStageStopwatch;
    private StageRecord? _currentStage;

    [JsonPropertyName("stages")]
    public List<StageRecord> Stages { get; init; } = new();

    [JsonPropertyName("totalElapsedMs")]
    public long TotalElapsedMs => _totalStopwatch.ElapsedMilliseconds;

    /// <summary>
    /// 开始记录一个新阶段。如果上一个阶段未结束，自动以失败状态结束。
    /// </summary>
    public void BeginStage(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        // 如果有未结束的阶段，自动结束（标记为失败）
        if (_currentStage != null)
            EndStage(success: false);

        if (!_totalStopwatch.IsRunning)
            _totalStopwatch.Start();

        _currentStage = new StageRecord
        {
            Name = name,
            StartTime = DateTime.UtcNow
        };
        _currentStageStopwatch = Stopwatch.StartNew();
    }

    /// <summary>
    /// 结束当前阶段并记录结果。
    /// </summary>
    public void EndStage(bool success)
    {
        if (_currentStage == null || _currentStageStopwatch == null)
            return;

        _currentStageStopwatch.Stop();
        _currentStage.DurationMs = _currentStageStopwatch.ElapsedMilliseconds;
        _currentStage.Success = success;
        Stages.Add(_currentStage);

        _currentStage = null;
        _currentStageStopwatch = null;
    }

    /// <summary>
    /// 停止总计时器。在所有阶段完成后调用。
    /// </summary>
    public void Stop()
    {
        // 如果有未结束的阶段，自动结束
        if (_currentStage != null)
            EndStage(success: false);

        _totalStopwatch.Stop();
    }

    /// <summary>
    /// 序列化为 JSON 字符串。
    /// </summary>
    public string ToJson()
    {
        // Why: Stop() 确保 TotalElapsedMs 是最终值，但不强制调用——
        // 如果调用方忘了 Stop()，TotalElapsedMs 仍然返回当前值。
        var snapshot = new TelemetrySnapshot
        {
            Stages = new List<StageRecord>(Stages),
            TotalElapsedMs = TotalElapsedMs
        };

        return JsonSerializer.Serialize(snapshot, TelemetryJsonContext.Default.TelemetrySnapshot);
    }

    /// <summary>
    /// 从 JSON 反序列化。
    /// </summary>
    public static StartupTelemetry? FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var snapshot = JsonSerializer.Deserialize(json, TelemetryJsonContext.Default.TelemetrySnapshot);
            if (snapshot == null)
                return null;

            var telemetry = new StartupTelemetry();
            foreach (var stage in snapshot.Stages)
            {
                telemetry.Stages.Add(stage);
            }
            return telemetry;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 生成人类可读的摘要文本。
    /// </summary>
    public string ToSummary()
    {
        var lines = new List<string>
        {
            $"[StartupTelemetry] Total: {TotalElapsedMs}ms, Stages: {Stages.Count}"
        };

        foreach (var stage in Stages)
        {
            var status = stage.Success ? "OK" : "FAIL";
            lines.Add($"  [{status}] {stage.Name}: {stage.DurationMs}ms");
        }

        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>
/// JSON 序列化用的快照结构（避免序列化 Stopwatch 等内部状态）
/// </summary>
public sealed class TelemetrySnapshot
{
    [JsonPropertyName("stages")]
    public List<StageRecord> Stages { get; init; } = new();

    [JsonPropertyName("totalElapsedMs")]
    public long TotalElapsedMs { get; init; }
}

/// <summary>
/// Source-generated JSON context for AOT-friendly serialization.
/// </summary>
[JsonSerializable(typeof(TelemetrySnapshot))]
[JsonSerializable(typeof(StageRecord))]
internal partial class TelemetryJsonContext : JsonSerializerContext
{
}
