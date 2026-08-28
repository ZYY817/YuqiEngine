// File: GameOptimizer.Shell/Services/BatchChangeConfirmationScope.cs
using System.Threading;

namespace GameOptimizer.Shell.Services;

/// <summary>
/// Carries an already accepted page-level batch confirmation through the async operation pipeline.
/// Individual change plans still obtain and consume their own plan-bound confirmation tokens.
/// </summary>
internal static class BatchChangeConfirmationScope
{
    private static readonly AsyncLocal<int> Depth = new();

    public static bool IsActive => Depth.Value > 0;

    public static IDisposable Begin()
    {
        Depth.Value++;
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Depth.Value = Math.Max(0, Depth.Value - 1);
        }
    }
}
