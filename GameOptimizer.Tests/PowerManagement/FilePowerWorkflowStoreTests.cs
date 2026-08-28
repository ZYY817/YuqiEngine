using System.Text;
using System.Security.Cryptography;
using GameOptimizer.Core.PowerManagement;
using GameOptimizer.Infrastructure.Windows.PowerManagement;

namespace GameOptimizer.Tests.PowerManagement;

public sealed class FilePowerWorkflowStoreTests
{
    [Fact]
    public async Task EnvelopeAndProtectedSecretPersistAcrossStoreInstances()
    {
        await WithDirectoryAsync(async directory =>
        {
            var protector = new XorProtector();
            var first = new FilePowerWorkflowStore(directory, protector);
            var plan = CreatePlan();
            await first.SavePreparedAsync(plan, default);
            var confirmationSecret = first.GetConfirmationSecret();

            var second = new FilePowerWorkflowStore(directory, protector);
            Assert.Equal(plan, await second.GetPreparedAsync(plan.PlanId, default));
            Assert.Equal(confirmationSecret, second.GetConfirmationSecret());
            var envelope = await File.ReadAllTextAsync(Path.Combine(directory, $"plan-{plan.PlanId:D}.json"));
            Assert.Contains("\"schemaVersion\":1", envelope);
            Assert.Contains("\"documentType\":\"prepared-plan\"", envelope);
            Assert.Contains("\"ownerId\":\"gameoptimizer.power-management\"", envelope);
            Assert.Contains("\"integrityMac\"", envelope);
            Assert.True(File.Exists(Path.Combine(directory, ".power-secret.dpapi")));
        });
    }

    [Fact]
    public async Task DpapiProtectedConfirmationSecretPersistsAndPlanClaimIsCrossInstanceAtomic()
    {
        await WithDirectoryAsync(async directory =>
        {
            var first = new FilePowerWorkflowStore(directory);
            var second = new FilePowerWorkflowStore(directory);
            var firstSecret = first.GetConfirmationSecret();
            var secondSecret = second.GetConfirmationSecret();
            Assert.Equal(firstSecret, secondSecret);
            Assert.Equal(32, firstSecret.Length);
            var plan = CreatePlan();
            await first.SavePreparedAsync(plan, default);
            var claims = await Task.WhenAll(
                first.TryClaimPlanAsync(plan.PlanId, plan.PlanHash, default),
                second.TryClaimPlanAsync(plan.PlanId, plan.PlanHash, default));
            Assert.Single(claims, claimed => claimed);
        });
    }

    [Theory]
    [InlineData("\"schemaVersion\":1", "\"schemaVersion\":2")]
    [InlineData("\"documentType\":\"prepared-plan\"", "\"documentType\":\"receipt\"")]
    [InlineData("\"ownerId\":\"gameoptimizer.power-management\"", "\"ownerId\":\"other\"")]
    [InlineData("display-timeout-and-active-scheme", "processor-timeout")]
    public async Task TamperedEnvelopeIdentityFailsClosed(string original, string replacement)
    {
        await WithDirectoryAsync(async directory =>
        {
            var store = new FilePowerWorkflowStore(directory, new XorProtector());
            var plan = CreatePlan();
            await store.SavePreparedAsync(plan, default);
            var path = Path.Combine(directory, $"plan-{plan.PlanId:D}.json");
            var json = await File.ReadAllTextAsync(path);
            await File.WriteAllTextAsync(path, json.Replace(original, replacement, StringComparison.Ordinal), new UTF8Encoding(false));
            await Assert.ThrowsAsync<InvalidDataException>(() => store.GetPreparedAsync(plan.PlanId, default));
        });
    }

    [Fact]
    public async Task LegacyDocumentWithoutMacAndOversizedDocumentFailClosed()
    {
        await WithDirectoryAsync(async directory =>
        {
            var store = new FilePowerWorkflowStore(directory, new XorProtector());
            var id = Guid.NewGuid();
            var path = Path.Combine(directory, $"plan-{id:D}.json");
            await File.WriteAllTextAsync(path, "{\"payload\":{}}", new UTF8Encoding(false));
            await Assert.ThrowsAsync<InvalidDataException>(() => store.GetPreparedAsync(id, default));
            await File.WriteAllBytesAsync(path, new byte[1024 * 1024 + 1]);
            await Assert.ThrowsAsync<InvalidDataException>(() => store.GetPreparedAsync(id, default));
        });
    }

