using System;

namespace GameOptimizer.Shell.Services;

public interface IGameAutoOptimizationService : IDisposable
{
	bool IsRunning { get; }

	void Start();

	void Stop();
}
