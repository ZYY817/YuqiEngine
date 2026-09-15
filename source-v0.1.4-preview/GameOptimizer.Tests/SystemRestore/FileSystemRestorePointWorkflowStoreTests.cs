using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GameOptimizer.Core.SystemRestore;
using GameOptimizer.Infrastructure.Windows.SystemRestore;

namespace GameOptimizer.Tests.SystemRestore;

public sealed class FileSystemRestorePointWorkflowStoreTests
{
    private static readonly byte[] AuthenticationKey = Enumerable.Repeat((byte)0x6D, 32).ToArray();

    [Fact]
    public async Task AuthenticatedDocumentsRoundTripAndReplayIsDurablyBlocked()
    {
        await WithStoreAsync(async (directory, store) =>
        {
            var plan = CreatePlan();
            await store.SaveCreatePlanAsync(plan, default);
            var loadedPlan = Assert.IsType<SystemRestorePointCreatePlan>(
                await store.LoadCreatePlanAsync(plan.PlanId, default));
            Assert.Equal(plan.PlanId, loadedPlan.PlanId);
            Assert.Equal(plan.PlanHash, loadedPlan.PlanHash);
            Assert.Equal(plan.Description, loadedPlan.Description);

            var claim = CreateClaim(plan);
            Assert.Equal(claim, await store.TryClaimCreatePlanAsync(claim, default));
            var secondProcess = new FileSystemRestorePointWorkflowStore(directory, AuthenticationKey);
            Assert.Null(await secondProcess.TryClaimCreatePlanAsync(claim with { ClaimId = Guid.NewGuid() }, default));

            var receipt = CreateReceipt(plan, claim);
            await store.SaveReceiptAsync(receipt, default);
            var terminalReceipt = receipt with
            {
                CompletedAtUtc = receipt.StartedAtUtc.AddSeconds(2),
                State = SystemRestorePointReceiptState.Failed,
                MutationOutcome = SystemRestorePointMutationOutcome.Failed,
                MutationState = SystemRestorePointMutationState.NotChanged,
                FailureCode = "TEST.FAIL"
            };
            await store.SaveReceiptAsync(terminalReceipt, default);
            Assert.Equal(terminalReceipt, await secondProcess.LoadReceiptAsync(receipt.ReceiptId, default));

            var terminalClaim = claim with
            {
                UpdatedAtUtc = claim.UpdatedAtUtc.AddSeconds(2),
                State = SystemRestorePointCreateClaimState.Failed,
                MutationState = SystemRestorePointMutationState.NotChanged,
                FailureCode = "TEST.FAIL"
            };
            await secondProcess.SaveCreateClaimAsync(terminalClaim, default);
            Assert.Equal(terminalClaim, await store.LoadCreateClaimAsync(plan.PlanId, plan.PlanHash, default));

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                store.SaveCreateClaimAsync(terminalClaim with { State = SystemRestorePointCreateClaimState.Verified }, default));
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                store.SaveReceiptAsync(receipt, default));
        });
    }

    [Fact]
    public async Task RecomputedPlainHashCannotForgeTheAuthenticatedEnvelope()
    {
        await WithStoreAsync(async (directory, store) =>
        {
            var plan = CreatePlan();
            await store.SaveCreatePlanAsync(plan, default);
            var path = Path.Combine(directory, $"create-plan-{plan.PlanId:D}.json");
            var document = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
            var payloadBytes = Convert.FromBase64String(document["payload"]!.GetValue<string>());
            var payload = JsonNode.Parse(payloadBytes)!.AsObject();
            payload["description"] = "forged";
            payloadBytes = Encoding.UTF8.GetBytes(payload.ToJsonString());
            document["payload"] = Convert.ToBase64String(payloadBytes);
            document["payloadSha256"] = Convert.ToHexString(SHA256.HashData(payloadBytes));
            await File.WriteAllTextAsync(path, document.ToJsonString(), new UTF8Encoding(false));

            await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadCreatePlanAsync(plan.PlanId, default));
        });
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("truncate")]
    [InlineData("wrong-key")]
    public async Task SchemaCorruptionTruncationAndWrongKeyFailClosed(string fault)
    {
        await WithStoreAsync(async (directory, store) =>
        {
            var plan = CreatePlan();
            await store.SaveCreatePlanAsync(plan, default);
            var path = Path.Combine(directory, $"create-plan-{plan.PlanId:D}.json");
            FileSystemRestorePointWorkflowStore reader = store;
            if (fault == "schema")
            {
                var document = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
                document["schemaVersion"] = 1;
                await File.WriteAllTextAsync(path, document.ToJsonString(), new UTF8Encoding(false));
            }
            else if (fault == "truncate")
            {
                await File.WriteAllTextAsync(path, "{\"schemaVersion\":2", new UTF8Encoding(false));
            }
            else
            {
                reader = new FileSystemRestorePointWorkflowStore(directory, Enumerable.Repeat((byte)0x39, 32).ToArray());
            }

            await Assert.ThrowsAsync<InvalidDataException>(() => reader.LoadCreatePlanAsync(plan.PlanId, default));
        });
    }

    [Fact]
    public async Task ClaimHashMismatchAndReceiptIdentityDriftFailClosed()
    {
        await WithStoreAsync(async (_, store) =>
        {
            var plan = CreatePlan();
            await store.SaveCreatePlanAsync(plan, default);
            var claim = CreateClaim(plan);
            await store.TryClaimCreatePlanAsync(claim, default);
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                store.LoadCreateClaimAsync(plan.PlanId, new string('B', 64), default));

            var receipt = CreateReceipt(plan, claim);
            await store.SaveReceiptAsync(receipt, default);
            await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveReceiptAsync(
                receipt with
                {
                    State = SystemRestorePointReceiptState.Failed,
                    PlanId = Guid.NewGuid(),
                    MutationState = SystemRestorePointMutationState.NotChanged
                },
                default));
        });
    }

    [Fact]
    public async Task ResourceLeaseSerializesIndependentStoreInstances()
    {
        await WithStoreAsync(async (directory, first) =>
        {
            var second = new FileSystemRestorePointWorkflowStore(directory, AuthenticationKey);
            await using var held = await first.AcquireResourceLeaseAsync(
                SystemRestorePointIdentityConstants.ResourceKey,
                TimeSpan.FromSeconds(1),
                default);

            await Assert.ThrowsAsync<TimeoutException>(() => second.AcquireResourceLeaseAsync(
                SystemRestorePointIdentityConstants.ResourceKey,
                TimeSpan.FromMilliseconds(120),
                default));

            await held.DisposeAsync();
            await using var reacquired = await second.AcquireResourceLeaseAsync(
                SystemRestorePointIdentityConstants.ResourceKey,
                TimeSpan.FromSeconds(1),
                default);
            Assert.Equal(SystemRestorePointIdentityConstants.ResourceKey, reacquired.ResourceKey);
        });
    }

    [Fact]
    public async Task KeyRotationRetainsAuthenticatedOldDocumentsAndRejectsMissingRetiredKey()
    {
        await WithStoreAsync(async (directory, store) =>
        {
            var plan = CreatePlan();
            await store.SaveCreatePlanAsync(plan, default);
            var nextKey = Enumerable.Repeat((byte)0x7E, 32).ToArray();

            await store.RotateAuthenticationKeyAsync(nextKey, default);
            Assert.NotNull(await store.LoadCreatePlanAsync(plan.PlanId, default));

            var withRetiredKey = new FileSystemRestorePointWorkflowStore(directory, nextKey, new[] { AuthenticationKey });
            Assert.NotNull(await withRetiredKey.LoadCreatePlanAsync(plan.PlanId, default));

            var withoutRetiredKey = new FileSystemRestorePointWorkflowStore(directory, nextKey);
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                withoutRetiredKey.LoadCreatePlanAsync(plan.PlanId, default));
        });
    }

    [Fact]
    public async Task LegacySchemaRequiresExplicitAuthenticatedMigrationAndKeepsBackup()
    {
        await WithStoreAsync(async (directory, store) =>
        {
            var plan = CreatePlan();
            var payload = JsonSerializer.SerializeToUtf8Bytes(plan, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var payloadHash = Convert.ToHexString(SHA256.HashData(payload));
            var canonical = Encoding.UTF8.GetBytes($"2\ncreate-plan\n{plan.PlanId:D}\n{payloadHash}");
            var hmac = Convert.ToHexString(HMACSHA256.HashData(AuthenticationKey, canonical));
            var legacy = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = 2,
                kind = "create-plan",
                identity = plan.PlanId,
                payloadSha256 = payloadHash,
                envelopeHmacSha256 = hmac,
                payload
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var path = Path.Combine(directory, $"create-plan-{plan.PlanId:D}.json");
            await File.WriteAllBytesAsync(path, legacy);

            var failure = await Assert.ThrowsAsync<InvalidDataException>(() =>
                store.LoadCreatePlanAsync(plan.PlanId, default));
            Assert.Contains("explicit authenticated migration", failure.Message, StringComparison.OrdinalIgnoreCase);

            Assert.Equal(
                SystemRestorePointStoreMigrationDisposition.Migrated,
                await store.MigrateLegacyDocumentAsync("create-plan", plan.PlanId, default));
            Assert.NotNull(await store.LoadCreatePlanAsync(plan.PlanId, default));
            Assert.Single(Directory.GetFiles(directory, $"create-plan-{plan.PlanId:D}.json.schema2-*.bak"));
        });
    }

    [Fact]
    public async Task LegacySchemaWithInvalidMacAndUnknownFutureSchemaFailClosed()
    {
        await WithStoreAsync(async (directory, store) =>
        {
            var plan = CreatePlan();
            var payload = JsonSerializer.SerializeToUtf8Bytes(plan, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var payloadHash = Convert.ToHexString(SHA256.HashData(payload));
            var path = Path.Combine(directory, $"create-plan-{plan.PlanId:D}.json");
            var invalidLegacy = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = 2,
                kind = "create-plan",
                identity = plan.PlanId,
                payloadSha256 = payloadHash,
                envelopeHmacSha256 = new string('0', 64),
                payload
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            await File.WriteAllBytesAsync(path, invalidLegacy);
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                store.MigrateLegacyDocumentAsync("create-plan", plan.PlanId, default));

            var future = JsonNode.Parse(invalidLegacy)!.AsObject();
            future["schemaVersion"] = 99;
            await File.WriteAllTextAsync(path, future.ToJsonString(), new UTF8Encoding(false));
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                store.MigrateLegacyDocumentAsync("create-plan", plan.PlanId, default));
        });
    }

    [Fact]
    public async Task CorruptReceiptRecoveryQuarantinesBytesAndWritesAuthenticatedUnknown()
    {
        await WithStoreAsync(async (directory, store) =>
        {
            var plan = CreatePlan();
            var claim = CreateClaim(plan);
            var receipt = CreateReceipt(plan, claim);
            await store.SaveReceiptAsync(receipt, default);
            var path = Path.Combine(directory, $"receipt-{receipt.ReceiptId:D}.json");
            await File.WriteAllTextAsync(path, "{corrupt", new UTF8Encoding(false));
            var replacement = receipt with
            {
                CompletedAtUtc = receipt.StartedAtUtc.AddSeconds(3),
                State = SystemRestorePointReceiptState.Unknown,
                MutationOutcome = SystemRestorePointMutationOutcome.Unknown,
                MutationState = SystemRestorePointMutationState.Unknown,
                FailureCode = "SYSTEM_RESTORE.CREATE.INTERRUPTED"
            };

            var recovered = await store.RecoverReceiptAsync(replacement, default);

            Assert.Equal(SystemRestorePointReceiptRecoveryDisposition.ReplacedCorrupt, recovered.Disposition);
            Assert.True(File.Exists(recovered.QuarantinedPath));
            Assert.Equal(replacement, await store.LoadReceiptAsync(receipt.ReceiptId, default));
        });
    }

    private static SystemRestorePointCreatePlan CreatePlan()
    {
        var now = new DateTimeOffset(2026, 8, 13, 6, 0, 0, TimeSpan.Zero);
        return new SystemRestorePointCreatePlan(
            Guid.NewGuid(),
            new string('A', 64),
            now,
            now.AddMinutes(5),
            "fake only",
            "test",
            null,
            @"\\?\Volume{11111111-1111-1111-1111-111111111111}\",
            Array.Empty<SystemRestorePointIdentity>(),
            TimeSpan.Zero,
            true,
            true);
    }

    private static SystemRestorePointCreateClaim CreateClaim(SystemRestorePointCreatePlan plan)
    {
        var now = plan.PreparedAtUtc.AddSeconds(1);
        return new SystemRestorePointCreateClaim(
            Guid.NewGuid(),
            plan.PlanId,
            plan.PlanHash,
            Guid.NewGuid(),
            now,
            now,
            SystemRestorePointCreateClaimState.Executing,
            SystemRestorePointMutationState.Unknown);
    }

    private static SystemRestorePointReceipt CreateReceipt(
        SystemRestorePointCreatePlan plan,
        SystemRestorePointCreateClaim claim) => new(
            claim.ReceiptId,
            plan.PlanId,
            plan.PlanHash,
            claim.ClaimedAtUtc,
            null,
            plan.VolumeIdentity,
            plan.Description,
            plan.Source,
            plan.BaselineId,
            SystemRestorePointReceiptState.Executing,
            SystemRestorePointMutationOutcome.Unknown,
            null,
            false,
            false,
            MutationState: SystemRestorePointMutationState.Unknown,
            ClaimId: claim.ClaimId);

    private static async Task WithStoreAsync(
        Func<string, FileSystemRestorePointWorkflowStore, Task> test)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "system-restore-store-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await test(directory, new FileSystemRestorePointWorkflowStore(directory, AuthenticationKey));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
