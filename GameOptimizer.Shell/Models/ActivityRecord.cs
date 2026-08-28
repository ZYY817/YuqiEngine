using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using GameOptimizer.Core.Localization;

namespace GameOptimizer.Shell.Models;

public sealed class ActivityRecord : INotifyPropertyChanged
{
    public ActivityRecord(DateTime timestamp, string categoryKey, string messageKey, object[]? messageArgs = null)
    {
        Timestamp = timestamp;
        CategoryKey = categoryKey;
        MessageKey = messageKey;
        MessageArgs = messageArgs;
    }

    public DateTime Timestamp { get; }
    public string CategoryKey { get; }
    public string MessageKey { get; }
    public object[]? MessageArgs { get; }

    public string Category => LocalizationService.Instance.Get(CategoryKey);
    public string Message
    {
        get
        {
            if (string.IsNullOrEmpty(MessageKey)) return "";
            
            // 特殊处理Log.PolicyResult：参数中包含需要翻译的key
            if (MessageKey == "Log.PolicyResult" && MessageArgs is { Length: 5 })
            {
                var loc = LocalizationService.Instance;
                var moduleTitle = OptimizationTranslations.Instance.GetModuleName(MessageArgs[0]?.ToString() ?? "");
                var operationId = MessageArgs[1]?.ToString() ?? "";
                var title = OptimizationTranslations.Instance.GetTitle(operationId) ?? operationId;
                var status = loc.Get(MessageArgs[2]?.ToString() ?? "");
                var change = loc.Get(MessageArgs[3]?.ToString() ?? "");
                var msg = MessageArgs[4]?.ToString() ?? "";
                return $"[{moduleTitle}] {status} · {title} · {change} · {msg}";
            }
            
            var result = MessageArgs != null 
                ? LocalizationService.Instance.Get(MessageKey, MessageArgs) 
                : LocalizationService.Instance.Get(MessageKey);
            return result == MessageKey ? MessageKey : result;
        }
    }
    public string Display => $"{Timestamp:HH:mm} [{Category}] {Message}";

    public void NotifyLanguageChanged()
    {
        OnPropertyChanged(nameof(Category));
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(Display));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
