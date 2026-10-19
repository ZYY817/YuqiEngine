// File: GameOptimizer.Shell/Services/SoundNotificationService.cs
using System.Diagnostics;
using System.Media;
using GameOptimizer.Shell.Models;

namespace GameOptimizer.Shell.Services;

public interface ISoundNotificationService
{
    void PlaySuccess();
    void PlayError();
    void PlayWarning();
    void PlayNotification();
}

public sealed class SoundNotificationService : ISoundNotificationService
{
    private readonly IAppSettingsStore _settings;

    public SoundNotificationService(IAppSettingsStore settings) => _settings = settings;

    private bool IsEnabled => _settings.Current.EnableSoundNotification;

    public void PlaySuccess()
    {
        if (!IsEnabled) return;
        try { SystemSounds.Asterisk.Play(); }
        catch (Exception ex) { Debug.WriteLine($"[SoundNotification] PlaySuccess error: {ex.Message}"); }
    }

    public void PlayError()
    {
        if (!IsEnabled) return;
        try { SystemSounds.Hand.Play(); }
        catch (Exception ex) { Debug.WriteLine($"[SoundNotification] PlayError error: {ex.Message}"); }
    }

    public void PlayWarning()
    {
        if (!IsEnabled) return;
        try { SystemSounds.Exclamation.Play(); }
        catch (Exception ex) { Debug.WriteLine($"[SoundNotification] PlayWarning error: {ex.Message}"); }
    }

    public void PlayNotification()
    {
        if (!IsEnabled) return;
        try { SystemSounds.Beep.Play(); }
        catch (Exception ex) { Debug.WriteLine($"[SoundNotification] PlayNotification error: {ex.Message}"); }
    }
}
