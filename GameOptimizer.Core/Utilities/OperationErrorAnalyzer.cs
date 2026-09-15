// File: GameOptimizer.Core/Utilities/OperationErrorAnalyzer.cs
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using GameOptimizer.Core.Diagnostics;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Services;

namespace GameOptimizer.Core.Utilities;

public static class OperationErrorAnalyzer
{
    private static readonly Lazy<DiagnosticService> _diagnosticService = new(() => new DiagnosticService());

    public static string AnalyzeException(Exception ex, string operationContext = "")
    {
        try
        {
            var context = new DiagnosticContext
            {
                OperationType = operationContext,
                TargetResource = string.Empty,
                IsAdminMode = WindowsElevation.IsElevated
            };

            var result = _diagnosticService.Value.Diagnose(ex, context);
            return result.UserMessage;
        }
        catch
        {
            return ex.Message;
        }
    }

    public static DiagnosticResult DiagnoseWithDetails(Exception ex, string operationContext = "", string targetResource = "")
    {
        var context = new DiagnosticContext
        {
            OperationType = operationContext,
            TargetResource = targetResource,
            IsAdminMode = WindowsElevation.IsElevated
        };

        return _diagnosticService.Value.Diagnose(ex, context);
    }

    public static (bool needsAdmin, string specificReason) DiagnosePermissionIssue(Exception ex, string operationContext = "")
    {
        try
        {
            var context = new DiagnosticContext
            {
                OperationType = operationContext,
                TargetResource = string.Empty,
                IsAdminMode = WindowsElevation.IsElevated
            };

            var result = _diagnosticService.Value.Diagnose(ex, context);
            
            if (context.IsAdminMode)
            {
                // Why: 已提权仍被拒绝访问时，不得再提示"需要管理员权限"。
                return (false, PermissionChecker.GetSystemDeniedMessage(
                    string.IsNullOrWhiteSpace(operationContext) ? "系统写入" : operationContext,
                    result.UserMessage));
            }

            var needsAdmin = result.Category == ErrorCategory.Permission && 
                           result.Severity >= ErrorSeverity.High;

            if (needsAdmin)
            {
                return (true, PermissionChecker.GetAdminRequiredMessage(
                    string.IsNullOrWhiteSpace(operationContext) ? "系统写入" : operationContext));
            }

            return (false, result.UserMessage);
        }
        catch
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// 为Operation提供统一的异常处理包装
    /// </summary>
    public static OperationInvokeResult HandleOperationException(Exception ex, string context, bool wasModified = false)
    {
        var analysis = AnalyzeException(ex, context);
        return new OperationInvokeResult(false, wasModified, analysis);
    }
}
