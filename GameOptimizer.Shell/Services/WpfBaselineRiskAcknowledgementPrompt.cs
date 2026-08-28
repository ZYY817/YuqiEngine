using System.Windows;
using System.Windows.Threading;
using GameOptimizer.Shell.Services.Baseline;
using GameOptimizer.Shell.Views;

namespace GameOptimizer.Shell.Services;

public sealed class WpfBaselineRiskAcknowledgementPrompt : IBaselineRiskAcknowledgementPrompt
{
    public async Task<bool> ConfirmAsync(
        BaselineRiskAcknowledgementRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var application = Application.Current
            ?? throw new InvalidOperationException("应用 UI 尚未初始化，无法请求无基线风险确认。");

        if (application.Dispatcher.CheckAccess())
        {
            return Show(application, request);
        }

        var operation = application.Dispatcher.InvokeAsync(
            () => Show(application, request),
            DispatcherPriority.Normal,
            cancellationToken);
        return await operation.Task.ConfigureAwait(false);
    }

    private static bool Show(Application application, BaselineRiskAcknowledgementRequest request)
    {
        var owner = application.Windows
            .OfType<Window>()
            .FirstOrDefault(window => window.IsActive)
            ?? application.MainWindow;
        return DangerousOperationDialog.Show(
            owner,
            "未建立可验证的首次安全基线",
            request.RiskSummary,
            new[]
            {
                request.RecoveryCapabilityDescription,
                "继续只授权本次操作；不会跳过管理员权限、能力检查、原有操作确认、写后校验或失败阻断。",
                "建议先在“备份与恢复”建立首次安全基线，以获得更完整的恢复依据。"
            },
            confirmationKeyword: null,
            countdownSeconds: 3);
    }
}
