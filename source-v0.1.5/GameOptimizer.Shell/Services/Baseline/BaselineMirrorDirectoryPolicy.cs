using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Constants;
using GameOptimizer.Shell.Models;

namespace GameOptimizer.Shell.Services.Baseline;

public static class BaselineMirrorDirectoryPolicy
{
	private const string MirrorRootFolderName = "YuqiEngineBaselineMirror";

	private const long MinFreeBytesForProbe = 33554432L;

	public static string PrimaryBaselineRoot => Path.Combine(AppPaths.BackupsDir, "Baseline");

	public static string PrimaryPackagesRoot => Path.Combine(PrimaryBaselineRoot, "packages");

	public static string GetConfiguredMirrorDirectory()
	{
		AppSettings appSettings = AppSettings.Load();
		return NormalizePath(appSettings.BaselineMirrorDirectory);
	}

	public static bool TrySetConfiguredMirrorDirectory(string? mirrorDirectory, out string normalizedPath, out string errorMessage)
	{
		normalizedPath = string.Empty;
		errorMessage = string.Empty;
		try
		{
			AppSettings appSettings = AppSettings.Load();
			normalizedPath = NormalizePath(mirrorDirectory);
			appSettings.BaselineMirrorDirectory = normalizedPath;
			if (!appSettings.Save())
			{
				errorMessage = "写入设置文件失败。";
				return false;
			}
			return true;
		}
		catch (Exception ex)
		{
			errorMessage = ex.Message;
			normalizedPath = string.Empty;
			return false;
		}
	}

	public static BaselineDirectoryProbeResult ProbePrimaryDirectory(long requiredBytes)
	{
		return ProbeDirectory(PrimaryBaselineRoot, requiredBytes, isConfigured: true);
	}

	public static BaselineDirectoryProbeResult ProbeMirrorDirectory(long requiredBytes)
	{
		string configuredMirrorDirectory = GetConfiguredMirrorDirectory();
		if (string.IsNullOrWhiteSpace(configuredMirrorDirectory))
		{
			return new BaselineDirectoryProbeResult
			{
				IsConfigured = false,
				Success = false,
				NormalizedPath = string.Empty,
				Message = "未配置镜像目录（可选）"
			};
		}
		return ProbeDirectory(configuredMirrorDirectory, requiredBytes, isConfigured: true);
	}

	public static string ResolveMirrorPackagesRoot(string normalizedMirrorDirectory)
	{
		return Path.Combine(normalizedMirrorDirectory, "YuqiEngineBaselineMirror", "packages");
	}

	public static async Task<BaselineMirrorSyncResult> SyncPackageToMirrorAsync(string sourcePackageDirectory, BaselinePackageManifest manifest, IBaselineIntegrityService integrityService, CancellationToken ct = default(CancellationToken))
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(sourcePackageDirectory, "sourcePackageDirectory");
		ArgumentNullException.ThrowIfNull(manifest, "manifest");
		ArgumentNullException.ThrowIfNull(integrityService, "integrityService");
		string mirrorDirectory = GetConfiguredMirrorDirectory();
		if (string.IsNullOrWhiteSpace(mirrorDirectory))
		{
			return new BaselineMirrorSyncResult
			{
				MirrorSyncState = "NotConfigured",
				MirrorDirectory = string.Empty,
				MirrorSyncMessage = "未配置镜像目录，已仅写入主路径。"
			};
		}
		long sourceBytes = ComputeDirectorySizeSafe(sourcePackageDirectory);
		long requiredBytes = Math.Max(33554432L, sourceBytes + 33554432);
		BaselineDirectoryProbeResult mirrorProbe = ProbeDirectory(mirrorDirectory, requiredBytes, isConfigured: true);
		if (!mirrorProbe.Success)
		{
			return new BaselineMirrorSyncResult
			{
				MirrorSyncState = "Failed",
				MirrorDirectory = mirrorProbe.NormalizedPath,
				MirrorSyncMessage = "镜像目录不可用：" + mirrorProbe.Message
			};
		}
		string mirrorPackagesRoot = ResolveMirrorPackagesRoot(mirrorProbe.NormalizedPath);
		Directory.CreateDirectory(mirrorPackagesRoot);
		string finalDirectory = Path.Combine(mirrorPackagesRoot, manifest.PackageId);
		string tempDirectory = Path.Combine(mirrorPackagesRoot, $".sync_{manifest.PackageId}_{Guid.NewGuid():N}");
		try
		{
			await Task.Run(delegate
			{
				ct.ThrowIfCancellationRequested();
				CopyDirectory(sourcePackageDirectory, tempDirectory, overwrite: true, ct);
			}, ct).ConfigureAwait(continueOnCapturedContext: false);
			(bool Success, string ErrorMessage) verifyResult = await integrityService.VerifyPackageAsync(tempDirectory, manifest, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (!verifyResult.Success)
			{
				return new BaselineMirrorSyncResult
				{
					MirrorSyncState = "Failed",
					MirrorDirectory = mirrorProbe.NormalizedPath,
					MirrorSyncMessage = "镜像目录校验失败：" + verifyResult.ErrorMessage
				};
			}
			if (Directory.Exists(finalDirectory))
			{
				Directory.Delete(finalDirectory, recursive: true);
			}
			Directory.Move(tempDirectory, finalDirectory);
			return new BaselineMirrorSyncResult
			{
				MirrorSyncState = "Synced",
				MirrorDirectory = mirrorProbe.NormalizedPath,
				MirrorSyncMessage = "已同步到镜像目录：" + finalDirectory,
				MirrorSyncedUtc = DateTime.UtcNow
			};
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			return new BaselineMirrorSyncResult
			{
				MirrorSyncState = "Failed",
				MirrorDirectory = mirrorProbe.NormalizedPath,
				MirrorSyncMessage = "镜像同步失败：" + ex2.Message
			};
		}
		finally
		{
			TryDeleteDirectorySafe(tempDirectory);
		}
	}

