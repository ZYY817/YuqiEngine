using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace GameOptimizer.Shell.Services.Baseline;

public sealed class BaselineIntegrityService : IBaselineIntegrityService
{
	private const string ManifestFileName = "manifest.json";

	private const string ChecksumFileName = "checksum.txt";

	private readonly ILogger<BaselineIntegrityService> _logger;

	public BaselineIntegrityService(ILogger<BaselineIntegrityService> logger)
	{
		_logger = logger;
	}

	public async Task<BaselinePackageManifest> WriteManifestAsync(string packageDirectory, BaselinePackageManifest manifest, CancellationToken ct = default(CancellationToken))
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory, "packageDirectory");
		ArgumentNullException.ThrowIfNull(manifest, "manifest");
		Directory.CreateDirectory(packageDirectory);
		manifest.UpdatedUtc = DateTime.UtcNow;
		string manifestPath = Path.Combine(packageDirectory, "manifest.json");
		string manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
		{
			WriteIndented = true,
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase
		});
		await File.WriteAllTextAsync(manifestPath, manifestJson, Encoding.UTF8, ct).ConfigureAwait(continueOnCapturedContext: false);
		StringBuilder checksumBuilder = new StringBuilder();
		foreach (BaselineDomainArtifact domain in manifest.Domains)
		{
			StringBuilder stringBuilder = checksumBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(2, 3, stringBuilder);
			handler.AppendFormatted(domain.DomainId);
			handler.AppendLiteral("|");
			handler.AppendFormatted(domain.Sha256);
			handler.AppendLiteral("|");
			handler.AppendFormatted(domain.SizeBytes);
			stringBuilder.AppendLine(ref handler);
		}
		string checksumPath = Path.Combine(packageDirectory, "checksum.txt");
		await File.WriteAllTextAsync(checksumPath, checksumBuilder.ToString(), Encoding.UTF8, ct).ConfigureAwait(continueOnCapturedContext: false);
		return manifest;
	}

	public async Task<(bool Success, string ErrorMessage)> VerifyPackageAsync(string packageDirectory, BaselinePackageManifest manifest, CancellationToken ct = default(CancellationToken))
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory, "packageDirectory");
		ArgumentNullException.ThrowIfNull(manifest, "manifest");
		try
		{
			foreach (BaselineDomainArtifact domain in manifest.Domains)
			{
				if (domain.Status == BaselineDomainStatus.Failed && domain.Required)
				{
					return (Success: false, ErrorMessage: "Domain " + domain.DomainId + " failed and is required.");
				}
				if (string.IsNullOrWhiteSpace(domain.RelativePath))
				{
					if (domain.Required)
					{
						return (Success: false, ErrorMessage: "Domain " + domain.DomainId + " artifact path is empty.");
					}
					continue;
				}
				string fullPath = Path.Combine(packageDirectory, domain.RelativePath);
				if (!File.Exists(fullPath))
				{
					if (domain.Required)
					{
						return (Success: false, ErrorMessage: "Domain " + domain.DomainId + " artifact missing: " + domain.RelativePath);
					}

					continue;
				}
				FileInfo fileInfo = new FileInfo(fullPath);
				if (fileInfo.Length != domain.SizeBytes)
				{
					return (Success: false, ErrorMessage: "Domain " + domain.DomainId + " size mismatch.");
				}
				string hash = BaselineHashHelper.ComputeSha256(fullPath);
				if (hash.Equals(domain.Sha256, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				return (Success: false, ErrorMessage: "Domain " + domain.DomainId + " checksum mismatch.");
			}
			string manifestPath = Path.Combine(packageDirectory, "manifest.json");
			if (!File.Exists(manifestPath))
			{
				return (Success: false, ErrorMessage: "manifest.json missing.");
			}
			string checksumPath = Path.Combine(packageDirectory, "checksum.txt");
			if (!File.Exists(checksumPath))
			{
				return (Success: false, ErrorMessage: "checksum.txt missing.");
			}
			await Task.CompletedTask;
			return (Success: true, ErrorMessage: string.Empty);
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			_logger.LogWarning(ex2, "[BaselineIntegrityService][VerifyPackageAsync] Package verification failed. package={PackageDir}", packageDirectory);
			return (Success: false, ErrorMessage: ex2.Message);
		}
	}
}
