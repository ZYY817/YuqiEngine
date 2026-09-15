using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Infrastructure.Windows.Changes;

namespace GameOptimizer.Tests.Changes;

public sealed class FileChangeStateStoreDurabilityTests
{
    [Fact]
    public async Task Receipt_TamperedPayloadWithStaleEnvelopeHash_IsRejected()
    {
        var root = EvidenceDirectory();
        var store = new FileChangeStateStore(root);
        var audit = (IChangeAuditStore)store;
        var receipt = CreateReceipt("original");
        await audit.SaveAsync(receipt, CancellationToken.None);
        var path = ReceiptPath(root, receipt.ReceiptId);
        var document = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        document["payload"]!["message"] = "tampered";
        await File.WriteAllTextAsync(path, document.ToJsonString(), new UTF8Encoding(false, true));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => audit.LoadAsync(receipt.ReceiptId, CancellationToken.None));
    }

    [Fact]
    public async Task Receipt_UnsupportedSchemaAndUnknownEnvelopeMember_AreRejected()
    {
        var root = EvidenceDirectory();
        var store = new FileChangeStateStore(root);
        var audit = (IChangeAuditStore)store;
        var receipt = CreateReceipt("schema");
        await audit.SaveAsync(receipt, CancellationToken.None);
        var path = ReceiptPath(root, receipt.ReceiptId);
        var document = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        document["schemaVersion"] = DurableChangeDocument.CurrentSchemaVersion + 1;
        await File.WriteAllTextAsync(path, document.ToJsonString(), new UTF8Encoding(false, true));
        await Assert.ThrowsAsync<InvalidDataException>(
            () => audit.LoadAsync(receipt.ReceiptId, CancellationToken.None));

        document["schemaVersion"] = DurableChangeDocument.CurrentSchemaVersion;
        document["unexpected"] = true;
        await File.WriteAllTextAsync(path, document.ToJsonString(), new UTF8Encoding(false, true));
        await Assert.ThrowsAnyAsync<JsonException>(
            () => audit.LoadAsync(receipt.ReceiptId, CancellationToken.None));
    }

    [Fact]
    public async Task Receipt_BadJsonAndOversizeDocument_AreRejected()
    {
        var root = EvidenceDirectory();
        var store = new FileChangeStateStore(root);
        var audit = (IChangeAuditStore)store;
        var receipt = CreateReceipt("invalid");
        var path = ReceiptPath(root, receipt.ReceiptId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{not-json", new UTF8Encoding(false, true));
        await Assert.ThrowsAnyAsync<JsonException>(
            () => audit.LoadAsync(receipt.ReceiptId, CancellationToken.None));

        await File.WriteAllBytesAsync(path, new byte[DurableChangeDocument.MaximumDocumentBytes + 1]);
        await Assert.ThrowsAsync<InvalidDataException>(
            () => audit.LoadAsync(receipt.ReceiptId, CancellationToken.None));
    }

    [Fact]
    public async Task Receipt_FileNameIdentityMismatch_IsRejected()
    {
        var root = EvidenceDirectory();
        var store = new FileChangeStateStore(root);
        var audit = (IChangeAuditStore)store;
        var receipt = CreateReceipt("identity");
        await audit.SaveAsync(receipt, CancellationToken.None);
        var otherId = Guid.NewGuid();
        var otherPath = ReceiptPath(root, otherId);
        File.Copy(ReceiptPath(root, receipt.ReceiptId), otherPath);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => audit.LoadAsync(otherId, CancellationToken.None));
    }

    [Fact]
    public async Task Receipt_Update_CannotReplaceImmutableOperationIdentity()
    {
        var root = EvidenceDirectory();
        var store = new FileChangeStateStore(root);
        var audit = (IChangeAuditStore)store;
        var receipt = CreateReceipt("identity");
        await audit.SaveAsync(receipt, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            audit.SaveAsync(receipt with { PlanId = Guid.NewGuid() }, CancellationToken.None));
    }

    [Fact]
    public async Task Plan_EnvelopeRehashedAfterPayloadTamper_IsRejectedByDomainHash()
    {
        var root = EvidenceDirectory();
        var store = new FileChangeStateStore(root);
        var plan = FileChangeCoordinationTestPlan.Create();
        await store.RegisterAsync(plan, CancellationToken.None);
        var path = Path.Combine(root, "plans", "issued", plan.PlanId.ToString("N") + ".json");
        var document = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        document["payload"]!["change"]!["targetPayload"] = "tampered-target";
        var tamperedPlan = document["payload"]!.Deserialize<PreparedChangePlan>(DurableChangeDocument.SerializerOptions)!;
        document["payloadSha256"] = DurableChangeDocument.PayloadHash(tamperedPlan);
        await File.WriteAllTextAsync(path, document.ToJsonString(), new UTF8Encoding(false, true));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.TryClaimAsync(plan.PlanId, plan.PlanHash, CancellationToken.None));
    }

    [Fact]
    public async Task Plan_Schema2IsRejectedAndSchema3RoundTrips()
    {
        var root = EvidenceDirectory();
        var store = new FileChangeStateStore(root);
        var plan = FileChangeCoordinationTestPlan.Create();
        await store.RegisterAsync(plan, CancellationToken.None);
        var path = Path.Combine(root, "plans", "issued", plan.PlanId.ToString("N") + ".json");
        var document = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.Equal(3, document["schemaVersion"]!.GetValue<int>());
        Assert.Equal(PlanClaimStatus.Claimed, await store.TryClaimAsync(plan.PlanId, plan.PlanHash, CancellationToken.None));

        var legacyRoot = EvidenceDirectory();
        var legacyStore = new FileChangeStateStore(legacyRoot);
        var legacyPlan = FileChangeCoordinationTestPlan.Create();
        await legacyStore.RegisterAsync(legacyPlan, CancellationToken.None);
        var legacyPath = Path.Combine(legacyRoot, "plans", "issued", legacyPlan.PlanId.ToString("N") + ".json");
        var legacyDocument = JsonNode.Parse(await File.ReadAllTextAsync(legacyPath))!.AsObject();
        legacyDocument["schemaVersion"] = 2;
        await File.WriteAllTextAsync(legacyPath, legacyDocument.ToJsonString(), new UTF8Encoding(false, true));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => legacyStore.TryClaimAsync(legacyPlan.PlanId, legacyPlan.PlanHash, CancellationToken.None));
    }

    [Fact]
    public async Task IssuedPlan_IsImmutableAndCannotBeRegisteredTwice()
    {
        var root = EvidenceDirectory();
        var store = new FileChangeStateStore(root);
        var plan = FileChangeCoordinationTestPlan.Create();
        await store.RegisterAsync(plan, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.RegisterAsync(plan, CancellationToken.None));
    }

    [Fact]
    public async Task RecoveryMaterial_IsIdempotentOnlyForIdenticalContent()
    {
        var root = EvidenceDirectory();
        var store = new FileChangeStateStore(root);
        var material = FileChangeCoordinationTestPlan.CreateRecovery("before");
        await store.SaveAsync(material, CancellationToken.None);
        await store.SaveAsync(material, CancellationToken.None);

        var different = FileChangeCoordinationTestPlan.CreateRecovery(
            "different-before",
            material.RecoveryId,
            material.PlanId);
        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.SaveAsync(different, CancellationToken.None));
    }

    [Fact]
    public async Task RecoveryMaterial_ReceiptBindingRoundTripsAcrossStoreInstances()
    {
        var root = EvidenceDirectory();
        var material = FileChangeCoordinationTestPlan.CreateRecovery("before", includeReceiptBinding: true);
        var writer = (IRecoveryRepository)new FileChangeStateStore(root);
        await writer.SaveAsync(material, CancellationToken.None);

        var reader = (IRecoveryRepository)new FileChangeStateStore(root);
        var reloaded = await reader.LoadAsync(material.RecoveryId, CancellationToken.None);

        Assert.NotNull(reloaded);
        Assert.NotEqual(Guid.Empty, reloaded.ReceiptId);
        Assert.Equal(material.ReceiptId, reloaded.ReceiptId);
    }

    [Fact]
    public async Task Recovery_EnvelopeRehashedAfterPayloadTamper_IsRejectedByDomainHash()
    {
        var root = EvidenceDirectory();
        var store = new FileChangeStateStore(root);
        var repository = (IRecoveryRepository)store;
        var material = FileChangeCoordinationTestPlan.CreateRecovery("before");
        await repository.SaveAsync(material, CancellationToken.None);
        var path = Path.Combine(root, "recovery", material.RecoveryId.ToString("N") + ".json");
        var document = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        document["payload"]!["beforePayload"] = "tampered-before";
        var tampered = document["payload"]!.Deserialize<RecoveryMaterial>(DurableChangeDocument.SerializerOptions)!;
        document["payloadSha256"] = DurableChangeDocument.PayloadHash(tampered);
        await File.WriteAllTextAsync(path, document.ToJsonString(), new UTF8Encoding(false, true));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => repository.LoadAsync(material.RecoveryId, CancellationToken.None));
    }

    [Fact]
    public async Task Recovery_EnvelopeRehashedAfterReceiptBindingTamper_IsRejected()
    {
        var root = EvidenceDirectory();
        var store = new FileChangeStateStore(root);
        var repository = (IRecoveryRepository)store;
        var material = FileChangeCoordinationTestPlan.CreateRecovery("before", includeReceiptBinding: true);
        await repository.SaveAsync(material, CancellationToken.None);
        var path = Path.Combine(root, "recovery", material.RecoveryId.ToString("N") + ".json");
        var document = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        document["payload"]!["receiptId"] = Guid.NewGuid();
        var tampered = document["payload"]!.Deserialize<RecoveryMaterial>(DurableChangeDocument.SerializerOptions)!;
        document["payloadSha256"] = DurableChangeDocument.PayloadHash(tampered);
        await File.WriteAllTextAsync(path, document.ToJsonString(), new UTF8Encoding(false, true));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => repository.LoadAsync(material.RecoveryId, CancellationToken.None));
    }

    [Fact]
    public async Task ConcurrentReceiptReplacement_AlwaysLeavesOneCompleteDocumentAndNoOwnTemporaryFile()
    {
        var root = EvidenceDirectory();
        var store = new FileChangeStateStore(root);
        var audit = (IChangeAuditStore)store;
        var receipt = CreateReceipt("initial");
        await audit.SaveAsync(receipt, CancellationToken.None);

        var writes = Enumerable.Range(0, 16)
            .Select(index => audit.SaveAsync(receipt with { Message = "message-" + index }, CancellationToken.None));
        await Task.WhenAll(writes);

        var loaded = await audit.LoadAsync(receipt.ReceiptId, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.StartsWith("message-", loaded.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(root, "receipts"), "*.tmp-*"));
    }

    [Fact]
    public async Task CancelledWrite_DoesNotPublishPartialDocumentOrLeaveOwnTemporaryFile()
    {
        var root = EvidenceDirectory();
        var store = new FileChangeStateStore(root);
        var audit = (IChangeAuditStore)store;
        var receipt = CreateReceipt(new string('x', 512 * 1024));
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => audit.SaveAsync(receipt, source.Token));

        var receiptDirectory = Path.Combine(root, "receipts");
        Assert.False(File.Exists(ReceiptPath(root, receipt.ReceiptId)));
        Assert.True(!Directory.Exists(receiptDirectory)
            || !Directory.EnumerateFiles(receiptDirectory, "*.tmp-*").Any());
    }

    [Fact]
    public async Task ConcurrentReceiptWriters_PreserveRecoveryRequiredAcrossStoreInstances()
    {
        var root = EvidenceDirectory();
        var first = (IChangeAuditStore)new FileChangeStateStore(root);
        var second = (IChangeAuditStore)new FileChangeStateStore(root);
        var baseline = CreateReceipt("baseline") with { Outcome = ChangeOutcome.SucceededVerified };
        var recoveryRequired = baseline with
        {
            Outcome = ChangeOutcome.RecoveryRequired,
            Message = "manual recovery required"
        };
        var weaker = baseline with
        {
            Outcome = ChangeOutcome.SucceededVerified,
            Message = "late stale success",
            CompletedAtUtc = baseline.CompletedAtUtc.AddMinutes(1)
        };

        await Task.WhenAll(
            first.SaveAsync(recoveryRequired, CancellationToken.None),
            second.SaveAsync(weaker, CancellationToken.None));

        var persisted = await first.LoadAsync(baseline.ReceiptId, CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal(ChangeOutcome.RecoveryRequired, persisted.Outcome);
        Assert.Equal("manual recovery required", persisted.Message);
    }

    [Fact]
    public async Task StaleReceiptTemporaryFile_IsRemovedBeforeACompleteCommit()
    {
        var root = EvidenceDirectory();
        var receipt = CreateReceipt("after crash");
        var receiptDirectory = Path.Combine(root, "receipts");
        Directory.CreateDirectory(receiptDirectory);
        var stalePath = Path.Combine(receiptDirectory, $".{receipt.ReceiptId:N}.json.tmp-crashed");
        await File.WriteAllTextAsync(stalePath, "partial", new UTF8Encoding(false, true));
        File.SetLastWriteTimeUtc(stalePath, DateTime.UtcNow.AddMinutes(-10));

        var audit = (IChangeAuditStore)new FileChangeStateStore(root);
        await audit.SaveAsync(receipt, CancellationToken.None);

        Assert.False(File.Exists(stalePath));
        Assert.Equal(receipt.Message, (await audit.LoadAsync(receipt.ReceiptId, CancellationToken.None))!.Message);
    }

    [Fact]
    public async Task ConcurrentReceiptReadback_SeesOnlyCompleteOldOrNewDocuments()
    {
        var root = EvidenceDirectory();
        var audit = (IChangeAuditStore)new FileChangeStateStore(root);
        var receipt = CreateReceipt("initial");
        await audit.SaveAsync(receipt, CancellationToken.None);

        var writes = Enumerable.Range(0, 12)
            .Select(index => audit.SaveAsync(receipt with { Message = "write-" + index }, CancellationToken.None));
        var reads = Enumerable.Range(0, 24)
            .Select(async _ =>
            {
                var loaded = await audit.LoadAsync(receipt.ReceiptId, CancellationToken.None);
                Assert.NotNull(loaded);
                Assert.True(
                    loaded.Message == "initial"
                    || loaded.Message.StartsWith("write-", StringComparison.Ordinal));
            });

        await Task.WhenAll(writes.Concat(reads));
        Assert.NotNull(await audit.LoadAsync(receipt.ReceiptId, CancellationToken.None));
    }

    private static OperationReceipt CreateReceipt(string message)
    {
        var now = DateTimeOffset.UtcNow;
        return new OperationReceipt(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            "test.capability",
            TargetScope.CurrentUser("test-user"),
            ChangeOutcome.FailedNoChange,
            "before",
            "target",
            "before",
            "test.reason",
            message,
            false,
            now,
            now.AddMilliseconds(1));
    }

    private static string ReceiptPath(string root, Guid receiptId) =>
        Path.Combine(root, "receipts", receiptId.ToString("N") + ".json");

    private static string EvidenceDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "file-store-evidence", Guid.NewGuid().ToString("N"));
}

