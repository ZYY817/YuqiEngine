// File: GameOptimizer.Shell/Models/BatchOperationStatus.cs
namespace GameOptimizer.Shell.Models;

/// <summary>
/// Represents the status of a batch optimization operation.
/// </summary>
public enum BatchOperationStatus
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
