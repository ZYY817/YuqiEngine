// File: GameOptimizer.Shell/Services/AiAssistant/AiConversationStore.cs
using System.IO;
using System.Text.Json;
using GameOptimizer.Core.Constants;

namespace GameOptimizer.Shell.Services.AiAssistant;

/// <summary>会话列表条目（历史下拉用）。</summary>
public sealed record AiConversationSummary(string Id, string Title, DateTimeOffset UpdatedAt);

/// <summary>完整会话：协议历史（含 system/tool 消息）。</summary>
public sealed class AiConversation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "新对话";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public List<AiChatMessage> Messages { get; set; } = new();
}

/// <summary>
/// 会话持久化：Config\Ai\history\{id}.json，最多保留 MaxSessions 个最新会话。
/// </summary>
public sealed class AiConversationStore
{
    private const int MaxSessions = 50;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _dir;

    /// <param name="directory">会话目录；生产为 Config\Ai\history，测试可传临时目录。</param>
    public AiConversationStore(string? directory = null)
    {
        _dir = directory ?? Path.Combine(AppPaths.ConfigDir, "Ai", "history");
    }

    public string DirectoryPath => _dir;

    public IReadOnlyList<AiConversationSummary> List()
    {
        if (!Directory.Exists(_dir))
            return Array.Empty<AiConversationSummary>();

        return Directory.EnumerateFiles(_dir, "*.json")
            .Select(TryReadSummary)
            .Where(s => s is not null)
            .Cast<AiConversationSummary>()
            .OrderByDescending(s => s.UpdatedAt)
            .ToArray();
    }

    public AiConversation? Load(string id) => TryRead(PathFor(id));

    public void Save(AiConversation conversation)
    {
        Directory.CreateDirectory(_dir);
        conversation.UpdatedAt = DateTimeOffset.Now;
        File.WriteAllText(PathFor(conversation.Id), JsonSerializer.Serialize(conversation, Json));
        TrimToLimit();
    }

    public void Delete(string id)
    {
        var path = PathFor(id);
        if (File.Exists(path))
            File.Delete(path);
    }

    public void Clear()
    {
        if (!Directory.Exists(_dir))
            return;
        foreach (var file in Directory.EnumerateFiles(_dir, "*.json"))
            File.Delete(file);
    }

    private void TrimToLimit()
    {
        var overflow = Directory.EnumerateFiles(_dir, "*.json")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Skip(MaxSessions);
        foreach (var file in overflow)
            file.Delete();
    }

    private string PathFor(string id) =>
        Path.Combine(_dir, $"{Sanitize(id)}.json");

    private static string Sanitize(string id) =>
        string.Concat(id.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_'));

    private AiConversationSummary? TryReadSummary(string path)
    {
        var conversation = TryRead(path);
        return conversation is null ? null : new AiConversationSummary(conversation.Id, conversation.Title, conversation.UpdatedAt);
    }

    private static AiConversation? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            return JsonSerializer.Deserialize<AiConversation>(File.ReadAllText(path), Json);
        }
        catch (Exception)
        {
            return null; // 损坏文件不阻断列表
        }
    }
}
