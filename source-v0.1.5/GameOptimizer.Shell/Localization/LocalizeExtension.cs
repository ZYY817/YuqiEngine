using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using GameOptimizer.Core.Localization;

namespace GameOptimizer.Shell.Localization;

[MarkupExtensionReturnType(typeof(string))]
public class LocExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;
    
    public LocExtension() { }
    
    public LocExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrEmpty(Key)) return string.Empty;
        
        var binding = new Binding("Value")
        {
            Source = new LocalizedString(Key),
            Mode = BindingMode.OneWay
        };
        
        return binding.ProvideValue(serviceProvider);
    }
}

// Why: 用 WeakReference 静态列表避免 LocalizationService 强引用导致的内存泄漏。
// EventHandler<string> 不满足 WeakEventManager 的 EventArgs 约束，所以手动实现弱订阅。
public class LocalizedString : INotifyPropertyChanged
{
    private static readonly List<WeakReference<LocalizedString>> _instances = new();
    private static bool _subscribed;

    private readonly string _key;

    public LocalizedString(string key)
    {
        _key = key;
        EnsureSubscribed();
        lock (_instances)
        {
            _instances.Add(new WeakReference<LocalizedString>(this));
        }
    }

    public string Value => LocalizationService.Instance.Get(_key);

    public event PropertyChangedEventHandler? PropertyChanged;

    private static void EnsureSubscribed()
    {
        if (_subscribed) return;
        _subscribed = true;
        LocalizationService.Instance.LanguageChanged += OnLanguageChangedRelay;
    }

    // Why: 静态方法持有弱引用，不会阻止 GC 回收 LocalizedString 实例
    private static void OnLanguageChangedRelay(object? sender, string e)
    {
        lock (_instances)
        {
            _instances.RemoveAll(wr => !wr.TryGetTarget(out _));
            foreach (var wr in _instances)
            {
                if (wr.TryGetTarget(out var target))
                {
                    target.PropertyChanged?.Invoke(target, new PropertyChangedEventArgs(nameof(Value)));
                }
            }
        }
    }
}
