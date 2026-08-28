// File: GameOptimizer.Shell/Models/OperationStatus.cs
namespace GameOptimizer.Shell.Models;

/// <summary>
/// Represents the status of an optimization operation.
/// </summary>
public enum OperationStatus
{
    /// <summary>
    /// Operation is starting.
    /// </summary>
    Starting,
    
    /// <summary>
    /// Operation is currently being processed.
    /// </summary>
    Processing,
    
    /// <summary>
    /// Operation completed successfully.
    /// </summary>
    Completed,
    
    /// <summary>
    /// Operation failed.
    /// </summary>
    Failed
}
