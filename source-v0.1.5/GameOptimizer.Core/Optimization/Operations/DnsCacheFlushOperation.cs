using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace GameOptimizer.Core.Optimization.Operations;

/// <summary>
/// Flushes the local DNS resolver cache by calling dnsapi.dll directly so the resolver reloads entries from upstream servers.
/// </summary>
public sealed class DnsCacheFlushOperation : IOptimizationOperation
{
    public DnsCacheFlushOperation(string id, string title, string description, string category)
    {
        Id = id;
        Title = title;
        Description = description;
        Category = category;
    }

    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public string Category { get; }
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Safe;

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            var entries = CountCacheEntries();
            var compliant = entries == 0;
            var details = compliant
                ? "DNS 缓存当前为空。"
                : $"当前 DNS 缓存条目数: {entries}";
            return Task.FromResult(new OperationStatus(compliant, details));
        }
        catch (Win32Exception ex)
        {
            return Task.FromResult(new OperationStatus(false, $"无法读取 DNS 缓存: {ex.Message}"));
        }
    }

    public Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        int before;
        try
        {
            before = CountCacheEntries();
        }
        catch (Win32Exception ex)
        {
            return Task.FromResult(new OperationInvokeResult(false, false, $"无法读取 DNS 缓存: {ex.Message}"));
        }

        if (before == 0)
        {
            return Task.FromResult(new OperationInvokeResult(true, false, "DNS 缓存本来就是空的，无需刷新。"));
        }

        var status = DnsFlushResolverCache();
        if (status != 0)
        {
            return Task.FromResult(new OperationInvokeResult(false, false, $"DnsFlushResolverCache 失败: 0x{status:X}"));
        }

        return Task.FromResult(new OperationInvokeResult(true, true, $"已刷新 DNS 缓存，刷新前共有 {before} 条缓存记录。"));
    }

    public Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken) =>
        Task.FromResult(new OperationInvokeResult(true, false, "DNS 缓存属于易失性运行时数据，系统会在后续联网过程中自动重建，无需回退。"));

    private static int CountCacheEntries()
    {
        var result = DnsGetCacheDataTable(out var entryPtr);
        if (result != 0)
        {
            throw new Win32Exception(result);
        }

        try
        {
            var count = 0;
            var current = entryPtr;
            while (current != IntPtr.Zero)
            {
                var entry = Marshal.PtrToStructure<DnsCacheEntry>(current);
                if (entry is null)
                {
                    break;
                }

                if (entry.Name != IntPtr.Zero)
                {
                    count++;
                }

                current = entry.Next;
            }

            return count;
        }
        finally
        {
            if (entryPtr != IntPtr.Zero)
            {
                DnsFree(entryPtr, DnsFreeType.DnsFreeFlat);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private sealed class DnsCacheEntry
    {
        public IntPtr Next;
        public IntPtr Name;
        public ushort Type;
        public ushort DataLength;
        public uint Flags;
    }

    private enum DnsFreeType
    {
        DnsFreeFlat = 0
    }

    [DllImport("dnsapi.dll", EntryPoint = "DnsGetCacheDataTable", SetLastError = true)]
    private static extern int DnsGetCacheDataTable(out IntPtr dnsCacheEntry);

    [DllImport("dnsapi.dll", EntryPoint = "DnsFree")]
    private static extern void DnsFree(IntPtr ptr, DnsFreeType freeType);

    [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache")]
    private static extern int DnsFlushResolverCache();
}
