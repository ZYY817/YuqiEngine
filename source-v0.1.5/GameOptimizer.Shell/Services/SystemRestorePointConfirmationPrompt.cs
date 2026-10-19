using System.Windows;
using GameOptimizer.Core.SystemRestore;
using GameOptimizer.Shell.Views;

namespace GameOptimizer.Shell.Services;

public interface ISystemRestorePointConfirmationPrompt
{
    Task<bool> ConfirmCreateAsync(
        SystemRestorePointCreatePlan plan,
        CancellationToken cancellationToken);
}

public sealed class WpfSystemRestorePointConfirmationPrompt : ISystemRestorePointConfirmationPrompt
{
    public async Task<bool> ConfirmCreateAsync(
        SystemRestorePointCreatePlan plan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();
        if (Application.Current?.Dispatcher is not { } dispatcher)
        {
            return false;
        }

        return await dispatcher.InvokeAsync(
            () => RestorePointConfirmDialog.ShowCreateConfirm(
                Application.Current.MainWindow,
                plan.Description),
            System.Windows.Threading.DispatcherPriority.Normal,
            cancellationToken);
    }
}
