using GameOptimizer.Core.Services.ApplicationRepair;
using Windows.Management.Deployment;

namespace GameOptimizer.Infrastructure.Windows.ApplicationRepair;

/// <summary>Read-only current-user package inventory through the official deployment API.</summary>
public sealed class WindowsApplicationPackageDiagnosticPlatform : IApplicationPackageDiagnosticPlatform
{
    public Task<ApplicationPackageSnapshot> ReadAsync(
        ApplicationPackageDefinition definition,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(definition.PackageName))
            throw new InvalidOperationException("Aggregate or arbitrary package queries are not allowed.");

        try
        {
            var manager = new PackageManager();
            var package = manager.FindPackagesForUser(string.Empty)
                .FirstOrDefault(candidate => string.Equals(
                    candidate.Id.Name,
                    definition.PackageName,
                    StringComparison.OrdinalIgnoreCase));
            cancellationToken.ThrowIfCancellationRequested();
            if (package is null)
                return Task.FromResult(Snapshot(definition.Target, installed: false, healthy: false, string.Empty, string.Empty));

            var version = package.Id.Version;
            var versionText = $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
            var healthy = false;
            try { healthy = package.Status.VerifyIsOK(); }
            catch { healthy = false; }
            return Task.FromResult(Snapshot(definition.Target, true, healthy, package.Id.FullName, versionText));
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(Failed(definition.Target, "AccessDenied"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Task.FromResult(Failed(definition.Target, ex.GetType().Name));
        }
    }

    private static ApplicationPackageSnapshot Snapshot(
        ApplicationPackageRepairTarget target,
        bool installed,
        bool healthy,
        string fullName,
        string version)
    {
        const string scope = "current-user";
        return new(target, true, installed, healthy, fullName, version, scope,
            ApplicationPackageRepairOwner.Fingerprint(target, fullName, version, scope), null);
    }

    private static ApplicationPackageSnapshot Failed(ApplicationPackageRepairTarget target, string errorCode) =>
        new(target, false, false, false, string.Empty, string.Empty, "current-user", string.Empty, errorCode);
}
