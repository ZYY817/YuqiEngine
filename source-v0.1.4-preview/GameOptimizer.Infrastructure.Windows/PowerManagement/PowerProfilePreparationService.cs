// File: GameOptimizer.Infrastructure.Windows/PowerManagement/PowerProfilePreparationService.cs
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Models;
using GameOptimizer.Core.Services;

namespace GameOptimizer.Infrastructure.Windows.PowerManagement;

/// <summary>
/// Read-only and prepare-only coordinator for the conservative power-profile catalog.
/// It intentionally has no execute method: profile execution must be introduced later as a
/// separately reviewed, explicitly-confirmed batch workflow rather than as a hidden shortcut.
/// </summary>
public interface IPowerProfilePreparationService
{
    Task<PowerProfilePreflight> ProbeAsync(CancellationToken cancellationToken);
    Task<PowerProfilePreparation> PrepareAsync(PowerProfileId profileId, CancellationToken cancellationToken);
}

public sealed record PowerProfileCapabilityPreflight(
    string CapabilityId,
    CapabilityAssessment? Assessment,
    string Status,
    string Details);

public sealed record PowerProfileCandidatePreflight(
    PowerProfileCandidate Candidate,
    IReadOnlyList<PowerProfileCapabilityPreflight> Capabilities)
{
    public bool CanPrepare => Candidate.Availability == PowerProfileAvailability.Available
        && Capabilities.All(item => item.Assessment is { Applicability: CapabilityApplicability.Applicable, HasRequiredPermission: true });
}

public sealed record PowerProfilePreflight(
    IReadOnlyList<PowerProfileCandidatePreflight> Candidates,
    string MachineName,
    bool ActiveSchemeIsReadable);

public sealed record PowerProfilePreparationItem(
    string CapabilityId,
    PreparationResult Result);

public sealed record PowerProfilePreparation(
    PowerProfileId ProfileId,
    PowerProfilePreflight Preflight,
    IReadOnlyList<PowerProfilePreparationItem> Items,
    string Message)
{
    public bool HasPreparedPlans => Items.Any(item => item.Result.Status == ChangePreparationStatus.Ready);
    public bool IsBlocked => Items.Count == 0 || Items.All(item => item.Result.Status == ChangePreparationStatus.Blocked);
}

public sealed class PowerProfilePreparationService : IPowerProfilePreparationService
{
    private readonly PowerProfilePlanner _planner;
    private readonly IReadOnlyDictionary<string, IChangeCapability> _capabilities;
    private readonly IChangeOrchestrator _orchestrator;
    private readonly IPowerPlatformFactsProvider _factsProvider;
    private readonly IPowerSchemeAccessor _schemeAccessor;
    private readonly Func<CpuTopology> _topologyReader;

