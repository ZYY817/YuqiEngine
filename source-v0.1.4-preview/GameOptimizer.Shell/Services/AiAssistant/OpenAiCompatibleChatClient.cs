// File: GameOptimizer.Shell/Services/AiAssistant/OpenAiCompatibleChatClient.cs
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace GameOptimizer.Shell.Services.AiAssistant;

/// <summary>
/// 对话客户端抽象：AI 供应商协议与业务逻辑解耦，测试可注入假实现。
/// </summary>
public interface IAiChatClient
{
    /// <summary>
    /// 发送一轮对话；返回的助手消息可能带 tool_calls，由调用方执行后以
    /// role=tool 消息回填并再次调用。
    /// </summary>
    Task<AiAssistantReply> SendAsync(
        IReadOnlyList<AiChatMessage> messages,
        IReadOnlyList<AiToolDefinition> tools,
        CancellationToken cancellationToken);
}

/// <summary>
/// OpenAI 兼容协议客户端（chat/completions + function calling）。
/// 覆盖 OpenAI/Azure/DeepSeek/智谱/通义/Kimi/Ollama/LM Studio 等所有
/// 实现该协议的服务商；无第三方依赖。
/// </summary>
public sealed class OpenAiCompatibleChatClient : IAiChatClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly string _endpoint;
    private readonly string _apiKey;
    private readonly string _model;

    public OpenAiCompatibleChatClient(HttpClient httpClient, string endpoint, string apiKey, string model)
    {
        _httpClient = httpClient;
        _endpoint = NormalizeEndpoint(endpoint);
        _apiKey = apiKey;
        _model = model;
    }

    /// <summary>
    /// 发送一轮对话；返回的助手消息可能带 tool_calls，由调用方执行后以
    /// role=tool 消息回填并再次调用。
    /// </summary>
    public async Task<AiAssistantReply> SendAsync(
        IReadOnlyList<AiChatMessage> messages,
        IReadOnlyList<AiToolDefinition> tools,
        CancellationToken cancellationToken)
    {
        var requestBody = new JsonObject
        {
            ["model"] = _model,
            ["messages"] = JsonSerializer.SerializeToNode(messages, JsonOptions),
            ["temperature"] = 0.3
        };
        if (tools.Count > 0)
        {
            requestBody["tools"] = JsonSerializer.SerializeToNode(tools, JsonOptions);
            requestBody["tool_choice"] = "auto";
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrEmpty(_apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new AiChatException(
                $"服务返回 {(int)response.StatusCode} {response.ReasonPhrase}",
                TryExtractApiError(body));
        }

        return ParseReply(body);
    }

    private static AiAssistantReply ParseReply(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var choices = doc.RootElement.GetProperty("choices");
        if (choices.GetArrayLength() == 0)
            return new AiAssistantReply(null, Array.Empty<AiToolCall>());

        var message = choices[0].GetProperty("message");
        string? text = message.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String
            ? contentEl.GetString()
            : null;

        var toolCalls = new List<AiToolCall>();
        if (message.TryGetProperty("tool_calls", out var toolCallsEl) && toolCallsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var call in toolCallsEl.EnumerateArray())
            {
                var function = call.GetProperty("function");
                var argumentsEl = function.TryGetProperty("arguments", out var argsEl)
                    ? argsEl.Clone()
                    : JsonDocument.Parse("{}").RootElement.Clone();
                toolCalls.Add(new AiToolCall
                {
                    Id = call.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? Guid.NewGuid().ToString("N") : Guid.NewGuid().ToString("N"),
                    Function = new AiToolCallFunction
                    {
                        Name = function.GetProperty("name").GetString() ?? string.Empty,
                        Arguments = argumentsEl
                    }
                });
            }
        }

        return new AiAssistantReply(text, toolCalls);
    }

    private static string? TryExtractApiError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var errorEl)
                && errorEl.TryGetProperty("message", out var messageEl))
            {
                return messageEl.GetString();
            }
        }
        catch (JsonException) { /* body 非 JSON 时忽略 */ }
        return null;
    }

    /// <summary>
    /// 拉取服务商的模型列表（OpenAI 兼容 GET /models）。
    /// 供设置弹窗"拉取模型"按钮使用 —— 自定义/中转站服务商没有预设模型表，
    /// 需要从接口读取用户可用的真实模型名。
    /// </summary>
    public static async Task<IReadOnlyList<string>> FetchModelIdsAsync(
        HttpClient httpClient, string endpoint, string apiKey, CancellationToken cancellationToken)
    {
        var modelsUrl = NormalizeEndpoint(endpoint);
        const string suffix = "/chat/completions";
        modelsUrl = modelsUrl[..^suffix.Length] + "/models";

        using var request = new HttpRequestMessage(HttpMethod.Get, modelsUrl);
        if (!string.IsNullOrEmpty(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new AiChatException(
                $"服务返回 {(int)response.StatusCode} {response.ReasonPhrase}",
                TryExtractApiError(body));
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("data", out var dataEl) || dataEl.ValueKind != JsonValueKind.Array)
        {
            throw new AiChatException("接口返回了模型列表，但格式不是 OpenAI 的 {data:[{id:…}]} 结构");
        }

        return dataEl.EnumerateArray()
            .Select(item => item.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                ? idEl.GetString() : null)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .ToList();
    }

    /// <summary>
    /// 允许用户填 base URL（/v1 结尾）或完整 completions 路径，统一规范化。
    /// </summary>
    internal static string NormalizeEndpoint(string endpoint)
    {
        var trimmed = endpoint.Trim().TrimEnd('/');
        return trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : trimmed + "/chat/completions";
    }
}

public sealed class AiChatException : Exception
{
    public AiChatException(string message, string? apiError = null)
        : base(string.IsNullOrWhiteSpace(apiError) ? message : $"{message}：{apiError}")
    {
        ApiError = apiError;
    }

    public string? ApiError { get; }
}
