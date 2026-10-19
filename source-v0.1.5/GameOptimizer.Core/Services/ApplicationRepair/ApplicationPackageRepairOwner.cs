using System.Security.Cryptography;
using System.Text;

namespace GameOptimizer.Core.Services.ApplicationRepair;

public enum ApplicationPackageRepairTarget
{
    MicrosoftStore,
    Calculator,
    Photos,
    XboxGameBar,
    Cortana,
    AllUwpApplications
}

public enum ApplicationPackageRepairAction { Diagnose, Reregister, Reset }

public sealed record ApplicationPackageDefinition(
    ApplicationPackageRepairTarget Target,
    string PackageName,
    string DisplayName,
    bool AllowsMutation,
    string BlockReason);

public sealed record ApplicationPackageSnapshot(
    ApplicationPackageRepairTarget Target,
    bool QuerySucceeded,
    bool Installed,
    bool Healthy,
    string PackageFullName,
    string Version,
    string UserScope,
    string Fingerprint,
    string? ErrorCode);

public sealed record ApplicationPackageRepairDecision(
    bool Success,
    bool IsApplicable,
    string Status,
    string Message,
    ApplicationPackageSnapshot? Snapshot = null);

public interface IApplicationPackageDiagnosticPlatform
{
    Task<ApplicationPackageSnapshot> ReadAsync(ApplicationPackageDefinition definition, CancellationToken cancellationToken);
}

public interface IApplicationPackageRepairOwner
{
    Task<ApplicationPackageRepairDecision> DiagnoseAsync(ApplicationPackageRepairTarget target, CancellationToken cancellationToken);
    Task<ApplicationPackageRepairDecision> PrepareAsync(
        ApplicationPackageRepairTarget target,
        ApplicationPackageRepairAction action,
        CancellationToken cancellationToken);
}

public static class ApplicationPackageRepairCatalog
{
    public static IReadOnlyList<ApplicationPackageDefinition> All { get; } =
    [
        Block(ApplicationPackageRepairTarget.MicrosoftStore, "Microsoft.WindowsStore", "Microsoft Store"),
        Block(ApplicationPackageRepairTarget.Calculator, "Microsoft.WindowsCalculator", "Calculator"),
        Block(ApplicationPackageRepairTarget.Photos, "Microsoft.Windows.Photos", "Photos"),
        Block(ApplicationPackageRepairTarget.XboxGameBar, "Microsoft.XboxGamingOverlay", "Xbox Game Bar"),
        Block(ApplicationPackageRepairTarget.Cortana, "Microsoft.549981C3F5F10", "Cortana"),
        new(ApplicationPackageRepairTarget.AllUwpApplications, string.Empty, "All UWP applications", false,
            "Bulk package re-registration cannot bind one package identity, recovery material or data-loss boundary.")
    ];

    public static ApplicationPackageDefinition Get(ApplicationPackageRepairTarget target) =>
        All.Single(definition => definition.Target == target);

    private static ApplicationPackageDefinition Block(
        ApplicationPackageRepairTarget target,
        string packageName,
        string displayName) =>
        new(target, packageName, displayName, false,
            "Package mutation remains blocked until an official typed API proves current-user identity, version, explicit confirmation, verified baseline, read-back and manual recovery evidence.");
}

/// <summary>
/// Central boundary for application-package repair. This milestone intentionally exposes authoritative
/// read-only diagnostics while every package mutation remains fail closed.
/// </summary>
public sealed class ApplicationPackageRepairOwner : IApplicationPackageRepairOwner
{
    private readonly IApplicationPackageDiagnosticPlatform _platform;

    public ApplicationPackageRepairOwner(IApplicationPackageDiagnosticPlatform platform) =>
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));

    public async Task<ApplicationPackageRepairDecision> DiagnoseAsync(
        ApplicationPackageRepairTarget target,
        CancellationToken cancellationToken)
    {
        var definition = ApplicationPackageRepairCatalog.Get(target);
        if (target == ApplicationPackageRepairTarget.AllUwpApplications)
            return new(false, false, "UnsupportedAggregate", definition.BlockReason);

        var snapshot = await _platform.ReadAsync(definition, cancellationToken).ConfigureAwait(false);
        if (!snapshot.QuerySucceeded)
            return new(false, false, "DiagnosticFailed", "Package state could not be read authoritatively.", snapshot);
        return new(true, snapshot.Installed, snapshot.Installed && snapshot.Healthy ? "Healthy" : "Unhealthy",
            snapshot.Installed ? "Package state was read successfully." : "Package is not installed for the current user.", snapshot);
    }

    public async Task<ApplicationPackageRepairDecision> PrepareAsync(
        ApplicationPackageRepairTarget target,
        ApplicationPackageRepairAction action,
        CancellationToken cancellationToken)
    {
        if (action == ApplicationPackageRepairAction.Diagnose)
            return await DiagnoseAsync(target, cancellationToken).ConfigureAwait(false);

        var definition = ApplicationPackageRepairCatalog.Get(target);
        if (target != ApplicationPackageRepairTarget.AllUwpApplications)
        {
            var diagnostic = await DiagnoseAsync(target, cancellationToken).ConfigureAwait(false);
            if (!diagnostic.Success) return diagnostic;
        }

        return new(false, false, "MutationBlocked", definition.BlockReason);
    }

    public static string Fingerprint(ApplicationPackageRepairTarget target, string fullName, string version, string scope) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{target}|{fullName}|{version}|{scope}")));
}

public sealed class UnavailableApplicationPackageRepairOwner : IApplicationPackageRepairOwner
{
    public static UnavailableApplicationPackageRepairOwner Instance { get; } = new();
    private UnavailableApplicationPackageRepairOwner() { }
    public Task<ApplicationPackageRepairDecision> DiagnoseAsync(ApplicationPackageRepairTarget target, CancellationToken cancellationToken) =>
        Task.FromResult(new ApplicationPackageRepairDecision(false, false, "OwnerUnavailable", "Application Package Owner is not connected."));
    public Task<ApplicationPackageRepairDecision> PrepareAsync(ApplicationPackageRepairTarget target, ApplicationPackageRepairAction action, CancellationToken cancellationToken) =>
        DiagnoseAsync(target, cancellationToken);
}
