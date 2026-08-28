using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Constants;

namespace GameOptimizer.Core.Services.NetworkReset;

public sealed class FileNetworkResetPlanStore : INetworkResetPlanStore
{
    private const int MaximumStateBytes = 1024 * 1024;
    private const string PlanEnvelopeType = "yuqi.network-reset.plan";
    private const string ClaimEnvelopeType = "yuqi.network-reset.plan-claim";
    private const int SchemaVersion = 2;
    private readonly string _root;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public FileNetworkResetPlanStore() : this(Path.Combine(AppPaths.AppDataRoot, "network-reset", "plans"))
    {
    }

    internal FileNetworkResetPlanStore(string root) =>
        _root = Path.GetFullPath(root ?? throw new ArgumentNullException(nameof(root)));

    public async Task RegisterAsync(NetworkResetPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        Directory.CreateDirectory(_root);
        var path = PlanPath(plan.ChangePlan.PlanId);
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(plan, StrictJson);
        var envelope = new PlanEnvelope(
            PlanEnvelopeType,
            SchemaVersion,
            plan.ChangePlan.PlanId,
            plan.ChangePlan.PlanHash,
            Convert.ToHexString(SHA256.HashData(payloadBytes)),
            plan);

        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(path))
            {
                throw new InvalidOperationException("计划标识重复，拒绝覆盖。");
            }
            await WriteCreateNewAsync(path, envelope, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<NetworkResetPlan?> LoadAsync(Guid planId, CancellationToken cancellationToken)
    {
        var path = PlanPath(planId);
        if (!File.Exists(path))
        {
            return null;
        }
        return await ReadPlanAsync(path, planId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PlanClaimStatus> TryClaimAsync(
        Guid planId,
        string planHash,
        CancellationToken cancellationToken)
    {
        var path = PlanPath(planId);
        var claimPath = ClaimPath(planId);
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path))
            {
                return PlanClaimStatus.Missing;
            }

            var plan = await ReadPlanAsync(path, planId, cancellationToken).ConfigureAwait(false);
            if (!FixedHashEquals(plan.ChangePlan.PlanHash, planHash))
            {
                return PlanClaimStatus.HashMismatch;
            }

            var claim = new ClaimEnvelope(
                ClaimEnvelopeType,
                SchemaVersion,
                planId,
                planHash,
                Guid.NewGuid(),
                DateTimeOffset.UtcNow);
            try
            {
                await WriteCreateNewAsync(claimPath, claim, cancellationToken).ConfigureAwait(false);
                return PlanClaimStatus.Claimed;
            }
            catch (IOException) when (File.Exists(claimPath))
            {
                // Claims are permanent. An unreadable or valid prior claim both block replay.
                return PlanClaimStatus.AlreadyClaimed;
            }
        }
        finally
        {
            _mutex.Release();
        }
    }

    private static async Task<NetworkResetPlan> ReadPlanAsync(
        string path,
        Guid expectedPlanId,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (info.Length is <= 0 or > MaximumStateBytes)
        {
            throw new InvalidDataException("网络重置计划大小无效。");
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var envelope = JsonSerializer.Deserialize<PlanEnvelope>(bytes, StrictJson)
            ?? throw new InvalidDataException("网络重置计划为空。");
        if (envelope.SchemaVersion != SchemaVersion
            || !string.Equals(envelope.Type, PlanEnvelopeType, StringComparison.Ordinal)
            || envelope.PlanId != expectedPlanId
            || envelope.Payload is null
            || envelope.Payload.ChangePlan.PlanId != expectedPlanId
            || !FixedHashEquals(envelope.PlanHash, envelope.Payload.ChangePlan.PlanHash))
        {
            throw new InvalidDataException("网络重置计划 envelope 身份无效。");
        }

        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(envelope.Payload, StrictJson);
        var actualPayloadHash = Convert.ToHexString(SHA256.HashData(payloadBytes));
        if (!FixedHashEquals(actualPayloadHash, envelope.PayloadHash))
        {
            throw new InvalidDataException("网络重置计划 payload hash 无效。");
        }
        return envelope.Payload;
    }

    private static async Task WriteCreateNewAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, StrictJson);
        if (bytes.Length > MaximumStateBytes)
        {
            throw new InvalidDataException("网络重置 durable 文档超过大小限制。");
        }

        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private static bool FixedHashEquals(string left, string right)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(left),
                Convert.FromHexString(right));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private string PlanPath(Guid id) => Path.Combine(_root, $"{id:N}.json");
    private string ClaimPath(Guid id) => Path.Combine(_root, $"{id:N}.claim");

    private static readonly JsonSerializerOptions StrictJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private sealed record PlanEnvelope(
        string Type,
        int SchemaVersion,
        Guid PlanId,
        string PlanHash,
        string PayloadHash,
        NetworkResetPlan Payload);

    private sealed record ClaimEnvelope(
        string Type,
        int SchemaVersion,
        Guid PlanId,
        string PlanHash,
        Guid ClaimId,
        DateTimeOffset ClaimedAtUtc);
}

