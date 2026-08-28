using GameOptimizer.Core.Services;

namespace GameOptimizer.Shell.Services;

public interface IConfigurationExportService
{
    Task<OperationResult<string>> ExportAsync(string? filePath = null, CancellationToken cancellationToken = default);
    Task<OperationResult<ExportedConfiguration>> ImportAsync(string filePath, CancellationToken cancellationToken = default);
    Task<OperationResult> ApplyConfigurationAsync(ExportedConfiguration config, IOperationProgress? progress = null, CancellationToken cancellationToken = default);
    IEnumerable<string> GetExportedConfigs();
}
