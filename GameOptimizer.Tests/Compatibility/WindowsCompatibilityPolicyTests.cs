// File: GameOptimizer.Tests/Compatibility/WindowsCompatibilityPolicyTests.cs
using GameOptimizer.Core.Models;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Optimization.Operations;
using GameOptimizer.Core.Optimization.Providers;
using GameOptimizer.Core.Services;
using GameOptimizer.Shell.Models;
using GameOptimizer.Tests.Changes;
using Microsoft.Extensions.Logging.Abstractions;
using EngineOperationStatus = GameOptimizer.Core.Optimization.OperationStatus;

namespace GameOptimizer.Tests.Compatibility;

public sealed class WindowsCompatibilityPolicyTests
{
    private static readonly IReadOnlySet<WindowsSystemComponent> AllComponents =
        Enum.GetValues<WindowsSystemComponent>().ToHashSet();

    [Fact]
    public void Catalog_ResolvesEveryBuiltInModuleAndOperationToAtLeastTheAppMinimumBuild()
    {
        var provider = new BuiltInOptimizationOperationProvider();
        var modules = provider.GetModules().ToArray();
        var operations = modules.SelectMany(module => module.Operations).ToArray();

        Assert.Equal(13, modules.Length);
        Assert.Equal(212, operations.Length);
        Assert.Equal(212, operations.Select(operation => operation.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var operation in operations)
        {
            var rule = WindowsCompatibilityRuleCatalog.Resolve(operation.Id);
            Assert.True(rule.MinimumBuild >= (int)WindowsBuild.Win10_2004, operation.Id);
        }
    }

    [Fact]
    public void EveryCatalogOperation_HasAnEffectOrAvailabilityDescription()
    {
        var operations = new BuiltInOptimizationOperationProvider()
            .GetModules()
            .SelectMany(module => module.Operations)
            .ToArray();

        Assert.All(operations, operation =>
        {
            foreach (var isEnglish in new[] { false, true })
            {
                var displayedDescription = OptimizationTranslations.Instance.GetDescription(
                    operation.Id,
                    operation.Description,
                    isEnglish);

                Assert.False(string.IsNullOrWhiteSpace(displayedDescription), $"{operation.Id}:{isEnglish}");
                Assert.DoesNotContain("暂无详细描述", displayedDescription, StringComparison.Ordinal);
                Assert.DoesNotContain("No description available", displayedDescription, StringComparison.OrdinalIgnoreCase);

                if (operation is not PolicyDeniedOptimizationOperation)
                {
                    var displayedTitle = OptimizationTranslations.Instance.GetTitle(
                        operation.Id,
                        operation.Title,
                        isEnglish);
                    Assert.DoesNotContain("不可关闭", displayedTitle, StringComparison.Ordinal);
                    Assert.DoesNotContain("Cannot Be Disabled", displayedTitle, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("策略接管并阻断", displayedDescription, StringComparison.Ordinal);
                    Assert.DoesNotContain("owned and denied by security policy", displayedDescription, StringComparison.OrdinalIgnoreCase);
                }
            }
        });
    }

    [Fact]
    public void ExecutablePerformanceSecurityOperations_ShowTheirEffectInsteadOfPolicyDeniedCopy()
    {
        var provider = new BuiltInOptimizationOperationProvider();
        var executableIds = new[]
        {
            "basic.disable_security_center",
            "basic.disable_uac",
            "basic.disable_firewall",
            "basic.disable_security_notifications",
            "security.disable_vbs",
            "security.disable_hvci",
            "security.disable_cfg",
            "security.disable_defender_realtime",
            "security.disable_smartscreen",
            "security.disable_sehop"
        };

        foreach (var operationId in executableIds)
        {
            var operation = provider.GetModules()
                .SelectMany(module => module.Operations)
                .Single(operation => operation.Id == operationId);
            var title = OptimizationTranslations.Instance.GetTitle(operation.Id, operation.Title, isEnglish: false);
            var description = OptimizationTranslations.Instance.GetDescription(operation.Id, operation.Description, isEnglish: false);

            Assert.DoesNotContain("不可关闭", title, StringComparison.Ordinal);
            Assert.DoesNotContain("策略接管并阻断", description, StringComparison.Ordinal);
            Assert.True(
                description.Contains("关闭", StringComparison.Ordinal)
                || description.Contains("停止", StringComparison.Ordinal),
                $"{operationId}: {description}");
        }

        var denied = provider.GetModules()
            .SelectMany(module => module.Operations)
            .Single(operation => operation.Id == "security.disable_dep");
        Assert.IsType<PolicyDeniedOptimizationOperation>(denied);
        Assert.Contains("不可关闭", OptimizationTranslations.Instance.GetTitle(denied.Id, denied.Title, isEnglish: false), StringComparison.Ordinal);
    }

    [Fact]
    public void EveryRegistryOperation_ExposesAStableTargetDescriptor()
    {
        var operations = new BuiltInOptimizationOperationProvider().GetModules()
            .SelectMany(module => module.Operations)
            .Where(operation => operation.OperationType == OperationType.Registry)
            .Where(operation => operation.Id != "security.disable_dep")
            .ToArray();

        Assert.NotEmpty(operations);
        Assert.All(operations, operation =>
        {
            var target = WindowsRegistryTargetCatalog.Resolve(operation, 22631);
            Assert.NotNull(target);
            Assert.False(string.IsNullOrWhiteSpace(target!.Path), operation.Id);
            Assert.False(string.IsNullOrWhiteSpace(target.ValueName), operation.Id);
        });
    }

    [Fact]
    public void CurrentWindowsBuildCatalog_Distinguishes25H2And26H1()
    {
        Assert.Equal(26200, (int)WindowsBuild.Win11_25H2);
        Assert.Equal(28000, (int)WindowsBuild.Win11_26H1);
    }

    [Fact]
    public async Task VariantCatalog_DescribesAll212OperationsOnCurrentWindowsGenerations()
    {
        var operations = new BuiltInOptimizationOperationProvider().GetModules()
            .SelectMany(module => module.Operations.Select(operation => (module.Key, Operation: operation)))
            .ToArray();

        foreach (var build in new[] { 19045, 22631, 26200, 28000 })
        {
            var checker = CreateChecker(build: build, wddm: new Version(3, 0));
            foreach (var (moduleKey, operation) in operations)
            {
                var metadata = await checker.DescribeAsync(moduleKey, operation, CancellationToken.None);
                Assert.False(string.IsNullOrWhiteSpace(metadata.VariantId), $"{build}:{operation.Id}");
                Assert.False(string.IsNullOrWhiteSpace(metadata.VariantLabel), $"{build}:{operation.Id}");
                Assert.False(string.IsNullOrWhiteSpace(metadata.Method), $"{build}:{operation.Id}");
                Assert.False(string.IsNullOrWhiteSpace(metadata.Information), $"{build}:{operation.Id}");
            }
        }
    }

    [Fact]
    public async Task FocusAssist_SelectsDifferentManualMethodsForWindows10AndWindows11()
    {
        var operation = GetOperation("explorer", "windows.disable_focus_assist");
        var windows10 = CreateChecker(build: 19045);
        var windows11 = CreateChecker(build: 26200);

        var windows10Status = await windows10.ProbeAsync("explorer", operation, CancellationToken.None);
        var windows11Status = await windows11.ProbeAsync("explorer", operation, CancellationToken.None);
        var windows10Metadata = await windows10.DescribeAsync("explorer", operation, CancellationToken.None);
        var windows11Metadata = await windows11.DescribeAsync("explorer", operation, CancellationToken.None);

        Assert.Equal(OperationSupportState.AlternativeAvailable, windows10Status?.SupportState);
        Assert.Equal(OperationSupportState.AlternativeAvailable, windows11Status?.SupportState);
        Assert.Equal("compatibility.method.manual-only", windows10Status?.SupportReasonCode);
        Assert.NotEqual(windows10Status?.Alternative, windows11Status?.Alternative);
        Assert.Equal("win10.focus-assist-manual", windows10Metadata.VariantId);
        Assert.Equal("win11.do-not-disturb-manual", windows11Metadata.VariantId);
    }

    [Fact]
    public async Task VersionSpecificMetadata_DistinguishesHagsEndTaskAndRetiredCortana()
    {
        var hags = GetOperation("graphics", "gpu.enable_hags");
        var endTask = GetOperation("explorer", "explorer.end_task");
        var cortana = GetOperation("privacy", "privacy.disable_cortana");

        var win10Hags = await CreateChecker(build: 19045, wddm: new Version(2, 7))
            .DescribeAsync("graphics", hags, CancellationToken.None);
        var win11Hags = await CreateChecker(build: 26200, wddm: new Version(3, 0))
            .DescribeAsync("graphics", hags, CancellationToken.None);
        var endTask25H2 = await CreateChecker(build: 26200)
            .DescribeAsync("explorer", endTask, CancellationToken.None);
        var retiredCortana = await CreateChecker(build: 26200)
            .DescribeAsync("privacy", cortana, CancellationToken.None);

        Assert.NotEqual(win10Hags.VariantId, win11Hags.VariantId);
        Assert.Equal("win11.25h2.end-task-advanced-settings", endTask25H2.VariantId);
        Assert.Equal("win11.cortana-retired", retiredCortana.VariantId);
    }

    [Fact]
    public void RegistryTargetCatalog_UsesOfficialCortanaPolicyPathAndValidatesOwnerPaths()
    {
        var provider = new BuiltInOptimizationOperationProvider();
        var operations = provider.GetModules().SelectMany(module => module.Operations).ToArray();
        var cortana = operations.Single(operation => operation.Id == "privacy.disable_cortana");

        var target = WindowsRegistryTargetCatalog.Resolve(cortana, 26200);

        Assert.NotNull(target);
        Assert.True(target!.IsMicrosoftDocumented);
        Assert.Equal("HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\Windows Search", target.DisplayPath);
        Assert.Equal("AllowCortana", target.ValueName);
        Assert.Contains("learn.microsoft.com", target.EvidenceUrl, StringComparison.OrdinalIgnoreCase);

        Assert.All(
            operations.Where(operation => !string.IsNullOrWhiteSpace(operation.RegistryPath)),
            operation => Assert.True(
                WindowsRegistryTargetCatalog.MatchesOwnerPath(
                    operation,
                    WindowsRegistryTargetCatalog.Resolve(operation, 26200)!),
                operation.Id));
    }

    [Fact]
    public void RegistryTargetCatalog_UsesMicrosoftPolicyMappingsForDocumentedOperations()
    {
        var provider = new BuiltInOptimizationOperationProvider();
        var operations = provider.GetModules().SelectMany(module => module.Operations).ToArray();

        var timeline = WindowsRegistryTargetCatalog.Resolve(
            operations.Single(operation => operation.Id == "windows.disable_timeline"), 22631);
        Assert.NotNull(timeline);
        Assert.True(timeline!.IsMicrosoftDocumented);
        Assert.Equal("HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\System", timeline.DisplayPath);
        Assert.Contains("EnableActivityFeed", timeline.ValueName, StringComparison.Ordinal);
        Assert.Contains("learn.microsoft.com", timeline.EvidenceUrl, StringComparison.OrdinalIgnoreCase);

        var gameDvr = WindowsRegistryTargetCatalog.Resolve(
            operations.Single(operation => operation.Id == "gaming.dvr.disable"), 19045);
        Assert.NotNull(gameDvr);
        Assert.True(gameDvr!.IsMicrosoftDocumented);
        Assert.Equal("HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\GameDVR\\AllowGameDVR", gameDvr.DisplayPath + "\\" + gameDvr.ValueName);

        var advertising = WindowsRegistryTargetCatalog.Resolve(
            operations.Single(operation => operation.Id == "privacy.disable_advertising_id"), 22631);
        Assert.NotNull(advertising);
        Assert.False(advertising!.IsMicrosoftDocumented);
        Assert.Equal("HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\AdvertisingInfo", advertising.OfficialPolicyPath);
        Assert.Equal("DisabledByGroupPolicy", advertising.OfficialPolicyValueName);
        Assert.Contains("learn.microsoft.com", advertising.OfficialPolicyEvidenceUrl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DescribeAsync_ShowsRegistryTargetAndOfficialAlternativeInLocalizedInformation()
    {
        var provider = new BuiltInOptimizationOperationProvider();
        var operations = provider.GetModules().SelectMany(module => module.Operations).ToArray();
        var checker = CreateChecker(build: 22631);

        var timeline = await checker.DescribeAsync(
            "explorer",
            operations.Single(operation => operation.Id == "windows.disable_timeline"),
            CancellationToken.None);
        Assert.Contains("SOFTWARE\\Policies\\Microsoft\\Windows\\System", timeline.Information, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EnableActivityFeed", timeline.Information, StringComparison.Ordinal);

        var advertising = await checker.DescribeAsync(
            "privacy",
            operations.Single(operation => operation.Id == "privacy.disable_advertising_id"),
            CancellationToken.None);
        Assert.Contains("官方策略替代定位", advertising.Information, StringComparison.Ordinal);
        Assert.Contains("AdvertisingInfo", advertising.Information, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OldWindowsBuild_BlocksAllCatalogSwitchesDuringReadOnlyProbe()
    {
        var checker = CreateChecker(build: 18362);
        var provider = new BuiltInOptimizationOperationProvider();

        foreach (var module in provider.GetModules())
        {
            foreach (var operation in module.Operations)
            {
                var status = await checker.ProbeAsync(module.Key, operation, CancellationToken.None);
                Assert.NotNull(status);
                Assert.Equal(OperationSupportState.Unsupported, status.SupportState);
                Assert.Equal("compatibility.os.build-too-old", status.SupportReasonCode);
            }
        }
    }

    [Fact]
    public async Task ServerEdition_IsBlockedEvenWhenBuildIsNewEnough()
    {
        var checker = CreateChecker(build: 26100, isServer: true);
        var operation = GetOperation("performance", "gaming.mode.enable");

        var status = await checker.ProbeAsync("performance", operation, CancellationToken.None);

        Assert.NotNull(status);
        Assert.Equal(OperationSupportState.Unsupported, status.SupportState);
        Assert.Equal("compatibility.os.server-unsupported", status.SupportReasonCode);
    }

    [Fact]
    public async Task MissingRequiredComponent_BlocksBeforeTheCapabilitySpecificProbe()
    {
        var components = AllComponents.Where(component => component != WindowsSystemComponent.BcdEdit).ToHashSet();
        var checker = CreateChecker(build: 22631, components: components);
        var operation = GetOperation("cpu", "cpu.disable_dynamic_tick");

        var status = await checker.ProbeAsync("cpu", operation, CancellationToken.None);

        Assert.NotNull(status);
        Assert.Equal(OperationSupportState.Unsupported, status.SupportState);
        Assert.Equal("compatibility.component.missing", status.SupportReasonCode);
        Assert.Contains("bcdedit", status.Details, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HardwareSpecificRules_BlockWrongCpuGpuAndNonHybridSystems()
    {
        var intelOnly = await CreateChecker(cpuVendor: CpuVendor.AMD)
            .ProbeAsync("cpu", GetOperation("cpu", "intel.disable_cstates"), CancellationToken.None);
        var amdGpuOnly = await CreateChecker(hasAmdGpu: false)
            .ProbeAsync("cpu", GetOperation("cpu", "amd.radeon_anti_lag"), CancellationToken.None);
        var hybridOnly = await CreateChecker(hasHybridProcessor: false)
            .ProbeAsync("system", GetOperation("system", "power.class1_perf_level"), CancellationToken.None);

        Assert.Equal(OperationSupportState.NotApplicable, intelOnly?.SupportState);
        Assert.Equal(OperationSupportState.NotApplicable, amdGpuOnly?.SupportState);
        Assert.Equal(OperationSupportState.NotApplicable, hybridOnly?.SupportState);
    }

    [Fact]
    public async Task Hags_UsesSupportedUnsupportedAndAlternativeStrategiesFromWddmFacts()
    {
        var operation = GetOperation("graphics", "gpu.enable_hags");
        var unknown = await CreateChecker(wddm: null).ProbeAsync("graphics", operation, CancellationToken.None);
        var old = await CreateChecker(wddm: new Version(2, 6)).ProbeAsync("graphics", operation, CancellationToken.None);
        var driverUnknown = await CreateChecker(wddm: new Version(2, 7), hagsSupported: null)
            .ProbeAsync("graphics", operation, CancellationToken.None);
        var hardwareUnsupported = await CreateChecker(wddm: new Version(2, 7), hagsSupported: false)
            .ProbeAsync("graphics", operation, CancellationToken.None);
        var supported = await CreateChecker(wddm: new Version(2, 7), hagsSupported: true)
            .ProbeAsync("graphics", operation, CancellationToken.None);

        Assert.Equal(OperationSupportState.AlternativeAvailable, unknown?.SupportState);
        Assert.Equal("compatibility.hardware.wddm-unknown", unknown?.SupportReasonCode);
        Assert.False(string.IsNullOrWhiteSpace(unknown?.Alternative));
        Assert.Equal(OperationSupportState.NotApplicable, old?.SupportState);
        Assert.Equal(OperationSupportState.AlternativeAvailable, driverUnknown?.SupportState);
        Assert.Equal("compatibility.hardware.hags-unknown", driverUnknown?.SupportReasonCode);
        Assert.Equal(OperationSupportState.NotApplicable, hardwareUnsupported?.SupportState);
        Assert.Null(supported);
    }

    [Fact]
    public async Task SupportedSwitchWithoutElevation_ReportsPermissionInsteadOfOsFailure()
    {
        var checker = CreateChecker(isAdministrator: false);
        var operation = GetOperation("performance", "basic.disable_uac");

        var status = await checker.ProbeAsync("performance", operation, CancellationToken.None);

        Assert.NotNull(status);
        Assert.Equal(OperationSupportState.PermissionRequired, status.SupportState);
        Assert.Equal("permission.administrator-required", status.SupportReasonCode);
    }

    [Fact]
    public void ShellModel_AlternativeStateIsVisibleAndCannotCreatePendingExecutionIntent()
    {
        var model = new OptimizationToggleModel(
            "HAGS",
            operationId: "gpu.enable_hags",
            operationRoute: OptimizationOperationRoute.TypedExecutable,
            requiresAdmin: false);
        model.SyncOperationContract(
            new EngineOperationStatus(
                false,
                "无法验证 WDDM。替代方案：使用 Windows 图形设置。",
                SupportState: OperationSupportState.AlternativeAvailable,
                SupportReasonCode: "compatibility.hardware.wddm-unknown",
                Alternative: "使用 Windows 图形设置",
                CompatibilityVariantId: "win11.hags.wddm27",
                CompatibilityVariantLabel: "Windows 11 变体",
                CompatibilityMethod: "注册表 typed Owner",
                CompatibilityInformation: "需要 WDDM 2.7"),
            isAdministrator: true);

        var accepted = model.OnUserToggled(enabled: true);

        Assert.False(accepted);
        Assert.False(model.CanToggle);
        Assert.False(model.IsPending);
        Assert.Equal(ModuleCapabilityState.AlternativeAvailable, model.CapabilityState);
        Assert.Contains("Windows 图形设置", model.CapabilityStatusReason, StringComparison.Ordinal);
        Assert.Equal("win11.hags.wddm27", model.CompatibilityVariantId);
        Assert.Equal("注册表 typed Owner", model.CompatibilityMethod);
    }

    [Fact]
    public void CompositeStatus_PreservesBlockedChildCompatibilityEvidence()
    {
        var child = new EngineOperationStatus(
            false,
            "组件缺失",
            SupportState: OperationSupportState.Unsupported,
            SupportReasonCode: "compatibility.component.missing");

        var aggregate = EngineOperationStatus.Aggregate(
            [new EngineOperationStatus(true, "ready"), child],
            isCompliant: false,
            "all ready",
            "some pending");

        Assert.Equal(OperationSupportState.Unsupported, aggregate.SupportState);
        Assert.Equal("compatibility.component.missing", aggregate.SupportReasonCode);
        Assert.Equal("组件缺失", aggregate.Details);
    }

    [Fact]
    public async Task Engine_UsesTheSamePolicyForStatusApplyAndRestoreWithoutCallingTheOperation()
    {
        var operation = new CountingOperation();
        var policy = new BlockingOptimizationPolicy();
        var engine = new OptimizationEngine(
            [new SingleOperationProvider(operation, "compatibility-policy-test")],
            new EmptyRestoreStore(),
            NullLoggerFactory.Instance,
            transactionLog: TestTransactionLogFactory.CreateIsolated(),
            compatibilityPolicy: policy);

        var statuses = await engine.GetStatusesAsync("compatibility-policy-test");
        var apply = await engine.ApplyAsync("compatibility-policy-test", [operation.Id]);
        var restore = await engine.RestoreAsync("compatibility-policy-test", [operation.Id]);

        Assert.Equal(OperationSupportState.Unsupported, Assert.Single(statuses).SupportState);
        Assert.False(Assert.Single(apply.Operations).Success);
        Assert.False(Assert.Single(restore.Operations).Success);
        Assert.Equal(0, operation.StatusCalls);
        Assert.Equal(0, operation.ApplyCalls);
        Assert.Equal(0, operation.RestoreCalls);
        Assert.Equal(3, policy.ProbeCalls);
    }

    [Fact]
    public async Task Engine_MergesSupportedVariantMetadataWithoutSkippingRealStatusReadback()
    {
        var operation = new CountingOperation();
        var checker = CreateChecker(build: 26200);
        var engine = new OptimizationEngine(
            [new SingleOperationProvider(operation)],
            new EmptyRestoreStore(),
            NullLoggerFactory.Instance,
            transactionLog: TestTransactionLogFactory.CreateIsolated(),
            compatibilityPolicy: checker);

        var status = Assert.Single(await engine.GetStatusesAsync("test"));

        Assert.Equal(1, operation.StatusCalls);
        Assert.Equal("win11.25h2.shared-other", status.CompatibilityVariantId);
        Assert.False(string.IsNullOrWhiteSpace(status.CompatibilityMethod));
        Assert.False(string.IsNullOrWhiteSpace(status.CompatibilityInformation));
    }

    [Fact]
    public void Engine_RejectsBuiltInCatalogWithoutCentralizedCompatibilityPolicy()
    {
        Assert.Throws<InvalidOperationException>(() => new OptimizationEngine(
            [new BuiltInOptimizationOperationProvider()],
            new EmptyRestoreStore(),
            NullLoggerFactory.Instance,
            transactionLog: TestTransactionLogFactory.CreateIsolated()));
    }

    [Fact]
    public async Task Apply_ReadsBackAfterChangedFailureAndKeepsFailureOutcome()
    {
        var operation = new PartialFailureOperation();
        var engine = new OptimizationEngine(
            [new SingleOperationProvider(operation)],
            new EmptyRestoreStore(),
            NullLoggerFactory.Instance,
            transactionLog: TestTransactionLogFactory.CreateIsolated(),
            compatibilityPolicy: new AllowingOptimizationPolicy());

        var result = await engine.ApplyAsync("test", [operation.Id]);
        var summary = Assert.Single(result.Operations);

        Assert.False(summary.Success);
        Assert.True(summary.Changed);
        Assert.True(summary.After.IsCompliant);
        Assert.Equal(3, operation.StatusCalls);
    }

    [Fact]
    public void BuiltInCatalog_AllExecutableOperationsRequireExplicitConfirmation()
    {
        var operations = new BuiltInOptimizationOperationProvider().GetModules()
            .SelectMany(module => module.Operations)
            .Where(operation => operation is not null)
            .ToArray();

        Assert.Equal(212, operations.Length);
        Assert.All(operations, operation => Assert.True(
            operation.RequiresExplicitConfirmation || operation is PolicyDeniedOptimizationOperation,
            $"{operation.Id} must declare an explicit confirmation boundary."));
    }

    [Fact]
    public void BuiltInCatalog_AuditReportsStableTypedAndFailClosedCounts()
    {
        var operations = new BuiltInOptimizationOperationProvider().GetModules()
            .SelectMany(module => module.Operations)
            .ToArray();
        var failClosedCount = operations.Count(operation =>
            operation is PolicyDeniedOptimizationOperation
            || operation.GetType().Name.StartsWith("FailClosed", StringComparison.Ordinal));

        Assert.Equal(212, operations.Length);
        Assert.Equal(5, failClosedCount);
        Assert.Equal(207, operations.Length - failClosedCount);
    }

    private static CompatibilityChecker CreateChecker(
        int build = 22631,
        bool isServer = false,
        bool isAdministrator = true,
        CpuVendor cpuVendor = CpuVendor.Intel,
        bool hasHybridProcessor = true,
        bool hasAmdGpu = true,
        Version? wddm = null,
        bool? hagsSupported = true,
        IReadOnlySet<WindowsSystemComponent>? components = null) =>
        new(new FakeEnvironmentProbe(new WindowsCompatibilityEnvironment(
            IsWindows: true,
            MajorVersion: 10,
            BuildNumber: build,
            Edition: isServer ? "ServerDatacenter" : "Professional",
            IsServer: isServer,
            IsAdministrator: isAdministrator,
            CpuVendor: cpuVendor,
            HasHybridProcessor: hasHybridProcessor,
            HasAmdGpu: hasAmdGpu,
            WddmVersion: wddm,
            HardwareAcceleratedGpuSchedulingSupported: hagsSupported,
            Components: components ?? AllComponents)));

    private static IOptimizationOperation GetOperation(string moduleKey, string operationId) =>
        new BuiltInOptimizationOperationProvider().GetModules()
            .Single(module => module.Key == moduleKey)
            .Operations.Single(operation => operation.Id == operationId);

    private sealed class FakeEnvironmentProbe(WindowsCompatibilityEnvironment environment)
        : IWindowsCompatibilityEnvironmentProbe
    {
        public Task<WindowsCompatibilityEnvironment> CaptureAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(environment);
        }
    }

    private sealed class BlockingOptimizationPolicy : IOptimizationCompatibilityPolicy
    {
        public int ProbeCalls { get; private set; }

        public Task<EngineOperationStatus?> ProbeAsync(
            string moduleKey,
            IOptimizationOperation operation,
            CancellationToken cancellationToken)
        {
            ProbeCalls++;
            return Task.FromResult<EngineOperationStatus?>(new EngineOperationStatus(
                false,
                "blocked by compatibility policy",
                SupportState: OperationSupportState.Unsupported,
                SupportReasonCode: "compatibility.test-block"));
        }
    }

    private sealed class AllowingOptimizationPolicy : IOptimizationCompatibilityPolicy
    {
        public Task<EngineOperationStatus?> ProbeAsync(
            string moduleKey,
            IOptimizationOperation operation,
            CancellationToken cancellationToken) => Task.FromResult<EngineOperationStatus?>(null);
    }

    private sealed class SingleOperationProvider(IOptimizationOperation operation, string moduleKey = "test") : IOptimizationOperationProvider
    {
        public IEnumerable<OptimizationModuleDescriptor> GetModules() =>
            [new OptimizationModuleDescriptor(moduleKey, "Test", "Test", [operation])];
    }

    private sealed class EmptyRestoreStore : IOperationRestoreStore
    {
        public Task SaveSnapshotAsync(string operationId, string payload, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<string?> LoadSnapshotAsync(string operationId, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task DeleteSnapshotAsync(string operationId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> HasSnapshotAsync(string operationId, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class CountingOperation : IOptimizationOperation
    {
        public string Id => "test.compatibility";
        public string Title => "Compatibility test";
        public string Description => "Compatibility test";
        public string Category => "test";
        public OperationRiskLevel RiskLevel => OperationRiskLevel.Safe;
        public bool RequiresAdmin => false;
        public int StatusCalls { get; private set; }
        public int ApplyCalls { get; private set; }
        public int RestoreCalls { get; private set; }

        public Task<EngineOperationStatus> GetStatusAsync(CancellationToken cancellationToken)
        {
            StatusCalls++;
            return Task.FromResult(new EngineOperationStatus(false, "not applied"));
        }

        public Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
        {
            ApplyCalls++;
            return Task.FromResult(new OperationInvokeResult(true, true, "applied"));
        }

        public Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
        {
            RestoreCalls++;
            return Task.FromResult(new OperationInvokeResult(true, true, "restored"));
        }
    }

    private sealed class PartialFailureOperation : IOptimizationOperation
    {
        private bool _applied;

        public string Id => "test.partial-failure";
        public string Title => "Partial failure test";
        public string Description => "Partial failure test";
        public string Category => "test";
        public OperationRiskLevel RiskLevel => OperationRiskLevel.Safe;
        public bool RequiresAdmin => false;
        public int StatusCalls { get; private set; }

        public Task<EngineOperationStatus> GetStatusAsync(CancellationToken cancellationToken)
        {
            StatusCalls++;
            return Task.FromResult(new EngineOperationStatus(_applied, _applied ? "applied" : "not applied"));
        }

        public Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
        {
            _applied = true;
            return Task.FromResult(new OperationInvokeResult(false, true, "partial failure"));
        }

        public Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new OperationInvokeResult(false, false, "restore intentionally unavailable"));
    }
}
