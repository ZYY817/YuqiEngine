namespace GameOptimizer.Shell.Models;

public class OptimizationItemInfo
{
    public string OperationId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string ModuleName { get; set; } = "";
    public string RiskLevel { get; set; } = "Safe"; // Safe, Medium, High
}
