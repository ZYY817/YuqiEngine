using System.Diagnostics;
using System.Text.Json;
using GameOptimizer.Core.Constants;
using GameOptimizer.Core.SystemRestore;

namespace GameOptimizer.Infrastructure.Windows.SystemRestore;

public interface ISystemRestorePointWriterProcessRunner
{
    bool IsAvailable { get; }

    Task<SystemRestorePointMutationResult> RunAsync(
        SystemRestorePointCreateCommand command,
        CancellationToken cancellationToken);
}

public sealed class SystemRestorePointWriterProcessRunner : ISystemRestorePointWriterProcessRunner
{
    private const int MaximumProtocolCharacters = 16 * 1024;
    private readonly string _executablePath;

    public SystemRestorePointWriterProcessRunner(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("A writer executable path is required.", nameof(executablePath));
        }
        _executablePath = Path.GetFullPath(executablePath);
    }

    public bool IsAvailable => OperatingSystem.IsWindows()
        && File.Exists(_executablePath)
        && string.Equals(Path.GetFileName(_executablePath), "YuqiEngine.exe", StringComparison.OrdinalIgnoreCase);

    public async Task<SystemRestorePointMutationResult> RunAsync(
        SystemRestorePointCreateCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsAvailable)
        {
            return Failed("The isolated System Restore writer executable is unavailable.");
        }

        var operationDirectory = Path.Combine(
            AppPaths.TempDir,
            "SystemRestoreWriter",
            Guid.NewGuid().ToString("N"));
        var requestPath = Path.Combine(operationDirectory, "request.json");
        var responsePath = Path.Combine(operationDirectory, "response.json");
        Directory.CreateDirectory(operationDirectory);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(_executablePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("--system-restore-writer");
        process.StartInfo.ArgumentList.Add("--request");
        process.StartInfo.ArgumentList.Add(requestPath);
        process.StartInfo.ArgumentList.Add("--response");
        process.StartInfo.ArgumentList.Add(responsePath);

        try
        {
            var request = new SystemRestorePointWriterRequest(
                SystemRestorePointWriterRequest.CurrentProtocolVersion,
                command);
            await File.WriteAllTextAsync(
                requestPath,
                JsonSerializer.Serialize(request),
                cancellationToken).ConfigureAwait(false);
            if (!process.Start())
            {
                return Failed("The isolated System Restore writer process did not start.");
            }

            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                await WaitForExitAfterKillAsync(process).ConfigureAwait(false);
                return new(
                    SystemRestorePointMutationOutcome.TimedOut,
                    null,
                    null,
                    "The isolated writer was terminated at its deadline; completion is unknown and requires authoritative readback.",
                    SystemRestorePointMutationState.Unknown);
            }

            if (process.ExitCode != 0)
            {
                return new(
                    SystemRestorePointMutationOutcome.Unknown,
                    null,
                    process.ExitCode,
                    "The isolated writer exited without a successful protocol response.",
                    SystemRestorePointMutationState.Unknown);
            }

            var responseText = await ReadBoundedFileAsync(responsePath, cancellationToken).ConfigureAwait(false);
            var response = JsonSerializer.Deserialize<SystemRestorePointWriterResponse>(responseText);
            if (response is null || response.ProtocolVersion != SystemRestorePointWriterRequest.CurrentProtocolVersion)
            {
                return new(
                    SystemRestorePointMutationOutcome.Unknown,
                    null,
                    null,
                    "The isolated writer returned an invalid protocol response.",
                    SystemRestorePointMutationState.Unknown);
            }

            return new(
                response.Outcome,
                response.SequenceNumber,
                response.NativeStatus,
                response.Message,
                response.MutationState);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return new(
                SystemRestorePointMutationOutcome.TimedOut,
                null,
                null,
                "The isolated writer was cancelled; completion is unknown and requires authoritative readback.",
                SystemRestorePointMutationState.Unknown);
        }
        catch (Exception ex)
        {
            TryKill(process);
            return new(
                SystemRestorePointMutationOutcome.Unknown,
                null,
                null,
                $"The isolated writer boundary failed: {ex.Message}",
                SystemRestorePointMutationState.Unknown);
        }
        finally
        {
            TryDeleteOperationDirectory(operationDirectory);
        }
    }

    private static async Task<string> ReadBoundedFileAsync(string path, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > MaximumProtocolCharacters * sizeof(char))
        {
            throw new InvalidDataException("The isolated writer response is missing or exceeds the protocol limit.");
        }
        var value = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        if (value.Length > MaximumProtocolCharacters)
        {
            throw new InvalidDataException("The isolated writer exceeded the protocol output limit.");
        }
        return value;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // The parent still reports Unknown; startup reconciliation remains authoritative.
        }
    }

    private static async Task WaitForExitAfterKillAsync(Process process)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch
        {
            // Unknown is returned even if the process cannot be observed exiting.
        }
    }

    private static void TryDeleteOperationDirectory(string path)
    {
        try
        {
            var root = Path.GetFullPath(Path.Combine(
                AppPaths.TempDir, "SystemRestoreWriter"));
            var target = Path.GetFullPath(path);
            if (target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }
        }
        catch
        {
            // Temporary protocol files contain no secrets and are bounded; cleanup can be retried by OS maintenance.
        }
    }

    private static SystemRestorePointMutationResult Failed(string message) => new(
        SystemRestorePointMutationOutcome.Failed,
        null,
        null,
        message,
        SystemRestorePointMutationState.NotChanged);
}
