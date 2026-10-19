// File: GameOptimizer.Tests/Changes/TestTransactionLogFactory.cs
using GameOptimizer.Core.Services;

namespace GameOptimizer.Tests.Changes;

/// <summary>
/// Engine tests that do not supply an explicit transaction log must not share the
/// production log at <c>AppPaths.TransactionLogFilePath</c>. That file is process-wide
/// and grows across every run; a shared 1MB+ log makes each <c>ReadState</c> parse the
/// entire history under parallel load and pushes timing-sensitive engine tests past
/// their 2-second <c>WaitAsync</c> budgets. Each engine gets a fresh, isolated temp log.
/// </summary>
internal static class TestTransactionLogFactory
{
    public static TransactionLogService CreateIsolated() =>
        new(Path.Combine(Path.GetTempPath(), $"yuqi-engine-test-{Guid.NewGuid():N}.log"));
}
