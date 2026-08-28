// File: GameOptimizer.Shell/ViewModels/ProcessPriorityViewModel.cs
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Services.ProcessPriority;
using GameOptimizer.Shell.Models;
using GameOptimizer.Shell.Services;

namespace GameOptimizer.Shell.ViewModels;

public class ProcessPriorityViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly IRegistryPriorityService _registryService;
    private readonly ProcessPriorityConfigManager _configManager;
    private readonly LocalizationService _loc;

    private ObservableCollection<ProcessPriorityConfigDisplayModel> _configs = new();
    private int _totalCount;
    private int _enabledCount;
    private string _lastModifiedText = string.Empty;
    private bool _isEmpty = true;

    public ObservableCollection<ProcessPriorityConfigDisplayModel> Configs
    {
        get => _configs;
        set
        {
            _configs = value;
            OnPropertyChanged(nameof(Configs));
        }
    }

    public int TotalCount
    {
        get => _totalCount;
        set
        {
            _totalCount = value;
            OnPropertyChanged(nameof(TotalCount));
        }
    }

    public int EnabledCount
    {
        get => _enabledCount;
        set
        {
            _enabledCount = value;
            OnPropertyChanged(nameof(EnabledCount));
        }
    }

    public string LastModifiedText
    {
        get => _lastModifiedText;
        set
        {
            _lastModifiedText = value;
            OnPropertyChanged(nameof(LastModifiedText));
        }
    }

    public bool IsEmpty
    {
        get => _isEmpty;
        set
        {
            _isEmpty = value;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public ProcessPriorityViewModel(IRegistryPriorityService registryService)
    {
        _registryService = registryService;
        _configManager = ProcessPriorityConfigManager.Instance;
        _loc = LocalizationService.Instance;
    }

    public void LoadConfigs()
    {
        _configManager.Reload();
        
        Configs.Clear();
        foreach (var config in _configManager.Configs)
        {
            Configs.Add(new ProcessPriorityConfigDisplayModel(config));
        }

        UpdateStats();
        IsEmpty = Configs.Count == 0;
    }

    public void AddConfig(ProcessPriorityConfig config)
    {
        _configManager.Add(config);
        
        if (config.IsEnabled)
        {
            var result = _registryService.SetProcessPriority(config.ExecutableName, config.Priority);
            if (!result.IsSuccess)
            {
                ToastService.Instance.Warning(
                    _loc.Get("Common.Warning"),
                    result.Message);
            }
        }
        
        LoadConfigs();
    }

    public void UpdateConfig(ProcessPriorityConfig config)
    {
        _configManager.Update(config);
        
        if (config.IsEnabled)
        {
            _registryService.SetProcessPriority(config.ExecutableName, config.Priority);
        }
        else
        {
            _registryService.RemoveProcessPriority(config.ExecutableName);
        }
        
        LoadConfigs();
    }

    public void RemoveConfig(string configId)
    {
        var config = _configManager.FindById(configId);
        if (config != null)
        {
            _registryService.RemoveProcessPriority(config.ExecutableName);
            _configManager.Remove(configId);
            LoadConfigs();
        }
    }

    public void ToggleEnabled(string configId, bool isEnabled)
    {
        var config = _configManager.FindById(configId);
        if (config == null) return;

        config.IsEnabled = isEnabled;
        
        if (isEnabled)
        {
            var result = _registryService.SetProcessPriority(config.ExecutableName, config.Priority);
            if (!result.IsSuccess)
            {
                ToastService.Instance.Error(
                    _loc.Get("Common.Error"),
                    result.Message);
                config.IsEnabled = false;
            }
        }
        else
        {
            _registryService.RemoveProcessPriority(config.ExecutableName);
        }
        
        _configManager.Update(config);
        LoadConfigs();
    }

    private void UpdateStats()
    {
        TotalCount = _configManager.GetTotalCount();
        EnabledCount = _configManager.GetEnabledCount();
        
        var lastModified = _configManager.GetLastModifiedDate();
        if (lastModified.HasValue)
        {
            var timeSpan = DateTime.Now - lastModified.Value;
            LastModifiedText = timeSpan.TotalMinutes < 1 
                ? _loc.Get("Common.JustNow")
                : timeSpan.TotalHours < 1 
                    ? $"{(int)timeSpan.TotalMinutes} {_loc.Get("Common.MinutesAgo")}"
                    : timeSpan.TotalDays < 1 
                        ? $"{(int)timeSpan.TotalHours} {_loc.Get("Common.HoursAgo")}"
                        : lastModified.Value.ToString("yyyy-MM-dd HH:mm");
        }
        else
        {
            LastModifiedText = _loc.Get("Common.Never");
        }
    }

    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class ProcessPriorityConfigDisplayModel : INotifyPropertyChanged
{
#pragma warning disable CS0067
    public event PropertyChangedEventHandler? PropertyChanged;
#pragma warning restore CS0067

    private readonly ProcessPriorityConfig _config;

    public string Id => _config.Id;
    public string GameName => _config.GameName;
    public string ExecutableName => _config.ExecutableName;
    public string ExecutablePath => _config.ExecutablePath;
    public bool IsEnabled => _config.IsEnabled;
    public CpuPriorityLevel Priority => _config.Priority;
    
    public string PriorityDisplay
    {
        get
        {
            var loc = LocalizationService.Instance;
            return _config.Priority switch
            {
                CpuPriorityLevel.Idle => loc.Get("ProcessPriority.PriorityIdle"),
                CpuPriorityLevel.BelowNormal => loc.Get("ProcessPriority.PriorityBelowNormal"),
                CpuPriorityLevel.Normal => loc.Get("ProcessPriority.PriorityNormal"),
                CpuPriorityLevel.AboveNormal => loc.Get("ProcessPriority.PriorityAboveNormal"),
                CpuPriorityLevel.High => loc.Get("ProcessPriority.PriorityHigh"),
                CpuPriorityLevel.Realtime => loc.Get("ProcessPriority.PriorityRealtime"),
                _ => "Unknown"
            };
        }
    }

    public ProcessPriorityConfigDisplayModel(ProcessPriorityConfig config)
    {
        _config = config;
    }
}
