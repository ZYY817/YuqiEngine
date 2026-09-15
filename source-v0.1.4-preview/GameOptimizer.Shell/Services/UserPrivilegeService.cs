using System.ComponentModel;
using GameOptimizer.Core.Services;

namespace GameOptimizer.Shell.Services;

/// <summary>
/// 提权状态的 UI 门面。判定本身由 <see cref="WindowsElevation"/> 完成，
/// 这里只负责在刷新时把变化广播给绑定方。
/// </summary>
public sealed class UserPrivilegeService : IUserPrivilegeService, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public UserPrivilegeService()
    {
        // Why: 优先走带缓存的 Current，避免与全仓 WindowsElevation 探测结果分叉。
        Apply(WindowsElevation.Current);
    }

    private bool _isAdministrator;
    private bool _isAdministratorAccount;
    private ElevationLevel _elevationLevel = ElevationLevel.Unknown;
    private string _elevationDetail = string.Empty;
    private string _elevationReasonCode = string.Empty;

    public bool IsAdministrator
    {
        get => _isAdministrator;
        private set
        {
            if (_isAdministrator != value)
            {
                _isAdministrator = value;
                Raise(nameof(IsAdministrator));
            }
        }
    }

    public bool IsAdministratorAccount
    {
        get => _isAdministratorAccount;
        private set
        {
            if (_isAdministratorAccount != value)
            {
                _isAdministratorAccount = value;
                Raise(nameof(IsAdministratorAccount));
            }
        }
    }

    public ElevationLevel ElevationLevel
    {
        get => _elevationLevel;
        private set
        {
            if (_elevationLevel != value)
            {
                _elevationLevel = value;
                Raise(nameof(ElevationLevel));
            }
        }
    }

    public string ElevationDetail
    {
        get => _elevationDetail;
        private set
        {
            if (!string.Equals(_elevationDetail, value, StringComparison.Ordinal))
            {
                _elevationDetail = value;
                Raise(nameof(ElevationDetail));
            }
        }
    }

    public string ElevationReasonCode
    {
        get => _elevationReasonCode;
        private set
        {
            if (!string.Equals(_elevationReasonCode, value, StringComparison.Ordinal))
            {
                _elevationReasonCode = value;
                Raise(nameof(ElevationReasonCode));
            }
        }
    }

    public void Refresh()
    {
        // Why: Probe 不读缓存，用于诊断/启动后复核；成功结果会再写入 Current 缓存。
        Apply(WindowsElevation.Probe());
    }

    /// <summary>
    /// 启动后或 UI 怀疑提权状态过期时调用。令牌生命周期内通常不变，
    /// 但首次 Probe 失败后必须允许 UI 再读一次权威判定。
    /// </summary>
    public void EnsureAuthoritativeSnapshot()
    {
        Apply(WindowsElevation.Current);
    }

    private void Apply(ElevationSnapshot snapshot)
    {
        ElevationLevel = snapshot.Level;
        IsAdministrator = snapshot.IsElevated;
        IsAdministratorAccount = snapshot.IsAdministratorAccount;
        ElevationDetail = snapshot.Detail;
        ElevationReasonCode = snapshot.ReasonCode;
    }

    private void Raise(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
