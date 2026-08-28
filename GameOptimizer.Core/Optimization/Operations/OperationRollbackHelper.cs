using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameOptimizer.Core.Optimization.Operations;

internal static class OperationRollbackHelper
{
    public static async Task<OperationInvokeResult> BuildApplyFailureResultAsync(
        OperationExecutionContext context,
        string operationId,
        string failureMessage,
        Func<bool> isOriginalStateIntact,
        Func<(bool Success, string Message)> restoreOriginalState,
        string intactMessage,
        string restoredMessage,
        CancellationToken cancellationToken)
    {
        var rollback = await TryRestoreOriginalStateAsync(
            context,
            operationId,
            isOriginalStateIntact,
            restoreOriginalState,
            intactMessage,
            restoredMessage,
            cancellationToken).ConfigureAwait(false);

        var message = string.IsNullOrWhiteSpace(failureMessage)
            ? rollback.Message
            : string.Concat(failureMessage, " ", rollback.Message);

        return new OperationInvokeResult(false, false, message);
    }

    public static async Task<(bool Success, string Message)> TryRestoreOriginalStateAsync(
        OperationExecutionContext context,
        string operationId,
        Func<bool> isOriginalStateIntact,
        Func<(bool Success, string Message)> restoreOriginalState,
        string intactMessage,
        string restoredMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            if (isOriginalStateIntact())
            {
                await context.TryDeleteSnapshotAsync(operationId, cancellationToken).ConfigureAwait(false);
                return (true, intactMessage);
            }

            var rollbackResult = restoreOriginalState();
            if (!rollbackResult.Success)
            {
                return rollbackResult;
            }

            await context.TryDeleteSnapshotAsync(operationId, cancellationToken).ConfigureAwait(false);
            return (true, restoredMessage);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}