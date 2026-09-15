// File: GameOptimizer.Shell/Models/OperationDetailCompletenessModel.cs
namespace GameOptimizer.Shell.Models;

public sealed record OperationDetailCompletenessModel(
    string Level,
    string Summary,
    string MissingItems);
