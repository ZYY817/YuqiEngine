// File: GameOptimizer.Shell/Services/PowerPlanTemplateService.cs
using System.Runtime.Versioning;

namespace GameOptimizer.Shell.Services;

/// <summary>
/// Legacy compatibility surface for power-plan templates.
/// Import and rename remain blocked until a reversible typed capability is implemented.
/// </summary>
[SupportedOSPlatform("windows")]
public static class PowerPlanTemplateService
{
    private const string BlockReason =
        "Power-plan import and rename are blocked because authoritative recovery semantics are not proven.";

    public static bool IsTemplateAvailable() => false;

    public static Task<Guid?> ImportPowerPlanFromTemplateAsync(string planName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<Guid?>(null);
    }

    public static Task<(bool Success, Guid? PlanGuid, string? ErrorMessage)> ImportPowerPlanFromTemplateAsync(
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult((false, (Guid?)null, (string?)BlockReason));
    }

    public static Task<bool> RenamePowerPlanAsync(Guid planGuid, string newName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }
}
