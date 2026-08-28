using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GameOptimizer.Shell.Services.Baseline;

public static class BaselinePreflightCapabilityChecker
{
	private static readonly string[] DefaultRequiredCommands = new string[5] { "cmd.exe", "powershell.exe", "reg.exe", "powercfg.exe", "bcdedit.exe" };

	public static (bool Success, string Message) CheckCommandCapability(IEnumerable<string>? requiredCommands = null)
	{
		List<string> list = (from command in requiredCommands ?? DefaultRequiredCommands
			select (command ?? string.Empty).Trim() into command
			where !string.IsNullOrWhiteSpace(command)
			select command).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToList();
		if (list.Count == 0)
		{
			return (Success: true, Message: "通过");
		}
		List<string> list2 = new List<string>();
		foreach (string item in list)
		{
			if (!TryResolveCommandPath(item, out string _))
			{
				list2.Add(Path.GetFileNameWithoutExtension(item));
			}
		}
		if (list2.Count > 0)
		{
			return (Success: false, Message: "缺少系统命令：" + string.Join(", ", list2));
		}
		return (Success: true, Message: "通过");
	}

	public static bool TryResolveCommandPath(string commandName, out string resolvedPath)
	{
		resolvedPath = string.Empty;
		if (string.IsNullOrWhiteSpace(commandName))
		{
			return false;
		}
		string text = commandName.Trim();
		HashSet<string> candidateSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (Path.IsPathRooted(text))
		{
			AddCandidate(text);
		}
		string text2 = (text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? text : (text + ".exe"));
		AddCandidate(Path.Combine(Environment.SystemDirectory, text2));
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
		AddCandidate(Path.Combine(folderPath, "System32", text2));
		AddCandidate(Path.Combine(folderPath, "Sysnative", text2));
		string text3 = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
		string[] array = text3.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
		foreach (string text4 in array)
		{
			string text5 = text4.Trim();
			if (!string.IsNullOrWhiteSpace(text5))
			{
				AddCandidate(Path.Combine(text5, text2));
			}
		}
		foreach (string item in candidateSet)
		{
			try
			{
				if (!File.Exists(item))
				{
					continue;
				}
				resolvedPath = item;
				return true;
			}
			catch
			{
			}
		}
		return false;
		void AddCandidate(string? candidate)
		{
			if (!string.IsNullOrWhiteSpace(candidate))
			{
				candidateSet.Add(candidate);
			}
		}
	}
}
