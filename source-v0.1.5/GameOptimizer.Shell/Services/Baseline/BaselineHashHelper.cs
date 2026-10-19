using System;
using System.IO;
using System.Security.Cryptography;

namespace GameOptimizer.Shell.Services.Baseline;

internal static class BaselineHashHelper
{
	public static string ComputeSha256(string filePath)
	{
		using FileStream source = File.OpenRead(filePath);
		byte[] inArray = SHA256.HashData(source);
		return Convert.ToHexString(inArray).ToLowerInvariant();
	}
}
