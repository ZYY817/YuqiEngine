using System;
using System.Collections.Generic;

namespace GameOptimizer.Shell.Services.Baseline;

public sealed class BaselineCatalog
{
	public int SchemaVersion { get; set; } = 1;

	public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

	public List<BaselineCatalogEntry> Entries { get; set; } = new List<BaselineCatalogEntry>();
}
