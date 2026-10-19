using GameOptimizer.ModuleContracts.Models;

namespace GameOptimizer.Shell.Models;

public sealed class WidgetHostModel
{
	public string Id { get; }

	public WidgetDescriptor Descriptor { get; }

	public object Content { get; }

	public WidgetHostModel(string id, WidgetDescriptor descriptor, object content)
	{
		Id = id;
		Descriptor = descriptor;
		Content = content;
	}
}
