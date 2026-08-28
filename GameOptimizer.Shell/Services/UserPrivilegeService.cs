using System.ComponentModel;
using System.Security.Principal;

namespace GameOptimizer.Shell.Services;

public sealed class UserPrivilegeService : IUserPrivilegeService, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public UserPrivilegeService()
    {
        Refresh();
    }

    private bool _isAdministrator;
    public bool IsAdministrator
    {
        get => _isAdministrator;
        private set
        {
            if (_isAdministrator != value)
            {
                _isAdministrator = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAdministrator)));
            }
        }
    }

    public void Refresh()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        IsAdministrator = principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
