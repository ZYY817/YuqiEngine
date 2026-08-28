using System;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using GameOptimizer.Core.Constants;
using Microsoft.Win32;

namespace GameOptimizer.Shell.Services.Baseline;

/// <summary>
/// Privacy-preserving binding between a baseline package and the environment in
/// which its system state was captured. Raw machine and user identities are
/// intentionally never persisted in the package.
/// </summary>
public sealed class BaselineEnvironmentBinding
{
	public string MachineFingerprintSha256 { get; set; } = string.Empty;

	public string UserSidSha256 { get; set; } = string.Empty;

	public string OsBuild { get; set; } = string.Empty;

	public string OsArchitecture { get; set; } = string.Empty;

	public string AppDataRootSha256 { get; set; } = string.Empty;
}

public sealed record BaselineEnvironmentCompatibilityResult(
	bool IsCompatible,
	string ReasonCode,
	string Message);

internal static class BaselineEnvironmentBindingPolicy
{
	internal static BaselineEnvironmentBinding CaptureCurrent()
	{
		string machineStableId = ReadMachineStableId();
		string userSid = ReadCurrentUserSid();
		return new BaselineEnvironmentBinding
		{
			MachineFingerprintSha256 = Hash(machineStableId),
			UserSidSha256 = Hash(userSid),
			OsBuild = Environment.OSVersion.Version.ToString(),
			OsArchitecture = Environment.Is64BitOperatingSystem ? "x64" : "x86",
			AppDataRootSha256 = Hash(AppPaths.AppDataRoot)
		};
	}

	internal static BaselineEnvironmentCompatibilityResult Evaluate(
		BaselineEnvironmentBinding? captured,
		BaselineEnvironmentBinding current)
	{
		ArgumentNullException.ThrowIfNull(current);
		if (captured is null
			|| string.IsNullOrWhiteSpace(captured.MachineFingerprintSha256)
			|| string.IsNullOrWhiteSpace(captured.UserSidSha256)
			|| string.IsNullOrWhiteSpace(captured.OsBuild)
			|| string.IsNullOrWhiteSpace(captured.OsArchitecture)
			|| string.IsNullOrWhiteSpace(captured.AppDataRootSha256))
		{
			return new(false, "baseline.environment_binding_missing", "基线包缺少同机同用户兼容性证据，已阻断恢复。");
		}

		if (!string.Equals(captured.MachineFingerprintSha256, current.MachineFingerprintSha256, StringComparison.Ordinal))
		{
			return new(false, "baseline.environment_machine_mismatch", "该基线包不是在当前设备创建，系统状态恢复已阻断。");
		}
		if (!string.Equals(captured.UserSidSha256, current.UserSidSha256, StringComparison.Ordinal))
		{
			return new(false, "baseline.environment_user_mismatch", "该基线包属于其他 Windows 用户，已阻断恢复。");
		}
		if (!string.Equals(captured.OsBuild, current.OsBuild, StringComparison.Ordinal)
			|| !string.Equals(captured.OsArchitecture, current.OsArchitecture, StringComparison.Ordinal))
		{
			return new(false, "baseline.environment_os_mismatch", "当前 Windows 版本或系统架构与基线创建时不一致，已阻断恢复。");
		}
		if (!string.Equals(captured.AppDataRootSha256, current.AppDataRootSha256, StringComparison.Ordinal))
		{
			return new(false, "baseline.environment_data_root_mismatch", "应用数据根目录与基线创建时不一致，已阻断恢复。");
		}

		return new(true, "baseline.environment_compatible", "同机、同用户与系统环境预检通过。");
	}

	private static string ReadMachineStableId()
	{
		try
		{
			string? machineGuid = Registry.GetValue(
				@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography",
				"MachineGuid",
				null) as string;
			if (!string.IsNullOrWhiteSpace(machineGuid))
			{
				return machineGuid.Trim();
			}
		}
		catch (Exception) when (OperatingSystem.IsWindows())
		{
			// A missing read permission is handled by the stable fallback below.
		}

		return Environment.MachineName;
	}

	private static string ReadCurrentUserSid()
	{
		try
		{
			using WindowsIdentity identity = WindowsIdentity.GetCurrent();
			return identity.User?.Value ?? string.Empty;
		}
		catch (PlatformNotSupportedException)
		{
			return string.Empty;
		}
	}

	private static string Hash(string input) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input ?? string.Empty)));
}
