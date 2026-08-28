using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameOptimizer.Core.Diagnostics;

public sealed class DiagnosticLogLoggerProvider : ILoggerProvider, IDisposable
{
	private sealed class DiagnosticsLogger : ILogger
	{
		private readonly IDiagnosticLogService _logService;

		private readonly string _categoryName;

		public DiagnosticsLogger(IDiagnosticLogService logService, string categoryName)
		{
			_logService = logService;
			_categoryName = categoryName;
		}

		public IDisposable? BeginScope<TState>(TState state) where TState : notnull
		{
			return null;
		}

		public bool IsEnabled(LogLevel logLevel)
		{
			return logLevel >= LogLevel.Information;
		}

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
		{
			if (!IsEnabled(logLevel))
			{
				return;
			}
			string message = formatter(state, exception) ?? state?.ToString() ?? string.Empty;
			string? detail = exception?.ToString();
			_logService.Append(logLevel, _categoryName, message, detail);
		}
	}

	private readonly IDiagnosticLogService _logService;

	public DiagnosticLogLoggerProvider(IDiagnosticLogService logService)
	{
		_logService = logService;
	}

	public ILogger CreateLogger(string categoryName)
	{
		if (!categoryName.StartsWith("GameOptimizer", StringComparison.Ordinal))
		{
			return NullLogger.Instance;
		}
		return new DiagnosticsLogger(_logService, categoryName);
	}

	public void Dispose()
	{
	}
}
