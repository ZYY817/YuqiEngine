using System.Collections.Generic;
using GameOptimizer.Core.Diagnostics;
using GameOptimizer.Core.Services;

namespace GameOptimizer.Core.Optimization;

public sealed record OperationInvokeResult(
    bool Success,
    bool Changed,
    string Message)
{
    /// <summary>
    /// 可选的诊断信息（用于详细错误分析）
    /// </summary>
    public DiagnosticResult? Diagnostic { get; init; }
    
    /// <summary>
    /// 可选的自动修复选项列表
    /// </summary>
    public List<AutoRepairOption>? RepairOptions { get; init; }
    
    /// <summary>
    /// 是否包含诊断信息
    /// </summary>
    public bool HasDiagnostic => Diagnostic != null;
    
    /// <summary>
    /// 是否可以自动修复
    /// </summary>
    public bool CanAutoRepair => RepairOptions?.Count > 0;
    
    /// <summary>
    /// 从 EnhancedOperationResult 创建 OperationInvokeResult
    /// </summary>
    public static OperationInvokeResult FromEnhanced(EnhancedOperationResult enhanced)
    {
        return new OperationInvokeResult(
            enhanced.Code == OperationResultCode.Success,
            enhanced.Code == OperationResultCode.Success,
            enhanced.Message)
        {
            Diagnostic = enhanced.Diagnostic,
            RepairOptions = enhanced.RepairOptions
        };
    }
    
    /// <summary>
    /// 创建带诊断信息的失败结果
    /// </summary>
    public static OperationInvokeResult FailedWithDiagnostic(
        string message,
        DiagnosticResult diagnostic,
        List<AutoRepairOption>? repairOptions = null)
    {
        return new OperationInvokeResult(false, false, message)
        {
            Diagnostic = diagnostic,
            RepairOptions = repairOptions
        };
    }
}
