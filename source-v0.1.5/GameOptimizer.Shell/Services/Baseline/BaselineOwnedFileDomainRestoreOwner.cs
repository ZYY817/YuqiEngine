using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Constants;

namespace GameOptimizer.Shell.Services.Baseline;

/// <summary>
/// The only automatic file restore path for first-run packages. It owns a small,
/// fixed allow-list of application data roots; it never accepts a target path
/// from a package manifest.
/// </summary>
internal sealed class BaselineOwnedFileDomainRestoreOwner
{
	internal bool HasUsableCapturedPayload(string domainId, BaselineDomainBundleManifest manifest, IEnumerable<string> archiveEntryNames)
	{
		ArgumentNullException.ThrowIfNull(manifest);
		ArgumentNullException.ThrowIfNull(archiveEntryNames);
		if (!string.Equals(manifest.DomainId, domainId, StringComparison.OrdinalIgnoreCase)
			|| manifest.RestoreSteps.Count != 1
			|| !string.Equals(manifest.RestoreSteps[0].StepType, "OwnedFileDomain", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		string[] entries = archiveEntryNames.Select(static entry => entry.Replace('\\', '/')).ToArray();
		IReadOnlyList<BaselineOwnedFileRestoreMapping> mappings = BuildMappings(domainId);
		int presentCount = mappings.Count(mapping => HasPayloadEntry(entries, mapping));
		return mappings.Where(static mapping => !mapping.Optional).All(mapping => HasPayloadEntry(entries, mapping))
			&& presentCount > 0;
	}

	internal async Task<BaselineOwnedFileDomainRestoreResult> RestoreAsync(string domainId, string extractedBundleDirectory, CancellationToken ct)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(domainId);
		ArgumentException.ThrowIfNullOrWhiteSpace(extractedBundleDirectory);
		return await RestoreMappingsAsync(BuildMappings(domainId), extractedBundleDirectory, ct).ConfigureAwait(false);
	}

	internal async Task<BaselineOwnedFileDomainRestoreResult> RestoreMappingsAsync(
		IReadOnlyList<BaselineOwnedFileRestoreMapping> mappings,
		string extractedBundleDirectory,
		CancellationToken ct)
	{
		ArgumentNullException.ThrowIfNull(mappings);
		ArgumentException.ThrowIfNullOrWhiteSpace(extractedBundleDirectory);
		if (mappings.Count == 0)
		{
			return BaselineOwnedFileDomainRestoreResult.Failed("未找到已注册的应用数据恢复范围。");
		}

		string bundleRoot = Path.GetFullPath(extractedBundleDirectory);
		string workRoot = Path.Combine(AppPaths.BackupsDir, "Baseline", "owned-file-restore", Guid.NewGuid().ToString("N"));
		string stageRoot = Path.Combine(workRoot, "stage");
		string rollbackRoot = Path.Combine(workRoot, "rollback");
		List<PreparedMapping> prepared = new();
		try
		{
			Directory.CreateDirectory(stageRoot);
			Directory.CreateDirectory(rollbackRoot);
			for (int index = 0; index < mappings.Count; index++)
			{
				ct.ThrowIfCancellationRequested();
				BaselineOwnedFileRestoreMapping mapping = mappings[index];
				string sourcePath = ResolveBundleSource(bundleRoot, mapping.SourceRelativePath);
				if (!PathExists(sourcePath, mapping.IsDirectory))
				{
					if (mapping.Optional)
					{
						continue;
					}
					return BaselineOwnedFileDomainRestoreResult.Failed("备份包缺少必要应用数据：" + mapping.SourceRelativePath);
				}

				string stagePath = Path.Combine(stageRoot, index.ToString("D2"));
				if (mapping.IsDirectory)
				{
					CopyDirectory(sourcePath, stagePath, ct);
				}
				else
				{
					Directory.CreateDirectory(Path.GetDirectoryName(stagePath) ?? stageRoot);
					File.Copy(sourcePath, stagePath, overwrite: false);
				}
				ValidateJsonPayload(stagePath, mapping.IsDirectory, ct);
				prepared.Add(new PreparedMapping(mapping, stagePath, BuildFingerprint(stagePath, mapping.IsDirectory), Path.Combine(rollbackRoot, index.ToString("D2"))));
			}

			if (prepared.Count == 0)
			{
				return BaselineOwnedFileDomainRestoreResult.Failed("备份包没有可恢复的应用数据，未执行任何写入。");
			}

			List<PreparedMapping> committed = new();
			try
			{
				foreach (PreparedMapping item in prepared)
				{
					ct.ThrowIfCancellationRequested();
					string target = Path.GetFullPath(item.Mapping.TargetPath);
					Directory.CreateDirectory(Path.GetDirectoryName(target) ?? throw new InvalidOperationException("恢复目标没有父目录。"));
					if (PathExists(target, item.Mapping.IsDirectory))
					{
						MovePath(target, item.RollbackPath, item.Mapping.IsDirectory);
					}
					// Why: 原数据一旦移入 RollbackPath 就必须计入回滚集。
					// 若下一步 MovePath 失败而 item 未提交，finally 会连同
					// workRoot 把原始数据永久删除。
					committed.Add(item);
					MovePath(item.StagePath, target, item.Mapping.IsDirectory);
				}

				foreach (PreparedMapping item in committed)
				{
					ct.ThrowIfCancellationRequested();
					string currentFingerprint = BuildFingerprint(item.Mapping.TargetPath, item.Mapping.IsDirectory);
					if (!string.Equals(currentFingerprint, item.ExpectedFingerprint, StringComparison.Ordinal))
					{
						throw new InvalidDataException("恢复后的应用数据与备份内容不一致：" + item.Mapping.SourceRelativePath);
					}
				}
			}
			catch (Exception restoreFailure)
			{
				string rollbackError = RollbackCommittedMappings(committed);
				return BaselineOwnedFileDomainRestoreResult.Failed(
					string.IsNullOrWhiteSpace(rollbackError)
						? "应用数据恢复失败，已回滚到恢复前状态：" + Sanitize(restoreFailure.Message)
						: "应用数据恢复失败，且自动回滚不完整：" + Sanitize(rollbackError));
			}

			return BaselineOwnedFileDomainRestoreResult.Succeeded(prepared.Count);
		}
		catch (OperationCanceledException)
		{
			return BaselineOwnedFileDomainRestoreResult.Failed("应用数据恢复已取消；未确认完成前请查看恢复报告。", cancelled: true);
		}
		catch (Exception ex)
		{
			return BaselineOwnedFileDomainRestoreResult.Failed("应用数据恢复预检失败，未执行写入：" + Sanitize(ex.Message));
		}
		finally
		{
			TryDeleteDirectory(workRoot);
		}
	}

	private static IReadOnlyList<BaselineOwnedFileRestoreMapping> BuildMappings(string domainId) => domainId.ToUpperInvariant() switch
	{
		"D04" =>
		[
			new("payload/config", AppPaths.ConfigDir, IsDirectory: true, Optional: false)
		],
		"D05" =>
		[
			new("payload/affinity-profiles", AppPaths.AffinityProfilesDir, IsDirectory: true, Optional: true),
			new("payload/optimization-profiles", AppPaths.OptimizationProfilesDir, IsDirectory: true, Optional: true),
			new("payload/config/game-profiles.json", AppPaths.GameProfilesFilePath, IsDirectory: false, Optional: true),
			new("payload/config/game-priority-profiles.json", AppPaths.GamePriorityProfilesFilePath, IsDirectory: false, Optional: true)
		],
		"D06" =>
		[
			new("payload/core-accelerator", AppPaths.CoreAcceleratorDir, IsDirectory: true, Optional: false)
		],
		_ => throw new InvalidOperationException("该域未注册应用数据恢复器：" + domainId)
	};

	private static string ResolveBundleSource(string bundleRoot, string relativePath)
	{
		string candidate = Path.GetFullPath(Path.Combine(bundleRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
		if (!candidate.StartsWith(bundleRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("备份包路径越界：" + relativePath);
		}
		return candidate;
	}

	private static bool PathExists(string path, bool isDirectory) => isDirectory ? Directory.Exists(path) : File.Exists(path);

	private static bool HasPayloadEntry(IEnumerable<string> entries, BaselineOwnedFileRestoreMapping mapping)
	{
		string expected = mapping.SourceRelativePath.Trim('/').Replace('\\', '/');
		return mapping.IsDirectory
			? entries.Any(entry => entry.StartsWith(expected + "/", StringComparison.OrdinalIgnoreCase))
			: entries.Any(entry => string.Equals(entry, expected, StringComparison.OrdinalIgnoreCase));
	}

	private static void CopyDirectory(string source, string destination, CancellationToken ct)
	{
		Directory.CreateDirectory(destination);
		foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
		{
			ct.ThrowIfCancellationRequested();
			Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
		}
		foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
		{
			ct.ThrowIfCancellationRequested();
			string target = Path.Combine(destination, Path.GetRelativePath(source, file));
			Directory.CreateDirectory(Path.GetDirectoryName(target) ?? destination);
			File.Copy(file, target, overwrite: false);
		}
	}

	private static void ValidateJsonPayload(string path, bool isDirectory, CancellationToken ct)
	{
		IEnumerable<string> jsonFiles = isDirectory
			? Directory.EnumerateFiles(path, "*.json", SearchOption.AllDirectories)
			: path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? [path] : Array.Empty<string>();
		foreach (string file in jsonFiles)
		{
			ct.ThrowIfCancellationRequested();
			using FileStream stream = File.OpenRead(file);
			using JsonDocument _ = JsonDocument.Parse(stream);
		}
	}

	private static string BuildFingerprint(string path, bool isDirectory)
	{
		using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		if (!isDirectory)
		{
			Append(hash, Path.GetFileName(path));
			AppendFile(hash, path);
			return Convert.ToHexString(hash.GetHashAndReset());
		}
		foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).OrderBy(static item => item, StringComparer.OrdinalIgnoreCase))
		{
			Append(hash, Path.GetRelativePath(path, file).Replace(Path.DirectorySeparatorChar, '/'));
			AppendFile(hash, file);
		}
		return Convert.ToHexString(hash.GetHashAndReset());
	}

	private static void Append(IncrementalHash hash, string value)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(value);
		hash.AppendData(BitConverter.GetBytes(bytes.Length));
		hash.AppendData(bytes);
	}

	private static void AppendFile(IncrementalHash hash, string path)
	{
		using FileStream stream = File.OpenRead(path);
		byte[] buffer = new byte[81920];
		int read;
		while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
		{
			hash.AppendData(buffer, 0, read);
		}
	}

	private static void MovePath(string source, string destination, bool isDirectory)
	{
		if (PathExists(destination, isDirectory))
		{
			throw new IOException("恢复工作目录存在同名冲突。 ");
		}
		Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? throw new InvalidOperationException("恢复工作目录无效。"));
		if (isDirectory)
		{
			Directory.Move(source, destination);
		}
		else
		{
			File.Move(source, destination);
		}
	}

