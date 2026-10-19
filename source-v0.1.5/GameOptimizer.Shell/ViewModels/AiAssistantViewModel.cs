// File: GameOptimizer.Shell/ViewModels/AiAssistantViewModel.cs
using System.Collections.ObjectModel;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameOptimizer.Shell.Models;
using GameOptimizer.Shell.Services;
using GameOptimizer.Shell.Services.AiAssistant;

namespace GameOptimizer.Shell.ViewModels;

/// <summary>聊天区的一条展示消息。</summary>
public sealed record AiChatEntry(string Role, string Text, DateTimeOffset Timestamp)
{
    public bool IsUser => Role == "user";
    public bool IsAssistant => Role == "assistant";
    public bool IsTool => Role == "tool";
    public bool IsSystem => Role == "system";
}

/// <summary>写操作确认卡里的单行条目。</summary>
public sealed record AiPendingItem(string OperationId, string Title, string RiskLevel, string ModuleKey);

/// <summary>
/// AI 助手页视图模型：连接配置、对话收发、写操作确认门、
/// 提示词/Skill 文件编辑、会话历史、交互节奏。确认流独立于具体 UI。
/// </summary>
public sealed partial class AiAssistantViewModel : ObservableObject
{
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(60) };

    private readonly IAppSettingsStore _settingsStore;
    private readonly IAiOptimizationGateway _gateway;
    private readonly IAiDiagnosticsRunner _diagnosticsRunner;
    private readonly AiSkillStore _skillStore;
    private readonly AiConversationStore _conversationStore;
    private readonly Func<AiConnectionConfig, IAiChatClient> _clientFactory;
    private readonly List<AiChatMessage> _protocolHistory = new();
    private TaskCompletionSource<bool>? _pendingConfirmation;
    private string _sessionId = Guid.NewGuid().ToString("N");
    private string _sessionTitle = "新对话";

    public AiAssistantViewModel(
        IAppSettingsStore settingsStore,
        IAiOptimizationGateway gateway,
        AiSkillStore? skillStore = null,
        IAiDiagnosticsRunner? diagnosticsRunner = null,
        AiConversationStore? conversationStore = null,
        Func<AiConnectionConfig, IAiChatClient>? clientFactory = null)
    {
        _settingsStore = settingsStore;
        _gateway = gateway;
        _skillStore = skillStore ?? new AiSkillStore();
        _diagnosticsRunner = diagnosticsRunner ?? new PowerShellDiagnosticsRunner(_skillStore);
        _conversationStore = conversationStore ?? new AiConversationStore();
        _clientFactory = clientFactory ?? DefaultClientFactory;

        LoadConfig();
        SeedSystemPrompt();
        RefreshSessions();
    }

    /// <summary>页面请求打开 AI 设置弹窗。</summary>
    public event Action? RequestOpenSettings;
    /// <summary>请求跳转到某个模块页面（路由 key 或 module key）。</summary>
    public event Action<string>? RequestNavigateToModule;

    // ---------- 配置 ----------
    public IReadOnlyList<AiProviderPreset> Providers => AiProviderPresets.All;

    [ObservableProperty] private AiProviderPreset? _selectedProvider;
    [ObservableProperty] private string _endpoint = string.Empty;
    [ObservableProperty] private string _model = string.Empty;
    [ObservableProperty] private string _apiKey = string.Empty; // 内存中明文，保存时 DPAPI 加密
    [ObservableProperty] private bool _isConfigured;
    [ObservableProperty] private string _configStatus = string.Empty;

    /// <summary>当前服务商的推荐模型列表；若已从接口拉取过模型，优先显示真实可用列表。</summary>
    public IReadOnlyList<AiModelOption> ModelOptions =>
        _fetchedModels ?? SelectedProvider?.Models ?? (IReadOnlyList<AiModelOption>)Array.Empty<AiModelOption>();

    /// <summary>从接口拉取的模型（key = 服务商+接口地址，换配置自动失效）。</summary>
    private List<AiModelOption>? _fetchedModels;
    private string _fetchedModelsKey = string.Empty;

    [ObservableProperty] private bool _isFetchingModels;
    [ObservableProperty] private string _modelFetchStatus = string.Empty;

    /// <summary>从接口地址拉取真实模型列表（GET /models），适配自定义/中转站服务商。</summary>
    [RelayCommand]
    private async Task FetchModelsAsync()
    {
        if (IsFetchingModels)
            return;
        var endpoint = Endpoint.Trim();
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            ModelFetchStatus = "先填接口地址再拉取。";
            return;
        }
        IsFetchingModels = true;
        ModelFetchStatus = "正在拉取模型列表…";
        try
        {
            var ids = await OpenAiCompatibleChatClient.FetchModelIdsAsync(
                SharedHttp, endpoint, ApiKey, CancellationToken.None);
            _fetchedModelsKey = endpoint;
            _fetchedModels = ids.Select(id => new AiModelOption(id, "接口返回")).ToList();
            OnPropertyChanged(nameof(ModelOptions));
            ModelFetchStatus = ids.Count > 0
                ? $"已拉取 {ids.Count} 个模型，点下拉框选择。"
                : "接口通了但没有返回模型，请手动输入模型名。";
        }
        catch (AiChatException ex)
        {
            ModelFetchStatus = FriendlyError(ex);
        }
        catch (Exception ex)
        {
            ModelFetchStatus = $"拉取失败：{ex.Message}（可手动输入模型名）";
        }
        finally
        {
            IsFetchingModels = false;
        }
    }

    /// <summary>交互节奏：true=问诊分轮，false=高效全量。</summary>
    [ObservableProperty] private bool _isGuidedMode = true;

    /// <summary>是否注入角色提示词（人设/问诊铁律）。</summary>
    [ObservableProperty] private bool _useSystemPrompt = true;

    /// <summary>是否注入 Skill 优化知识库。</summary>
    [ObservableProperty] private bool _useSkill = true;

    // ---------- 聊天 ----------
    public ObservableCollection<AiChatEntry> ChatEntries { get; } = new();
    [ObservableProperty] private string _inputText = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public bool HasMessages => ChatEntries.Count > 0;
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanSend));
    public bool CanSend => !IsBusy && IsConfigured;

    /// <summary>建议气泡（空状态快捷提问）。</summary>
    public IReadOnlyList<string> Suggestions { get; } = new[]
    {
        "帮我看看这台电脑的整体状态",
        "玩游戏会一顿一顿的，帮我排查",
        "电脑最近开机很慢，日常也卡",
        "列出还没应用的优化项，给我建议"
    };

    // ---------- 会话历史 ----------
    public ObservableCollection<AiConversationSummary> Sessions { get; } = new();
    [ObservableProperty] private AiConversationSummary? _selectedSession;

    private void RefreshSessions()
    {
        Sessions.Clear();
        foreach (var s in _conversationStore.List())
            Sessions.Add(s);
    }

    [RelayCommand]
    private void NewChat()
    {
        SaveCurrentSession();
        _sessionId = Guid.NewGuid().ToString("N");
        _sessionTitle = "新对话";
        ChatEntries.Clear();
        OnPropertyChanged(nameof(HasMessages));
        _protocolHistory.Clear();
        SeedSystemPrompt();
        SelectedSession = null;
    }

    partial void OnSelectedSessionChanged(AiConversationSummary? value)
    {
        if (value is null || value.Id == _sessionId)
            return;
        var conversation = _conversationStore.Load(value.Id);
        if (conversation is null)
            return;
        SaveCurrentSession();
        _sessionId = conversation.Id;
        _sessionTitle = conversation.Title;
        _protocolHistory.Clear();
        _protocolHistory.AddRange(conversation.Messages);
        ChatEntries.Clear();
        foreach (var m in conversation.Messages.Where(m => m.Role != "system"))
        {
            var text = m.Role == "tool" ? $"[工具 {m.Name}] {m.Content}" : m.Content ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(text))
                ChatEntries.Add(new AiChatEntry(m.Role, text, conversation.UpdatedAt));
        }
        OnPropertyChanged(nameof(HasMessages));
    }

    [RelayCommand]
    private void DeleteSession(AiConversationSummary? summary)
    {
        if (summary is null)
            return;
        _conversationStore.Delete(summary.Id);
        RefreshSessions();
        if (summary.Id == _sessionId)
            NewChat();
    }

    private void SaveCurrentSession()
    {
        var userMessages = _protocolHistory.Where(m => m.Role == "user").ToList();
        if (userMessages.Count == 0)
            return; // 空会话不落盘
        if (_sessionTitle == "新对话")
        {
            var first = userMessages[0].Content ?? string.Empty;
            _sessionTitle = first.Length > 24 ? first[..24] + "…" : first;
        }
        _conversationStore.Save(new AiConversation
        {
            Id = _sessionId,
            Title = _sessionTitle,
            Messages = _protocolHistory.ToList()
        });
    }

    // ---------- Skill / 提示词编辑（供 AI 设置弹窗使用） ----------
    public IReadOnlyList<string> SkillFiles => AiSkillStore.EditableFiles;
    [ObservableProperty] private string _selectedSkillFile = "system-prompt.md";
    [ObservableProperty] private string _skillEditorText = string.Empty;
    [ObservableProperty] private string _skillEditorStatus = string.Empty;

    partial void OnSelectedSkillFileChanged(string value) => LoadSkillFile();

    public void LoadSkillFile()
    {
        SkillEditorText = _skillStore.Read(SelectedSkillFile);
        SkillEditorStatus = string.Empty;
    }

    [RelayCommand]
    private void SaveSkillFile()
    {
        _skillStore.Save(SelectedSkillFile, SkillEditorText);
        SeedSystemPrompt(refresh: true);
        SkillEditorStatus = "已保存，下一轮对话生效。";
    }

    [RelayCommand]
    private void ResetSkillFile()
    {
        _skillStore.ResetToDefault(SelectedSkillFile);
        LoadSkillFile();
        SeedSystemPrompt(refresh: true);
        SkillEditorStatus = "已恢复默认。";
    }

    [RelayCommand]
    private void OpenSkillFolder()
    {
        _skillStore.EnsureSeeded();
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = _skillStore.UserDirectory,
            UseShellExecute = true
        });
    }

    // ---------- 待确认写操作卡片 ----------
    [ObservableProperty] private AiPendingWriteAction? _pendingAction;
    [ObservableProperty] private string _pendingActionDescription = string.Empty;
    public ObservableCollection<AiPendingItem> PendingItems { get; } = new();
    public bool HasPendingAction => PendingAction is not null;

    partial void OnPendingActionChanged(AiPendingWriteAction? value) =>
        OnPropertyChanged(nameof(HasPendingAction));

    /// <summary>点击确认卡条目跳转对应模块页面。</summary>
    [RelayCommand]
    private void JumpToModule(AiPendingItem? item)
    {
        if (item is not null)
            RequestNavigateToModule?.Invoke(item.ModuleKey);
    }

    [RelayCommand]
    private void OpenSettings() => RequestOpenSettings?.Invoke();

    private bool _loadingConfig;

    private void LoadConfig()
    {
        _loadingConfig = true;
        try
        {
            var settings = _settingsStore.Current;
            var preset = AiProviderPresets.Find(settings.AiProviderKey) ?? AiProviderPresets.All[^1];
        SelectedProvider = preset;
        Endpoint = string.IsNullOrWhiteSpace(settings.AiEndpoint) ? preset.Endpoint : settings.AiEndpoint;
        Model = string.IsNullOrWhiteSpace(settings.AiModel) ? preset.DefaultModel : settings.AiModel;
        ApiKey = AiCredentialProtector.Unprotect(settings.AiApiKeyProtected);
        IsGuidedMode = AiSystemPromptComposer.ParseMode(settings.AiInteractionMode) == AiInteractionMode.Guided;
            UseSystemPrompt = settings.AiUseSystemPrompt;
            UseSkill = settings.AiUseSkill;
            UpdateConfiguredState();
        }
        finally
        {
            _loadingConfig = false;
        }
    }

    partial void OnSelectedProviderChanged(AiProviderPreset? value)
    {
        if (value is not null && !_loadingConfig)
        {
            // 用户主动切换服务商：预设填默认值；自定义/中转站清空残留，
            // 让用户看到干净的空输入框而不是上一个服务商的地址。
            if (value.Key != "custom")
            {
                Endpoint = value.Endpoint;
                Model = value.DefaultModel;
            }
            else
            {
                Endpoint = string.Empty;
                Model = string.Empty;
            }
        }
        _fetchedModels = null;
        _fetchedModelsKey = string.Empty;
        ModelFetchStatus = string.Empty;
        OnPropertyChanged(nameof(ModelOptions));
    }

    partial void OnEndpointChanged(string value)
    {
        // 换了接口地址后，之前拉取的模型列表不再可信。
        if (!string.Equals(value.Trim(), _fetchedModelsKey, StringComparison.OrdinalIgnoreCase)
            && _fetchedModels is not null)
        {
            _fetchedModels = null;
            _fetchedModelsKey = string.Empty;
            OnPropertyChanged(nameof(ModelOptions));
        }
    }

    partial void OnIsGuidedModeChanged(bool value)
    {
        _settingsStore.TryUpdate(s =>
            s.AiInteractionMode = AiSystemPromptComposer.ToSetting(
                value ? AiInteractionMode.Guided : AiInteractionMode.FullBatch));
        SeedSystemPrompt(refresh: true);
    }

    partial void OnUseSystemPromptChanged(bool value)
    {
        if (_loadingConfig) return;
        _settingsStore.TryUpdate(s => s.AiUseSystemPrompt = value);
        SeedSystemPrompt(refresh: true);
    }

    partial void OnUseSkillChanged(bool value)
    {
        if (_loadingConfig) return;
        _settingsStore.TryUpdate(s => s.AiUseSkill = value);
        SeedSystemPrompt(refresh: true);
    }

    [RelayCommand]
    private void SaveConfig()
    {
        _settingsStore.TryUpdate(settings =>
        {
            settings.AiProviderKey = SelectedProvider?.Key ?? "custom";
            settings.AiEndpoint = Endpoint.Trim();
            settings.AiModel = Model.Trim();
            settings.AiApiKeyProtected = AiCredentialProtector.Protect(ApiKey);
        });
        UpdateConfiguredState();
        ConfigStatus = IsConfigured
            ? "配置已保存。"
            : "配置已保存，但尚未完整：需要接口地址和模型名（密钥按服务商要求填写）。";
    }

    private void UpdateConfiguredState()
    {
        IsConfigured = !string.IsNullOrWhiteSpace(Endpoint) && !string.IsNullOrWhiteSpace(Model);
        OnPropertyChanged(nameof(CanSend));
    }

    private AiConnectionConfig CurrentConfig() =>
        new(Endpoint.Trim(), ApiKey, Model.Trim());

    // ---------- 对话 ----------

    private void SeedSystemPrompt(bool refresh = false)
    {
        if (refresh)
        {
            // Why: 模式/提示词变更后保留对话但重建首条 system 消息。
            _protocolHistory.RemoveAll(m => m.Role == "system");
            _protocolHistory.Insert(0, BuildSystemPrompt());
            return;
        }
        _protocolHistory.Add(BuildSystemPrompt());
    }

    private AiChatMessage BuildSystemPrompt() =>
        AiChatMessage.System(AiSystemPromptComposer.Compose(
            _skillStore.ComposeSkillContext(UseSystemPrompt, UseSkill),
            IsGuidedMode ? AiInteractionMode.Guided : AiInteractionMode.FullBatch));

    [RelayCommand]
    private void UseSuggestion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        if (!IsConfigured)
        {
            RequestOpenSettings?.Invoke();
            return;
        }
        InputText = text;
        SendCommand.Execute(null);
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        var input = InputText.Trim();
        if (input.Length == 0 || IsBusy)
            return;
        if (!IsConfigured)
        {
            RequestOpenSettings?.Invoke();
            return;
        }

        InputText = string.Empty;
        ChatEntries.Add(new AiChatEntry("user", input, DateTimeOffset.Now));
        OnPropertyChanged(nameof(HasMessages));
        _protocolHistory.Add(AiChatMessage.User(input));
        IsBusy = true;
        try
        {
            var client = _clientFactory(CurrentConfig());
            var executor = new AiToolExecutor(_gateway, RequestWriteConfirmationAsync, _diagnosticsRunner);
            var runner = new AiConversationRunner(client, executor);
            // Why: 事件在线程池触发，必须经 Dispatcher 才能写 ObservableCollection。
            runner.ToolMessageProduced += msg =>
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    ChatEntries.Add(new AiChatEntry("tool", $"[工具 {msg.Name}] {msg.Content}", DateTimeOffset.Now)));

            var reply = await runner.RunAsync(_protocolHistory, CancellationToken.None);
            ChatEntries.Add(new AiChatEntry("assistant", reply, DateTimeOffset.Now));
            SaveCurrentSession();
            RefreshSessions();
            SelectedSession = Sessions.FirstOrDefault(s => s.Id == _sessionId);
        }
        catch (AiChatException ex)
        {
            ChatEntries.Add(new AiChatEntry("system", FriendlyError(ex), DateTimeOffset.Now));
            if (_protocolHistory.Count > 0 && _protocolHistory[^1].Role == "user")
                _protocolHistory.RemoveAt(_protocolHistory.Count - 1);
        }
        catch (Exception ex)
        {
            ChatEntries.Add(new AiChatEntry("system", $"对话出错：{ex.GetType().Name}: {ex.Message}", DateTimeOffset.Now));
            if (_protocolHistory.Count > 0 && _protocolHistory[^1].Role == "user")
                _protocolHistory.RemoveAt(_protocolHistory.Count - 1);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string FriendlyError(AiChatException ex)
    {
        if (ex.Message.Contains("401") || ex.Message.Contains("Unauthorized"))
            return "认证失败（401）——请检查 API 密钥是否正确、是否过期。点右上角 ⚙ AI 设置修改。";
        if (ex.Message.Contains("404"))
            return "接口地址不对（404）——检查是否填了 base URL 或服务商换了路径。";
        if (ex.Message.Contains("429"))
            return "请求过频/额度用尽（429）——稍后再试或检查服务商额度。";
        return $"请求失败：{ex.Message}";
    }

    /// <summary>工具执行器的确认回调：挂起直到用户点确认/拒绝。</summary>
    private async Task<bool> RequestWriteConfirmationAsync(
        AiPendingWriteAction action, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>();
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            PendingItems.Clear();
            foreach (var id in action.OperationIds)
            {
                var detail = _gateway.GetOperationDetail(id);
                PendingItems.Add(new AiPendingItem(
                    id,
                    detail?.Summary.Title ?? id,
                    detail?.Summary.RiskLevel ?? "?",
                    detail?.Summary.ModuleKey ?? string.Empty));
            }
            PendingAction = action;
            PendingActionDescription =
                $"AI 请求{(action.ActionKind == "restore" ? "恢复" : "应用")}以下操作——批准后才会真正写入系统：";
        });
        _pendingConfirmation = tcs;

        using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        return await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>确认执行待处理的写操作。</summary>
    [RelayCommand]
    private void ApprovePending()
    {
        PendingAction = null;
        _pendingConfirmation?.TrySetResult(true);
        _pendingConfirmation = null;
    }

    /// <summary>拒绝执行待处理的写操作。</summary>
    [RelayCommand]
    private void DeclinePending()
    {
        PendingAction = null;
        _pendingConfirmation?.TrySetResult(false);
        _pendingConfirmation = null;
    }

    private static IAiChatClient DefaultClientFactory(AiConnectionConfig config) =>
        new OpenAiCompatibleChatClient(SharedHttp, config.Endpoint, config.ApiKey, config.Model);
}

/// <summary>AI 连接配置快照（构造客户端用）。</summary>
public sealed record AiConnectionConfig(string Endpoint, string ApiKey, string Model);
