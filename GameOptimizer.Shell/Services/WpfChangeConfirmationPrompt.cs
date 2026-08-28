using System.Windows;
using System.Windows.Threading;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Shell.Views;

namespace GameOptimizer.Shell.Services;

public sealed class WpfChangeConfirmationPrompt : IChangeConfirmationPrompt
{
    public async Task<bool> ConfirmAsync(
        ChangeConfirmationPrompt prompt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // The master-module page has already shown one scrollable confirmation that
        // lists every selected operation and its risk. Keep the core token boundary
        // intact, but do not show a duplicate WPF dialog for each plan in that batch.
        if (BatchChangeConfirmationScope.IsActive)
        {
            return true;
        }

        var application = Application.Current
            ?? throw new InvalidOperationException("应用 UI 尚未初始化，无法请求用户确认。");

        if (application.Dispatcher.CheckAccess())
        {
            return Show(application, prompt);
        }

        var operation = application.Dispatcher.InvokeAsync(
            () => Show(application, prompt),
            DispatcherPriority.Normal,
            cancellationToken);
        return await operation.Task.ConfigureAwait(false);
    }

    private static bool Show(Application application, ChangeConfirmationPrompt prompt)
    {
        var owner = application.Windows
            .OfType<Window>()
            .FirstOrDefault(window => window.IsActive)
            ?? application.MainWindow;
        var consequences = string.Equals(
            prompt.CapabilityId,
            GameOptimizer.Core.Services.Memory.MemoryCleaningCapabilityIds.ProcessTermination,
            StringComparison.OrdinalIgnoreCase)
            ? new[]
            {
                $"操作：{CapabilityDisplayFormatter.GetDisplayName(prompt.CapabilityId)}",
                "强制结束会丢失未保存数据，且无法恢复；执行前会再次核验进程身份。",
                "PID 已复用、目标退出、系统保护或权限不足时会阻断。"
            }
            : new[]
            {
                $"操作：{CapabilityDisplayFormatter.GetDisplayName(prompt.CapabilityId)}",
                "执行前将再次回读当前状态并持久化恢复材料。",
                "设置发生外部漂移、备份失败或权限不足时会阻断。"
            };
        return DangerousOperationDialog.Show(
            owner,
            prompt.Title,
            prompt.Summary,
            consequences,
            confirmationKeyword: null,
            countdownSeconds: 2);
    }
}
