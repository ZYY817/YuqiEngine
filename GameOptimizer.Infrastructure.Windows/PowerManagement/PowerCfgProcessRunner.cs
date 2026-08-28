using System.Diagnostics;
using System.Text;
using GameOptimizer.Core.PowerManagement;

namespace GameOptimizer.Infrastructure.Windows.PowerManagement;

public interface IPowerProcessHandle : IAsyncDisposable
{
    StreamReader StandardOutput { get; }
    StreamReader StandardError { get; }
    int ExitCode { get; }
    Task WaitForExitAsync(CancellationToken cancellationToken);
    void KillEntireTree();
}

public interface IPowerProcessFactory
{
    IPowerProcessHandle Start(IReadOnlyList<string> arguments);
}

public interface IPowerCfgProcessRunner
{
    Task<PowerCommandResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken);
}

public sealed class PowerCfgProcessRunner : IPowerCfgProcessRunner
{
    private readonly IPowerProcessFactory _factory;

    public PowerCfgProcessRunner(IPowerProcessFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public async Task<PowerCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ValidateArguments(arguments);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        await using var process = _factory.Start(arguments);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.KillEntireTree();
            var terminated = await AwaitTerminationAsync(process).ConfigureAwait(false);
            var output = terminated ? await stdoutTask.ConfigureAwait(false) : string.Empty;
            var error = terminated ? await stderrTask.ConfigureAwait(false) : string.Empty;
            var outcome = cancellationToken.IsCancellationRequested ? PowerCommandOutcome.Cancelled : PowerCommandOutcome.TimedOut;
            return new PowerCommandResult(outcome, arguments[0], null, output, error,
                terminated
                    ? outcome == PowerCommandOutcome.Cancelled ? "powercfg was cancelled and its process tree was terminated." : "powercfg timed out and its process tree was terminated."
                    : "powercfg process-tree termination could not be confirmed within two seconds; execution remains failed closed.");
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        return new PowerCommandResult(process.ExitCode == 0 ? PowerCommandOutcome.Success : PowerCommandOutcome.Failed,
            arguments[0], process.ExitCode, stdout, stderr,
            process.ExitCode == 0 ? null : $"powercfg exited with code {process.ExitCode}.");
    }

    internal static void ValidateArguments(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count is not (2 or 5))
            throw new ArgumentException("Only fixed setactive or display-timeout invocations are allowed.", nameof(arguments));
        var verb = arguments[0].ToUpperInvariant();
        if (arguments.Count == 2)
        {
            if (verb != "/SETACTIVE" || !Guid.TryParseExact(arguments[1], "D", out _))
                throw new ArgumentException("Invalid setactive invocation.", nameof(arguments));
            return;
        }

        if (verb is not ("/SETACVALUEINDEX" or "/SETDCVALUEINDEX")
            || !Guid.TryParseExact(arguments[1], "D", out _)
            || !Guid.TryParseExact(arguments[2], "D", out var subgroup)
            || !Guid.TryParseExact(arguments[3], "D", out var setting)
            || subgroup != PowerManagementGuids.DisplaySubgroup
            || setting != PowerManagementGuids.DisplayIdleTimeout
            || !uint.TryParse(arguments[4], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var seconds)
            || seconds > 86_400)
        {
            throw new ArgumentException("Invocation is outside the fixed display-timeout allowlist.", nameof(arguments));
        }
    }

    private static async Task<bool> AwaitTerminationAsync(IPowerProcessHandle process)
    {
        using var awaitCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await process.WaitForExitAsync(awaitCts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) { return false; }
    }
}

public sealed class WindowsPowerProcessFactory : IPowerProcessFactory
{
    public IPowerProcessHandle Start(IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "powercfg.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("powercfg.exe could not be started.");
        }
        return new WindowsPowerProcessHandle(process);
    }

    private sealed class WindowsPowerProcessHandle : IPowerProcessHandle
    {
        private readonly Process _process;
        public WindowsPowerProcessHandle(Process process) => _process = process;
        public StreamReader StandardOutput => _process.StandardOutput;
        public StreamReader StandardError => _process.StandardError;
        public int ExitCode => _process.ExitCode;
        public Task WaitForExitAsync(CancellationToken cancellationToken) => _process.WaitForExitAsync(cancellationToken);
        public void KillEntireTree()
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        public ValueTask DisposeAsync()
        {
            _process.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
