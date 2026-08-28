using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace GameOptimizer.Core.PowerManagement;

public sealed class PowerConfirmationAuthority : IPowerConfirmationAuthority
{
    private readonly byte[] _secret;
    private readonly ConcurrentDictionary<Guid, byte> _consumed = new();

    public PowerConfirmationAuthority(byte[] secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        if (secret.Length < 32)
        {
            throw new ArgumentException("The confirmation secret must contain at least 32 bytes.", nameof(secret));
        }

        _secret = secret.ToArray();
    }

    public PowerConfirmationToken Issue(PowerPreparedPlan plan, DateTimeOffset nowUtc, TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        }

        var expires = nowUtc.Add(lifetime);
        if (expires > plan.ExpiresAtUtc)
        {
            expires = plan.ExpiresAtUtc;
        }

        var nonce = Guid.NewGuid();
        return new PowerConfirmationToken(plan.PlanId, plan.PlanHash, nonce, expires, Sign(plan.PlanId, plan.PlanHash, nonce, expires));
    }

    public bool TryConsume(PowerPreparedPlan plan, PowerConfirmationToken token, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(token);
        if (token.PlanId != plan.PlanId
            || !FixedEquals(token.PlanHash, plan.PlanHash)
            || token.ExpiresAtUtc <= nowUtc
            || token.ExpiresAtUtc > plan.ExpiresAtUtc)
        {
            return false;
        }

        var expected = Sign(token.PlanId, token.PlanHash, token.Nonce, token.ExpiresAtUtc);
        if (!FixedEquals(expected, token.Signature))
        {
            return false;
        }

        return _consumed.TryAdd(token.Nonce, 0);
    }

    private string Sign(Guid planId, string planHash, Guid nonce, DateTimeOffset expires)
    {
        var payload = Encoding.UTF8.GetBytes($"{planId:D}|{planHash}|{nonce:D}|{expires.UtcTicks}");
        return Convert.ToHexString(HMACSHA256.HashData(_secret, payload));
    }

    private static bool FixedEquals(string left, string right)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(left), Convert.FromHexString(right));
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