	private static BaselineDirectoryProbeResult ProbeDirectory(string directoryPath, long requiredBytes, bool isConfigured)
	{
		string text = NormalizePath(directoryPath);
		if (string.IsNullOrWhiteSpace(text))
		{
			return new BaselineDirectoryProbeResult
			{
				IsConfigured = isConfigured,
				Success = false,
				NormalizedPath = string.Empty,
				Message = "路径格式无效"
			};
		}
		try
		{
			Directory.CreateDirectory(text);
			string path = Path.Combine(text, $".baseline_probe_{Guid.NewGuid():N}.tmp");
			File.WriteAllText(path, "probe");
			File.Delete(path);
		}
		catch (Exception ex)
		{
			return new BaselineDirectoryProbeResult
			{
				IsConfigured = isConfigured,
				Success = false,
				NormalizedPath = text,
				Message = "不可写：" + ex.Message
			};
		}
		try
		{
			string? pathRoot = Path.GetPathRoot(text);
			if (!string.IsNullOrWhiteSpace(pathRoot))
			{
				DriveInfo driveInfo = new DriveInfo(pathRoot);
				if (!driveInfo.IsReady)
				{
					return new BaselineDirectoryProbeResult
					{
						IsConfigured = isConfigured,
						Success = false,
						NormalizedPath = text,
						Message = "存储设备当前不可用"
					};
				}
				if (requiredBytes > 0 && driveInfo.AvailableFreeSpace < requiredBytes)
				{
					double value = (double)requiredBytes / 1024.0 / 1024.0;
					double value2 = (double)driveInfo.AvailableFreeSpace / 1024.0 / 1024.0;
					return new BaselineDirectoryProbeResult
					{
						IsConfigured = isConfigured,
						Success = false,
						NormalizedPath = text,
						Message = $"剩余空间不足（可用 {value2:F0}MB，至少需要 {value:F0}MB）"
					};
				}
			}
		}
		catch (Exception ex2)
		{
			return new BaselineDirectoryProbeResult
			{
				IsConfigured = isConfigured,
				Success = false,
				NormalizedPath = text,
				Message = "路径连接异常：" + ex2.Message
			};
		}
		return new BaselineDirectoryProbeResult
		{
			IsConfigured = isConfigured,
			Success = true,
			NormalizedPath = text,
			Message = "通过"
		};
	}

	private static string NormalizePath(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return string.Empty;
		}
		try
		{
			string path2 = Environment.ExpandEnvironmentVariables(path.Trim());
			return Path.GetFullPath(path2);
		}
		catch
		{
			return string.Empty;
		}
	}

	private static long ComputeDirectorySizeSafe(string directory)
	{
		try
		{
			if (!Directory.Exists(directory))
			{
				return 0L;
			}
			return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Select(delegate(string path)
			{
				try
				{
					return new FileInfo(path).Length;
				}
				catch
				{
					return 0L;
				}
			}).Sum();
		}
		catch
		{
			return 0L;
		}
	}

	private static void CopyDirectory(string sourceDirectory, string targetDirectory, bool overwrite, CancellationToken ct)
	{
		if (!Directory.Exists(sourceDirectory))
		{
			throw new DirectoryNotFoundException("Source directory not found: " + sourceDirectory);
		}
		Directory.CreateDirectory(targetDirectory);
		foreach (string item in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
		{
			ct.ThrowIfCancellationRequested();
			string relativePath = Path.GetRelativePath(sourceDirectory, item);
			Directory.CreateDirectory(Path.Combine(targetDirectory, relativePath));
		}
		foreach (string item2 in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
		{
			ct.ThrowIfCancellationRequested();
			string relativePath2 = Path.GetRelativePath(sourceDirectory, item2);
			string text = Path.Combine(targetDirectory, relativePath2);
			string? directoryName = Path.GetDirectoryName(text);
			if (!string.IsNullOrWhiteSpace(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			File.Copy(item2, text, overwrite);
		}
	}

	private static void TryDeleteDirectorySafe(string directoryPath)
	{
		if (string.IsNullOrWhiteSpace(directoryPath))
		{
			return;
		}
		try
		{
			if (Directory.Exists(directoryPath))
			{
				Directory.Delete(directoryPath, recursive: true);
			}
		}
		catch
		{
		}
	}
}
