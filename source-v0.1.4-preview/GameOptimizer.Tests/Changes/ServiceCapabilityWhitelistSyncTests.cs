using GameOptimizer.Core.Services.SystemServices;
using GameOptimizer.Infrastructure.Windows.Extensions;
using GameOptimizer.Infrastructure.Windows.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GameOptimizer.Tests.Changes;

/// <summary>
/// 防漂移门禁：WindowsServiceAccessor 的服务白名单必须覆盖全部已注册的
/// ServiceChangeCapabilityDefinition 服务名；任何一边单独新增都必须让本测试失败。
/// </summary>
public sealed class ServiceCapabilityWhitelistSyncTests
{
    [Fact]
    public async Task EveryRegisteredServiceCapabilityNameIsAcceptedByTheAccessorWhitelist()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddGameOptimizerWindowsInfrastructure();
        using var provider = services.BuildServiceProvider();

        var accessor = provider.GetRequiredService<IWindowsServiceAccessor>();
        var definitions = ServiceCapabilityDefinitions.Optimization
            .Concat(ServiceCapabilityDefinitions.Repair)
            .ToArray();

        Assert.Equal(
            definitions.Select(definition => definition.ServiceName).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            definitions.Length);

        foreach (var definition in definitions)
        {
            // 白名单外服务会在 ProbeAsync 一进来就抛 InvalidOperationException；
            // 白名单内则继续走 OS/存在性检查并返回评估结果（本测试机只读查询，无写入）。
            var exception = await Record.ExceptionAsync(
                () => accessor.ProbeAsync(definition.ServiceName, CancellationToken.None));

            Assert.True(
                exception is null,
                $"服务 '{definition.ServiceName}'（能力 {definition.CapabilityId}）不在访问器白名单内：" +
                $"{exception?.Message}——真机上该能力会被误阻断。");
        }
    }

    [Fact]
    public void WhitelistIsExactlyTheDefinitionUnionWithoutExtras()
    {
        // 通过反射读取私有 AllowedServices，确保没有游离于定义之外的手工条目残留。
        var field = typeof(WindowsServiceAccessor)
            .GetField("AllowedServices", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(field);
        var whitelist = Assert.IsType<HashSet<string>>(field!.GetValue(null));
        var expected = ServiceCapabilityDefinitions.AllServiceNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Superset(expected, whitelist);
        Assert.Subset(expected, whitelist);
    }
}
