using System.Text.Json;

namespace GameOptimizer.Infrastructure.Windows.PowerManagement;

public sealed record ManagedPowerPlanStoreState(
    int SchemaVersion,
    IReadOnlyList<ManagedPowerPlanRecord> Plans,
    IReadOnlyList<ManagedPowerPlanBackup> Backups)
{
    public static ManagedPowerPlanStoreState Empty { get; } = new(1, [], []);
}

public interface IManagedPowerPlanStore
{
    string BackupDirectory { get; }
    Task<ManagedPowerPlanStoreState> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(ManagedPowerPlanStoreState state, CancellationToken cancellationToken);
}

public sealed class FileManagedPowerPlanStore : IManagedPowerPlanStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _statePath;
    private readonly IPowerDataProtector _protector;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileManagedPowerPlanStore(string recoveryRoot, IPowerDataProtector protector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recoveryRoot);
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        var root = Path.GetFullPath(Path.Combine(recoveryRoot, "managed-plans"));
        BackupDirectory = Path.Combine(root, "backups");
        _statePath = Path.Combine(root, "state.protected");
    }

    public string BackupDirectory { get; }

    public async Task<ManagedPowerPlanStoreState> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_statePath))
                return ManagedPowerPlanStoreState.Empty;
            var protectedBytes = await File.ReadAllBytesAsync(_statePath, cancellationToken).ConfigureAwait(false);
            var plaintext = _protector.Unprotect(protectedBytes);
            var state = JsonSerializer.Deserialize<ManagedPowerPlanStoreState>(plaintext, JsonOptions);
            if (state is null || state.SchemaVersion != 1)
                throw new InvalidDataException("受管电源计划状态文件版本无效。");
            return state;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(ManagedPowerPlanStoreState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(_statePath)!;
            Directory.CreateDirectory(directory);
            Directory.CreateDirectory(BackupDirectory);
            var temp = Path.Combine(directory, $"state.{Guid.NewGuid():N}.tmp");
            try
            {
                var plaintext = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);
                var protectedBytes = _protector.Protect(plaintext);
                await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
                                 FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await stream.WriteAsync(protectedBytes, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                File.Move(temp, _statePath, true);
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
