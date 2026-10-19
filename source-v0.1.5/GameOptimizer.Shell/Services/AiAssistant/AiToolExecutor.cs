// File: GameOptimizer.Shell/Services/AiAssistant/AiToolExecutor.cs
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GameOptimizer.Shell.Services.AiAssistant;

/// <summary>
/// AI 工具注册与执行。只读工具直接执行；写工具（apply/restore）先产出
/// 待确认动作交给 UI，用户确认后才经网关走完整管线——AI 不能绕过确认。
/// </summary>
public sealed class AiToolExecutor
{
    /// <summary>写工具名称集合——这些工具必须经用户确认卡才执行。</summary>
    public static readonly IReadOnlySet<string> WriteTools =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "apply_operations", "restore_operations" };

    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IAiOptimizationGateway _gateway;
    private readonly Func<AiPendingWriteAction, CancellationToken, Task<bool>> _requestUserConfirmation;
    private readonly IAiDiagnosticsRunner? _diagnosticsRunner;

    /// <param name="requestUserConfirmation">写操作确认回调：返回 true=用户批准，false=拒绝。</param>
    /// <param name="diagnosticsRunner">只读诊断采集器；为空时 collect_diagnostics 工具返回不可用提示。</param>
    public AiToolExecutor(
        IAiOptimizationGateway gateway,
        Func<AiPendingWriteAction, CancellationToken, Task<bool>> requestUserConfirmation,
        IAiDiagnosticsRunner? diagnosticsRunner = null)
    {
        _gateway = gateway;
        _requestUserConfirmation = requestUserConfirmation;
        _diagnosticsRunner = diagnosticsRunner;
    }

    public static bool IsWriteTool(string toolName) => WriteTools.Contains(toolName);

    /// <summary>供 API 请求的 tools 数组定义。</summary>
    public IReadOnlyList<AiToolDefinition> GetToolDefinitions()
    {
        static JsonElement Schema(string json) =>
            JsonDocument.Parse(json).RootElement.Clone();

        return new List<AiToolDefinition>
        {
            new()
            {
                Function = new AiToolFunctionSpec
                {
                    Name = "list_modules",
                    Description = "列出本软件全部优化模块及其已应用/总操作数",
                    Parameters = Schema("""{"type":"object","properties":{}}""")
                }
            },
            new()
            {
                Function = new AiToolFunctionSpec
                {
                    Name = "list_operations",
                    Description = "列出指定模块（缺省为全部模块）的优化操作：operationId、标题、风险等级、是否已应用、是否可应用/可恢复",
                    Parameters = Schema("""{"type":"object","properties":{"module_key":{"type":"string","description":"模块键，如 memory-optimization；留空返回全部"}}}""")
                }
            },
            new()
            {
                Function = new AiToolFunctionSpec
                {
                    Name = "get_operation_status",
                    Description = "读取单个优化操作的详细状态：当前值、建议值、风险说明",
                    Parameters = Schema("""{"type":"object","properties":{"operation_id":{"type":"string"}},"required":["operation_id"]}""")
                }
            },
            new()
            {
                Function = new AiToolFunctionSpec
                {
                    Name = "collect_diagnostics",
                    Description = "运行只读诊断脚本采集系统关键信号（CPU/GPU/内存/存储/温度/后台占用等），不改任何设置。回答性能问题时优先调用获取真实数据",
                    Parameters = Schema("""{"type":"object","properties":{}}""")
                }
            },
            new()
            {
                Function = new AiToolFunctionSpec
                {
                    Name = "get_system_overview",
                    Description = "读取系统概要：操作系统版本、CPU、内存、GPU 列表",
                    Parameters = Schema("""{"type":"object","properties":{}}""")
                }
            },
            new()
            {
                Function = new AiToolFunctionSpec
                {
                    Name = "apply_operations",
                    Description = "应用（启用）一个或多个优化操作。执行前会向用户展示确认卡片，用户批准后才真正写入系统。",
                    Parameters = Schema("""{"type":"object","properties":{"operation_ids":{"type":"array","items":{"type":"string"}}},"required":["operation_ids"]}""")
                }
            },
            new()
            {
                Function = new AiToolFunctionSpec
                {
                    Name = "restore_operations",
                    Description = "恢复（撤销）一个或多个此前应用的优化操作。执行前会向用户展示确认卡片，用户批准后才真正执行。",
                    Parameters = Schema("""{"type":"object","properties":{"operation_ids":{"type":"array","items":{"type":"string"}}},"required":["operation_ids"]}""")
                }
            }
        };
    }

    /// <summary>
    /// 执行一个工具调用，返回给 AI 的 tool 消息内容。
    /// 写工具会阻塞等待用户确认决定。
    /// </summary>
    public async Task<string> ExecuteAsync(AiToolCall call, CancellationToken cancellationToken)
    {
        try
        {
            return call.Function.Name switch
            {
                "list_modules" => JsonSerializer.Serialize(
                    _gateway.ListModules().Select(m => new
                    {
                        key = m.Key, title = m.Title, applied = m.AppliedCount, total = m.TotalCount
                    }), Pretty),

                "list_operations" => JsonSerializer.Serialize(
                    _gateway.ListOperations(ReadModuleKey(call)).Select(o => new
                    {
                        id = o.OperationId, title = o.Title, module = o.ModuleKey,
                        risk = o.RiskLevel, applied = o.IsApplied,
                        can_apply = o.CanApply, can_restore = o.CanRestore
                    }), Pretty),

                "get_operation_status" => GetOperationStatus(call),

                "get_system_overview" => _gateway.GetSystemOverview(),

                "collect_diagnostics" => _diagnosticsRunner is null
                    ? "诊断采集器不可用"
                    : await _diagnosticsRunner.CollectAsync(cancellationToken).ConfigureAwait(false),

                "apply_operations" => await ExecuteWriteAsync(call, isRestore: false, cancellationToken)
                    .ConfigureAwait(false),

                "restore_operations" => await ExecuteWriteAsync(call, isRestore: true, cancellationToken)
                    .ConfigureAwait(false),

                var unknown => $"未知工具：{unknown}"
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return $"工具执行失败：{ex.GetType().Name}: {ex.Message}";
        }
    }

    private static string? ReadModuleKey(AiToolCall call)
    {
        using var doc = call.Function.ParseArguments();
        if (doc is null)
            return null;
        return doc.RootElement.TryGetProperty("module_key", out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;
    }

    private string GetOperationStatus(AiToolCall call)
    {
        using var doc = call.Function.ParseArguments();
        var operationId = doc?.RootElement.TryGetProperty("operation_id", out var el) == true
            ? el.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(operationId))
            return "缺少参数 operation_id";

        var detail = _gateway.GetOperationDetail(operationId);
        if (detail is null)
            return $"未找到操作 {operationId}";

        return JsonSerializer.Serialize(new
        {
            id = detail.Summary.OperationId,
            title = detail.Summary.Title,
            module = detail.Summary.ModuleKey,
            risk = detail.Summary.RiskLevel,
            applied = detail.Summary.IsApplied,
            can_apply = detail.Summary.CanApply,
            can_restore = detail.Summary.CanRestore,
            description = detail.Description,
            current_value = detail.CurrentValue,
            suggested_value = detail.SuggestedValue,
            impact_warning = detail.ImpactWarning
        }, Pretty);
    }

    private async Task<string> ExecuteWriteAsync(AiToolCall call, bool isRestore, CancellationToken cancellationToken)
    {
        using var doc = call.Function.ParseArguments();
        var ids = ReadOperationIds(doc);
        if (ids.Count == 0)
            return "缺少参数 operation_ids 或为空数组";

        var action = new AiPendingWriteAction(isRestore ? "restore" : "apply", ids);
        var approved = await _requestUserConfirmation(action, cancellationToken).ConfigureAwait(false);
        if (!approved)
            return isRestore ? "用户拒绝了恢复操作，未做任何更改。" : "用户拒绝了应用操作，未做任何更改。";

        var result = isRestore
            ? await _gateway.RestoreAsync(ids, cancellationToken).ConfigureAwait(false)
            : await _gateway.ApplyAsync(ids, cancellationToken).ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            succeeded = result.Succeeded,
            applied_count = isRestore ? 0 : result.SucceededCount,
            restored_count = isRestore ? result.SucceededCount : 0,
            failed_count = result.FailedCount,
            failed_ids = result.FailedOperationIds,
            message = result.Message
        }, Pretty);
    }

    private static List<string> ReadOperationIds(JsonDocument? doc)
    {
        var ids = new List<string>();
        if (doc is null
            || !doc.RootElement.TryGetProperty("operation_ids", out var el)
            || el.ValueKind != JsonValueKind.Array)
        {
            return ids;
        }

        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var id = item.GetString();
                if (!string.IsNullOrWhiteSpace(id))
                    ids.Add(id.Trim());
            }
        }
        return ids;
    }
}

/// <summary>待用户确认的写动作。</summary>
public sealed record AiPendingWriteAction(
    string ActionKind,            // "apply" | "restore"
    IReadOnlyList<string> OperationIds);
