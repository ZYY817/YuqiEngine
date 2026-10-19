namespace GameOptimizer.Modules;

public sealed class ModuleDescriptor
{
	public string? AssemblyPath { get; set; }

	public string? TypeName { get; set; }

	public bool Enabled { get; set; } = true;
}
