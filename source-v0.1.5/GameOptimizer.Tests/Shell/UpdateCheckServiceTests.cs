// File: GameOptimizer.Tests/Shell/UpdateCheckServiceTests.cs
using GameOptimizer.Shell.Services;

namespace GameOptimizer.Tests.Shell;

/// <summary>
/// 版本检测解析逻辑回归测试。
/// 目标不变式：release tag（v0.1.5-preview+commit 形态）与程序集版本
/// （0.1.4-preview+hash 形态）都只比较数值部分，且无法解析时不许假装成功。
/// </summary>
public sealed class UpdateCheckServiceTests
{
    [Theory]
    [InlineData("v0.1.5", "0.1.5")]
    [InlineData("0.1.5-preview", "0.1.5")]
    [InlineData("v0.2.0+fd49d2ab", "0.2.0")]
    [InlineData("V1.0.0-rc1", "1.0.0")]
    [InlineData("0.1.4-preview+hash", "0.1.4")]
    public void ParseVersionStripsTagPrefixesAndSuffixes(string tag, string expected)
    {
        var parsed = UpdateCheckService.ParseVersion(tag);
        Assert.NotNull(parsed);
        Assert.Equal(Version.Parse(expected), parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-version")]
    [InlineData("v")]
    public void ParseVersionRejectsUnparseableTags(string? tag)
    {
        Assert.Null(UpdateCheckService.ParseVersion(tag));
    }

    [Fact]
    public void NewerTagBeatsOlderCurrent()
    {
        var latest = UpdateCheckService.ParseVersion("v0.1.5-preview");
        var current = UpdateCheckService.ParseVersion("0.1.4-preview+hash");
        Assert.NotNull(latest);
        Assert.NotNull(current);
        Assert.True(latest > current);
    }

    [Fact]
    public void SameNumericVersionIsNotAnUpdate()
    {
        var latest = UpdateCheckService.ParseVersion("v0.1.4");
        var current = UpdateCheckService.ParseVersion("0.1.4-preview+hash");
        Assert.Equal(latest, current);
        Assert.False(latest > current);
    }
}
