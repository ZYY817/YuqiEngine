using System.Net;
using System.Text;
using System.Text.Json;
using GameOptimizer.Shell.Models;
using GameOptimizer.Shell.Services;
using GameOptimizer.Shell.Services.AiAssistant;
using GameOptimizer.Shell.ViewModels;

namespace GameOptimizer.Tests.Shell;

public class AiAssistantTests
{
    // ---------- 端点规范化 ----------

    [Theory]
    [InlineData("https://api.deepseek.com/v1", "https://api.deepseek.com/v1/chat/completions")]
    [InlineData("https://api.deepseek.com/v1/", "https://api.deepseek.com/v1/chat/completions")]
    [InlineData("https://x.com/v1/chat/completions", "https://x.com/v1/chat/completions")]
    public void NormalizeEndpoint_AppendsCompletionsWhenMissing(string input, string expected) =>
        Assert.Equal(expected, OpenAiCompatibleChatClient.NormalizeEndpoint(input));

    // ---------- 客户端解析（假 HTTP handler 端到端） ----------

    private sealed class FakeHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest;
        public string? LastBody;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return response;
        }
    }

    private static OpenAiCompatibleChatClient MakeClient(string responseJson, out FakeHandler handler, HttpStatusCode code = HttpStatusCode.OK)
    {
        handler = new FakeHandler(new HttpResponseMessage(code)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
        });
        return new OpenAiCompatibleChatClient(new HttpClient(handler), "https://api.test/v1", "key-1", "m");
    }

    [Fact]
    public async Task SendAsync_ParsesTextReply()
    {
        var client = MakeClient("""{"choices":[{"message":{"role":"assistant","content":"你好"}}]}""", out _);
        var reply = await client.SendAsync(new List<AiChatMessage> { AiChatMessage.User("hi") }, Array.Empty<AiToolDefinition>(), CancellationToken.None);
        Assert.Equal("你好", reply.Text);
        Assert.False(reply.HasToolCalls);
    }

    [Fact]
    public async Task SendAsync_ParsesToolCalls()
    {
        var json = """{"choices":[{"message":{"role":"assistant","content":null,"tool_calls":[{"id":"c1","type":"function","function":{"name":"list_modules","arguments":"{}"}}]}}]}""";
        var client = MakeClient(json, out _);
        var reply = await client.SendAsync(new List<AiChatMessage> { AiChatMessage.User("hi") }, Array.Empty<AiToolDefinition>(), CancellationToken.None);
        Assert.True(reply.HasToolCalls);
        Assert.Equal("list_modules", reply.ToolCalls[0].Function.Name);
        Assert.Equal("c1", reply.ToolCalls[0].Id);
    }

    [Fact]
    public async Task SendAsync_SendsBearerAndBody()
    {
        var client = MakeClient("""{"choices":[{"message":{"content":"ok"}}]}""", out var handler);
        await client.SendAsync(new List<AiChatMessage> { AiChatMessage.User("hi") }, Array.Empty<AiToolDefinition>(), CancellationToken.None);
        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("key-1", handler.LastRequest.Headers.Authorization.Parameter);
        Assert.Contains("\"model\":\"m\"", handler.LastBody);
    }

    [Fact]
    public async Task SendAsync_NonSuccess_ThrowsWithApiError()
    {
        var client = MakeClient("""{"error":{"message":"bad key"}}""", out _, HttpStatusCode.Unauthorized);
        var ex = await Assert.ThrowsAsync<AiChatException>(() =>
            client.SendAsync(new List<AiChatMessage>(), Array.Empty<AiToolDefinition>(), CancellationToken.None));
        Assert.Contains("bad key", ex.Message);
    }

    // ---------- 模型列表拉取（GET /models） ----------

    [Fact]
    public async Task FetchModelIds_ParsesDataAndHitsModelsEndpoint()
    {
        var handler = new FakeHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"data":[{"id":"glm-4-flash"},{"id":"glm-4.6"},{"id":42},{"id":null}]}""",
                Encoding.UTF8, "application/json")
        });
        var ids = await OpenAiCompatibleChatClient.FetchModelIdsAsync(
            new HttpClient(handler), "https://relay.example.com/v1", "key-9", CancellationToken.None);

        Assert.Equal(new[] { "glm-4-flash", "glm-4.6" }, ids);
        Assert.Equal("https://relay.example.com/v1/models", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("key-9", handler.LastRequest.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task FetchModelIds_AcceptsFullCompletionsUrl()
    {
        var handler = new FakeHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"data":[{"id":"m1"}]}""", Encoding.UTF8, "application/json")
        });
        var ids = await OpenAiCompatibleChatClient.FetchModelIdsAsync(
            new HttpClient(handler), "https://relay.example.com/v1/chat/completions", "", CancellationToken.None);
        Assert.Equal("https://relay.example.com/v1/models", handler.LastRequest!.RequestUri!.ToString());
        Assert.Single(ids);
    }

    [Fact]
    public async Task FetchModelIds_NonSuccess_Throws()
    {
        var handler = new FakeHandler(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("""{"error":{"message":"invalid key"}}""", Encoding.UTF8, "application/json")
        });
        var ex = await Assert.ThrowsAsync<AiChatException>(() =>
            OpenAiCompatibleChatClient.FetchModelIdsAsync(
                new HttpClient(handler), "https://x.com/v1", "bad", CancellationToken.None));
        Assert.Contains("invalid key", ex.Message);
    }

    // ---------- 工具执行器 ----------

    private sealed class FakeGateway : IAiOptimizationGateway
    {
        public List<string> AppliedIds = new();
        public List<string> RestoredIds = new();

        public IReadOnlyList<AiModuleSummary> ListModules() =>
            new[] { new AiModuleSummary("memory-optimization", "内存优化", 1, 3) };

        public IReadOnlyList<AiOperationSummary> ListOperations(string? moduleKey) =>
            new[] { new AiOperationSummary("mem.a", "压缩内存", "memory-optimization", "Medium", false, true, false) };

        public AiOperationDetail? GetOperationDetail(string operationId) =>
            operationId == "mem.a"
                ? new AiOperationDetail(
                    new AiOperationSummary("mem.a", "压缩内存", "memory-optimization", "Medium", false, true, false),
                    "描述", "0", "1", "警告")
                : null;

        public string GetSystemOverview() => "OS: test";

        public Task<AiWriteResult> ApplyAsync(IReadOnlyList<string> operationIds, CancellationToken cancellationToken)
        {
            AppliedIds.AddRange(operationIds);
            return Task.FromResult(new AiWriteResult(true, operationIds.Count, 0, Array.Empty<string>(), "ok"));
        }

        public Task<AiWriteResult> RestoreAsync(IReadOnlyList<string> operationIds, CancellationToken cancellationToken)
        {
            RestoredIds.AddRange(operationIds);
            return Task.FromResult(new AiWriteResult(true, operationIds.Count, 0, Array.Empty<string>(), "ok"));
        }
    }

    private static AiToolCall MakeCall(string name, string argumentsJson) =>
        new()
        {
            Id = "c1",
            Function = new AiToolCallFunction
            {
                Name = name,
                Arguments = JsonDocument.Parse(argumentsJson).RootElement
            }
        };

    [Fact]
    public async Task Executor_ReadTools_ReturnJson()
    {
        var executor = new AiToolExecutor(new FakeGateway(), (_, _) => Task.FromResult(false));
        var modules = await executor.ExecuteAsync(MakeCall("list_modules", "{}"), CancellationToken.None);
        Assert.Contains("memory-optimization", modules);
        var ops = await executor.ExecuteAsync(MakeCall("list_operations", "{}"), CancellationToken.None);
        Assert.Contains("mem.a", ops);
        var status = await executor.ExecuteAsync(MakeCall("get_operation_status", """{"operation_id":"mem.a"}"""), CancellationToken.None);
        Assert.Contains("压缩内存", status);
    }

    [Fact]
    public async Task Executor_UnknownOperationId_ReportsNotFound()
    {
        var executor = new AiToolExecutor(new FakeGateway(), (_, _) => Task.FromResult(false));
        var result = await executor.ExecuteAsync(MakeCall("get_operation_status", """{"operation_id":"nope"}"""), CancellationToken.None);
        Assert.Contains("未找到", result);
    }

    [Fact]
    public async Task Executor_WriteTool_UserApproves_ExecutesViaGateway()
    {
        var gateway = new FakeGateway();
        AiPendingWriteAction? shown = null;
        var executor = new AiToolExecutor(gateway, (a, _) => { shown = a; return Task.FromResult(true); });
        var result = await executor.ExecuteAsync(MakeCall("apply_operations", """{"operation_ids":["mem.a"]}"""), CancellationToken.None);
        Assert.Equal(new List<string> { "mem.a" }, gateway.AppliedIds);
        Assert.Equal("apply", shown!.ActionKind);
        Assert.Contains("ok", result);
    }

    [Fact]
    public async Task Executor_WriteTool_UserDeclines_NothingExecuted()
    {
        var gateway = new FakeGateway();
        var executor = new AiToolExecutor(gateway, (_, _) => Task.FromResult(false));
        var result = await executor.ExecuteAsync(MakeCall("restore_operations", """{"operation_ids":["mem.a"]}"""), CancellationToken.None);
        Assert.Empty(gateway.RestoredIds);
        Assert.Contains("拒绝", result);
    }

    [Fact]
    public async Task Executor_WriteTool_EmptyIds_ReturnsErrorNotExecuted()
    {
        var gateway = new FakeGateway();
        var executor = new AiToolExecutor(gateway, (_, _) => Task.FromResult(true));
        var result = await executor.ExecuteAsync(MakeCall("apply_operations", """{"operation_ids":[]}"""), CancellationToken.None);
        Assert.Contains("缺少参数", result);
        Assert.Empty(gateway.AppliedIds);
    }

    // ---------- 对话循环 ----------

    private sealed class ScriptedClient : IAiChatClient
    {
        public readonly Queue<AiAssistantReply> Replies = new();
        public int SendCount;
        public Task<AiAssistantReply> SendAsync(IReadOnlyList<AiChatMessage> messages, IReadOnlyList<AiToolDefinition> tools, CancellationToken cancellationToken)
        {
            SendCount++;
            return Task.FromResult(Replies.Dequeue());
        }
    }

    [Fact]
    public async Task Runner_PlainReply_NoToolRound()
    {
        var client = new ScriptedClient();
        client.Replies.Enqueue(new AiAssistantReply("完成", Array.Empty<AiToolCall>()));
        var messages = new List<AiChatMessage> { AiChatMessage.System("s"), AiChatMessage.User("hi") };
        var runner = new AiConversationRunner(client, new AiToolExecutor(new FakeGateway(), (_, _) => Task.FromResult(true)));
        var reply = await runner.RunAsync(messages, CancellationToken.None);
        Assert.Equal("完成", reply);
        Assert.Equal(1, client.SendCount);
        Assert.Equal("assistant", messages[^1].Role);
    }

    [Fact]
    public async Task Runner_ToolCall_ExecutesAndContinues()
    {
        var client = new ScriptedClient();
        client.Replies.Enqueue(new AiAssistantReply(null, new[]
        {
            new AiToolCall { Id = "t1", Function = new AiToolCallFunction { Name = "list_modules", Arguments = JsonDocument.Parse("{}").RootElement } }
        }));
        client.Replies.Enqueue(new AiAssistantReply("共 1 个模块", Array.Empty<AiToolCall>()));

        var gateway = new FakeGateway();
        var messages = new List<AiChatMessage> { AiChatMessage.User("列出模块") };
        var runner = new AiConversationRunner(client, new AiToolExecutor(gateway, (_, _) => Task.FromResult(true)));
        var reply = await runner.RunAsync(messages, CancellationToken.None);

        Assert.Equal("共 1 个模块", reply);
        Assert.Equal(2, client.SendCount);
        Assert.Contains(messages, m => m.Role == "tool" && m.ToolCallId == "t1" && m.Content!.Contains("memory-optimization"));
    }

    [Fact]
    public async Task Runner_WriteTool_RequiresConfirmationBeforeGateway()
    {
        var client = new ScriptedClient();
        client.Replies.Enqueue(new AiAssistantReply(null, new[]
        {
            new AiToolCall { Id = "t1", Function = new AiToolCallFunction { Name = "apply_operations", Arguments = JsonDocument.Parse("""{"operation_ids":["mem.a"]}""").RootElement } }
        }));
        client.Replies.Enqueue(new AiAssistantReply("已应用", Array.Empty<AiToolCall>()));

        var gateway = new FakeGateway();
        var messages = new List<AiChatMessage> { AiChatMessage.User("启用内存压缩") };
        var runner = new AiConversationRunner(client, new AiToolExecutor(gateway, (_, _) => Task.FromResult(true)));
        await runner.RunAsync(messages, CancellationToken.None);
        Assert.Equal(new List<string> { "mem.a" }, gateway.AppliedIds);
    }

    // ---------- 真实 API 冒烟测试（需环境变量，未配置时跳过） ----------
    // 用法: set YQ_AI_TEST_ENDPOINT=https://open.bigmodel.cn/api/paas/v4
    //       set YQ_AI_TEST_KEY=你的key   set YQ_AI_TEST_MODEL=glm-4-flash
    //       dotnet test --filter FullyQualifiedName~RealApi

    [Fact]
    public async Task RealApi_RoundTrip_WhenEnvConfigured()
    {
        var endpoint = Environment.GetEnvironmentVariable("YQ_AI_TEST_ENDPOINT");
        var key = Environment.GetEnvironmentVariable("YQ_AI_TEST_KEY");
        var model = Environment.GetEnvironmentVariable("YQ_AI_TEST_MODEL");
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(model))
            return; // 未配置真实服务时视为通过（跳过）

        var client = new OpenAiCompatibleChatClient(new HttpClient(), endpoint, key, model);
        var reply = await client.SendAsync(
            new List<AiChatMessage> { AiChatMessage.User("回复'ok'两个字") },
            Array.Empty<AiToolDefinition>(), CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(reply.Text));
    }

    [Fact]
    public async Task RealApi_ToolCallRoundTrip_WhenEnvConfigured()
    {
        var endpoint = Environment.GetEnvironmentVariable("YQ_AI_TEST_ENDPOINT");
        var key = Environment.GetEnvironmentVariable("YQ_AI_TEST_KEY");
        var model = Environment.GetEnvironmentVariable("YQ_AI_TEST_MODEL");
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(model))
            return;

        var client = new OpenAiCompatibleChatClient(new HttpClient(), endpoint, key, model);
        var gateway = new FakeGateway();
        var executor = new AiToolExecutor(gateway, (_, _) => Task.FromResult(true));
        var runner = new AiConversationRunner(client, executor);
        var messages = new List<AiChatMessage>
        {
            AiChatMessage.System("你是系统优化助手。使用提供的工具回答。"),
            AiChatMessage.User("调用 list_modules 工具告诉我有哪些优化模块")
        };
        var reply = await runner.RunAsync(messages, CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(reply));
        Assert.Contains(messages, m => m.Role == "tool");
    }

    // ---------- Skill 存储 / 提示词组装 ----------

    [Fact]
    public void SkillStore_SeedsDefaults_AndRoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "yq-skill-" + Guid.NewGuid().ToString("N"));
        var defaults = Path.Combine(dir, "defaults");
        var user = Path.Combine(dir, "user");
        Directory.CreateDirectory(defaults);
        File.WriteAllText(Path.Combine(defaults, "system-prompt.md"), "persona-default");
        File.WriteAllText(Path.Combine(defaults, "SKILL.md"), "skill-default");

        var store = new AiSkillStore(user, defaults);
        store.EnsureSeeded();

        Assert.Equal("persona-default", store.Read("system-prompt.md"));
        Assert.Equal("skill-default", store.Read("SKILL.md"));
        Assert.Contains("persona-default", store.ComposeSkillContext());

        // 用户编辑后不被播种覆盖
        store.Save("system-prompt.md", "user-edited");
        store.EnsureSeeded();
        Assert.Equal("user-edited", store.Read("system-prompt.md"));

        // 重置回出厂
        store.ResetToDefault("system-prompt.md");
        Assert.Equal("persona-default", store.Read("system-prompt.md"));
    }

    [Fact]
    public void SkillStore_RejectsUnknownFile()
    {
        var store = new AiSkillStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        Assert.Throws<ArgumentException>(() => store.Save("evil.txt", "x"));
    }

    [Fact]
    public void Composer_GuidedVsFull_ProducesDifferentModeRules()
    {
        var guided = AiSystemPromptComposer.Compose("persona", AiInteractionMode.Guided);
        var full = AiSystemPromptComposer.Compose("persona", AiInteractionMode.FullBatch);
        Assert.Contains("persona", guided);
        Assert.Contains("问诊分轮", guided);
        Assert.Contains("高效全量", full);
        Assert.Equal(AiInteractionMode.FullBatch, AiSystemPromptComposer.ParseMode("full"));
        Assert.Equal(AiInteractionMode.Guided, AiSystemPromptComposer.ParseMode("guided"));
        Assert.Equal(AiInteractionMode.Guided, AiSystemPromptComposer.ParseMode(null));
    }

    [Fact]
    public void Presets_EveryPresetWithModels_HasDefaultModelInList()
    {
        foreach (var preset in AiProviderPresets.All)
        {
            if (preset.Models.Count == 0)
                continue;
            Assert.Contains(preset.Models, m => m.Id == preset.DefaultModel);
        }
    }

    // ---------- collect_diagnostics 工具 ----------

    private sealed class FakeDiagnostics : IAiDiagnosticsRunner
    {
        public int Calls;
        public Task<string> CollectAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult("## SUMMARY\ncpu ok");
        }
    }

    [Fact]
    public async Task Executor_CollectDiagnostics_UsesRunner()
    {
        var diag = new FakeDiagnostics();
        var executor = new AiToolExecutor(new FakeGateway(), (_, _) => Task.FromResult(true), diag);
        var result = await executor.ExecuteAsync(MakeCall("collect_diagnostics", "{}"), CancellationToken.None);
        Assert.Equal(1, diag.Calls);
        Assert.Contains("SUMMARY", result);
    }

    [Fact]
    public void DiagnosticsRunner_ExtractSummary_ReturnsSummaryBlock()
    {
        var report = "header\n====\n## SUMMARY\nkey signals\n====\n## DETAILS\nmore";
        var summary = PowerShellDiagnosticsRunner.ExtractSummary(report);
        Assert.Contains("SUMMARY", summary);
        Assert.Contains("key signals", summary);
        Assert.DoesNotContain("DETAILS", summary);
    }

    // ---------- 会话历史 ----------

    [Fact]
    public void ConversationStore_SaveListLoadDelete()
    {
        var dir = Path.Combine(Path.GetTempPath(), "yq-hist-" + Guid.NewGuid().ToString("N"));
        var store = new AiConversationStore(dir);

        var convo = new AiConversation
        {
            Id = "s1",
            Title = "优化内存",
            Messages = { AiChatMessage.System("s"), AiChatMessage.User("帮我优化内存"), new AiChatMessage { Role = "assistant", Content = "好的" } }
        };
        store.Save(convo);

        var list = store.List();
        Assert.Single(list);
        Assert.Equal("s1", list[0].Id);
        Assert.Equal("优化内存", list[0].Title);

        var loaded = store.Load("s1");
        Assert.NotNull(loaded);
        Assert.Equal(3, loaded!.Messages.Count);

        store.Delete("s1");
        Assert.Empty(store.List());
        Assert.Null(store.Load("s1"));
    }

    [Fact]
    public void ConversationStore_GarbageFile_DoesNotBreakList()
    {
        var dir = Path.Combine(Path.GetTempPath(), "yq-hist-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "bad.json"), "{not json");
        var store = new AiConversationStore(dir);
        Assert.Empty(store.List());
    }

    [Fact]
    public void ViewModel_NewChat_ClearsAndResetsSession()
    {
        var dir = Path.Combine(Path.GetTempPath(), "yq-hist-" + Guid.NewGuid().ToString("N"));
        var vm = new AiAssistantViewModel(new FakeSettingsStore(), new FakeGateway(),
            conversationStore: new AiConversationStore(dir));
        vm.ChatEntries.Add(new AiChatEntry("user", "x", DateTimeOffset.Now));
        vm.NewChatCommand.Execute(null);
        Assert.Empty(vm.ChatEntries);
    }

    // ---------- 凭据保护 ----------

    [Fact]
    public void CredentialProtector_RoundTrips()
    {
        var protected_ = AiCredentialProtector.Protect("sk-test-123");
        Assert.NotEqual("sk-test-123", protected_);
        Assert.Equal("sk-test-123", AiCredentialProtector.Unprotect(protected_));
    }

    [Fact]
    public void CredentialProtector_Garbage_ReturnsEmpty() =>
        Assert.Equal(string.Empty, AiCredentialProtector.Unprotect("not-base64!!!"));

    // ---------- ViewModel ----------

    private sealed class FakeSettingsStore : IAppSettingsStore
    {
        public AppSettings Current { get; private set; } = new();
        public event EventHandler<AppSettingsChangedEventArgs>? Changed;
        public bool TryUpdate(Action<AppSettings> update)
        {
            var previous = Current;
            update(Current);
            Changed?.Invoke(this, new AppSettingsChangedEventArgs(previous, Current));
            return true;
        }
    }

    [Fact]
    public void ViewModel_SaveConfig_PersistsEncryptedKey()
    {
        var store = new FakeSettingsStore();
        var vm = new AiAssistantViewModel(store, new FakeGateway());
        vm.SelectedProvider = AiProviderPresets.Find("deepseek");
        vm.ApiKey = "sk-secret";
        vm.SaveConfigCommand.Execute(null);

        Assert.Equal("deepseek", store.Current.AiProviderKey);
        Assert.Equal("https://api.deepseek.com/v1", store.Current.AiEndpoint);
        Assert.Equal("deepseek-chat", store.Current.AiModel);
        Assert.Equal("sk-secret", AiCredentialProtector.Unprotect(store.Current.AiApiKeyProtected));
        Assert.DoesNotContain("sk-secret", store.Current.AiApiKeyProtected);
        Assert.True(vm.IsConfigured);
    }

    [Fact]
    public void ViewModel_NotConfigured_BlocksSend()
    {
        var vm = new AiAssistantViewModel(new FakeSettingsStore(), new FakeGateway());
        vm.InputText = "hi";
        Assert.False(vm.IsConfigured);
    }
}
