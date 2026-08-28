using System.Collections.Generic;

namespace GameOptimizer.Modules;

public sealed class ModuleManifest
{
	public List<ModuleDescriptor> Modules { get; set; } = new List<ModuleDescriptor>();
}
