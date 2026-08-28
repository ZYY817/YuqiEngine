using System.Text.Json;
using System.Text.Json.Serialization;
using GameOptimizer.Shell.Models;

namespace GameOptimizer.Shell.Services;

public sealed record AppSettingsChangedEventArgs(AppSettings Previous, AppSettings Current);

public interface IAppSettingsStore
{
    AppSettings Current { get; }
    event EventHandler<AppSettingsChangedEventArgs>? Changed;
    bool TryUpdate(Action<AppSettings> update);
}

public sealed class AppSettingsStore : IAppSettingsStore
{
    private static readonly JsonSerializerOptions CloneOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };
    private readonly object _gate = new();
    private readonly Func<AppSettings, bool> _save;
    private AppSettings _current;

    public AppSettingsStore() : this(AppSettings.Load(), settings => settings.Save()) { }

    public AppSettingsStore(AppSettings initial, Func<AppSettings, bool> save)
    {
        _current = Clone(initial ?? throw new ArgumentNullException(nameof(initial)));
        _save = save ?? throw new ArgumentNullException(nameof(save));
    }

    public AppSettings Current
    {
        get { lock (_gate) return _current; }
    }

    public event EventHandler<AppSettingsChangedEventArgs>? Changed;

    public bool TryUpdate(Action<AppSettings> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        AppSettings previous;
        AppSettings candidate;
        lock (_gate)
        {
            previous = _current;
            candidate = Clone(previous);
            update(candidate);
            if (!_save(candidate)) return false;
            _current = candidate;
        }

        Changed?.Invoke(this, new AppSettingsChangedEventArgs(previous, candidate));
        return true;
    }

    private static AppSettings Clone(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, CloneOptions);
        return JsonSerializer.Deserialize<AppSettings>(json, CloneOptions)
            ?? throw new InvalidOperationException("App settings could not be cloned.");
    }
}
