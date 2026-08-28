using Microsoft.Win32;

namespace GameOptimizer.Core.Services.Startup;

/// <summary>
/// Read-only startup inventory. All mutation methods fail closed until the typed StartupEntry owner
/// is composed with confirmation, baseline, durable store and a platform adapter.
/// </summary>
public sealed class StartupService : IStartupService
{
    private static readonly string[] RegistryRunKeys =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"
    ];

    private readonly List<StartupItem> _cachedItems = new();

    public async Task<IReadOnlyList<StartupItem>> GetStartupItemsAsync()
    {
        await RefreshAsync().ConfigureAwait(false);
        return _cachedItems.AsReadOnly();
    }

    public Task<IReadOnlyList<StartupItem>> GetStartupItemsByTypeAsync(StartupItemType type) =>
        Task.FromResult<IReadOnlyList<StartupItem>>(_cachedItems.Where(item => item.Type == type).ToArray());

    public Task<StartupOperationResult> EnableItemAsync(string itemId) => Block(itemId);
    public Task<StartupOperationResult> DisableItemAsync(string itemId) => Block(itemId);
    public Task<StartupOperationResult> DeleteItemAsync(string itemId) => Block(itemId);

    public Task<StartupOperationResult> AddItemAsync(string name, string path, string arguments = "") =>
        Task.FromResult(new StartupOperationResult
        {
            Success = false,
            Message = "Startup entry creation requires the typed StartupEntry owner, explicit confirmation and durable recovery."
        });

    public Task RefreshAsync()
    {
        _cachedItems.Clear();
        ScanRegistry(Registry.CurrentUser, "HKEY_CURRENT_USER");
        ScanRegistry(Registry.LocalMachine, "HKEY_LOCAL_MACHINE");
        ScanFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup));
        ScanFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup));
        return Task.CompletedTask;
    }

    private void ScanRegistry(RegistryKey root, string rootName)
    {
        foreach (var keyPath in RegistryRunKeys)
        {
            try
            {
                using var key = root.OpenSubKey(keyPath, writable: false);
                if (key is null) continue;
                foreach (var valueName in key.GetValueNames())
                {
                    var command = key.GetValue(valueName)?.ToString() ?? string.Empty;
                    var (path, arguments) = ParseCommand(command);
                    _cachedItems.Add(new StartupItem
                    {
                        Name = valueName,
                        Path = path,
                        Arguments = arguments,
                        Type = StartupItemType.Registry,
                        IsEnabled = true,
                        RegistryKey = $@"{rootName}\{keyPath}",
                        RegistryValue = valueName,
                        Impact = EstimateImpact(path)
                    });
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
    }

    private void ScanFolder(string folder)
    {
        try
        {
            if (!Directory.Exists(folder)) return;
            foreach (var file in Directory.EnumerateFiles(folder, "*.lnk", SearchOption.TopDirectoryOnly))
                _cachedItems.Add(new StartupItem
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    Path = file,
                    Type = StartupItemType.StartupFolder,
                    IsEnabled = true,
                    Impact = StartupImpact.Medium
                });
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    private Task<StartupOperationResult> Block(string itemId) => Task.FromResult(new StartupOperationResult
    {
        Success = false,
        Message = _cachedItems.Any(item => item.Id == itemId)
            ? "Startup mutation requires the typed StartupEntry owner, explicit confirmation and durable recovery."
            : "Startup entry does not exist."
    });

    private static (string Path, string Arguments) ParseCommand(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            if (end > 0) return (command[1..end], command[(end + 1)..].Trim());
        }
        var split = command.IndexOf(' ');
        return split > 0 ? (command[..split], command[(split + 1)..]) : (command, string.Empty);
    }

    private static StartupImpact EstimateImpact(string path)
    {
        var fileName = Path.GetFileName(path);
        if (fileName.Contains("security", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("defender", StringComparison.OrdinalIgnoreCase)) return StartupImpact.High;
        if (fileName.Contains("update", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("sync", StringComparison.OrdinalIgnoreCase)) return StartupImpact.Medium;
        return string.IsNullOrWhiteSpace(fileName) ? StartupImpact.Unknown : StartupImpact.Low;
    }
}
