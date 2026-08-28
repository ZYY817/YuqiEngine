using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.Changes;

namespace GameOptimizer.Tests.Changes;

public sealed class ChangeConfirmationAuthorityTests
{
    private static readonly TargetScope Scope = TargetScope.CurrentUser("test-user");

    [Fact]
    public async Task RequestAsync_WhenUserDeclines_DoesNotIssueToken()
    {
        var authority = new ChangeConfirmationAuthority(new Prompt(false));

        var token = await authority.RequestAsync(
            CreatePlan(),
            "Confirm change",
            "Impact summary",
            CancellationToken.None);

        Assert.Null(token);
    }

    [Fact]
    public async Task TryConsume_BindsTokenToPlanAndConsumesItExactlyOnce()
    {
        var authority = new ChangeConfirmationAuthority(new Prompt(true));
        var plan = CreatePlan();
        var token = await authority.RequestAsync(
            plan,
            "Confirm change",
            "Impact summary",
            CancellationToken.None);

        Assert.NotNull(token);
        Assert.True(authority.TryConsume(plan, token));
        Assert.False(authority.TryConsume(plan, token));
    }

    [Fact]
    public async Task TryConsume_WhenPlanDoesNotMatch_DoesNotBurnValidToken()
    {
        var authority = new ChangeConfirmationAuthority(new Prompt(true));
        var plan = CreatePlan();
        var token = await authority.RequestAsync(
            plan,
            "Confirm change",
            "Impact summary",
            CancellationToken.None);
        var otherPlan = CreatePlan();

        Assert.NotNull(token);
        Assert.False(authority.TryConsume(otherPlan, token));
        Assert.True(authority.TryConsume(plan, token));
    }

    [Fact]
    public async Task RequestAsync_PresentsThePlanActualHighRisk()
    {
        var prompt = new Prompt(true);
        var authority = new ChangeConfirmationAuthority(prompt);

        var token = await authority.RequestAsync(
            CreatePlan(),
            "Confirm change",
            "Impact summary",
            CancellationToken.None);

        Assert.NotNull(token);
        Assert.Equal(ChangeRisk.HighSystem, prompt.LastPrompt?.Risk);
    }

    private static PreparedChangePlan CreatePlan()
    {
        var now = DateTimeOffset.UtcNow;
        return new PreparedChangePlan(
            Guid.NewGuid(),
            new PreparedCapabilityChange(
                "test.explicit",
                "1.0.0",
                Scope,
                "0",
                "before",
                "1",
                "target",
                "registry:hkcu:test"),
            Guid.NewGuid().ToString("N"),
            now,
            now.AddMinutes(5),
            RequiresAdministrator: false,
            RequiresExplicitConfirmation: true,
            RequiresRecoveryMaterial: true,
            Risk: ChangeRisk.HighSystem);
    }

    private sealed class Prompt(bool decision) : IChangeConfirmationPrompt
    {
        public ChangeConfirmationPrompt? LastPrompt { get; private set; }

        public Task<bool> ConfirmAsync(
            ChangeConfirmationPrompt prompt,
            CancellationToken cancellationToken)
        {
            LastPrompt = prompt;
            return Task.FromResult(decision);
        }
    }
}
