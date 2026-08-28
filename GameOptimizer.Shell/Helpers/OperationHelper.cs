// File: GameOptimizer.Shell/Helpers/OperationHelper.cs
using System;
using System.Threading.Tasks;
using System.Windows;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Services;
using GameOptimizer.Shell.Views;

namespace GameOptimizer.Shell.Helpers;

/// <summary>
/// 统一的操作执行包装器
/// 自动处理Loading、Toast通知、错误处理
/// </summary>
public static class OperationHelper
{
    /// <summary>
    /// 执行异步操作，自动显示Loading和Toast
    /// </summary>
    public static async Task<bool> ExecuteAsync(
        Func<Task> operation,
        string operationName,
        Window? owner = null,
        bool showSuccessToast = true,
        bool showErrorToast = true)
    {
        var loc = LocalizationService.Instance;
        
        try
        {
            await operation();
            
            if (showSuccessToast)
            {
                ToastService.Instance.Success(
                    loc.Get("Operation.Success", "操作成功"),
                    loc.Get($"Operation.{operationName}.Success", operationName)
                );
            }
            
            return true;
        }
        catch (Exception ex)
        {
            if (showErrorToast)
            {
                ToastService.Instance.Error(
                    loc.Get("Operation.Failed", "操作失败"),
                    ex.Message
                );
            }
            
            // 显示详细错误对话框
            if (owner != null)
            {
                var errorDialog = new SmartErrorDialog();
                errorDialog.Owner = owner;
                errorDialog.SetError(operationName, ex.Message);
                errorDialog.ShowDialog();
            }
            
            return false;
        }
    }
    
    /// <summary>
    /// 执行危险操作，带确认对话框
    /// </summary>
    public static async Task<bool> ExecuteDangerousAsync(
        Func<Task> operation,
        string title,
        string message,
        string[]? consequences = null,
        string? confirmationKeyword = null,
        int countdownSeconds = 0,
        Window? owner = null,
        bool showSuccessToast = true)
    {
        var confirmed = DangerousOperationDialog.Show(
            owner,
            title,
            message,
            consequences,
            confirmationKeyword,
            countdownSeconds
        );
        
        if (!confirmed)
            return false;
        
        return await ExecuteAsync(operation, title, owner, showSuccessToast);
    }
    
    /// <summary>
    /// 执行带进度的操作
    /// </summary>
    public static async Task<bool> ExecuteWithProgressAsync(
        Func<IProgress<string>, Task> operation,
        string operationName,
        Window? owner = null)
    {
        var loc = LocalizationService.Instance;
        ProgressDialog? progressDialog = null;
        
        try
        {
            progressDialog = new ProgressDialog
            {
                Owner = owner,
                Title = loc.Get("Operation.Progress", "操作进度"),
                Message = loc.Get("Operation.PleaseWait", "请稍候...")
            };

            var progress = new Progress<string>(message =>
            {
                if (progressDialog != null)
                {
                    progressDialog.Message = message;
                }
            });

            progressDialog.Show();

            await operation(progress);
            
            progressDialog.Close();
            return true;
        }
        catch (Exception ex)
        {
            progressDialog?.Close();
            
            ToastService.Instance.Error(
                loc.Get("Operation.Failed", "操作失败"),
                ex.Message
            );
            
            if (owner != null)
            {
                var errorDialog = new SmartErrorDialog();
                errorDialog.Owner = owner;
                errorDialog.SetError(operationName, ex.Message);
                errorDialog.ShowDialog();
            }
            
            return false;
        }
        finally
        {
            progressDialog?.Close();
        }
    }
}
