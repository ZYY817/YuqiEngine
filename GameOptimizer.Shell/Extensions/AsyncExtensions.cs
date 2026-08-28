using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace GameOptimizer.Shell.Extensions;

/// <summary>
/// async void 事件处理器的安全包装扩展
/// </summary>
public static class AsyncExtensions
{
    /// <summary>
    /// 安全执行异步操作，捕获并记录异常（用于 async void 事件处理器）
    /// </summary>
    public static async void SafeFireAndForget(this Task task, string context = "Unknown")
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 取消操作是正常的，不记录
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SafeFireAndForget][{context}] 异常: {ex.Message}");
            // 可以在这里添加更多的日志记录或遥测
        }
    }

    /// <summary>
    /// 安全执行异步操作，带自定义错误处理
    /// </summary>
    public static async void SafeFireAndForget(this Task task, Action<Exception>? onError, string context = "Unknown")
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 取消操作是正常的
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SafeFireAndForget][{context}] 异常: {ex.Message}");
            onError?.Invoke(ex);
        }
    }

    /// <summary>
    /// 包装 async void 事件处理器，确保异常被捕获
    /// </summary>
    public static Func<Task> WrapAsync(Func<Task> asyncAction, string context = "Unknown")
    {
        return async () =>
        {
            try
            {
                await asyncAction().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 正常取消
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WrapAsync][{context}] 异常: {ex.Message}");
            }
        };
    }
}
