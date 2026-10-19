using GameOptimizer.Core.Optimization;

namespace GameOptimizer.Core.Optimization.Operations;

internal static class DelegatedOperationResultMapper
{
    internal static OperationInvokeResult WithMessages(
        OperationInvokeResult source,
        string changedMessage,
        string unchangedMessage)
    {
        if (!source.Success)
        {
            return source;
        }

        return new OperationInvokeResult(source.Success, source.Changed, source.Changed ? changedMessage : unchangedMessage)
        {
            Diagnostic = source.Diagnostic,
            RepairOptions = source.RepairOptions
        };
    }
}