internal static class FileChangeCoordinationTestPlan
{
    internal static PreparedChangePlan Create()
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(5);
        var policy = ChangeExecutionPolicy.Default;
        var change = new PreparedCapabilityChange(
            "test.capability",
            "1",
            TargetScope.CurrentUser("test-user"),
            "before",
            "before-fingerprint",
            "target",
            "target-fingerprint",
            "test.resource");
        var planId = Guid.NewGuid();
        var hash = Hash(
            planId.ToString("N"),
            change.CapabilityId,
            change.DefinitionVersion,
            change.Scope.Kind.ToString(),
            change.Scope.Identity,
            change.BeforePayload,
            change.BeforeFingerprint,
            change.TargetPayload,
            change.TargetFingerprint,
            change.ResourceKey,
            now.UtcTicks.ToString(),
            expires.UtcTicks.ToString(),
            false.ToString(),
            false.ToString(),
            true.ToString(),
            ChangeRisk.LowReversible.ToString(),
            policy.Version,
            policy.Fingerprint);
        return new PreparedChangePlan(
            planId,
            change,
            hash,
            now,
            expires,
            false,
            false,
            true,
            ChangeRisk.LowReversible,
            policy.Version,
            policy.Fingerprint);
    }

    internal static RecoveryMaterial CreateRecovery(
        string beforePayload,
        Guid? recoveryId = null,
        Guid? planId = null,
        bool includeReceiptBinding = false)
    {
        var actualRecoveryId = recoveryId ?? Guid.NewGuid();
        var actualPlanId = planId ?? Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;
        var scope = TargetScope.CurrentUser("test-user");
        var hash = Hash(
            actualRecoveryId.ToString("N"),
            actualPlanId.ToString("N"),
            "test.capability",
            "1",
            scope.Kind.ToString(),
            scope.Identity,
            beforePayload,
            "before-fingerprint",
            "target-fingerprint",
            "test.resource",
            createdAt.UtcTicks.ToString());
        return new RecoveryMaterial(
            actualRecoveryId,
            actualPlanId,
            "test.capability",
            "1",
            scope,
            beforePayload,
            "before-fingerprint",
            "target-fingerprint",
            "test.resource",
            createdAt,
            hash,
            includeReceiptBinding
                ? RecoveryMaterialBinding.ComputeReceiptId(actualRecoveryId, "test.resource", hash)
                : Guid.Empty);
    }

    private static string Hash(params string[] values) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes(string.Join('\u001F', values))));
}
