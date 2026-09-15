// File: GameOptimizer.Core/Localization/LocalizationService.cs
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace GameOptimizer.Core.Localization;

public sealed partial class LocalizationService : ILocalizationService
{
    private static readonly Lazy<LocalizationService> _instance = new(() => new LocalizationService());
    public static LocalizationService Instance => _instance.Value;

    private readonly Dictionary<string, Dictionary<string, string>> _resources = new();
    private string _currentLanguage = "zh-CN";

    public string CurrentLanguage => _currentLanguage;

    public IReadOnlyList<LanguageInfo> AvailableLanguages { get; } = new List<LanguageInfo>
    {
        new("zh-CN", "简体中文", "Chinese (Simplified)"),
        new("en-US", "English", "English (US)")
    };

    public event EventHandler<string>? LanguageChanged;

    private LocalizationService()
    {
        LoadBuiltInResources();
        
        var savedLang = LoadSavedLanguage();
        if (!string.IsNullOrEmpty(savedLang) && (savedLang == "zh-CN" || savedLang == "en-US"))
        {
            _currentLanguage = savedLang;
        }
        else
        {
            var systemLang = CultureInfo.CurrentUICulture.Name;
            _currentLanguage = systemLang.StartsWith("zh") ? "zh-CN" : "en-US";
        }
    }
    
    private static string? LoadSavedLanguage()
    {
        // Why: 优先从当前路径(YuqiEngine)读取，回退到旧路径(GameOptimizer)兼容未迁移用户
        var paths = new[]
        {
            Constants.AppPaths.SettingsFilePath,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameOptimizer", "settings.json")
        };

        foreach (var settingsPath in paths)
        {
            try
            {
                if (!File.Exists(settingsPath)) continue;
                var json = File.ReadAllText(settingsPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("Language", out var langProp))
                {
                    var lang = langProp.GetString();
                    if (!string.IsNullOrEmpty(lang)) return lang;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LocalizationService] Failed to load language from {settingsPath}: {ex.Message}");
            }
        }
        return null;
    }

    public string Get(string key, params object[] args)
    {
        if (_resources.TryGetValue(_currentLanguage, out var langDict) && langDict.TryGetValue(key, out var value))
        {
            return SafeFormat(value, args);
        }
        
        if (_currentLanguage != "en-US" && _resources.TryGetValue("en-US", out var enDict) && enDict.TryGetValue(key, out var enValue))
        {
            System.Diagnostics.Debug.WriteLine($"[LocalizationService] Key '{key}' not found in {_currentLanguage}, using en-US fallback");
            return SafeFormat(enValue, args);
        }

        System.Diagnostics.Debug.WriteLine($"[LocalizationService] CRITICAL: Key '{key}' not found in any language! Current: {_currentLanguage}, Available keys in {_currentLanguage}: {(_resources.ContainsKey(_currentLanguage) ? _resources[_currentLanguage].Count : 0)}");
        return key;
    }

    public string GetOrDefault(string key, string defaultValue, params object[] args)
    {
        if (_resources.TryGetValue(_currentLanguage, out var langDict) && langDict.TryGetValue(key, out var value))
        {
            return SafeFormat(value, args);
        }
        return SafeFormat(defaultValue, args);
    }

    private static string SafeFormat(string format, object[] args)
    {
        if (args.Length == 0) return format;
        try
        {
            return string.Format(format, args);
        }
        catch (FormatException)
        {
            return format;
        }
    }

    public void SetLanguage(string languageCode)
    {
        if (_currentLanguage == languageCode) return;
        if (!AvailableLanguages.Any(l => l.Code == languageCode)) return;

        _currentLanguage = languageCode;
        LanguageChanged?.Invoke(this, languageCode);
    }

    public void LoadFromJson(string languageCode, string json)
    {
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (dict != null)
            {
                _resources[languageCode] = dict;
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LocalizationService] Failed to load JSON: {ex.Message}"); }
    }

    private void LoadBuiltInResources()
    {
        System.Diagnostics.Debug.WriteLine("[LocalizationService] Loading built-in resources...");
        _resources["zh-CN"] = LoadLanguageResources("zh-CN");
        _resources["en-US"] = LoadLanguageResources("en-US");
        System.Diagnostics.Debug.WriteLine($"[LocalizationService] zh-CN: {_resources["zh-CN"].Count} keys loaded");
        System.Diagnostics.Debug.WriteLine($"[LocalizationService] en-US: {_resources["en-US"].Count} keys loaded");
    }

    private Dictionary<string, string> LoadLanguageResources(string languageCode)
    {
        var result = new Dictionary<string, string>();
        
        var hardcodedResources = languageCode switch
        {
            "zh-CN" => GetChineseResources(),
            "en-US" => GetEnglishResources(),
            _ => new Dictionary<string, string>()
        };
        
        // JSON is the legacy/fallback catalog. Some historical zh-CN JSON entries
        // still contain English text, so authoritative built-in translations must
        // win when both catalogs define the same key.
        var jsonResources = LoadFromEmbeddedJson(languageCode);
        if (jsonResources != null)
        {
            foreach (var kvp in jsonResources)
            {
                result[kvp.Key] = kvp.Value;
            }
        }

        foreach (var kvp in hardcodedResources)
        {
            result[kvp.Key] = kvp.Value;
        }
        
        return result;
    }

    private Dictionary<string, string>? LoadFromEmbeddedJson(string languageCode)
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = $"GameOptimizer.Core.Localization.Strings.{languageCode}.json";
            
            System.Diagnostics.Debug.WriteLine($"[LocalizationService] Attempting to load embedded resource: {resourceName}");
            
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                System.Diagnostics.Debug.WriteLine($"[LocalizationService] ERROR: Embedded resource not found: {resourceName}");
                System.Diagnostics.Debug.WriteLine($"[LocalizationService] Available resources:");
                foreach (var name in assembly.GetManifestResourceNames())
                {
                    System.Diagnostics.Debug.WriteLine($"  - {name}");
                }
                return null;
            }
            
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            
            if (string.IsNullOrWhiteSpace(json))
            {
                System.Diagnostics.Debug.WriteLine($"[LocalizationService] ERROR: Embedded resource is empty: {resourceName}");
                return null;
            }
            
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (dict == null || dict.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine($"[LocalizationService] ERROR: Failed to deserialize or empty dictionary: {resourceName}");
                return null;
            }
            
            System.Diagnostics.Debug.WriteLine($"[LocalizationService] SUCCESS: Loaded {dict.Count} strings from {resourceName}");
            return dict;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LocalizationService] EXCEPTION loading embedded JSON for {languageCode}: {ex.GetType().Name} - {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[LocalizationService] Stack trace: {ex.StackTrace}");
            return null;
        }
    }
}
