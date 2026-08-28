using System.Diagnostics;
using System.Text;
using System.Text.Json;
using GameOptimizer.Core.Constants;

namespace GameOptimizer.Core.Services;

public sealed class TransactionLogService
{
    public const string StartedStatus = "STARTED";
    public const string PendingConfirmationStatus = "PENDING_CONFIRMATION";
    public const string CompletedStatus = "COMPLETED";
    public const string FailedStatus = "FAILED";
    public const string RollbackStatus = "ROLLBACK";
    public const string ReconciledTargetStatus = "RECONCILED_TARGET";
    public const string ReconciledOriginalStatus = "RECONCILED_ORIGINAL";
    public const string RecoveryRequiredStatus = "RECOVERY_REQUIRED";
    public const int CurrentSchemaVersion = 1;

    private readonly string _logPath;
    private readonly Func<Exception?>? _readFailureFactory;
    private readonly Func<TransactionEntry, Exception?>? _writeFailureFactory;

    public record TransactionEntry(
        string OperationId,
        string Status,
        DateTime Timestamp,
        string? Error = null,
        string? ModuleKey = null,
        string? Intent = null,
        int SchemaVersion = 0,
        string? TransactionId = null)
    {
        public string EffectiveTransactionId => string.IsNullOrWhiteSpace(TransactionId)
            ? OperationId
            : TransactionId;
    }

    private enum TransactionWriteKind
    {
        Start,
        Pending,
        Finalization,
        Reconciliation,
        Recovery
    }

    public sealed record TransactionLogIssue(
        string Code,
        string Message,
        int LineNumber = 0,
        string? ModuleKey = null,
        string? OperationId = null)
    {
        public bool IsGlobal => string.IsNullOrWhiteSpace(ModuleKey)
            && string.IsNullOrWhiteSpace(OperationId);
    }

    public sealed record TransactionLogReadResult(
        IReadOnlyList<TransactionEntry> Entries,
        IReadOnlyList<TransactionLogIssue> Issues)
    {
        public bool IsReliable => Issues.Count == 0;
    }

    public sealed class TransactionLogReadException : IOException
    {
        public TransactionLogReadException(IReadOnlyList<TransactionLogIssue> issues)
            : base(BuildMessage(issues))
        {
            Issues = issues;
        }

        public IReadOnlyList<TransactionLogIssue> Issues { get; }

        private static string BuildMessage(IReadOnlyList<TransactionLogIssue> issues) =>
            issues.Count == 0
                ? "The transaction log could not be verified."
                : $"The transaction log could not be verified: {string.Join(" | ", issues.Select(issue => issue.Message))}";
    }

    // Engine instances are short-lived in tests and can also be created by
    // more than one host path. The transaction file is process-wide state,
    // so an instance-local lock cannot protect readers from other writers.
    private static readonly object FileLock = new();

