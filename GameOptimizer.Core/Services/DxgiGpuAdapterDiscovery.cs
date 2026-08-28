using System.Runtime.InteropServices;
using GameOptimizer.Core.Models;

namespace GameOptimizer.Core.Services;

/// <summary>
/// Enumerates the current-session DXGI adapters solely to obtain their AdapterLuid.
/// GPU Engine performance-counter instances use that same LUID; WMI exposes no
/// equivalent field, so it cannot prove a live-load-to-device association by itself.
/// </summary>
internal static class DxgiGpuAdapterDiscovery
{
    private const int DxgiErrorNotFound = unchecked((int)0x887A0002);
    private static readonly Guid Factory1Id = new("770aae78-f26f-4dba-a829-253c83d1b387");

    internal static IReadOnlyList<DxgiGpuAdapter> TryEnumerate()
    {
        var adapters = new List<DxgiGpuAdapter>();
        IntPtr factory = IntPtr.Zero;
        try
        {
            Guid factoryId = Factory1Id;
            int result = CreateDXGIFactory1(ref factoryId, out factory);
            if (result < 0 || factory == IntPtr.Zero)
                return adapters;

            for (uint index = 0; ; index++)
            {
                IntPtr adapter = IntPtr.Zero;
                result = Invoke<EnumAdapters1Delegate>(factory, 12)(factory, index, out adapter);
                if (result == DxgiErrorNotFound)
                    break;
                if (result < 0 || adapter == IntPtr.Zero)
                    continue;

                try
                {
                    result = Invoke<GetDesc1Delegate>(adapter, 10)(adapter, out DxgiAdapterDesc1 desc);
                    if (result >= 0)
                    {
                        adapters.Add(new DxgiGpuAdapter(
                            FormatLuid(desc.AdapterLuid),
                            desc.Description?.Trim() ?? string.Empty,
                            desc.VendorId,
                            desc.DeviceId,
                            (desc.Flags & 0x2) != 0));
                    }
                }
                finally
                {
                    Marshal.Release(adapter);
                }
            }
        }
        catch (DllNotFoundException)
        {
            // DXGI is absent only on unsupported Windows variants; keep WMI inventory usable.
        }
        catch (EntryPointNotFoundException)
        {
        }
        catch (COMException)
        {
        }
        finally
        {
            if (factory != IntPtr.Zero)
                Marshal.Release(factory);
        }

        return adapters;
    }

    internal static string FormatLuid(Luid luid) => $"0x{luid.HighPart:X8}_0x{luid.LowPart:X8}";

    private static T Invoke<T>(IntPtr comObject, int methodIndex) where T : Delegate
    {
        IntPtr vtable = Marshal.ReadIntPtr(comObject);
        IntPtr method = Marshal.ReadIntPtr(vtable, methodIndex * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<T>(method);
    }

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters1Delegate(IntPtr self, uint index, out IntPtr adapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDesc1Delegate(IntPtr self, out DxgiAdapterDesc1 desc);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Luid
    {
        internal uint LowPart;
        internal int HighPart;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DxgiAdapterDesc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        internal string? Description;
        internal uint VendorId;
        internal uint DeviceId;
        internal uint SubSysId;
        internal uint Revision;
        internal nuint DedicatedVideoMemory;
        internal nuint DedicatedSystemMemory;
        internal nuint SharedSystemMemory;
        internal Luid AdapterLuid;
        internal uint Flags;
    }
}

internal sealed record DxgiGpuAdapter(
    string Luid,
    string Name,
    uint VendorId,
    uint DeviceId,
    bool IsSoftware);
