using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.ModuleContracts.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace GameOptimizer.ModuleContracts.Abstractions;

public interface IModule
{
	string Id { get; }

	string DisplayName { get; }

	Version Version { get; }

	ModuleScope Scope { get; }

	void ConfigureServices(IServiceCollection services);

	Task InitializeAsync(IModuleContext context, CancellationToken cancellationToken);

	IEnumerable<IModuleWidget> CreateWidgets(IServiceProvider services);

	IEnumerable<IModuleAction> CreateActions(IServiceProvider services);
}
