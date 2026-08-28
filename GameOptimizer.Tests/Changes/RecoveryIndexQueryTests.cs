using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.Changes;
using GameOptimizer.Infrastructure.Windows.Changes;

namespace GameOptimizer.Tests.Changes;

public sealed class RecoveryIndexQueryTests
{
    [Fact]
    public async Task ListAsync_ClassifiesDurableStatesAndExcludesRestoredAcrossInstances()
    {
        var fixture = new Fixture();
        var recoverable = await fixture.AddAsync("recoverable", ChangeOutcome.SucceededVerified, "execution.verified");
        var restoring = await fixture.AddAsync("restoring", ChangeOutcome.SucceededVerified, "execution.verified");
        _ = await fixture.Claims.TryBeginRestoreAsync(restoring.RecoveryId, restoring.IntegrityHash, CancellationToken.None);
        var manual = await fixture.AddAsync("manual", ChangeOutcome.RecoveryRequired, "restore.state-unknown");
        _ = await fixture.Claims.TryBeginRestoreAsync(manual.RecoveryId, manual.IntegrityHash, CancellationToken.None);
        var missingClaim = await fixture.AddAsync("missing-claim", ChangeOutcome.SucceededVerified, "execution.verified", registerClaim: false);
        var restored = await fixture.AddAsync("restored", ChangeOutcome.SucceededVerified, "execution.verified");
        var restoredClaim = await fixture.Claims.TryBeginRestoreAsync(restored.RecoveryId, restored.IntegrityHash, CancellationToken.None);
        await fixture.Claims.MarkRestoredAsync(restored.RecoveryId, restored.IntegrityHash, restoredClaim.ClaimId!.Value, CancellationToken.None);

        IRecoveryIndexQuery restarted = new FileChangeStateStore(fixture.Root);
        var entries = await restarted.ListAsync(CancellationToken.None);

        Assert.Equal(4, entries.Count);
        Assert.Equal(RecoveryIndexStatus.Recoverable, Assert.Single(entries, entry => entry.RecoveryId == recoverable.RecoveryId).Status);
        Assert.Equal(RecoveryIndexStatus.Restoring, Assert.Single(entries, entry => entry.RecoveryId == restoring.RecoveryId).Status);
        var manualEntry = Assert.Single(entries, entry => entry.RecoveryId == manual.RecoveryId);
        Assert.Equal(RecoveryIndexStatus.ManualRecovery, manualEntry.Status);
        Assert.Equal("restore.state-unknown", manualEntry.TerminalReasonCode);
        var missingClaimEntry = Assert.Single(entries, entry => entry.RecoveryId == missingClaim.RecoveryId);
        Assert.Equal(RecoveryIndexStatus.ManualRecovery, missingClaimEntry.Status);
        Assert.Equal("index.claim-missing", missingClaimEntry.TerminalReasonCode);
        Assert.DoesNotContain(entries, entry => entry.RecoveryId == restored.RecoveryId);
    }

