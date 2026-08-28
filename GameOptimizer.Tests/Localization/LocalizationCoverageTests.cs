using GameOptimizer.Core.Localization;

namespace GameOptimizer.Tests.Localization;

public sealed class LocalizationCoverageTests
{
    [Fact]
    public void ChineseBuiltInCatalogWinsOverLegacyEnglishJsonFallback()
    {
        var localization = LocalizationService.Instance;
        var previous = localization.CurrentLanguage;
        try
        {
            localization.SetLanguage("zh-CN");
            Assert.Equal("确认", localization.Get("Common.Confirm"));
            Assert.Equal("全局配置", localization.Get("Settings.Title"));

            localization.SetLanguage("en-US");
            Assert.Equal("Confirm", localization.Get("Common.Confirm"));
            Assert.Equal("Settings", localization.Get("Settings.Title"));
        }
        finally
        {
            localization.SetLanguage(previous);
        }
    }
}
