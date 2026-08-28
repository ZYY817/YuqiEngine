using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace GameOptimizer.Shell.Services.Baseline;

public sealed class BaselineManualGuideService : IBaselineManualGuideService
{
    private readonly ISystemRestoreService _systemRestoreService;
    private readonly ILogger<BaselineManualGuideService> _logger;

    public BaselineManualGuideService(
        ISystemRestoreService systemRestoreService,
        ILogger<BaselineManualGuideService> logger)
    {
        _systemRestoreService = systemRestoreService ?? throw new ArgumentNullException(nameof(systemRestoreService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void OpenSystemProtectionSettings()
    {
        try
        {
            _systemRestoreService.OpenSystemRestoreSettings();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "[BaselineManualGuideService][OpenSystemProtectionSettings] Failed to open system restore settings.");
        }
    }

    public async Task<BaselineManualGuideCheckResult> RecheckRestorePointAsync(
        string packageId,
        DateTime flowStartedUtc,
        CancellationToken ct = default)
    {
        var safePackageId = string.IsNullOrWhiteSpace(packageId)
            ? $"BL-{DateTime.UtcNow:yyyyMMddHHmmss}"
            : packageId.Trim();
        var marker = "YuqiEngine Baseline " + safePackageId;
        try
        {
            var pointsBeforeCreate = await _systemRestoreService
                .GetAllRestorePointsAsync(ct)
                .ConfigureAwait(false);
            HashSet<int> identitiesBeforeCreate = pointsBeforeCreate
                .Select(point => point.SequenceNumber)
                .ToHashSet();
            var createStartedLocal = DateTime.Now;
            if (!await _systemRestoreService.CreateRestorePointAsync(marker, ct).ConfigureAwait(false))
            {
                return new BaselineManualGuideCheckResult
                {
                    Success = false,
                    Message = string.IsNullOrWhiteSpace(_systemRestoreService.LastError)
                        ? "还原点创建失败；首次基线保持阻断，旧还原点不会替代本次创建。"
                        : _systemRestoreService.LastError
                };
            }

            var latest = (await _systemRestoreService.GetAllRestorePointsAsync(ct).ConfigureAwait(false))
                .Where(point => !identitiesBeforeCreate.Contains(point.SequenceNumber))
                .Where(point => string.Equals(point.Description, marker, StringComparison.Ordinal))
                .Where(point => point.CreationTime >= createStartedLocal.AddMinutes(-1))
                .OrderByDescending(point => point.CreationTime)
                .SingleOrDefault();
            if (latest is null)
            {
                return new BaselineManualGuideCheckResult
                {
                    Success = false,
                    Message = "未回读到本次新建且身份唯一的还原点；首次基线保持阻断。"
                };
            }

            return new BaselineManualGuideCheckResult
            {
                Success = true,
                Message = $"还原点校验通过：{latest.CreationTime:yyyy-MM-dd HH:mm:ss}。"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[BaselineManualGuideService][RecheckRestorePointAsync] Recheck failed. package={PackageId}",
                safePackageId);
            return new BaselineManualGuideCheckResult
            {
                Success = false,
                Message = ex.Message
            };
        }
    }
}
