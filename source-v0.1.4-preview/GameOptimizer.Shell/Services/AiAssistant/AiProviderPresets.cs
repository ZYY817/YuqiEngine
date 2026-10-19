// File: GameOptimizer.Shell/Services/AiAssistant/AiProviderPresets.cs
namespace GameOptimizer.Shell.Services.AiAssistant;

/// <summary>预设服务商下可选模型（按能力从高到低排，第一项为默认）。</summary>
public sealed record AiModelOption(string Id, string Tier);

/// <summary>
/// OpenAI 兼容协议服务商预设。用户始终可选择"自定义"手工填写地址与模型。
/// </summary>
public sealed record AiProviderPreset(
    string Key,
    string DisplayName,
    string Endpoint,
    string DefaultModel,
    IReadOnlyList<AiModelOption> Models);

public static class AiProviderPresets
{
    public static readonly IReadOnlyList<AiProviderPreset> All = new[]
    {
        new AiProviderPreset("zhipu-flash", "智谱 GLM-4-Flash（免费）", "https://open.bigmodel.cn/api/paas/v4", "glm-4-flash",
            new[]
            {
                new AiModelOption("glm-4-flash", "免费 · 日常够用"),
                new AiModelOption("glm-4.5-air", "更强 · 低价"),
                new AiModelOption("glm-4.6", "旗舰 · 最强诊断"),
            }),
        new AiProviderPreset("siliconflow", "硅基流动（免费小模型）", "https://api.siliconflow.cn/v1", "Qwen/Qwen2.5-7B-Instruct",
            new[]
            {
                new AiModelOption("Qwen/Qwen2.5-7B-Instruct", "免费"),
                new AiModelOption("Qwen/Qwen2.5-32B-Instruct", "更强"),
                new AiModelOption("deepseek-ai/DeepSeek-V3", "旗舰"),
            }),
        new AiProviderPreset("groq", "Groq（免费额度）", "https://api.groq.com/openai/v1", "llama-3.3-70b-versatile",
            new[]
            {
                new AiModelOption("llama-3.3-70b-versatile", "免费额度 · 快"),
                new AiModelOption("openai/gpt-oss-120b", "旗舰"),
            }),
        new AiProviderPreset("github-models", "GitHub Models（免费）", "https://models.github.ai/inference", "openai/gpt-4o-mini",
            new[]
            {
                new AiModelOption("openai/gpt-4o-mini", "免费 · 轻量"),
                new AiModelOption("openai/gpt-4o", "更强"),
            }),
        new AiProviderPreset("gemini", "Google Gemini（免费额度）", "https://generativelanguage.googleapis.com/v1beta/openai", "gemini-2.0-flash",
            new[]
            {
                new AiModelOption("gemini-2.0-flash", "免费额度 · 快"),
                new AiModelOption("gemini-2.5-pro", "旗舰 · 最强推理"),
            }),
        new AiProviderPreset("deepseek", "DeepSeek", "https://api.deepseek.com/v1", "deepseek-chat",
            new[]
            {
                new AiModelOption("deepseek-chat", "标准"),
                new AiModelOption("deepseek-reasoner", "旗舰 · 深度推理"),
            }),
        new AiProviderPreset("zhipu", "智谱 GLM（Z.ai）", "https://open.bigmodel.cn/api/paas/v4", "glm-4.6",
            new[]
            {
                new AiModelOption("glm-4.6", "旗舰"),
                new AiModelOption("glm-4.5-air", "均衡"),
                new AiModelOption("glm-4-flash", "免费"),
            }),
        new AiProviderPreset("qwen", "通义千问（阿里云百炼）", "https://dashscope.aliyuncs.com/compatible-mode/v1", "qwen-plus",
            new[]
            {
                new AiModelOption("qwen-plus", "均衡"),
                new AiModelOption("qwen-max", "旗舰"),
                new AiModelOption("qwen-turbo", "轻量 · 低价"),
            }),
        new AiProviderPreset("kimi", "Kimi（月之暗面）", "https://api.moonshot.cn/v1", "moonshot-v1-8k",
            new[]
            {
                new AiModelOption("moonshot-v1-8k", "标准"),
                new AiModelOption("moonshot-v1-32k", "长上下文"),
                new AiModelOption("kimi-k2-0711-preview", "旗舰"),
            }),
        new AiProviderPreset("openai", "OpenAI", "https://api.openai.com/v1", "gpt-4o-mini",
            new[]
            {
                new AiModelOption("gpt-4o-mini", "轻量 · 低价"),
                new AiModelOption("gpt-4o", "均衡"),
                new AiModelOption("gpt-5", "旗舰"),
            }),
        new AiProviderPreset("azure", "Azure OpenAI", "", "", Array.Empty<AiModelOption>()),
        new AiProviderPreset("ollama", "Ollama（本地）", "http://localhost:11434/v1", "qwen2.5",
            new[]
            {
                new AiModelOption("qwen2.5", "本地 · 免费"),
                new AiModelOption("qwen2.5:14b", "本地 · 更强"),
                new AiModelOption("llama3.1:8b", "本地 · 轻量"),
            }),
        new AiProviderPreset("lmstudio", "LM Studio（本地）", "http://localhost:1234/v1", "local-model", Array.Empty<AiModelOption>()),
        new AiProviderPreset("custom", "自定义 / 中转站", "", "", Array.Empty<AiModelOption>())
    };

    public static AiProviderPreset? Find(string? key) =>
        All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
}
