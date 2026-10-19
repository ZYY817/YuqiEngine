// File: GameOptimizer.Shell/Services/AiAssistant/AiConversationRunner.cs
using System.Text.Json;

namespace GameOptimizer.Shell.Services.AiAssistant;

/// <summary>
/// 对话编排：发送 → 若有 tool_calls 则执行工具回填 → 再发送，直到拿到文本回复
/// 或达到轮次上限。纯逻辑无 UI 依赖，配合假客户端可全链路测试。
/// </summary>
public sealed class AiConversationRunner
{
    /// <summary>防御性上限：模型异常反复要求工具调用时兜底。</summary>
    private const int MaxToolRounds = 8;

    private readonly IAiChatClient _client;
    private readonly AiToolExecutor _toolExecutor;

    /// <summary>每个工具执行完成（或待确认）时通知 UI 刷新消息流。</summary>
    public event Action<AiChatMessage>? ToolMessageProduced;

    public AiConversationRunner(IAiChatClient client, AiToolExecutor toolExecutor)
    {
        _client = client;
        _toolExecutor = toolExecutor;
    }

    /// <summary>
    /// 运行一轮用户消息直到助手文本回复。messages 为可变历史列表，
    /// 方法内部向其追加 assistant/tool 消息。
    /// </summary>
    public async Task<string> RunAsync(
        List<AiChatMessage> messages,
        CancellationToken cancellationToken)
    {
        var tools = _toolExecutor.GetToolDefinitions();

        for (var round = 0; round < MaxToolRounds; round++)
        {
            var reply = await _client.SendAsync(messages, tools, cancellationToken).ConfigureAwait(false);

            if (!reply.HasToolCalls)
            {
                var text = string.IsNullOrWhiteSpace(reply.Text)
                    ? "（模型未返回内容）"
                    : reply.Text!;
                messages.Add(new AiChatMessage { Role = "assistant", Content = text });
                return text;
            }

            // Why: 把带 tool_calls 的 assistant 消息按协议原样回填，否则多数服务端拒绝后续请求。
            messages.Add(new AiChatMessage
            {
                Role = "assistant",
                Content = reply.Text,
                ToolCalls = reply.ToolCalls.ToList()
            });

            foreach (var call in reply.ToolCalls)
            {
                var toolResult = await _toolExecutor.ExecuteAsync(call, cancellationToken).ConfigureAwait(false);
                var toolMessage = AiChatMessage.Tool(call.Id, call.Function.Name, toolResult);
                messages.Add(toolMessage);
                ToolMessageProduced?.Invoke(toolMessage);
            }
        }

        const string exhausted = "（已达工具调用轮次上限，未能得到最终回复；请把请求拆小后重试）";
        messages.Add(new AiChatMessage { Role = "assistant", Content = exhausted });
        return exhausted;
    }
}