    [Fact]
    public async Task ListAsync_MaterialTamperIsSurfacedWithoutTrustingSensitivePayload()
    {
        var fixture = new Fixture();
        var material = await fixture.AddAsync("tampered", ChangeOutcome.SucceededVerified, "execution.verified");
        var path = Path.Combine(fixture.Root, "recovery", material.RecoveryId.ToString("N") + ".json");
        var document = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        document["payload"]!["beforePayload"] = "PRIVATE-TAMPERED-VALUE";
        var tampered = document["payload"]!.Deserialize<RecoveryMaterial>(DurableChangeDocument.SerializerOptions)!;
        document["payloadSha256"] = DurableChangeDocument.PayloadHash(tampered);
        await File.WriteAllTextAsync(path, document.ToJsonString(), new UTF8Encoding(false, true));

        var entries = await ((IRecoveryIndexQuery)new FileChangeStateStore(fixture.Root)).ListAsync(CancellationToken.None);

        var entry = Assert.Single(entries);
        Assert.Equal(RecoveryIndexStatus.ManualRecovery, entry.Status);
        Assert.Equal("index.material-invalid", entry.TerminalReasonCode);
        Assert.Equal("index.unavailable", entry.ResourceKey);
        Assert.DoesNotContain("PRIVATE", JsonSerializer.Serialize(entry), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListAsync_ClaimTamperRequiresManualRecoveryAfterRestart()
    {
        var fixture = new Fixture();
        var material = await fixture.AddAsync("claim-tampered", ChangeOutcome.SucceededVerified, "execution.verified");
        var claimPath = Path.Combine(fixture.Root, "recovery-claims", $"{material.RecoveryId:N}.available");
        await File.WriteAllTextAsync(claimPath, "{not-json", new UTF8Encoding(false, true));

        var entries = await ((IRecoveryIndexQuery)new FileChangeStateStore(fixture.Root)).ListAsync(CancellationToken.None);

        var entry = Assert.Single(entries);
        Assert.Equal(RecoveryIndexStatus.ManualRecovery, entry.Status);
        Assert.Equal("index.claim-invalid", entry.TerminalReasonCode);
    }

    [Fact]
    public async Task ListAsync_ReceiptIdentityTamperRequiresManualRecovery()
    {
        var fixture = new Fixture();
        var material = await fixture.AddAsync("receipt-tampered", ChangeOutcome.SucceededVerified, "execution.verified");
        var path = Path.Combine(fixture.Root, "receipts", material.ReceiptId.ToString("N") + ".json");
        var document = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        document["payload"]!["recoveryId"] = Guid.NewGuid();
        var tampered = document["payload"]!.Deserialize<OperationReceipt>(DurableChangeDocument.SerializerOptions)!;
        document["payloadSha256"] = DurableChangeDocument.PayloadHash(tampered);
        await File.WriteAllTextAsync(path, document.ToJsonString(), new UTF8Encoding(false, true));

        var entries = await ((IRecoveryIndexQuery)new FileChangeStateStore(fixture.Root)).ListAsync(CancellationToken.None);

        var entry = Assert.Single(entries);
        Assert.Equal(RecoveryIndexStatus.ManualRecovery, entry.Status);
        Assert.Equal("index.receipt-material-mismatch", entry.TerminalReasonCode);
    }

    [Fact]
    public async Task ListAsync_SortsByLatestDurableTimestampThenRecoveryId()
    {
        var fixture = new Fixture();
        var baseTime = DateTimeOffset.UtcNow.AddMinutes(10);
        var laterId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var earlierTieId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var laterTieId = Guid.Parse("00000000-0000-0000-0000-000000000003");
        await fixture.AddAsync("latest", ChangeOutcome.SucceededVerified, "execution.verified", laterId, baseTime.AddMinutes(1));
        await fixture.AddAsync("tie-a", ChangeOutcome.SucceededVerified, "execution.verified", earlierTieId, baseTime);
        await fixture.AddAsync("tie-b", ChangeOutcome.SucceededVerified, "execution.verified", laterTieId, baseTime);

        var entries = await ((IRecoveryIndexQuery)new FileChangeStateStore(fixture.Root)).ListAsync(CancellationToken.None);

        Assert.Equal([laterId, earlierTieId, laterTieId], entries.Select(static entry => entry.RecoveryId));
    }

    [Fact]
    public async Task ListAsync_DoesNotExposePayloadScopeFingerprintMessageOrUnsafeReason()
    {
        var fixture = new Fixture();
        await fixture.AddAsync(
            "sensitive",
            ChangeOutcome.RecoveryRequired,
            "token_SECRET_REASON",
            beforePayload: "password=SECRET-PAYLOAD",
            scopeIdentity: "S-1-SECRET-SCOPE",
            receiptMessage: "token=SECRET-MESSAGE");

        var entries = await ((IRecoveryIndexQuery)new FileChangeStateStore(fixture.Root)).ListAsync(CancellationToken.None);
        var serialized = JsonSerializer.Serialize(Assert.Single(entries));

        Assert.DoesNotContain("SECRET", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("audit.receipt-missing", Assert.Single(entries).TerminalReasonCode);
    }

    [Fact]
    public async Task ListAsync_IsReadOnlyAndDoesNotCreateOrModifyPersistentFiles()
    {
        var fixture = new Fixture();
        await fixture.AddAsync("read-only", ChangeOutcome.SucceededVerified, "execution.verified");
        var before = PersistentFileEvidence(fixture.Root);

        _ = await ((IRecoveryIndexQuery)new FileChangeStateStore(fixture.Root)).ListAsync(CancellationToken.None);

        Assert.Equal(before, PersistentFileEvidence(fixture.Root));
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Root = Path.Combine(AppContext.BaseDirectory, "recovery-index-evidence", Guid.NewGuid().ToString("N"));
            Store = new FileChangeStateStore(Root);
            Claims = new FileRecoveryClaimStore(Path.Combine(Root, "recovery-claims"));
        }

        public string Root { get; }
        public FileChangeStateStore Store { get; }
        public FileRecoveryClaimStore Claims { get; }

        public async Task<RecoveryMaterial> AddAsync(
            string identity,
            ChangeOutcome outcome,
            string reasonCode,
            Guid? recoveryId = null,
            DateTimeOffset? completedAt = null,
            string? beforePayload = null,
            string? scopeIdentity = null,
            string? receiptMessage = null,
            bool registerClaim = true)
        {
            var completed = completedAt ?? DateTimeOffset.UtcNow;
            var material = CreateMaterial(
                identity,
                recoveryId ?? Guid.NewGuid(),
                completed.AddSeconds(-1),
                beforePayload ?? $"before-{identity}",
                scopeIdentity ?? $"scope-{identity}");
            await ((IRecoveryRepository)Store).SaveAsync(material, CancellationToken.None);
            if (registerClaim)
            {
                await Claims.RegisterAvailableAsync(material.RecoveryId, material.IntegrityHash, CancellationToken.None);
            }
            await ((IChangeAuditStore)Store).SaveAsync(
                CreateReceipt(material, outcome, reasonCode, receiptMessage ?? $"message-{identity}", completed),
                CancellationToken.None);
            return material;
        }
    }

    private static RecoveryMaterial CreateMaterial(
        string identity,
        Guid recoveryId,
        DateTimeOffset createdAt,
        string beforePayload,
        string scopeIdentity)
    {
        var planId = Guid.NewGuid();
        var scope = TargetScope.CurrentUser(scopeIdentity);
        var resourceKey = $"test.resource.{identity}";
        var integrityHash = Hash(
            recoveryId.ToString("N"),
            planId.ToString("N"),
            "test.recovery-index",
            "1",
            scope.Kind.ToString(),
            scope.Identity,
            beforePayload,
            $"before-fingerprint-{identity}",
            $"target-fingerprint-{identity}",
            resourceKey,
            createdAt.UtcTicks.ToString());
        return new RecoveryMaterial(
            recoveryId,
            planId,
            "test.recovery-index",
            "1",
            scope,
            beforePayload,
            $"before-fingerprint-{identity}",
            $"target-fingerprint-{identity}",
            resourceKey,
            createdAt,
            integrityHash,
            RecoveryMaterialBinding.ComputeReceiptId(recoveryId, resourceKey, integrityHash));
    }

    private static OperationReceipt CreateReceipt(
        RecoveryMaterial material,
        ChangeOutcome outcome,
        string reasonCode,
        string message,
        DateTimeOffset completedAt) =>
        new(
            material.ReceiptId,
            material.PlanId,
            material.RecoveryId,
            material.CapabilityId,
            material.Scope,
            outcome,
            material.BeforeFingerprint,
            material.TargetFingerprint,
            material.TargetFingerprint,
            reasonCode,
            message,
            true,
            material.CreatedAtUtc,
            completedAt);

    private static string Hash(params string[] values) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001F', values))));

    private static string[] PersistentFileEvidence(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => $"{Path.GetRelativePath(root, path)}:{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))}")
            .ToArray();
}
