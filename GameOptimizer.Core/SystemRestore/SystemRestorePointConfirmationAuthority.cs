using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace GameOptimizer.Core.SystemRestore;

public sealed class SystemRestorePointConfirmationAuthority : ISystemRestorePointConfirmationAuthority
{
    private readonly byte[] _secret;
    private readonly ConcurrentDictionary<Guid, byte> _consumed = new();

    public SystemRestorePointConfirmationAuthority(byte[] secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        if (secret.Length < 32)
        {
            throw new ArgumentException("The System Restore confirmation secret must contain at least 32 bytes.", nameof(secret));
        }

        _secret = secret.ToArray();
    }

    public SystemRestorePointConfirmationToken Issue(
        SystemRestorePointCreatePlan plan,
        DateTimeOffset nowUtc,
        TimeSpan lifetime) =>
        Issue(plan.PlanId, plan.PlanHash, plan.ExpiresAtUtc, SystemRestorePointIntent.CreateRestorePoint, nowUtc, lifetime);

    public SystemRestorePointConfirmationToken Issue(
        SystemRestoreExecutionPlan plan,
        DateTimeOffset nowUtc,
        TimeSpan lifetime) =>
        Issue(plan.PlanId, plan.PlanHash, plan.ExpiresAtUtc, SystemRestorePointIntent.ExecuteSystemRestore, nowUtc, lifetime);

    public bool TryConsume(
        SystemRestorePointCreatePlan plan,
        SystemRestorePointConfirmationToken token,
        DateTimeOffset nowUtc) =>
        TryConsume(plan.PlanId, plan.PlanHash, plan.ExpiresAtUtc, SystemRestorePointIntent.CreateRestorePoint, token, nowUtc);

    public bool TryConsume(
        SystemRestoreExecutionPlan plan,
        SystemRestorePointConfirmationToken token,
        DateTimeOffset nowUtc) =>
        TryConsume(plan.PlanId, plan.PlanHash, plan.ExpiresAtUtc, SystemRestorePointIntent.ExecuteSystemRestore, token, nowUtc);

    private SystemRestorePointConfirmationToken Issue(
        Guid planId,
        string planHash,
        DateTimeOffset planExpiry,
        SystemRestorePointIntent intent,
        DateTimeOffset nowUtc,
        TimeSpan lifetime)
    {
        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        }

        var expiresAtUtc = nowUtc.Add(lifetime);
        if (expiresAtUtc > planExpiry)
        {
            expiresAtUtc = planExpiry;
        }

        var nonce = Guid.NewGuid();
        return new SystemRestorePointConfirmationToken(
            planId,
            planHash,
            intent,
            nonce,
            expiresAtUtc,
            Sign(planId, planHash, intent, nonce, expiresAtUtc));
    }

    private bool TryConsume(
        Guid planId,
        string planHash,
        DateTimeOffset planExpiry,
        SystemRestorePointIntent intent,
        SystemRestorePointConfirmationToken token,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (token.PlanId != planId
            || token.Intent != intent
            || !FixedEquals(token.PlanHash, planHash)
            || token.ExpiresAtUtc <= nowUtc
            || token.ExpiresAtUtc > planExpiry)
        {
            return false;
        }

        var expected = Sign(token.PlanId, token.PlanHash, token.Intent, token.Nonce, token.ExpiresAtUtc);
        return FixedEquals(expected, token.Signature) && _consumed.TryAdd(token.Nonce, 0);
    }

    private string Sign(
        Guid planId,
        string planHash,
        SystemRestorePointIntent intent,
        Guid nonce,
        DateTimeOffset expiresAtUtc)
    {
        var payload = Encoding.UTF8.GetBytes(
            $"{planId:D}|{planHash}|{intent}|{nonce:D}|{expiresAtUtc.UtcTicks}");
        return Convert.ToHexString(HMACSHA256.HashData(_secret, payload));
    }

    private static bool FixedEquals(string left, string right)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(left),
                Convert.FromHexString(right));
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
