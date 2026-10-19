// File: GameOptimizer.Shell/Services/AiAssistant/AiCredentialProtector.cs
using System.Security.Cryptography;
using System.Text;

namespace GameOptimizer.Shell.Services.AiAssistant;

/// <summary>
/// 用 Windows DPAPI（当前用户域）保护 API 密钥，配置文件落盘的不是明文；
/// 拷到其他机器/其他用户无法解密。
/// </summary>
public static class AiCredentialProtector
{
    // 与密钥本体拼接的域分隔熵，防止同值在其他用途被复用解密。
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("YuqiEngine.AiCredential.v1");

    public static string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            return string.Empty;
        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plaintext),
            Entropy,
            DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    public static string Unprotect(string? protectedBase64)
    {
        if (string.IsNullOrWhiteSpace(protectedBase64))
            return string.Empty;
        try
        {
            var bytes = ProtectedData.Unprotect(
                Convert.FromBase64String(protectedBase64),
                Entropy,
                DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception)
        {
            // Why: 解密失败（换机器/换用户/数据损坏）时按"未配置密钥"处理，不抛出。
            return string.Empty;
        }
    }
}
