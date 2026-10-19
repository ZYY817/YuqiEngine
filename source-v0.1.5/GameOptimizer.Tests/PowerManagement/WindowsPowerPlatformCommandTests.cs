using GameOptimizer.Core.PowerManagement;
using GameOptimizer.Infrastructure.Windows.PowerManagement;

namespace GameOptimizer.Tests.PowerManagement;

public sealed class WindowsPowerPlatformCommandTests
{
    [Fact]
    public async Task DcFailureStopsBeforeActivationAndPreservesPartialResults()
    {
        var runner = new FakeRunner(
            new PowerCommandResult(PowerCommandOutcome.Success, "/SETACVALUEINDEX", 0, "ac-ok", string.Empty),
            new PowerCommandResult(PowerCommandOutcome.Failed, "/SETDCVALUEINDEX", 5, string.Empty, "dc-failed"));
        var platform = new WindowsPowerPlatform(runner);
        var scheme = Guid.NewGuid();
        var result = await platform.ApplyDisplayTimeoutAsync(scheme, new PowerSettingValues(900, 420), scheme, default);
        Assert.Equal(PowerCommandOutcome.Failed, result.Outcome);
        Assert.Equal(2, result.Commands.Count);
        Assert.Equal(2, runner.Invocations.Count);
        Assert.DoesNotContain(runner.Invocations, invocation => invocation[0] == "/SETACTIVE");
    }

    [Fact]
    public async Task DesktopInvocationOmitsDcAndActivatesOnlyAfterAcSuccess()
    {
        var runner = new FakeRunner(
            new PowerCommandResult(PowerCommandOutcome.Success, "/SETACVALUEINDEX", 0, string.Empty, string.Empty),
            new PowerCommandResult(PowerCommandOutcome.Success, "/SETACTIVE", 0, string.Empty, string.Empty));
        var platform = new WindowsPowerPlatform(runner);
        var scheme = Guid.NewGuid();
        var result = await platform.ApplyDisplayTimeoutAsync(scheme, new PowerSettingValues(900, null), scheme, default);
        Assert.Equal(PowerCommandOutcome.Success, result.Outcome);
        Assert.Equal(new[] { "/SETACVALUEINDEX", "/SETACTIVE" }, runner.Invocations.Select(item => item[0]));
    }

    private sealed class FakeRunner : IPowerCfgProcessRunner
    {
        private readonly Queue<PowerCommandResult> _results;
        public FakeRunner(params PowerCommandResult[] results) => _results = new Queue<PowerCommandResult>(results);
        public List<IReadOnlyList<string>> Invocations { get; } = new();
        public Task<PowerCommandResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Invocations.Add(arguments.ToArray());
            return Task.FromResult(_results.Dequeue());
        }
    }
}
