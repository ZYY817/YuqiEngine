using GameOptimizer.Core.Services;

namespace GameOptimizer.Tests.Shell;

/// <summary>
/// 管理员体系架构守护：禁止再散落硬编码「请以管理员身份运行」文案。
/// 唯一合法生成点是 ElevationGate（及转发层 PermissionChecker / WindowsElevation 说明）。
/// </summary>
public sealed class ElevationArchitectureTests
{
    private static readonly string[] ForbiddenLiterals =
    [
        "请以管理员身份运行程序",
        "请以管理员身份重新启动应用程序",
        "请以管理员身份重新启动应用",
        "请用管理员身份重新打开软件",
        "请右键以管理员身份运行程序",
        "请确认已使用管理员权限运行",
    ];

    private static readonly string[] AllowedSourceFiles =
    [
        "ElevationGate.cs",
        "PermissionChecker.cs",
        "WindowsElevation.cs",
    ];

    [Fact]
    public void ProductionSources_DoNotHardcodeRestartAsAdminInstructions()
    {
        var root = FindRepositoryRoot();
        var projects = new[]
        {
            "GameOptimizer.Core",
            "GameOptimizer.Infrastructure.Windows",
            "GameOptimizer.Shell",
        };

        var violations = new List<string>();
        foreach (var project in projects)
        {
            foreach (var file in Directory.EnumerateFiles(
                         Path.Combine(root, project),
                         "*.cs",
                         SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                {
                    continue;
                }

                var name = Path.GetFileName(file);
                if (AllowedSourceFiles.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Localization dictionaries are the single content store; consumers must not inline copies.
                if (name.Contains("LocalizationService", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var text = File.ReadAllText(file);
                foreach (var literal in ForbiddenLiterals)
                {
                    if (text.Contains(literal, StringComparison.Ordinal))
                    {
                        violations.Add($"{file.Replace(root, "").TrimStart(Path.DirectorySeparatorChar, '/')}: contains \"{literal}\"");
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Hardcoded admin-restart instructions found outside ElevationGate:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void ElevationGate_ElevatedWriteDenialNeverAsksToRestartAsAdmin()
    {
        var snapshot = new ElevationSnapshot(
            ElevationLevel.ElevatedAdministrator,
            IsElevated: true,
            IsAdministratorAccount: true,
            IsWindows: true,
            WindowsElevation.ReasonElevated,
            "进程令牌已提权");

        var message = ElevationGate.DescribeWriteDenial("测试写入", snapshot, "access denied");
        Assert.DoesNotContain("请以管理员身份运行", message, StringComparison.Ordinal);
        Assert.DoesNotContain("以管理员身份重新启动", message, StringComparison.Ordinal);
        Assert.Contains("系统拒绝", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ElevationGate_UnelevatedStillGivesActionableRestartGuidance()
    {
        var snapshot = new ElevationSnapshot(
            ElevationLevel.AdministratorNotElevated,
            IsElevated: false,
            IsAdministratorAccount: true,
            IsWindows: true,
            WindowsElevation.ReasonAdministratorNotElevated,
            "UAC 筛选令牌");

        var decision = ElevationGate.Evaluate("测试写入", operationRequiresAdministrator: true, snapshot);
        Assert.True(decision.NeedsElevation);
        Assert.Equal(ElevationAttribution.UacFilteredToken, decision.Attribution);
        Assert.Contains("以管理员身份运行", decision.UserMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ElevationGate_AttributeFailure_ElevatedMapsAccessDenialToSystemDenied()
    {
        var snapshot = new ElevationSnapshot(
            ElevationLevel.ElevatedAdministrator,
            true,
            true,
            true,
            WindowsElevation.ReasonElevated,
            "elevated");

        // AttributeFailure 读 Current；这里只验证 Describe/Evaluate 体系语义。
        var decision = ElevationGate.Evaluate("x", true, snapshot, "UnauthorizedAccessException");
        Assert.Equal(ElevationAttribution.SystemDenied, decision.Attribution);
        Assert.False(decision.NeedsElevation);
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "GameOptimizer.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test base directory.");
    }
}
