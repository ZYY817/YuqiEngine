namespace GameOptimizer.ModuleContracts.Models;

public sealed record WidgetDescriptor(string Title, string Subtitle, int ColumnSpan = 1, int RowSpan = 1, bool AllowResize = true, bool AllowReorder = true);
