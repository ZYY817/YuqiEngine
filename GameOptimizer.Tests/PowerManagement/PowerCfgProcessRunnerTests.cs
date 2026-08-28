using System.Text;
using GameOptimizer.Core.PowerManagement;
using GameOptimizer.Infrastructure.Windows.PowerManagement;

namespace GameOptimizer.Tests.PowerManagement;

public sealed class PowerCfgProcessRunnerTests
{
    [Fact]
    public async Task Timeout_KillsEntireTreeAndAwaitsTermination()
    {
        var handle = new FakeHandle();
        var runner = new PowerCfgProcessRunner(new FakeFactory(handle));
        var result = await runner.RunAsync(ValidArguments(), TimeSpan.FromMilliseconds(20), default);
        Assert.Equal(PowerCommandOutcome.TimedOut, result.Outcome);
        Assert.Equal(1, handle.KillCalls);
        Assert.True(handle.TerminationAwaited);
    }

    [Fact]
    public async Task Cancellation_KillsEntireTreeAndAwaitsTermination()
    {
        var handle = new FakeHandle();
        var runner = new PowerCfgProcessRunner(new FakeFactory(handle));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var result = await runner.RunAsync(ValidArguments(), TimeSpan.FromSeconds(1), cts.Token);
        Assert.Equal(PowerCommandOutcome.Cancelled, result.Outcome);
        Assert.Equal(1, handle.KillCalls);
        Assert.True(handle.TerminationAwaited);
    }

    [Fact]
    public async Task UnknownArgumentsAreRejectedBeforeProcessStart()
    {
        var factory = new FakeFactory(new FakeHandle());
        var runner = new PowerCfgProcessRunner(factory);
        await Assert.ThrowsAsync<ArgumentException>(() => runner.RunAsync(new[] { "/DELETE", Guid.NewGuid().ToString("D") }, TimeSpan.FromSeconds(1), default));
        Assert.Equal(0, factory.StartCalls);
    }

    [Fact]
    public async Task NonZeroExitPreservesExitCodeStdoutAndStderr()
    {
        var handle = new FakeHandle(7, "machine-output", "machine-error");
        handle.Complete();
        var runner = new PowerCfgProcessRunner(new FakeFactory(handle));
        var result = await runner.RunAsync(ValidArguments(), TimeSpan.FromSeconds(1), default);
        Assert.Equal(PowerCommandOutcome.Failed, result.Outcome);
        Assert.Equal(7, result.ExitCode);
        Assert.Equal("machine-output", result.StandardOutput);
        Assert.Equal("machine-error", result.StandardError);
    }

    private static string[] ValidArguments() => new[]
    {
        "/SETACVALUEINDEX",
        Guid.NewGuid().ToString("D"),
        PowerManagementGuids.DisplaySubgroup.ToString("D"),
        PowerManagementGuids.DisplayIdleTimeout.ToString("D"),
        "600"
    };

    private sealed class FakeFactory : IPowerProcessFactory
    {
        private readonly FakeHandle _handle;
        public FakeFactory(FakeHandle handle) => _handle = handle;
        public int StartCalls { get; private set; }
        public IPowerProcessHandle Start(IReadOnlyList<string> arguments) { StartCalls++; return _handle; }
    }

    private sealed class FakeHandle : IPowerProcessHandle
    {
        private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public FakeHandle(int exitCode = 0, string stdout = "out", string stderr = "err")
        {
            ExitCode = exitCode;
            StandardOutput = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(stdout)), Encoding.UTF8);
            StandardError = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(stderr)), Encoding.UTF8);
        }
        public StreamReader StandardOutput { get; }
        public StreamReader StandardError { get; }
        public int ExitCode { get; }
        public int KillCalls { get; private set; }
        public bool TerminationAwaited { get; private set; }
        public async Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            if (KillCalls > 0) TerminationAwaited = true;
            await _exited.Task.WaitAsync(cancellationToken);
        }
        public void KillEntireTree() { KillCalls++; _exited.TrySetResult(); }
        public void Complete() => _exited.TrySetResult();
        public ValueTask DisposeAsync()
        {
            StandardOutput.Dispose();
            StandardError.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
