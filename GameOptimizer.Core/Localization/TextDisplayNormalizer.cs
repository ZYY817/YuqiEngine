using System;
using System.Collections.Concurrent;
using System.Text;

namespace GameOptimizer.Core.Localization;

public static class TextDisplayNormalizer
{
	private static readonly ConcurrentDictionary<string, string> Cache = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

	private static readonly Encoding Utf8 = Encoding.UTF8;

	private static readonly Encoding Gbk = BuildGbkEncoding();

	private const string MojibakeMarkers = "鍙鍏鍑鍔鍕鍖鍗鍚鍛鍜鍝鍞鍟鍠鍡鍢鍣鍤鍥鍦鍧鍨鍩鍪鍫鍬鍭鍮鍯鍰鍱鍲鍳鍴鍵鍶鍷鍸鍹鍺鍻鍼鍽鍾鍿绂绋绌绛绠绡绣绤绥绦继绨绩绪绫绬续绮绯绰绱绲绳维绵绶绷绸绹绺绻综绽绾绿缁缂缃缄缅缆缇缈鎴鎵鎶挎捀掷揿搀鏀鏁鏂鏃娓闂闃闄";

	public static string Normalize(string? input, string fallback = "")
	{
		if (string.IsNullOrWhiteSpace(input))
		{
			return fallback;
		}
		string orAdd = Cache.GetOrAdd(input, (string value) => NormalizeCore(value));
		return string.IsNullOrWhiteSpace(orAdd) ? fallback : orAdd;
	}

	public static string? NormalizeNullable(string? input)
	{
		if (string.IsNullOrWhiteSpace(input))
		{
			return null;
		}
		string text = NormalizeCore(input);
		return string.IsNullOrWhiteSpace(text) ? null : text;
	}

	private static string NormalizeCore(string value)
	{
		string text = value.Replace("\0", string.Empty, StringComparison.Ordinal).Replace("\ufffd", string.Empty, StringComparison.Ordinal).Trim();
		if (text.Length == 0)
		{
			return string.Empty;
		}
		string text2 = TryRepairGbkUtf8Mojibake(text);
		if (IsBetterCandidate(text, text2))
		{
			text = text2;
		}
		while (text.Contains("???", StringComparison.Ordinal))
		{
			text = text.Replace("???", "?", StringComparison.Ordinal);
		}
		return text.Trim();
	}

	private static string TryRepairGbkUtf8Mojibake(string text)
	{
		try
		{
			byte[] bytes = Gbk.GetBytes(text);
			return Utf8.GetString(bytes);
		}
		catch
		{
			return text;
		}
	}

	private static bool IsBetterCandidate(string original, string candidate)
	{
		if (string.IsNullOrWhiteSpace(candidate) || string.Equals(original, candidate, StringComparison.Ordinal))
		{
			return false;
		}
		int num = ScoreMojibakeLikelihood(original);
		int num2 = ScoreMojibakeLikelihood(candidate);
		return num2 + 2 <= num;
	}

	private static int ScoreMojibakeLikelihood(string text)
	{
		int num = 0;
		foreach (char c in text)
		{
			if ("鍙鍏鍑鍔鍕鍖鍗鍚鍛鍜鍝鍞鍟鍠鍡鍢鍣鍤鍥鍦鍧鍨鍩鍪鍫鍬鍭鍮鍯鍰鍱鍲鍳鍴鍵鍶鍷鍸鍹鍺鍻鍼鍽鍾鍿绂绋绌绛绠绡绣绤绥绦继绨绩绪绫绬续绮绯绰绱绲绳维绵绶绷绸绹绺绻综绽绾绿缁缂缃缄缅缆缇缈鎴鎵鎶挎捀掷揿搀鏀鏁鏂鏃娓闂闃闄".IndexOf(c) >= 0)
			{
				num += 3;
				continue;
			}
			switch (c)
			{
			case '?':
				num += 2;
				break;
			case '\ufffd':
				num += 4;
				break;
			}
		}
		return num;
	}

	private static Encoding BuildGbkEncoding()
	{
		try
		{
			Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
			return Encoding.GetEncoding("GB18030");
		}
		catch
		{
			return Encoding.UTF8;
		}
	}
}
