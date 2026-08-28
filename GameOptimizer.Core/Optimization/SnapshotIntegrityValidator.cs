// File: GameOptimizer.Core/Optimization/SnapshotIntegrityValidator.cs
using System.Security.Cryptography;
using System.Text;

namespace GameOptimizer.Core.Optimization;

/// <summary>
/// P0-3: 快照完整性验证器 - 防止快照损坏导致回滚失败
/// </summary>
public static class SnapshotIntegrityValidator
{
    /// <summary>
    /// 计算快照内容的SHA256校验和
    /// </summary>
    public static string ComputeChecksum(string content)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;

        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// 验证快照内容的完整性
    /// </summary>
    public static bool ValidateChecksum(string content, string expectedChecksum)
    {
        if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(expectedChecksum))
            return false;

        var actualChecksum = ComputeChecksum(content);
        return string.Equals(actualChecksum, expectedChecksum, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 创建带校验和的快照包装
    /// </summary>
    public static string WrapWithChecksum(string content)
    {
        var checksum = ComputeChecksum(content);
        return $"{{\"checksum\":\"{checksum}\",\"data\":{content}}}";
    }

    /// <summary>
    /// 解包并验证快照
    /// </summary>
    public static (bool IsValid, string? Content) UnwrapAndValidate(string wrappedContent)
    {
        if (string.IsNullOrEmpty(wrappedContent))
            return (false, null);

        try
        {
            // 尝试解析包装格式
            var json = System.Text.Json.JsonDocument.Parse(wrappedContent);
            var root = json.RootElement;

            if (!root.TryGetProperty("checksum", out var checksumElement) ||
                !root.TryGetProperty("data", out var dataElement))
            {
                // 旧格式快照，没有校验和，直接返回原内容
                return (true, wrappedContent);
            }

            var expectedChecksum = checksumElement.GetString();
            var dataJson = dataElement.GetRawText();

            if (string.IsNullOrEmpty(expectedChecksum) || string.IsNullOrEmpty(dataJson))
                return (false, null);

            var isValid = ValidateChecksum(dataJson, expectedChecksum);
            if (!isValid)
            {
                // Why: 校验和不匹配可能是杀毒软件修改换行符、BOM头变化、磁盘编码差异等
                // 数据结构本身可能完整，降级返回数据让调用方尝试反序列化，而非直接丢弃
                System.Diagnostics.Debug.WriteLine(
                    $"[SnapshotIntegrityValidator] 校验和不匹配，降级返回数据（expected={expectedChecksum?[..8]}...）");
            }
            return (isValid, dataJson);
        }
        catch (System.Text.Json.JsonException)
        {
            // Why: JSON 解析失败说明文件确实损坏或是非 JSON 旧格式
            // 旧格式快照不含 {"checksum":...} 结构，直接返回原内容（向后兼容）
            // 但如果内容以 '{' 开头却解析失败，说明是损坏的 JSON，应拒绝
            if (wrappedContent.TrimStart().StartsWith('{'))
                return (false, null);
            
            return (true, wrappedContent);
        }
        catch
        {
            return (false, null);
        }
    }

    /// <summary>
    /// 检查快照文件大小是否合理（防止异常大文件）
    /// </summary>
    public static bool IsReasonableSize(long sizeInBytes)
    {
        const long MaxSnapshotSize = 10 * 1024 * 1024; // 10MB
        return sizeInBytes > 0 && sizeInBytes <= MaxSnapshotSize;
    }
}