public sealed class FileNetworkResetReceiptStore : INetworkResetReceiptStore
{
    private const int MaximumReceiptBytes = 1024 * 1024;
    private const string EnvelopeType = "yuqi.network-reset.receipt";
    private const int SchemaVersion = 2;
    private readonly string _root;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public FileNetworkResetReceiptStore() : this(Path.Combine(AppPaths.AppDataRoot, "network-reset", "receipts"))
    {
    }

    internal FileNetworkResetReceiptStore(string root) =>
        _root = Path.GetFullPath(root ?? throw new ArgumentNullException(nameof(root)));

    public async Task SaveAsync(NetworkResetReceipt receipt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        Directory.CreateDirectory(_root);
        var path = ReceiptPath(receipt.ReceiptId);
        var temp = path + $".{Guid.NewGuid():N}.tmp";
        var identity = ModelIdentity(receipt);
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(path))
            {
                var existing = await LoadPathAsync(path, cancellationToken).ConfigureAwait(false);
                ValidateUpdate(existing, receipt);
            }

            var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(receipt, StrictJson);
            var envelope = new ReceiptEnvelope(
                EnvelopeType,
                SchemaVersion,
                identity,
                Convert.ToHexString(SHA256.HashData(payloadBytes)),
                receipt);
            var envelopeBytes = JsonSerializer.SerializeToUtf8Bytes(envelope, StrictJson);
            if (envelopeBytes.Length > MaximumReceiptBytes)
            {
                throw new InvalidDataException("网络重置收据超过大小限制。");
            }

