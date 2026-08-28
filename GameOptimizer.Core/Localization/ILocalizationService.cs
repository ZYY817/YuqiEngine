namespace GameOptimizer.Core.Localization;

public interface ILocalizationService
{
    string CurrentLanguage { get; }
    IReadOnlyList<LanguageInfo> AvailableLanguages { get; }
    
    string Get(string key, params object[] args);
    string GetOrDefault(string key, string defaultValue, params object[] args);
    
    void SetLanguage(string languageCode);
    event EventHandler<string>? LanguageChanged;
}

public record LanguageInfo(string Code, string NativeName, string EnglishName);
