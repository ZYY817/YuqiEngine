using System.Windows;
using GameOptimizer.Shell.Models;
using GameOptimizer.Shell.ViewModels;
using GameOptimizer.Shell.Views;

namespace GameOptimizer.Shell.Services;

public static class OperationDetailCardLauncher
{
	public static bool TryGetCard(string? operationId, out OperationDetailCardModel? card)
	{
		card = null;
		if (string.IsNullOrWhiteSpace(operationId))
		{
			return false;
		}
		if (!(Application.Current?.MainWindow?.DataContext is DashboardViewModel dashboardViewModel))
		{
			return false;
		}
		return dashboardViewModel.TryBuildOperationDetailCard(operationId, out card) && card != null;
	}

	public static bool TryOpen(Window? owner, string? operationId)
	{
		if (!TryGetCard(operationId, out var card) || card is null)
		{
			return false;
		}
		OperationDetailCardDialog.Show(owner ?? Application.Current?.MainWindow, card);
		return true;
	}
}
