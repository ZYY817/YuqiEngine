// File: GameOptimizer.Shell/Services/AiAssistant/AiChatModels.cs
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameOptimizer.Shell.Services.AiAssistant;

/// <summary>OpenAI 兼容协议的消息。</summary>
public sealed class AiChatMessage
{
    [JsonPropertyName("role")] public string Role { get; set; } = "user";
    [JsonPropertyName("content")] public string? Content { get; set; }
    [JsonPropertyName("tool_calls")] public List<AiToolCall>? ToolCalls { get; set; }
    [JsonPropertyName("tool_call_id")] public string? ToolCallId { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }

    public static AiChatMessage User(string content) => new() { Role = "user", Content = content };
    public static AiChatMessage System(string content) => new() { Role = "system", Content = content };
    public static AiChatMessage Tool(string toolCallId, string name, string content) =>
        new() { Role = "tool", ToolCallId = toolCallId, Name = name, Content = content };
}

public sealed class AiToolCall
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("type")] public string Type { get; set; } = "function";
    [JsonPropertyName("function")] public AiToolCallFunction Function { get; set; } = new();
}

public sealed class AiToolCallFunction
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    // Why: 兼容实现可能返回 JSON 字符串或已解析对象，统一保留原文由调用方再解析。
    [JsonPropertyName("arguments")] public JsonElement Arguments { get; set; }

    public JsonDocument? ParseArguments()
    {
        try
        {
            return Arguments.ValueKind == JsonValueKind.String
                ? JsonDocument.Parse(Arguments.GetString() ?? "{}")
                : JsonDocument.Parse(Arguments.GetRawText());
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed class AiToolDefinition
{
    [JsonPropertyName("type")] public string Type { get; set; } = "function";
    [JsonPropertyName("function")] public AiToolFunctionSpec Function { get; set; } = new();
}

public sealed class AiToolFunctionSpec
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
    [JsonPropertyName("parameters")] public JsonElement Parameters { get; set; }
}

/// <summary>助手回复：纯文本或带工具调用。</summary>
public sealed record AiAssistantReply(string? Text, IReadOnlyList<AiToolCall> ToolCalls)
{
    public bool HasToolCalls => ToolCalls.Count > 0;
}
