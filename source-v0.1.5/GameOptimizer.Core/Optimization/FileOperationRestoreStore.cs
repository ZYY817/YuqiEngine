using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Utilities;

namespace GameOptimizer.Core.Optimization;

public sealed class FileOperationRestoreStore : IOperationRestoreStore
{
    private readonly string _root;
    private const long MinDiskSpaceBytes = 10 * 1024 * 1024; // 10MB 最小磁盘空间
    private readonly bool _enableIntegrityCheck;
    // Why: 使用引用计数包装，避免 TryRemove+Dispose 竞态导致 ObjectDisposedException
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, RefCountedSemaphore> _fileLocks = new();
    private readonly object _fileLocksGate = new();

    private sealed class RefCountedSemaphore
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        private int _refCount;

        public void AddRef() => Interlocked.Increment(ref _refCount);
        
        /// <summary>递减引用计数，返回递减后的值</summary>
        public int Release()
        {
            return Interlocked.Decrement(ref _refCount);
        }
    }

    public FileOperationRestoreStore(bool enableIntegrityCheck = true)
    {
        // Why: 恢复快照属恢复关键状态，必须落系统盘状态根而非可选数据根。
        var basePath = Path.Combine(
            Constants.AppPaths.StateDataRoot,
            "restore");
        
        string? dirError = null;
        if (!SafeDirectoryOperations.TryCreateDirectory(basePath, out dirError))
        {
            System.Diagnostics.Debug.WriteLine($"[FileOperationRestoreStore] Failed to create restore directory: {dirError}");
        }
        
        _root = basePath;
        _enableIntegrityCheck = enableIntegrityCheck;
    }

    public async Task SaveSnapshotAsync(string operationId, string payload, CancellationToken cancellationToken)
    {
        var path = GetPath(operationId);
        
        RefCountedSemaphore refSem;
        lock (_fileLocksGate)
        {
            refSem = _fileLocks.GetOrAdd(operationId, _ => new RefCountedSemaphore());
            refSem.AddRef();
        }

        try
        {
            await refSem.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var driveInfo = new DriveInfo(Path.GetPathRoot(_root)!);
                if (driveInfo.AvailableFreeSpace < MinDiskSpaceBytes)
                {
                    throw new IOException($"Insufficient disk space, at least {MinDiskSpaceBytes / 1024 / 1024}MB required");
                }

                // P0-3: 添加完整性校验和包装
                var contentToSave = _enableIntegrityCheck 
                    ? SnapshotIntegrityValidator.WrapWithChecksum(payload)
                    : payload;
            
                // P0-3: 使用唯一临时文件名防止并发冲突
                var tempPath = $"{path}.tmp.{Guid.NewGuid():N}";
                try
                {
                    await SafeFileOperations.WriteTextWithRetryAsync(tempPath, contentToSave, cancellationToken).ConfigureAwait(false);
                
                    // Why: 必须单次原子覆写，先 Delete 再 Move 之间存在崩溃窗口，
                    // 旧快照已删而新快照未落位会永久丢失唯一恢复材料。
                    File.Move(tempPath, path, overwrite: true);
                }
                catch
                {
                    // Why: 写入或移动失败时必须清理 temp 文件，
                    // 否则 Guid 命名的残留文件会永久堆积在 restore 目录
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); }
                    catch (Exception cleanupEx) { System.Diagnostics.Debug.WriteLine($"[FileOperationRestoreStore] 清理临时文件失败: {cleanupEx.Message}"); }
                    throw;
                }
            }
            finally
            {
                refSem.Semaphore.Release();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FileOperationRestoreStore] 保存快照失败: {ex.Message}");
            throw;
        }
        finally
        {
            var dispose = false;
            lock (_fileLocksGate)
            {
                var remaining = refSem.Release();
                if (remaining == 0 &&
                    _fileLocks.TryGetValue(operationId, out var current) &&
                    ReferenceEquals(current, refSem))
                {
                    _fileLocks.TryRemove(operationId, out _);
                    dispose = true;
                }
            }

            if (dispose)
            {
                refSem.Semaphore.Dispose();
            }
        }
    }

    public async Task<string?> LoadSnapshotAsync(string operationId, CancellationToken cancellationToken)
    {
        var path = GetPath(operationId);
        if (!File.Exists(path))
        {
            return null;
        }

        // Why: 文件大小异常时记录警告但仍尝试读取，
        // 因为用户环境千差万别，不能因为文件稍大就丢弃有效快照
        var fileInfo = new FileInfo(path);
        if (!SnapshotIntegrityValidator.IsReasonableSize(fileInfo.Length))
        {
            System.Diagnostics.Debug.WriteLine(
                $"[FileOperationRestoreStore][LoadSnapshot] 快照文件大小异常: {operationId}, {fileInfo.Length} bytes，仍尝试读取");
            if (fileInfo.Length <= 0)
                return null;
        }

        string content;
        try
        {
            using var stream = File.OpenText(path);
            content = await stream.ReadToEndAsync().ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[FileOperationRestoreStore][LoadSnapshot] 读取快照文件失败: {operationId}, {ex.Message}");
            return null;
        }

        if (!_enableIntegrityCheck)
            return content;

        var (isValid, unwrappedContent) = SnapshotIntegrityValidator.UnwrapAndValidate(content);
        if (!isValid)
        {
            // Why: 校验失败必须 fail-closed 拒绝——降级使用会把被篡改/部分覆写的
            // 快照当作恢复依据写回系统，等于伪造恢复。无 checksum 的旧格式快照
            // 仍由 UnwrapAndValidate 按 IsValid=true 放行以保持兼容。
            System.Diagnostics.Debug.WriteLine(
                $"[FileOperationRestoreStore][LoadSnapshot] 快照完整性验证失败，已拒绝恢复材料: {operationId}");
            return null;
        }
        return unwrappedContent;
    }

    public Task DeleteSnapshotAsync(string operationId, CancellationToken cancellationToken)
    {
        var path = GetPath(operationId);
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            // Why: 删除失败不应阻断主流程，但需要记录以便排查残留文件
            System.Diagnostics.Debug.WriteLine($"[FileOperationRestoreStore] 删除快照失败 {operationId}: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public async Task<bool> HasSnapshotAsync(string operationId, CancellationToken cancellationToken)
    {
        // Why: 零字节/损坏/校验失败的快照文件不能被计为"有恢复材料"——
        // 存在性判断必须与可读性一致，否则上层会把损坏快照当作可恢复证据
        // 走 ambiguous 阻断分支或伪造恢复。
        var path = GetPath(operationId);
        if (!File.Exists(path))
        {
            return false;
        }
        return await LoadSnapshotAsync(operationId, cancellationToken).ConfigureAwait(false) is not null;
    }

    private string GetPath(string operationId)
    {
        var safeName = string.Concat(operationId.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
        return Path.Combine(_root, $"{safeName}.json");
    }
}
