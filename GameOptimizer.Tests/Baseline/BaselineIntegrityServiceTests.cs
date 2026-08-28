using System.Text.Json;
using GameOptimizer.Shell.Services.Baseline;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameOptimizer.Tests.Baseline;

public sealed class BaselineIntegrityServiceTests
{
    private readonly BaselineIntegrityService _service =
        new(NullLogger<BaselineIntegrityService>.Instance);

    [Fact]
    public async Task VerifyPackageAsync_RejectsArtifactWithMismatchedHash()
    {
        string packageDirectory = CreateUniqueTestDirectory();
        const string relativePath = "D03.bundle.zip";
        string artifactPath = Path.Combine(packageDirectory, relativePath);
        await File.WriteAllTextAsync(artifactPath, "corrupted baseline artifact");

        var manifest = new BaselinePackageManifest
        {
            Domains =
            {
                new BaselineDomainArtifact
                {
                    DomainId = "D03",
                    Required = true,
                    Status = BaselineDomainStatus.Succeeded,
                    RelativePath = relativePath,
                    SizeBytes = new FileInfo(artifactPath).Length,
                    Sha256 = new string('0', 64)
                }
            }
        };

        await _service.WriteManifestAsync(packageDirectory, manifest);

        (bool success, string errorMessage) =
            await _service.VerifyPackageAsync(packageDirectory, manifest);

        Assert.False(success);
        Assert.Contains("checksum mismatch", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyPackageAsync_RejectsRequiredArtifactWithNullPathFromJson()
    {
        const string corruptedJson =
            """
            {
              "domains": [
                {
                  "domainId": "D03",
                  "required": true,
                  "status": 2,
                  "relativePath": null
                }
              ]
            }
            """;
        BaselinePackageManifest? manifest =
            JsonSerializer.Deserialize<BaselinePackageManifest>(
                corruptedJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (manifest is null)
        {
            throw new InvalidOperationException("Test fixture JSON did not produce a manifest.");
        }

        (bool success, string errorMessage) =
            await _service.VerifyPackageAsync(CreateUniqueTestDirectory(), manifest);

        Assert.False(success);
        Assert.Contains("artifact path is empty", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyPackageAsync_AllowsUnavailableOptionalAuditArtifactWithoutWeakeningRequiredArtifacts()
    {
        string packageDirectory = CreateUniqueTestDirectory();
        var manifest = new BaselinePackageManifest
        {
            Domains =
            {
                new BaselineDomainArtifact
                {
                    DomainId = "D09",
                    Required = false,
                    Status = BaselineDomainStatus.Failed,
                    RelativePath = "D09.bundle.zip"
                }
            }
        };

        await _service.WriteManifestAsync(packageDirectory, manifest);

        (bool success, string errorMessage) =
            await _service.VerifyPackageAsync(packageDirectory, manifest);

        Assert.True(success, errorMessage);
    }

    [Fact]
    public async Task WriteManifestAsync_PropagatesCancellationWithoutReturningSuccess()
    {
        string packageDirectory = CreateUniqueTestPath();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _service.WriteManifestAsync(
                packageDirectory,
                new BaselinePackageManifest(),
                cancellation.Token));
    }

    [Fact]
    public async Task WriteManifestAsync_PropagatesStorageFailureWithoutReturningSuccess()
    {
        string parentDirectory = CreateUniqueTestDirectory();
        string fileBlockingDirectoryCreation = Path.Combine(parentDirectory, "not-a-directory");
        await File.WriteAllTextAsync(fileBlockingDirectoryCreation, "storage failure fixture");

        await Assert.ThrowsAnyAsync<IOException>(() =>
            _service.WriteManifestAsync(
                fileBlockingDirectoryCreation,
                new BaselinePackageManifest(),
                CancellationToken.None));
    }

    private static string CreateUniqueTestDirectory()
    {
        string path = CreateUniqueTestPath();
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CreateUniqueTestPath()
    {
        return Path.Combine(
            @"F:\YuqiEngine\Verification\BaselineIntegrityServiceTests",
            Guid.NewGuid().ToString("N"));
    }
}
