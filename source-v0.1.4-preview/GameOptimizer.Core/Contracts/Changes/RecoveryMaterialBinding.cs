using System.Security.Cryptography;
using System.Text;

namespace GameOptimizer.Core.Contracts.Changes;

public static class RecoveryMaterialBinding
{
    public static Guid ComputeReceiptId(Guid recoveryId, string resourceKey, string integrityHash)
    {
        if (recoveryId == Guid.Empty)
        {
            throw new ArgumentException("RecoveryId must not be empty.", nameof(recoveryId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(integrityHash);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(
            '\u001F',
            "yuqi.recovery-receipt.v1",
            recoveryId.ToString("N"),
            resourceKey,
            integrityHash)));
        return new Guid(bytes.AsSpan(0, 16));
    }
}
