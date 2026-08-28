using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GameOptimizer.Core.Services.Latency
{
    public interface ILatencyTestService
    {
        // 新版响应性测试
        Task<ResponsivenessTestSession> RunResponsivenessTestAsync(
            int sampleCount, 
            IProgress<(int current, string phase)>? progress = null, 
            CancellationToken ct = default);

        // 兼容旧接口
        Task<LatencyTestResult> RunSingleTestAsync(CancellationToken ct = default);
        Task<LatencyTestSession> RunBatchTestAsync(int sampleCount, IProgress<int>? progress = null, CancellationToken ct = default);
        LatencyStatistics CalculateStatistics(IReadOnlyList<LatencyTestResult> results, LatencyTestType type);
        IReadOnlyList<LatencyChainStep> GetLatencyChain(LatencyTestResult result);
        
        // 历史记录
        Task SaveSessionAsync(LatencyTestSession session);
        Task<IReadOnlyList<LatencyTestSession>> LoadHistoryAsync(int maxCount = 10);
        Task ClearHistoryAsync();
        
        // 导出
        Task<string> ExportReportAsync(LatencyTestSession session);
    }
}
