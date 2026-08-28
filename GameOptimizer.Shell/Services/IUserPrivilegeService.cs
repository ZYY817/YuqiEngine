namespace GameOptimizer.Shell.Services;

public interface IUserPrivilegeService
{
	bool IsAdministrator { get; }

	void Refresh();
}
