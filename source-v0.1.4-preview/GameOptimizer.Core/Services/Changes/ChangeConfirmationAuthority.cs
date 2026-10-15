using System.Collections.Concurrent;
using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Core.Services.Changes;

public sealed class ChangeConfirmationAuthority : IChangeConfirmationAuthority
{
    private readonly IChangeConfirmationPrompt _prompt;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<Guid, IssuedConfirmation> _issued = new();

    public ChangeConfirmationAuthority(
        IChangeConfirmationPrompt prompt,
        TimeProvider? timeProvider = null)
    {
        _prompt = prompt;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ConfirmationToken?> RequestAsync(
        PreparedChangePlan plan,
        string title,
        string summary,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.RequiresExplicitConfirmation)
        {
            throw new InvalidOperationException("无需显式确认的计划不得请求显式确认凭据。");
        }

        var now = _timeProvider.GetUtcNow();
        if (now > plan.ExpiresAtUtc)
        {
            return null;
        }

        var confirmed = await _prompt.ConfirmAsync(
            new ChangeConfirmationPrompt(
                plan.PlanId,
                plan.Change.CapabilityId,
                plan.Risk,
                title,
                summary,
                plan.ExpiresAtUtc),
            cancellationToken).ConfigureAwait(false);
        if (!confirmed)
        {
            return null;
        }

        // Why: _issued 只在 TryConsume 时移除，过期未消费的令牌会无界累积；
        // 签发新令牌前顺手清掉已过期的条目。
        foreach (var stale in _issued)
        {
            if (now > stale.Value.ExpiresAtUtc)
            {
                _issued.TryRemove(stale.Key, out _);
            }
        }

        var tokenId = Guid.NewGuid();
        var token = new ConfirmationToken(
            tokenId,
            plan.PlanHash,
            plan.Change.Scope,
            plan.ExpiresAtUtc,
            explicitlyConfirmed: true);
        _issued[tokenId] = new IssuedConfirmation(
            plan.PlanId,
            plan.PlanHash,
            plan.Change.Scope,
            plan.ExpiresAtUtc);
        return token;
    }

    public bool TryConsume(PreparedChangePlan plan, ConfirmationToken token)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(token);
        if (!token.ExplicitlyConfirmed
            || token.TokenId == Guid.Empty
            || !_issued.TryGetValue(token.TokenId, out var issued))
        {
            return false;
        }

        var now = _timeProvider.GetUtcNow();
        var matches = now <= issued.ExpiresAtUtc
            && issued.PlanId == plan.PlanId
            && string.Equals(issued.PlanHash, plan.PlanHash, StringComparison.Ordinal)
            && issued.Scope == plan.Change.Scope
            && string.Equals(token.PlanHash, issued.PlanHash, StringComparison.Ordinal)
            && token.Scope == issued.Scope
            && token.ExpiresAtUtc == issued.ExpiresAtUtc;
        return matches
            && _issued.TryRemove(token.TokenId, out var consumed)
            && consumed == issued;
    }

    private sealed record IssuedConfirmation(
        Guid PlanId,
        string PlanHash,
        TargetScope Scope,
        DateTimeOffset ExpiresAtUtc);
}

public sealed class DenyChangeConfirmationPrompt : IChangeConfirmationPrompt
{
    public Task<bool> ConfirmAsync(ChangeConfirmationPrompt prompt, CancellationToken cancellationToken) =>
        Task.FromResult(false);
}