	private static string RollbackCommittedMappings(IEnumerable<PreparedMapping> committed)
	{
		List<string> errors = new();
		foreach (PreparedMapping item in committed.Reverse())
		{
			try
			{
				if (PathExists(item.Mapping.TargetPath, item.Mapping.IsDirectory))
				{
					if (item.Mapping.IsDirectory) Directory.Delete(item.Mapping.TargetPath, recursive: true);
					else File.Delete(item.Mapping.TargetPath);
				}
				if (PathExists(item.RollbackPath, item.Mapping.IsDirectory))
				{
					MovePath(item.RollbackPath, item.Mapping.TargetPath, item.Mapping.IsDirectory);
				}
			}
			catch (Exception rollbackFailure)
			{
				errors.Add(Sanitize(rollbackFailure.Message));
			}
		}
		return string.Join(" | ", errors);
	}

	private static void TryDeleteDirectory(string path)
	{
		try
		{
			if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}

	private static string Sanitize(string? message) => string.IsNullOrWhiteSpace(message) ? "未知错误。" : message.Replace('\r', ' ').Replace('\n', ' ');

	private sealed record PreparedMapping(BaselineOwnedFileRestoreMapping Mapping, string StagePath, string ExpectedFingerprint, string RollbackPath);
}

internal sealed record BaselineOwnedFileRestoreMapping(string SourceRelativePath, string TargetPath, bool IsDirectory, bool Optional);

internal sealed record BaselineOwnedFileDomainRestoreResult(bool Success, bool Cancelled, int RestoredTargetCount, string Message)
{
	public static BaselineOwnedFileDomainRestoreResult Succeeded(int restoredTargetCount) =>
		new(true, false, restoredTargetCount, $"已事务恢复 {restoredTargetCount} 项应用数据并完成内容回读验证；建议重新打开对应页面以加载新配置。");

	public static BaselineOwnedFileDomainRestoreResult Failed(string message, bool cancelled = false) =>
		new(false, cancelled, 0, message);
}
