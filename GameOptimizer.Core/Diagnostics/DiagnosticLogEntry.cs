using System;
using Microsoft.Extensions.Logging;

namespace GameOptimizer.Core.Diagnostics;

public sealed record DiagnosticLogEntry(DateTime Timestamp, LogLevel Level, string Source, string Message, string? Detail = null);