    public PowerProfilePreparationService(
        IEnumerable<IChangeCapability> capabilities,
        IChangeOrchestrator orchestrator,
        IPowerPlatformFactsProvider factsProvider,
        IPowerSchemeAccessor schemeAccessor,
        CpuTopologyService topologyService,
        PowerProfilePlanner? planner = null,
        Func<CpuTopology>? topologyReader = null)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        _capabilities = capabilities.ToDictionary(item => item.Descriptor.CapabilityId, StringComparer.OrdinalIgnoreCase);
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _factsProvider = factsProvider ?? throw new ArgumentNullException(nameof(factsProvider));
        _schemeAccessor = schemeAccessor ?? throw new ArgumentNullException(nameof(schemeAccessor));
        ArgumentNullException.ThrowIfNull(topologyService);
        _topologyReader = topologyReader ?? topologyService.GetTopology;
        _planner = planner ?? new PowerProfilePlanner();
    }

    public async Task<PowerProfilePreflight> ProbeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var facts = _factsProvider.GetCurrent();
        var topology = GetTopologyFailClosed();
        var activeSchemeReadable = await IsActiveSchemeReadableAsync(cancellationToken).ConfigureAwait(false);
        var candidates = _planner.Plan(new PowerProfilePlanningContext(facts, topology, activeSchemeReadable)).Candidates;
        var scope = TargetScope.Machine(facts.MachineName);
        var preflight = new List<PowerProfileCandidatePreflight>(candidates.Count);

        foreach (var candidate in candidates)
        {
            var items = new List<PowerProfileCapabilityPreflight>(candidate.PreviewItems.Count);
            foreach (var item in candidate.PreviewItems)
            {
                items.Add(await ProbeCapabilityAsync(item.CapabilityId, scope, cancellationToken).ConfigureAwait(false));
            }
            preflight.Add(new PowerProfileCandidatePreflight(candidate, items));
        }

        return new PowerProfilePreflight(preflight, facts.MachineName, activeSchemeReadable);
    }

    public async Task<PowerProfilePreparation> PrepareAsync(PowerProfileId profileId, CancellationToken cancellationToken)
    {
        var preflight = await ProbeAsync(cancellationToken).ConfigureAwait(false);
        var selected = preflight.Candidates.SingleOrDefault(item => item.Candidate.Id == profileId);
        if (selected is null)
        {
            return new PowerProfilePreparation(profileId, preflight, Array.Empty<PowerProfilePreparationItem>(), "所选电源预设不存在，未执行任何写入。");
        }

        if (!selected.CanPrepare)
        {
            return new PowerProfilePreparation(profileId, preflight, Array.Empty<PowerProfilePreparationItem>(),
                "当前设备或权限不满足预设条件，未生成可执行计划，也未执行任何写入。");
        }

        var items = new List<PowerProfilePreparationItem>(selected.Candidate.PreviewItems.Count);
        var scope = TargetScope.Machine(preflight.MachineName);
        foreach (var item in selected.Candidate.PreviewItems)
        {
            var result = await _orchestrator.PrepareAsync(
                new ChangeRequest(item.CapabilityId, scope, "apply"),
                cancellationToken).ConfigureAwait(false);
            items.Add(new PowerProfilePreparationItem(item.CapabilityId, result));
        }

        var ready = items.Count(item => item.Result.Status == ChangePreparationStatus.Ready);
        var blocked = items.Count(item => item.Result.Status == ChangePreparationStatus.Blocked);
        var message = ready > 0
            ? $"已准备 {ready} 项待确认计划{(blocked > 0 ? $"；{blocked} 项已阻止" : string.Empty)}。本阶段不会切换计划或写入电源设置。"
            : "没有生成待确认计划；当前状态已符合目标或被安全条件阻止。";
        return new PowerProfilePreparation(profileId, preflight, items, message);
    }

    private async Task<PowerProfileCapabilityPreflight> ProbeCapabilityAsync(
        string capabilityId,
        TargetScope scope,
        CancellationToken cancellationToken)
    {
        if (!_capabilities.TryGetValue(capabilityId, out var capability))
        {
            return new PowerProfileCapabilityPreflight(capabilityId, null, "不可用", "能力未注册，未执行任何写入。");
        }

        try
        {
            var assessment = await capability.ProbeAsync(scope, ChangeIntent.Prepare, cancellationToken).ConfigureAwait(false);
            var status = assessment.Applicability == CapabilityApplicability.Applicable && assessment.HasRequiredPermission
                ? "可准备"
                : "已阻止";
            return new PowerProfileCapabilityPreflight(capabilityId, assessment, status, assessment.Details);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new PowerProfileCapabilityPreflight(capabilityId, null, "不可用", "无法可靠探测该设置，已按安全策略阻止。");
        }
    }

    private async Task<bool> IsActiveSchemeReadableAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _schemeAccessor.GetActiveSchemeAsync(cancellationToken).ConfigureAwait(false) != Guid.Empty;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private CpuTopology GetTopologyFailClosed()
    {
        try
        {
            return _topologyReader();
        }
        catch
        {
            return new CpuTopology();
        }
    }
}