    [Fact]
    public async Task ReceiptOwnerScopeResourceAndRangeAreStrictlyValidated()
    {
        await WithDirectoryAsync(async directory =>
        {
            var store = new FilePowerWorkflowStore(directory, new XorProtector());
            var receipt = CreateReceipt() with
            {
                OwnerId = "other",
                OriginalDisplayTimeout = new PowerSettingValues(PowerManagementIdentity.MaximumDisplayTimeoutSeconds + 1, 300)
            };
            await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveReceiptAsync(receipt, default));
        });
    }

    [Fact]
    public async Task ResourceLeaseSerializesDifferentStoreInstances()
    {
        await WithDirectoryAsync(async directory =>
        {
            var first = new FilePowerWorkflowStore(directory, new XorProtector());
            var second = new FilePowerWorkflowStore(directory, new XorProtector());
            var resource = PowerManagementIdentity.DisplayTimeoutAndActiveSchemeResource;
            var firstLease = await first.AcquireResourceLeaseAsync(resource, TimeSpan.FromSeconds(1), default);
            var waiting = second.AcquireResourceLeaseAsync(resource, TimeSpan.FromSeconds(1), default);
            await Task.Delay(50);
            Assert.False(waiting.IsCompleted);
            await firstLease.DisposeAsync();
            await using var secondLease = await waiting;
            Assert.Equal(resource, secondLease.ResourceKey);
        });
    }

    [Fact]
    public async Task CrossInstanceReceiptTerminalUpdateAllowsOnlyOneWinner()
    {
        await WithDirectoryAsync(async directory =>
        {
            var protector = new XorProtector();
            var first = new FilePowerWorkflowStore(directory, protector);
            var second = new FilePowerWorkflowStore(directory, protector);
            var receipt = CreateReceipt();
            await first.SaveReceiptAsync(receipt, default);
            var applied = first.SaveReceiptAsync(receipt with { State = PowerReceiptState.Applied, ApplyVerified = true }, default);
            var failed = second.SaveReceiptAsync(receipt with { State = PowerReceiptState.ApplyFailed, FailureDetail = "failed" }, default);
            var results = await Task.WhenAll(ObserveAsync(applied), ObserveAsync(failed));
            Assert.Single(results, success => success);
            var stored = await first.GetReceiptAsync(receipt.ReceiptId, default);
            Assert.Contains(stored!.State, new[] { PowerReceiptState.Applied, PowerReceiptState.ApplyFailed });
        });
    }

    [Fact]
    public async Task PersistenceLockHonorsCancellationAndProtectorFailureIsVisible()
    {
        await WithDirectoryAsync(async directory =>
        {
            var receipt = CreateReceipt();
            var store = new FilePowerWorkflowStore(directory, new XorProtector());
            var lockPath = Path.Combine(directory, $"receipt-{receipt.ReceiptId:D}.json.update.lock");
            Directory.CreateDirectory(directory);
            await using (var held = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveReceiptAsync(receipt, cts.Token));
            }

            var broken = new FilePowerWorkflowStore(Path.Combine(directory, "broken"), new ThrowingProtector());
            await Assert.ThrowsAsync<CryptographicException>(() => broken.SavePreparedAsync(CreatePlan(), default));
        });
    }

    private static PowerPreparedPlan CreatePlan()
    {
        var target = new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
        var plan = new PowerPreparedPlan(Guid.NewGuid(), string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5),
            new Guid("381b4222-f694-41f0-9685-ff5bb260df2e"), target, new(600, 300), new(900, 420),
            "risk", "drawback", true, true, false, false, ResourceKey: PowerManagementIdentity.DisplayTimeoutAndActiveSchemeResource);
        return plan with { PlanHash = PowerManagementOwner.ComputeHash(plan) };
    }

    private static PowerReceipt CreateReceipt()
    {
        var plan = CreatePlan();
        return new PowerReceipt(Guid.NewGuid(), plan.PlanId, plan.PlanHash, DateTimeOffset.UtcNow, plan.OriginalActiveSchemeId,
            plan.TargetSchemeId, plan.OriginalDisplayTimeout, plan.TargetDisplayTimeout, "baseline-1", false, false,
            State: PowerReceiptState.Executing, ResourceKey: plan.ResourceKey);
    }

    private static async Task<bool> ObserveAsync(Task task)
    {
        try { await task; return true; }
        catch (InvalidDataException) { return false; }
    }

    private static async Task WithDirectoryAsync(Func<string, Task> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "GameOptimizer-PowerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { await action(directory); }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    private sealed class XorProtector : IPowerDataProtector
    {
        public byte[] Protect(byte[] plaintext) => plaintext.Select(value => (byte)(value ^ 0xA5)).ToArray();
        public byte[] Unprotect(byte[] protectedData) => Protect(protectedData);
    }

    private sealed class ThrowingProtector : IPowerDataProtector
    {
        public byte[] Protect(byte[] plaintext) => throw new CryptographicException("fake protector failure");
        public byte[] Unprotect(byte[] protectedData) => throw new CryptographicException("fake protector failure");
    }
}
