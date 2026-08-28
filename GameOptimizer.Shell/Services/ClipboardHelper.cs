using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace GameOptimizer.Shell.Services;

public static class ClipboardHelper
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    public static bool SetText(string text, int maxRetries = 10)
    {
        if (string.IsNullOrEmpty(text)) return false;

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            if (TrySetTextInternal(text))
                return true;
            
            Thread.Sleep(50);
        }
        return false;
    }

    private static bool TrySetTextInternal(string text)
    {
        IntPtr hGlobal = IntPtr.Zero;
        bool clipboardOpened = false;

        try
        {
            if (!OpenClipboard(IntPtr.Zero))
                return false;
            
            clipboardOpened = true;

            if (!EmptyClipboard())
                return false;

            var bytes = (text.Length + 1) * 2;
            hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes);
            if (hGlobal == IntPtr.Zero)
                return false;

            var target = GlobalLock(hGlobal);
            if (target == IntPtr.Zero)
            {
                GlobalFree(hGlobal);
                return false;
            }

            try
            {
                Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
                Marshal.WriteInt16(target, text.Length * 2, 0);
            }
            finally
            {
                GlobalUnlock(hGlobal);
            }

            if (SetClipboardData(CF_UNICODETEXT, hGlobal) == IntPtr.Zero)
            {
                GlobalFree(hGlobal);
                return false;
            }

            hGlobal = IntPtr.Zero;
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (clipboardOpened)
                CloseClipboard();
        }
    }
}
