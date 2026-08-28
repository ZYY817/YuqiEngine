using System;
using GameOptimizer.ModuleContracts.Models;

namespace GameOptimizer.ModuleContracts.Abstractions;

public interface IModuleWidget
{
	string Id { get; }

	WidgetDescriptor Descriptor { get; }

	object BuildContent(IServiceProvider services);
}
