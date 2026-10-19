using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace GameOptimizer.Core.Diagnostics;

public interface IDiagnosticLogService
{
	string LogDirectory { get; }

	event EventHandler<DiagnosticLogEntry>? EntryPublished;

	void Append(LogLevel level, string source, string message, string? detail = null);

	IReadOnlyList<DiagnosticLogEntry> GetRecentEntries(int count = 32);

	Task FlushAsync(CancellationToken cancellationToken = default(CancellationToken));
}
