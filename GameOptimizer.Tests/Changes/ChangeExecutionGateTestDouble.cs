using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Tests.Changes;

internal sealed class ChangeExecutionGateTestDouble : IChangeExecutionGate
{
    private readonly Func<string, ChangeExecutionIntent, CancellationToken, Task<ChangeExecutionGateDecision>> _handler;

    public ChangeExecutionGateTestDouble(
        Func<string, ChangeExecutionIntent, CancellationToken, Task<ChangeExecutionGateDecision>> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public static ChangeExecutionGateTestDouble Allowed() =>
        new((_, _, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ChangeExecutionGateDecision.Allowed("test.allowed"));
        });

    public static ChangeExecutionGateTestDouble ForIntents(
        ChangeExecutionGateDecision apply,
        ChangeExecutionGateDecision restore) =>
        new((_, intent, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(intent == ChangeExecutionIntent.Apply ? apply : restore);
        });

    public static ChangeExecutionGateTestDouble Denied(ChangeExecutionGateDecisionKind kind) =>
        new((_, _, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ChangeExecutionGateDecision(kind, "test.denied"));
        });

    public static ChangeExecutionGateTestDouble Throwing(Exception exception) =>
        new((_, _, _) => Task.FromException<ChangeExecutionGateDecision>(exception));

    public static ChangeExecutionGateTestDouble Cancelling() =>
        new((_, _, cancellationToken) =>
            Task.FromException<ChangeExecutionGateDecision>(new OperationCanceledException(cancellationToken)));

    public Task<ChangeExecutionGateDecision> EnsureCanExecuteAsync(
        string capabilityId,
        ChangeExecutionIntent intent,
        CancellationToken cancellationToken = default) =>
        _handler(capabilityId, intent, cancellationToken);
}
