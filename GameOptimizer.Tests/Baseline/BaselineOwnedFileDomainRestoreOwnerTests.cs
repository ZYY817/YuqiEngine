using GameOptimizer.Shell.Services.Baseline;

namespace GameOptimizer.Tests.Baseline;

public sealed class BaselineOwnedFileDomainRestoreOwnerTests
{
    [Fact]
    public void HasUsableCapturedPayload_RejectsOwnedStepWhenRequiredPayloadWasNotArchived()
    {
        var owner = new BaselineOwnedFileDomainRestoreOwner();
        var manifest = new BaselineDomainBundleManifest
        {
            DomainId = "D04",
            RestoreSteps = [new BaselineDomainRestoreStep { StepType = "OwnedFileDomain" }]
        };

        Assert.False(owner.HasUsableCapturedPayload("D04", manifest, Array.Empty<string>()));
        Assert.True(owner.HasUsableCapturedPayload("D04", manifest, ["payload/config/settings.json"]));
    }

    [Fact]
    public async Task RestoreMappingsAsync_ReplacesAllowedApplicationDataAndVerifiesContent()
    {
        string root = CreateRoot();
        try
        {
            string bundle = Path.Combine(root, "bundle");
            string source = Path.Combine(bundle, "payload", "config");
            string target = Path.Combine(root, "target", "config");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(target);
            await File.WriteAllTextAsync(Path.Combine(source, "settings.json"), "{\"mode\":\"baseline\"}");
            await File.WriteAllTextAsync(Path.Combine(target, "settings.json"), "{\"mode\":\"changed\"}");

            var owner = new BaselineOwnedFileDomainRestoreOwner();
            BaselineOwnedFileDomainRestoreResult result = await owner.RestoreMappingsAsync(
                [new("payload/config", target, IsDirectory: true, Optional: false)], bundle, default);

            Assert.True(result.Success);
            Assert.Equal(1, result.RestoredTargetCount);
            Assert.Equal("{\"mode\":\"baseline\"}", await File.ReadAllTextAsync(Path.Combine(target, "settings.json")));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task RestoreMappingsAsync_CommitFailureRollsBackEarlierTarget()
    {
        string root = CreateRoot();
        try
        {
            string bundle = Path.Combine(root, "bundle");
            string firstSource = Path.Combine(bundle, "payload", "first");
            string secondSource = Path.Combine(bundle, "payload", "second");
            string firstTarget = Path.Combine(root, "target", "first");
            string blockedParent = Path.Combine(root, "blocked-parent");
            Directory.CreateDirectory(firstSource);
            Directory.CreateDirectory(secondSource);
            Directory.CreateDirectory(firstTarget);
            await File.WriteAllTextAsync(Path.Combine(firstSource, "settings.json"), "{\"mode\":\"baseline\"}");
            await File.WriteAllTextAsync(Path.Combine(secondSource, "settings.json"), "{\"mode\":\"other\"}");
            await File.WriteAllTextAsync(Path.Combine(firstTarget, "settings.json"), "{\"mode\":\"before\"}");
            await File.WriteAllTextAsync(blockedParent, "not a directory");

            var owner = new BaselineOwnedFileDomainRestoreOwner();
            BaselineOwnedFileDomainRestoreResult result = await owner.RestoreMappingsAsync(
                [
                    new("payload/first", firstTarget, IsDirectory: true, Optional: false),
                    new("payload/second", Path.Combine(blockedParent, "second"), IsDirectory: true, Optional: false)
                ], bundle, default);

            Assert.False(result.Success);
            Assert.Equal("{\"mode\":\"before\"}", await File.ReadAllTextAsync(Path.Combine(firstTarget, "settings.json")));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(@"F:\YuqiEngine\Verification\BaselineOwnedFileDomainRestoreOwnerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