    public TransactionLogService(
        string? logPath = null,
        Func<Exception?>? readFailureFactory = null,
        Func<TransactionEntry, Exception?>? writeFailureFactory = null)
    {
        _logPath = string.IsNullOrWhiteSpace(logPath)
            ? AppPaths.TransactionLogFilePath
            : Path.GetFullPath(logPath);
        _readFailureFactory = readFailureFactory;
        _writeFailureFactory = writeFailureFactory;

        var dir = Path.GetDirectoryName(_logPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    public void LogStart(string operationId, string? moduleKey = null, string? intent = null)
    {
        _ = TryLogStart(operationId, moduleKey, intent, transactionId: null);
    }

    public bool TryLogStart(
        string operationId,
        string? moduleKey = null,
        string? intent = null,
        string? transactionId = null)
    {
        return WriteEntry(
            new TransactionEntry(
                operationId,
                StartedStatus,
                DateTime.UtcNow,
                null,
                moduleKey,
                intent,
                CurrentSchemaVersion,
                transactionId ?? CreateTransactionId()),
            TransactionWriteKind.Start);
    }

    public void LogPendingConfirmation(
        string operationId,
        string? moduleKey,
        string? intent,
        string? message = null,
        string? transactionId = null)
    {
        _ = WriteEntry(
            new TransactionEntry(
                operationId,
                PendingConfirmationStatus,
                DateTime.UtcNow,
                message,
                moduleKey,
                intent,
                CurrentSchemaVersion,
                transactionId),
            TransactionWriteKind.Pending);
    }

    public void LogComplete(
        string operationId,
        bool success,
        string? error = null,
        string? moduleKey = null,
        string? intent = null,
        string? transactionId = null)
    {
        var status = success ? CompletedStatus : FailedStatus;
        _ = WriteEntry(
            new TransactionEntry(operationId, status, DateTime.UtcNow, error, moduleKey, intent, CurrentSchemaVersion, transactionId),
            TransactionWriteKind.Finalization);
    }

    public void FinalizePending(
        string operationId,
        bool success,
        string? error = null,
        string? moduleKey = null,
        string? intent = null,
        string? transactionId = null)
    {
        var status = success ? CompletedStatus : FailedStatus;
        _ = WriteEntry(
            new TransactionEntry(operationId, status, DateTime.UtcNow, error, moduleKey, intent, CurrentSchemaVersion, transactionId),
            TransactionWriteKind.Finalization);
    }

    public void MarkReconciled(
        string operationId,
        bool targetConfirmed,
        string message,
        string? moduleKey = null,
        string? intent = null,
        string? transactionId = null)
    {
        var status = targetConfirmed ? ReconciledTargetStatus : ReconciledOriginalStatus;
        _ = WriteEntry(
            new TransactionEntry(operationId, status, DateTime.UtcNow, message, moduleKey, intent, CurrentSchemaVersion, transactionId),
            TransactionWriteKind.Reconciliation);
    }

    public void MarkRecoveryRequired(
        string operationId,
        string message,
        string? moduleKey = null,
        string? intent = null,
        string? transactionId = null)
    {
        _ = WriteEntry(
            new TransactionEntry(
                operationId,
                RecoveryRequiredStatus,
                DateTime.UtcNow,
                message,
                moduleKey,
                intent,
                CurrentSchemaVersion,
                transactionId),
            TransactionWriteKind.Recovery);
    }

    public void LogRollback(
        string operationId,
        string? moduleKey = null,
        string? intent = null,
        string? transactionId = null)
    {
        _ = WriteEntry(
            new TransactionEntry(operationId, RollbackStatus, DateTime.UtcNow, null, moduleKey, intent, CurrentSchemaVersion, transactionId),
            TransactionWriteKind.Finalization);
    }

    public TransactionLogReadResult ReadState()
    {
        lock (FileLock)
        {
            return ReadStateUnsafe();
        }
    }

    public IReadOnlyList<TransactionEntry> GetPendingOperations()
    {
        var result = ReadState();
        ThrowIfUnreliable(result);
        return result.Entries
            .Where(entry => entry.Status is StartedStatus or PendingConfirmationStatus or RecoveryRequiredStatus)
            .ToList();
    }

    public TransactionEntry? GetPendingOperation(string operationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        var result = ReadState();
        ThrowIfUnreliable(result);
        return result.Entries.FirstOrDefault(entry =>
            string.Equals(entry.OperationId, operationId, StringComparison.OrdinalIgnoreCase)
            && entry.Status is StartedStatus or PendingConfirmationStatus or RecoveryRequiredStatus);
    }

    private static void ThrowIfUnreliable(TransactionLogReadResult result)
    {
        if (!result.IsReliable)
        {
            throw new TransactionLogReadException(result.Issues);
        }
    }

    public void ClearLog()
    {
        lock (FileLock)
        {
            if (File.Exists(_logPath))
                File.Delete(_logPath);
        }
    }

    private TransactionLogReadResult ReadStateUnsafe()
    {
        var entries = new Dictionary<string, TransactionEntry>(StringComparer.OrdinalIgnoreCase);
        var issues = new List<TransactionLogIssue>();

        try
        {
            var injectedReadFailure = _readFailureFactory?.Invoke();
            if (injectedReadFailure is not null)
            {
                issues.Add(new TransactionLogIssue(
                    "READ_FAILED",
                    $"Transaction log read failed: {injectedReadFailure.Message}"));
                return new TransactionLogReadResult(entries.Values.ToList(), issues);
            }
        }
        catch (Exception ex)
        {
            issues.Add(new TransactionLogIssue("READ_FAILED", $"Transaction log read failed: {ex.Message}"));
            return new TransactionLogReadResult(entries.Values.ToList(), issues);
        }

        if (!File.Exists(_logPath))
        {
            return new TransactionLogReadResult(entries.Values.ToList(), issues);
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(_logPath, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            issues.Add(new TransactionLogIssue("READ_FAILED", $"Transaction log read failed: {ex.Message}"));
            return new TransactionLogReadResult(entries.Values.ToList(), issues);
        }

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var lineNumber = index + 1;
            if (string.IsNullOrWhiteSpace(line))
            {
                issues.Add(new TransactionLogIssue(
                    "EMPTY_RECORD",
                    $"Transaction log record {lineNumber} is empty or partially written.",
                    lineNumber));
                continue;
            }

            var identity = ExtractIdentity(line);
            try
            {
                using var document = JsonDocument.Parse(line);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new JsonException("Record root is not a JSON object.");
                }

                if (!TryReadSchemaVersion(document.RootElement, out var schemaVersion, out var schemaError)
                    || schemaVersion is not (0 or CurrentSchemaVersion))
                {
                    issues.Add(new TransactionLogIssue(
                        "UNKNOWN_SCHEMA",
                        schemaError ?? $"Transaction log record {lineNumber} uses unsupported schema version {schemaVersion}.",
                        lineNumber,
                        identity.ModuleKey,
                        identity.OperationId));
                    continue;
                }

                var entry = JsonSerializer.Deserialize<TransactionEntry>(line);
                if (entry is null
                    || string.IsNullOrWhiteSpace(entry.OperationId)
                    || string.IsNullOrWhiteSpace(entry.Status)
                    || entry.Timestamp == default
                    || !IsKnownStatus(entry.Status))
                {
                    throw new JsonException("Record is missing required transaction fields or has an unknown status.");
                }

                var normalizedEntry = entry with
                {
                    SchemaVersion = schemaVersion == 0 ? 0 : schemaVersion,
                    TransactionId = string.IsNullOrWhiteSpace(entry.TransactionId)
                        ? entry.OperationId
                        : entry.TransactionId
                };
                var transactionKey = normalizedEntry.EffectiveTransactionId;
                entries.TryGetValue(transactionKey, out var currentEntry);
                entries[transactionKey] = ApplyMonotonicTransition(currentEntry, normalizedEntry);
            }
            catch (Exception ex)
            {
                issues.Add(new TransactionLogIssue(
                    "CORRUPT_RECORD",
                    $"Transaction log record {lineNumber} could not be parsed: {ex.Message}",
                    lineNumber,
                    identity.ModuleKey,
                    identity.OperationId));
                Debug.WriteLine($"[TransactionLogService] Error parsing log entry {lineNumber}: {ex.Message}");
            }
        }

        return new TransactionLogReadResult(entries.Values.ToList(), issues);
    }

    private bool WriteEntry(TransactionEntry entry, TransactionWriteKind writeKind)
    {
        lock (FileLock)
        {
            string? temporaryPath = null;
            try
            {
                var injectedWriteFailure = _writeFailureFactory?.Invoke(entry);
                if (injectedWriteFailure is not null)
                {
                    throw injectedWriteFailure;
                }

                // Why: 日志文件无限增长会耗尽磁盘，超过 5MB 时截断保留最后 1000 行
                var existingLines = File.Exists(_logPath)
                    ? File.ReadAllLines(_logPath, Encoding.UTF8).ToList()
                    : new List<string>();
                if (File.Exists(_logPath) && new FileInfo(_logPath).Length > 5 * 1024 * 1024)
                {
                    existingLines = existingLines
                        .Skip(Math.Max(0, existingLines.Count - 1000))
                        .ToList();
                }

                var state = ReadStateUnsafe();
                var normalizedEntry = entry with
                {
                    TransactionId = ResolveTransactionId(state, entry)
                };
                var relevantIssues = state.Issues.Where(issue =>
                    issue.IsGlobal
                    || string.Equals(issue.OperationId, normalizedEntry.OperationId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(issue.ModuleKey, normalizedEntry.ModuleKey, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (relevantIssues.Length > 0)
                {
                    return false;
                }

                var currentEntry = state.Entries.FirstOrDefault(current =>
                    string.Equals(current.EffectiveTransactionId, normalizedEntry.EffectiveTransactionId, StringComparison.OrdinalIgnoreCase));
                var nextEntry = ApplyMonotonicTransition(currentEntry, normalizedEntry, writeKind);
                if (currentEntry is not null
                    && string.Equals(currentEntry.Status, nextEntry.Status, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(currentEntry.Error, nextEntry.Error, StringComparison.Ordinal))
                {
                    return true;
                }

                var json = JsonSerializer.Serialize(nextEntry);
                existingLines.Add(json);
                temporaryPath = $"{_logPath}.{Guid.NewGuid():N}.tmp";
                WriteAndFlush(temporaryPath, string.Join(Environment.NewLine, existingLines) + Environment.NewLine);
                if (File.Exists(_logPath))
                {
                    File.Replace(temporaryPath, _logPath, destinationBackupFileName: null);
                }
                else
                {
                    File.Move(temporaryPath, _logPath);
                }
                temporaryPath = null;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TransactionLogService] 写入事务日志失败: {ex.Message}");
                return false;
            }
            finally
            {
                if (temporaryPath is not null)
                {
                    try
                    {
                        if (File.Exists(temporaryPath))
                            File.Delete(temporaryPath);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[TransactionLogService] 清理临时事务日志失败: {ex.Message}");
                    }
                }
            }
        }
    }

    private static TransactionEntry ApplyMonotonicTransition(
        TransactionEntry? current,
        TransactionEntry requested,
        TransactionWriteKind writeKind = TransactionWriteKind.Finalization)
    {
        if (current is null)
        {
            return requested;
        }

        var currentRank = GetStatusRank(current.Status);
        var requestedRank = GetStatusRank(requested.Status);
        if (requestedRank < currentRank)
        {
            return current;
        }

        if (string.Equals(current.Status, requested.Status, StringComparison.OrdinalIgnoreCase))
        {
            return current;
        }

        if (current.Status is ReconciledTargetStatus or ReconciledOriginalStatus
            && requested.Status is ReconciledTargetStatus or ReconciledOriginalStatus)
        {
            return new TransactionEntry(
                requested.OperationId,
                RecoveryRequiredStatus,
                requested.Timestamp,
                "Conflicting reconciliation evidence was observed; recovery is required.",
                requested.ModuleKey ?? current.ModuleKey,
                requested.Intent ?? current.Intent,
                Math.Max(requested.SchemaVersion, current.SchemaVersion),
                requested.TransactionId ?? current.TransactionId);
        }

        if (currentRank == requestedRank && currentRank >= GetStatusRank(CompletedStatus))
        {
            return current;
        }

        if (requested.Status == RecoveryRequiredStatus)
        {
            return requested;
        }

        if (writeKind == TransactionWriteKind.Start
            && currentRank >= GetStatusRank(PendingConfirmationStatus))
        {
            return current;
        }

        return requested;
    }

    private static int GetStatusRank(string status) => status switch
    {
        StartedStatus => 10,
        PendingConfirmationStatus => 20,
        CompletedStatus or FailedStatus
            or ReconciledTargetStatus or ReconciledOriginalStatus => 30,
        RollbackStatus => 35,
        RecoveryRequiredStatus => 40,
        _ => 0
    };

    private static string CreateTransactionId() => $"tx:{Guid.NewGuid():N}";

    private static string ResolveTransactionId(
        TransactionLogReadResult state,
        TransactionEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.TransactionId))
        {
            return entry.TransactionId;
        }

        var active = state.Entries
            .Where(candidate =>
                string.Equals(candidate.OperationId, entry.OperationId, StringComparison.OrdinalIgnoreCase)
                && candidate.Status is StartedStatus or PendingConfirmationStatus or RecoveryRequiredStatus)
            .OrderByDescending(candidate => candidate.Timestamp)
            .FirstOrDefault();
        return active?.EffectiveTransactionId ?? entry.OperationId;
    }

    private static bool IsKnownStatus(string status) => status is
        StartedStatus or PendingConfirmationStatus or CompletedStatus or FailedStatus
        or RollbackStatus or ReconciledTargetStatus or ReconciledOriginalStatus or RecoveryRequiredStatus;

    private static void WriteAndFlush(string path, string contents)
    {
        var bytes = Encoding.UTF8.GetBytes(contents);
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            options: FileOptions.WriteThrough);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(flushToDisk: true);
    }

    private static bool TryReadSchemaVersion(
        JsonElement root,
        out int schemaVersion,
        out string? error)
    {
        schemaVersion = 0;
        error = null;
        if (!TryGetProperty(root, "SchemaVersion", out var schema)
            && !TryGetProperty(root, "Schema", out schema)
            && !TryGetProperty(root, "Version", out schema))
        {
            return true;
        }

        if (schema.ValueKind == JsonValueKind.Number && schema.TryGetInt32(out schemaVersion))
        {
            return true;
        }

        if (schema.ValueKind == JsonValueKind.String
            && int.TryParse(schema.GetString(), out schemaVersion))
        {
            return true;
        }

        if (schema.ValueKind != JsonValueKind.Number && schema.ValueKind != JsonValueKind.String)
        {
            error = "Transaction log record contains an invalid schema version.";
            return false;
        }

        error = "Transaction log record contains an invalid schema version.";
        return false;
    }

    private static bool TryGetProperty(JsonElement root, string name, out JsonElement value)
    {
        if (root.TryGetProperty(name, out value))
            return true;

        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static (string? ModuleKey, string? OperationId) ExtractIdentity(string line)
    {
        return (ExtractJsonString(line, "ModuleKey"), ExtractJsonString(line, "OperationId"));
    }

    private static string? ExtractJsonString(string line, string propertyName)
    {
        var propertyToken = $"\"{propertyName}\"";
        var start = line.IndexOf(propertyToken, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return null;

        var colon = line.IndexOf(':', start + propertyToken.Length);
        if (colon < 0)
            return null;

        var quote = line.IndexOf('"', colon + 1);
        if (quote < 0)
            return null;

        var end = quote + 1;
        while (end < line.Length)
        {
            if (line[end] == '"' && line[end - 1] != '\\')
            {
                var raw = line.Substring(quote, end - quote + 1);
                try
                {
                    return JsonSerializer.Deserialize<string>(raw);
                }
                catch (JsonException)
                {
                    return null;
                }
            }

            end++;
        }

        return null;
    }
}
