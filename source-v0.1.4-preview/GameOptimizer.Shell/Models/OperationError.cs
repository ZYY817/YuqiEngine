// File: GameOptimizer.Shell/Models/OperationError.cs
using System;

namespace GameOptimizer.Shell.Models;

/// <summary>
/// Represents an error that occurred during an optimization operation.
/// </summary>
public sealed class OperationError
{
    /// <summary>
    /// Gets the name of the operation that failed.
    /// Empty string if no name is available.
    /// </summary>
    public string ItemName { get; init; } = string.Empty;
    
    /// <summary>
    /// Gets the error message describing what went wrong.
    /// Empty string if no message is available.
    /// </summary>
    public string ErrorMessage { get; init; } = string.Empty;
    
    /// <summary>
    /// Gets the exception that caused the error, if available.
    /// Null if only a message was recorded without the exception object.
    /// </summary>
    public Exception? Exception { get; init; }
}
