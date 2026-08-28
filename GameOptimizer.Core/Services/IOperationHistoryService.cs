using GameOptimizer.Core.Models;

namespace GameOptimizer.Core.Services;

public interface IOperationHistoryService
{
    void Record(OperationHistoryRecord record);
    void RecordBatch(IEnumerable<OperationHistoryRecord> records);
    IReadOnlyList<OperationHistoryRecord> GetRecent(int count = 50);
    IReadOnlyList<OperationHistoryRecord> GetByDateRange(DateTime from, DateTime to);
    IReadOnlyList<OperationHistoryRecord> GetByModule(string moduleKey, int count = 50);
    IReadOnlyList<OperationHistorySummary> GetDailySummary(int days = 7);
    void ClearOlderThan(DateTime cutoff);
    int TotalCount { get; }
}