            await using (var stream = new FileStream(
                temp,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(envelopeBytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            var readBack = await LoadPathAsync(temp, cancellationToken).ConfigureAwait(false);
            var readBackBytes = JsonSerializer.SerializeToUtf8Bytes(readBack, StrictJson);
            if (!CryptographicOperations.FixedTimeEquals(payloadBytes, readBackBytes))
            {
                throw new InvalidDataException("网络重置收据写后回读不一致。");
            }
            File.Move(temp, path, overwrite: true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not NetworkResetReceiptPersistenceException)
        {
            throw new NetworkResetReceiptPersistenceException(receipt.ReceiptId, receipt.PlanId, identity, ex);
        }
        finally
        {
            _mutex.Release();
            try
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            catch
            {
                // The authoritative document is already durable; temporary cleanup is best effort.
            }
        }
    }

    public async Task<IReadOnlyList<NetworkResetReceipt>> LoadPendingAsync(
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_root))
        {
            return Array.Empty<NetworkResetReceipt>();
        }

        var pending = new List<NetworkResetReceipt>();
        foreach (var path in Directory.EnumerateFiles(_root, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var receipt = await LoadPathAsync(path, cancellationToken).ConfigureAwait(false);
            if (receipt.State != NetworkResetReceiptState.Completed
                || receipt.ManualInterventionRequired
                || receipt.RebootRequired)
            {
                pending.Add(ProjectPending(receipt));
            }
        }
        return pending.OrderByDescending(item => item.StartedAtUtc).ToArray();
    }

    internal Task<NetworkResetReceipt> LoadAsync(
        Guid receiptId,
        CancellationToken cancellationToken = default) =>
        LoadPathAsync(ReceiptPath(receiptId), cancellationToken);

    private static async Task<NetworkResetReceipt> LoadPathAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is <= 0 or > MaximumReceiptBytes)
        {
            throw new InvalidDataException("网络重置收据大小无效。");
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var envelope = JsonSerializer.Deserialize<ReceiptEnvelope>(bytes, StrictJson)
            ?? throw new InvalidDataException("网络重置收据 JSON 损坏。");
        if (envelope.SchemaVersion != SchemaVersion
            || !string.Equals(envelope.Type, EnvelopeType, StringComparison.Ordinal)
            || envelope.Payload is null
            || !string.Equals(envelope.ModelIdentity, ModelIdentity(envelope.Payload), StringComparison.Ordinal))
        {
            throw new InvalidDataException("网络重置收据 schema/type/model identity 无效。");
        }

        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(envelope.Payload, StrictJson);
        byte[] expectedHash;
        try
        {
            expectedHash = Convert.FromHexString(envelope.PayloadHash);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("网络重置收据 hash 格式无效。", ex);
        }
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(payloadBytes), expectedHash))
        {
            throw new InvalidDataException("网络重置收据 hash 校验失败。");
        }
        return envelope.Payload;
    }

    private static void ValidateUpdate(NetworkResetReceipt existing, NetworkResetReceipt next)
    {
        if (existing.ReceiptId != next.ReceiptId
            || existing.PlanId != next.PlanId
            || !string.Equals(existing.PlanHash, next.PlanHash, StringComparison.Ordinal)
            || existing.Profile != next.Profile
            || existing.StartedAtUtc != next.StartedAtUtc
            || next.Steps.Count < existing.Steps.Count
            || !existing.Steps.SequenceEqual(next.Steps.Take(existing.Steps.Count)))
        {
            throw new InvalidDataException("网络重置收据更新改变了不可变身份或回退了步骤。");
        }
        if (existing.State != NetworkResetReceiptState.Executing)
        {
            throw new InvalidOperationException("网络重置终态收据不可覆盖。");
        }
    }

    private static NetworkResetReceipt ProjectPending(NetworkResetReceipt receipt)
    {
        if (receipt.State != NetworkResetReceiptState.Executing)
        {
            return receipt;
        }

        if (!receipt.MutationOccurred)
        {
            return receipt with
            {
                State = NetworkResetReceiptState.Unknown,
                Outcome = NetworkResetOutcome.FailedNoChange,
                ReasonCode = "receipt.incomplete-before-write",
                Message = "发现未完成的 durable receipt；最后检查点位于写入前，原计划已禁止重放。",
                ManualInterventionRequired = false,
                RebootRequired = false,
                RecoveryDecision = NetworkRecoveryDecision.NotRequired,
                RecoveryInstructions = "未发现网络写入检查点；不要重放旧计划，可重新 Prepare 生成新计划。"
            };
        }

        return receipt with
        {
            State = NetworkResetReceiptState.Unknown,
            Outcome = NetworkResetOutcome.RecoveryRequired,
            ReasonCode = "receipt.incomplete-write-unknown",
            Message = "发现网络写入后的未完成 durable receipt；状态未知，禁止自动重试。",
            ManualInterventionRequired = true,
            RebootRequired = receipt.Profile != NetworkResetProfile.DnsCache,
            RecoveryDecision = NetworkRecoveryDecision.RecoveryRequired,
            RecoveryInstructions = receipt.Profile == NetworkResetProfile.DnsCache
                ? "不要自动重试；先检查 DNS Client 状态和名称解析，再由用户决定是否重新生成计划。"
                : "不要自动重试。手动重启 Windows 后检查联网、静态 IP、DNS、VPN、代理和自定义路由；异常时依据首次基线人工重配。此流程没有自动 RecoveryId。"
        };
    }

    private static string ModelIdentity(NetworkResetReceipt receipt) =>
        $"NetworkResetReceipt:{receipt.PlanId:N}:{receipt.ReceiptId:N}:{receipt.Profile}";

    private string ReceiptPath(Guid id) => Path.Combine(_root, $"{id:N}.json");

    private static readonly JsonSerializerOptions StrictJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private sealed record ReceiptEnvelope(
        string Type,
        int SchemaVersion,
        string ModelIdentity,
        string PayloadHash,
        NetworkResetReceipt Payload);
}
