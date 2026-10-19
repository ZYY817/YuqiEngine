using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Security;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using GameOptimizer.Core.Constants;
using GameOptimizer.Core.Services;
using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Optimization.Providers;

namespace GameOptimizer.Core.Services.Snapshot;

/// <summary>
/// Builds a self-contained, environment-bound user configuration package.
/// Capture is broad, while automatic restore remains limited to explicit owner contracts.
/// </summary>
public sealed class ComprehensiveBackupService : IComprehensiveBackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ISnapshotService _snapshotService;
    private readonly IUserRegistryExportBackend _registryBackend;
    private readonly string _backupDirectory;
    private readonly IOptimizationOperationProvider? _operationProvider;
    private readonly IOperationRestoreStore? _operationRestoreStore;

    public ComprehensiveBackupService(
        ISnapshotService snapshotService,
        IUserRegistryExportBackend? registryBackend = null,
        string? backupDirectory = null,
        IOptimizationOperationProvider? operationProvider = null,
        IOperationRestoreStore? operationRestoreStore = null)
    {
        _snapshotService = snapshotService ?? throw new ArgumentNullException(nameof(snapshotService));
        _registryBackend = registryBackend ?? new RegExeUserRegistryExportBackend();
        _backupDirectory = backupDirectory ?? AppPaths.ComprehensiveBackupsDir;
        _operationProvider = operationProvider;
        _operationRestoreStore = operationRestoreStore;
    }

    public async Task<ComprehensiveBackupPackageInfo> CreatePackageAsync(
        string name,
        IProgress<ComprehensiveBackupProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("备份名称不能为空。", nameof(name));

        var packageId = $"{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}";
        var safeName = SanitizeName(name);
        var packageDirectory = Path.Combine(_backupDirectory, $"{safeName}_{packageId}");
        var stagingDirectory = packageDirectory + ".staging";
        Directory.CreateDirectory(_backupDirectory);
        Directory.CreateDirectory(stagingDirectory);

        var artifacts = new List<ComprehensiveBackupArtifact>();
        try
        {
            progress?.Report(new("准备", 0, 3, "准备完整用户配置备份包"));

            progress?.Report(new("注册表", 1, 3, "导出当前用户注册表（仅生成备份文件，不执行导入）"));
            artifacts.Add(await CaptureUserRegistryAsync(stagingDirectory, ct).ConfigureAwait(false));

            progress?.Report(new("应用配置", 2, 3, "采集应用配置文件并校验哈希"));
            artifacts.AddRange(await CaptureApplicationConfigurationAsync(stagingDirectory, ct).ConfigureAwait(false));

            progress?.Report(new("typed 快照", 3, 3, "采集已有恢复契约的设置快照"));
            var snapshot = await _snapshotService.CreateSnapshotAsync($"{name}（typed）", ct: ct).ConfigureAwait(false);
            var typedRelativePath = Path.Combine("payload", "typed-snapshot.json");
            var typedPath = Path.Combine(stagingDirectory, typedRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(typedPath)!);
            await using (var stream = File.Create(typedPath))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, ct).ConfigureAwait(false);
            }
            artifacts.Add(CreateArtifact(
                ComprehensiveBackupArtifactKind.TypedSnapshot,
                typedRelativePath,
                typedPath,
                ComprehensiveBackupArtifactStatus.Captured,
                automaticRestoreSupported: true,
                "已有恢复契约的 typed 设置快照"));

            var coverage = await _snapshotService.GetCoverageReportAsync(ct).ConfigureAwait(false);
            var operationStates = await CaptureOperationStatesAsync(coverage, progress, ct).ConfigureAwait(false);
            var recoveryMaterial = await CaptureOperationRecoveryMaterialAsync(stagingDirectory, operationStates, progress, ct).ConfigureAwait(false);
            if (recoveryMaterial is not null)
                artifacts.Add(recoveryMaterial);
            var incomplete = artifacts.Where(item => item.Status is ComprehensiveBackupArtifactStatus.Failed or ComprehensiveBackupArtifactStatus.Partial).ToArray();
            // typed 覆盖缺口是设计边界（仅部分操作有 typed 恢复契约），不是采集失败；
            // 未覆盖操作仍由 HKCU 导出、声明式注册表状态和恢复材料承载，不计入未完成。
            var failedProbeIds = operationStates
                .Where(item => item.IsCompliant is null)
                .Select(item => item.OperationId)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var registryCaptureFailures = operationStates.Count(item =>
                item.CaptureStatus is "PermissionDenied" or "UnsupportedHive" or "ReadFailed");
            var complete = incomplete.Length == 0
                && failedProbeIds.Length == 0
                && registryCaptureFailures == 0;
            var manifest = new ComprehensiveBackupManifest(
                FormatVersion: 1,
                PackageId: packageId,
                CreatedUtc: DateTimeOffset.UtcNow,
                UserSidSha256: HashIdentity(GetCurrentUserSid()),
                MachineIdentitySha256: HashIdentity(Environment.MachineName),
                WindowsVersion: WindowsVersionService.GetFriendlyName(),
                WindowsBuild: WindowsVersionService.Current.BuildNumber,
                Architecture: RuntimeInformation.ProcessArchitecture.ToString(),
                ProcessorCount: Environment.ProcessorCount,
                Artifacts: artifacts,
                TypedCoverage: coverage,
                OperationStates: operationStates,
                IsComplete: complete,
                IncompleteReason: complete
                    ? null
                    : string.Join("；", new[]
                    {
                        incomplete.Length == 0 ? null : $"{incomplete.Length} 个备份资产未能完成采集",
                        failedProbeIds.Length == 0 ? null : $"{failedProbeIds.Length} 个操作状态探测失败（{string.Join("、", failedProbeIds.Take(3))}{(failedProbeIds.Length > 3 ? " 等" : "")}）",
                        registryCaptureFailures == 0 ? null : $"{registryCaptureFailures} 个声明式注册表目标读取失败"
                    }.Where(item => item != null)) + "。");

            var manifestPath = Path.Combine(stagingDirectory, "manifest.json");
            await using (var stream = File.Create(manifestPath))
            {
                await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, ct).ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();
            if (Directory.Exists(packageDirectory))
                throw new IOException("备份包目录已存在。");
            Directory.Move(stagingDirectory, packageDirectory);
            progress?.Report(new("完成", 3, 3, manifest.IsComplete ? "完整用户配置备份包已生成" : "备份包已生成，但存在未完成资产"));
            return new ComprehensiveBackupPackageInfo(packageId, packageDirectory, manifest);
        }
        catch
        {
            TryDeleteDirectory(stagingDirectory);
            throw;
        }
    }

    public async Task<IReadOnlyList<ComprehensiveBackupPackageInfo>> GetPackagesAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_backupDirectory))
            return Array.Empty<ComprehensiveBackupPackageInfo>();

        var result = new List<ComprehensiveBackupPackageInfo>();
        foreach (var directory in Directory.EnumerateDirectories(_backupDirectory))
        {
            ct.ThrowIfCancellationRequested();
            var manifestPath = Path.Combine(directory, "manifest.json");
            if (!File.Exists(manifestPath))
                continue;
            try
            {
                await using var stream = File.OpenRead(manifestPath);
                var manifest = await JsonSerializer.DeserializeAsync<ComprehensiveBackupManifest>(stream, JsonOptions, ct).ConfigureAwait(false);
                if (manifest != null)
                {
                    var integrityError = VerifyArtifacts(directory, manifest);
                    result.Add(new ComprehensiveBackupPackageInfo(
                        manifest.PackageId,
                        directory,
                        manifest,
                        IntegrityVerified: integrityError is null,
                        IntegrityError: integrityError));
                }
            }
            catch (JsonException)
            {
                // A partially written package is not exposed as restorable data.
            }
        }
        return result.OrderByDescending(item => item.Manifest.CreatedUtc).ToArray();
    }

    public async Task<ComprehensiveBackupRestorePreparation> PrepareTypedRestoreAsync(
        string packageId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            throw new ArgumentException("备份包标识不能为空。", nameof(packageId));

        var package = (await GetPackagesAsync(ct).ConfigureAwait(false))
            .FirstOrDefault(item => string.Equals(item.PackageId, packageId, StringComparison.Ordinal));
        if (package is null)
            throw new FileNotFoundException("备份包不存在。", packageId);
        if (!package.IntegrityVerified)
            throw new InvalidDataException(package.IntegrityError ?? "备份包校验失败，无法生成恢复计划。");

        ValidateRestoreEnvironment(package.Manifest);

        var typedArtifact = package.Manifest.Artifacts
            .FirstOrDefault(item => item.Kind == ComprehensiveBackupArtifactKind.TypedSnapshot);
        if (typedArtifact is null || !typedArtifact.AutomaticRestoreSupported)
            throw new InvalidDataException("备份包不包含可自动恢复的 typed 快照。");

        var packageRoot = Path.GetFullPath(package.DirectoryPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var snapshotPath = Path.GetFullPath(Path.Combine(packageRoot, typedArtifact.RelativePath));
        var packagePrefix = packageRoot + Path.DirectorySeparatorChar;
        if (!snapshotPath.StartsWith(packagePrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("typed 快照路径越界，已阻断恢复计划。");
        if (!File.Exists(snapshotPath))
            throw new FileNotFoundException("typed 快照资产缺失。", snapshotPath);

        ct.ThrowIfCancellationRequested();
        var imported = await _snapshotService.ImportSnapshotAsync(snapshotPath).ConfigureAwait(false);
        if (imported is null)
            throw new InvalidDataException("typed 快照无法导入现有快照仓库。");

        var plan = await _snapshotService.PrepareRestoreAsync(
            imported.Id,
            SnapshotRestoreKind.Normal,
            ct).ConfigureAwait(false);
        var manualOnly = package.Manifest.Artifacts
            .Where(item => !item.AutomaticRestoreSupported)
            .ToArray();
        return new ComprehensiveBackupRestorePreparation(package.PackageId, imported.Id, plan, manualOnly);
    }

    private static void ValidateRestoreEnvironment(ComprehensiveBackupManifest manifest)
    {
        var currentSid = HashIdentity(GetCurrentUserSid());
        if (!string.Equals(manifest.UserSidSha256, currentSid, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("备份包属于其他 Windows 用户，已阻断恢复计划。");

        var currentArchitecture = RuntimeInformation.ProcessArchitecture.ToString();
        if (!string.Equals(manifest.Architecture, currentArchitecture, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"备份包架构为 {manifest.Architecture}，当前系统为 {currentArchitecture}，已阻断恢复计划。");

        var currentBuild = WindowsVersionService.Current.BuildNumber;
        if (manifest.WindowsBuild <= 0 || currentBuild <= 0)
            throw new InvalidDataException("无法确认备份包或当前系统的 Windows build，已阻断恢复计划。");

        // 仅允许同一 Windows 大版本且当前 build 不低于备份时版本；更高版本
        // 仍由逐项 capability/owner 兼容策略决定，不在此处猜测注册表位置。
        var packageIsWin11 = manifest.WindowsBuild >= 22000;
        var currentIsWin11 = currentBuild >= 22000;
        if (packageIsWin11 != currentIsWin11 || currentBuild < manifest.WindowsBuild)
            throw new InvalidDataException(
                $"备份包来自 Windows build {manifest.WindowsBuild}，当前为 {currentBuild}；仅支持同一大版本且当前 build 不低于备份版本，已阻断恢复计划。");
    }

    private static string? VerifyArtifacts(string packageDirectory, ComprehensiveBackupManifest manifest)
    {
        foreach (var artifact in manifest.Artifacts)
        {
            var fullPath = Path.GetFullPath(Path.Combine(packageDirectory, artifact.RelativePath));
            var root = Path.GetFullPath(packageDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return "备份资产路径越界。";
            if (!File.Exists(fullPath))
                return $"备份资产缺失：{artifact.RelativePath}";
            if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
                return $"备份资产包含不受支持的链接：{artifact.RelativePath}";
            if (artifact.Status is ComprehensiveBackupArtifactStatus.Captured or ComprehensiveBackupArtifactStatus.ManualOnly)
            {
                var actualHash = ComputeSha256(fullPath);
                if (!actualHash.Equals(artifact.Sha256, StringComparison.OrdinalIgnoreCase))
                    return $"备份资产校验失败：{artifact.RelativePath}";
            }
        }
        return null;
    }

    private async Task<IReadOnlyList<ComprehensiveBackupOperationState>> CaptureOperationStatesAsync(
        SnapshotCoverageReport coverage,
        IProgress<ComprehensiveBackupProgress>? progress,
        CancellationToken ct)
    {
        if (_operationProvider is null)
            return Array.Empty<ComprehensiveBackupOperationState>();

        var operations = _operationProvider.GetModules()
            .SelectMany(module => module.Operations.Select(operation => (module.Key, Operation: operation)))
            .ToArray();
        if (operations.Length == 0)
            return Array.Empty<ComprehensiveBackupOperationState>();

        var uncapturedIds = coverage.UncapturedOperationIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var results = new ConcurrentBag<ComprehensiveBackupOperationState>();
        using var gate = new SemaphoreSlim(4, 4);
        var completed = 0;
        await Task.WhenAll(operations.Select(async item =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // 注册表捕获独立于状态探测：探测超时/失败不应连带丢弃可恢复值。
                var registry = CaptureDeclaredRegistryState(item.Operation);
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                // 探测上限需高于清理预览内部 4s 扫描预算（含逐文件哈希），
                // 并容忍并发探测与大文件导出时的 I/O 争抢。
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(25));
                try
                {
                    var status = await item.Operation.GetStatusAsync(timeoutCts.Token).ConfigureAwait(false);
                    results.Add(new ComprehensiveBackupOperationState(
                        item.Key,
                        item.Operation.Id,
                        status.IsCompliant,
                        status.SupportState.ToString(),
                        status.SupportReasonCode,
                        status.Details,
                        !uncapturedIds.Contains(item.Operation.Id),
                        RegistryHive: registry.Hive,
                        RegistryPath: registry.Path,
                        RegistryValueName: registry.ValueName,
                        CapturedValueJson: registry.ValueJson,
                        CaptureStatus: registry.Status));
                }
                catch (OperationCanceledException)
                {
                    results.Add(new ComprehensiveBackupOperationState(
                        item.Key, item.Operation.Id, null, "Unknown", "backup.probe-timeout", "操作状态探测超时。", false, "探测超时",
                        RegistryHive: registry.Hive, RegistryPath: registry.Path, RegistryValueName: registry.ValueName,
                        CapturedValueJson: registry.ValueJson, CaptureStatus: registry.Status));
                }
                catch (Exception)
                {
                    results.Add(new ComprehensiveBackupOperationState(
                        item.Key, item.Operation.Id, null, "Unknown", "backup.probe-failed", "操作状态探测失败。", false, "探测失败",
                        RegistryHive: registry.Hive, RegistryPath: registry.Path, RegistryValueName: registry.ValueName,
                        CapturedValueJson: registry.ValueJson, CaptureStatus: registry.Status));
                }
            }
            finally
            {
                gate.Release();
                var current = Interlocked.Increment(ref completed);
                progress?.Report(new("操作状态", current, operations.Length, $"已读取 {current}/{operations.Length} 个优化状态"));
            }
        }));

        return results.OrderBy(item => item.OperationId, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private async Task<ComprehensiveBackupArtifact?> CaptureOperationRecoveryMaterialAsync(
        string stagingDirectory,
        IReadOnlyList<ComprehensiveBackupOperationState> operationStates,
        IProgress<ComprehensiveBackupProgress>? progress,
        CancellationToken ct)
    {
        if (_operationRestoreStore is null || operationStates.Count == 0)
            return null;

        var captured = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        var failed = new List<string>();
        var completed = 0;
        foreach (var state in operationStates)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
                var payload = await _operationRestoreStore.LoadSnapshotAsync(state.OperationId, timeoutCts.Token).ConfigureAwait(false);
                if (payload is null)
                    missing.Add(state.OperationId);
                else
                    captured[state.OperationId] = payload;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                failed.Add(state.OperationId);
            }
            catch (Exception)
            {
                failed.Add(state.OperationId);
            }
            finally
            {
                progress?.Report(new("恢复材料", ++completed, operationStates.Count, $"已读取 {completed}/{operationStates.Count} 个持久化恢复材料"));
            }
        }

        var relativePath = Path.Combine("payload", "operation-recovery-materials.json");
        var destinationPath = Path.Combine(stagingDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var document = new OperationRecoveryMaterialDocument(
            FormatVersion: 1,
            Captured: captured,
            MissingOperationIds: missing.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray(),
            FailedOperationIds: failed.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray());
        await using (var stream = File.Create(destinationPath))
        {
            await JsonSerializer.SerializeAsync(stream, document, JsonOptions, ct).ConfigureAwait(false);
        }

        var status = failed.Count == 0
            ? ComprehensiveBackupArtifactStatus.Captured
            : ComprehensiveBackupArtifactStatus.Partial;
        var reason = failed.Count == 0
            ? missing.Count == 0 ? null : $"{missing.Count} 个操作尚未产生持久化 before 材料。"
            : $"{failed.Count} 个操作的持久化恢复材料读取失败。";
        return CreateArtifact(
            ComprehensiveBackupArtifactKind.OperationRecoveryMaterial,
            relativePath,
            destinationPath,
            status,
            automaticRestoreSupported: false,
            "所有操作已有的持久化 before 恢复材料（仅供对应 Owner 使用）",
            reason);
    }

    /// <summary>
    /// Reads only the registry values explicitly declared by an operation. This is a
    /// read-only diagnostic record; it never grants restore permission and never writes
    /// to the registry. The full HKCU export remains the broad recovery artifact.
    /// </summary>
    private static (string? Hive, string? Path, string? ValueName, string? ValueJson, string Status)
        CaptureDeclaredRegistryState(IOptimizationOperation operation)
    {
        var hiveText = operation.RegistryHiveText;
        var path = operation.RegistryPath;
        var valueNamesText = operation.RegistryValueName;
        if (string.IsNullOrWhiteSpace(hiveText)
            || string.IsNullOrWhiteSpace(path)
            || string.IsNullOrWhiteSpace(valueNamesText))
        {
            return (hiveText, path, valueNamesText, null, "NotApplicable");
        }

        if (!TryResolveHive(hiveText, out var hive))
            return (hiveText, path, valueNamesText, null, "UnsupportedHive");

        var normalizedPath = NormalizeRegistryPath(path, hive);
        var valueNames = valueNamesText
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (valueNames.Length == 0)
            return (hiveText, normalizedPath, valueNamesText, null, "NotApplicable");

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using var key = baseKey.OpenSubKey(normalizedPath, writable: false);
            if (key is null)
                return (hiveText, normalizedPath, valueNamesText, "{\"keyPresent\":false}", "Missing");

            var existingNames = key.GetValueNames();
            var existingSet = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
            var values = new SortedDictionary<string, RegistryValueCapture>(StringComparer.OrdinalIgnoreCase);
            var missingCount = 0;
            foreach (var valueName in valueNames)
            {
                if (!existingSet.Contains(valueName))
                {
                    values[valueName] = new RegistryValueCapture(false, null, null);
                    missingCount++;
                    continue;
                }

                var value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                var kind = key.GetValueKind(valueName).ToString();
                values[valueName] = new RegistryValueCapture(true, kind, value);
            }

            var json = JsonSerializer.Serialize(new RegistryCaptureEnvelope(true, values), JsonOptions);
            return (hiveText, normalizedPath, valueNamesText, json,
                missingCount == 0 ? "Captured" : "Partial");
        }
        catch (UnauthorizedAccessException)
        {
            return (hiveText, normalizedPath, valueNamesText, null, "PermissionDenied");
        }
        catch (SecurityException)
        {
            return (hiveText, normalizedPath, valueNamesText, null, "PermissionDenied");
        }
        catch (IOException)
        {
            return (hiveText, normalizedPath, valueNamesText, null, "ReadFailed");
        }
        catch (Exception) when (OperatingSystem.IsWindows())
        {
            // Registry providers can surface Win32-specific exceptions. Do not allow
            // an optional diagnostic read to fail the complete backup package.
            return (hiveText, normalizedPath, valueNamesText, null, "ReadFailed");
        }
    }

    private static bool TryResolveHive(string hiveText, out RegistryHive hive)
    {
        switch (hiveText.Trim().ToUpperInvariant())
        {
            case "HKCU":
            case "HKEY_CURRENT_USER":
                hive = RegistryHive.CurrentUser;
                return true;
            case "HKLM":
            case "HKEY_LOCAL_MACHINE":
                hive = RegistryHive.LocalMachine;
                return true;
            case "HKCR":
            case "HKEY_CLASSES_ROOT":
                hive = RegistryHive.ClassesRoot;
                return true;
            case "HKU":
            case "HKEY_USERS":
                hive = RegistryHive.Users;
                return true;
            case "HKCC":
            case "HKEY_CURRENT_CONFIG":
                hive = RegistryHive.CurrentConfig;
                return true;
            default:
                hive = default;
                return false;
        }
    }

    private static string NormalizeRegistryPath(string path, RegistryHive hive)
    {
        var trimmed = path.Trim().TrimStart('\\');
        var prefix = hive switch
        {
            RegistryHive.CurrentUser => new[] { "HKCU\\", "HKEY_CURRENT_USER\\" },
            RegistryHive.LocalMachine => new[] { "HKLM\\", "HKEY_LOCAL_MACHINE\\" },
            RegistryHive.ClassesRoot => new[] { "HKCR\\", "HKEY_CLASSES_ROOT\\" },
            RegistryHive.Users => new[] { "HKU\\", "HKEY_USERS\\" },
            RegistryHive.CurrentConfig => new[] { "HKCC\\", "HKEY_CURRENT_CONFIG\\" },
            _ => Array.Empty<string>()
        };
        foreach (var candidate in prefix)
        {
            if (trimmed.StartsWith(candidate, StringComparison.OrdinalIgnoreCase))
                return trimmed[candidate.Length..];
        }
        return trimmed;
    }

    private sealed record RegistryCaptureEnvelope(bool KeyPresent, IReadOnlyDictionary<string, RegistryValueCapture> Values);

    private sealed record RegistryValueCapture(bool Present, string? Kind, object? Value);

    private sealed record OperationRecoveryMaterialDocument(
        int FormatVersion,
        IReadOnlyDictionary<string, string> Captured,
        IReadOnlyList<string> MissingOperationIds,
        IReadOnlyList<string> FailedOperationIds);

    private async Task<ComprehensiveBackupArtifact> CaptureUserRegistryAsync(string stagingDirectory, CancellationToken ct)
    {
        var relativePath = Path.Combine("payload", "user-hkcu.reg");
        var destinationPath = Path.Combine(stagingDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var result = await _registryBackend.ExportCurrentUserAsync(destinationPath, ct).ConfigureAwait(false);
        if (!result.Succeeded || !File.Exists(destinationPath))
        {
            return new ComprehensiveBackupArtifact(
                ComprehensiveBackupArtifactKind.UserRegistry,
                relativePath,
                ComprehensiveBackupArtifactStatus.Failed,
                0,
                string.Empty,
                false,
                "当前用户 HKCU 注册表导出",
                result.ErrorMessage ?? "导出结果不存在。");
        }

        return CreateArtifact(
            ComprehensiveBackupArtifactKind.UserRegistry,
            relativePath,
            destinationPath,
            ComprehensiveBackupArtifactStatus.ManualOnly,
            automaticRestoreSupported: false,
            "当前用户 HKCU 注册表完整导出；自动恢复暂不启用，避免未经逐项契约验证的 hive 合并。",
            "仅支持作为本地人工恢复材料，Backup Manager 不会自动执行 reg import。");
    }

    private static async Task<IReadOnlyList<ComprehensiveBackupArtifact>> CaptureApplicationConfigurationAsync(string stagingDirectory, CancellationToken ct)
    {
        var sourceFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var enumerationFailures = new List<(string Directory, string Reason)>();
        AddFiles(AppPaths.ConfigDir, sourceFiles, enumerationFailures);
        AddFiles(AppPaths.CoreAcceleratorDir, sourceFiles, enumerationFailures);
        AddFiles(AppPaths.AffinityProfilesDir, sourceFiles, enumerationFailures);
        AddFiles(AppPaths.OptimizationProfilesDir, sourceFiles, enumerationFailures);

        var artifacts = new List<ComprehensiveBackupArtifact>();
        foreach (var failure in enumerationFailures)
        {
            ct.ThrowIfCancellationRequested();
            var relativeDirectory = Path.GetRelativePath(AppPaths.AppDataRoot, failure.Directory);
            var relativePath = Path.Combine("payload", "config", relativeDirectory, ".capture-error.txt");
            var markerPath = Path.Combine(stagingDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
            await File.WriteAllTextAsync(markerPath, failure.Reason, ct).ConfigureAwait(false);
            artifacts.Add(new ComprehensiveBackupArtifact(
                ComprehensiveBackupArtifactKind.ApplicationConfiguration,
                relativePath,
                ComprehensiveBackupArtifactStatus.Failed,
                new FileInfo(markerPath).Length,
                string.Empty,
                false,
                "应用配置目录",
                failure.Reason));
        }

        foreach (var sourcePath in sourceFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            var relativeSource = Path.GetRelativePath(AppPaths.AppDataRoot, sourcePath);
            var relativePath = Path.Combine("payload", "config", relativeSource);
            var destinationPath = Path.Combine(stagingDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            try
            {
                var sourceHashBefore = await ComputeSha256Async(sourcePath, ct).ConfigureAwait(false);
                File.Copy(sourcePath, destinationPath, overwrite: false);
                var destinationHash = await ComputeSha256Async(destinationPath, ct).ConfigureAwait(false);
                var sourceHashAfter = await ComputeSha256Async(sourcePath, ct).ConfigureAwait(false);
                var stable = sourceHashBefore.Equals(sourceHashAfter, StringComparison.OrdinalIgnoreCase)
                    && sourceHashBefore.Equals(destinationHash, StringComparison.OrdinalIgnoreCase);
                artifacts.Add(CreateArtifact(
                    ComprehensiveBackupArtifactKind.ApplicationConfiguration,
                    relativePath,
                    destinationPath,
                    stable ? ComprehensiveBackupArtifactStatus.Captured : ComprehensiveBackupArtifactStatus.Partial,
                    automaticRestoreSupported: false,
                    "应用配置文件",
                    stable ? null : "采集期间文件发生变化，需重新备份后才能作为恢复材料。"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                artifacts.Add(new ComprehensiveBackupArtifact(
                    ComprehensiveBackupArtifactKind.ApplicationConfiguration,
                    relativePath,
                    ComprehensiveBackupArtifactStatus.Failed,
                    0,
                    string.Empty,
                    false,
                    "应用配置文件",
                    "配置文件无法读取或复制。"));
            }
        }
        return artifacts;
    }

    private static void AddFiles(
        string directory,
        ISet<string> files,
        ICollection<(string Directory, string Reason)> failures)
    {
        if (!Directory.Exists(directory))
            return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                files.Add(file);
        }
        catch (UnauthorizedAccessException)
        {
            failures.Add((directory, "应用配置目录权限不足，未能完整枚举。"));
        }
        catch (IOException)
        {
            failures.Add((directory, "应用配置目录读取失败，未能完整枚举。"));
        }
    }

    private static ComprehensiveBackupArtifact CreateArtifact(
        ComprehensiveBackupArtifactKind kind,
        string relativePath,
        string path,
        ComprehensiveBackupArtifactStatus status,
        bool automaticRestoreSupported,
        string description,
        string? reason = null)
    {
        var info = new FileInfo(path);
        var hash = status is ComprehensiveBackupArtifactStatus.Captured or ComprehensiveBackupArtifactStatus.ManualOnly
            ? ComputeSha256(path)
            : string.Empty;
        return new ComprehensiveBackupArtifact(kind, relativePath, status, info.Exists ? info.Length : 0, hash, automaticRestoreSupported, description, reason);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
            hash.AppendData(buffer, 0, read);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string HashIdentity(string? value) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty)));

    private static string? GetCurrentUserSid()
    {
        try { return WindowsIdentity.GetCurrent().User?.Value; }
        catch { return null; }
    }

    private static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Trim().Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars).TrimEnd('.', ' ');
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { /* 不影响原始失败结果 */ }
    }
}
