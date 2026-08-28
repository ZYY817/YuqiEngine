// File: GameOptimizer.Shell/Models/PeripheralAdvancedSettings.cs
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GameOptimizer.Shell.Models;

public class PeripheralAdvancedSettings : INotifyPropertyChanged
{
    private int _mouseSpeed = 0;
    private int _mouseThreshold1 = 0;
    private int _mouseThreshold2 = 0;
    private string _keyboardDelay = "0";
    private string _keyboardSpeed = "31";
    private int _mouseDataQueueSize = 200;
    private int _keyboardDataQueueSize = 200;

    public int MouseSpeed
    {
        get => _mouseSpeed;
        set { _mouseSpeed = value; OnPropertyChanged(); }
    }

    public int MouseThreshold1
    {
        get => _mouseThreshold1;
        set { _mouseThreshold1 = value; OnPropertyChanged(); }
    }

    public int MouseThreshold2
    {
        get => _mouseThreshold2;
        set { _mouseThreshold2 = value; OnPropertyChanged(); }
    }

    public string KeyboardDelay
    {
        get => _keyboardDelay;
        set { _keyboardDelay = value; OnPropertyChanged(); }
    }

    public string KeyboardSpeed
    {
        get => _keyboardSpeed;
        set { _keyboardSpeed = value; OnPropertyChanged(); }
    }

    public int MouseDataQueueSize
    {
        get => _mouseDataQueueSize;
        set { _mouseDataQueueSize = value; OnPropertyChanged(); }
    }

    public int KeyboardDataQueueSize
    {
        get => _keyboardDataQueueSize;
        set { _keyboardDataQueueSize = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public static PeripheralAdvancedSettings GetDefault() => new()
    {
        MouseSpeed = 0,
        MouseThreshold1 = 0,
        MouseThreshold2 = 0,
        KeyboardDelay = "0",
        KeyboardSpeed = "31",
        MouseDataQueueSize = 200,
        KeyboardDataQueueSize = 200
    };

    public static PeripheralAdvancedSettings GetWindowsDefault() => new()
    {
        MouseSpeed = 1,
        MouseThreshold1 = 6,
        MouseThreshold2 = 10,
        KeyboardDelay = "1",
        KeyboardSpeed = "31",
        MouseDataQueueSize = 100,
        KeyboardDataQueueSize = 100
    };
}
