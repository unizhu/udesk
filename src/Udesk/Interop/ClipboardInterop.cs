using System.Runtime.InteropServices;

namespace Udesk.Interop;

/// <summary>
/// Win32 clipboard API interop for text clipboard synchronization.
/// </summary>
public static class ClipboardInterop
{
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(IntPtr hWndNewOwner);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr GetClipboardData(uint uFormat);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsClipboardFormatAvailable(uint format);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GlobalLock(IntPtr hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(IntPtr hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GlobalFree(IntPtr hMem);

    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    /// <summary>
    /// Reads text from the Windows clipboard.
    /// Returns null if clipboard is empty or not text.
    /// </summary>
    public static string? GetText()
    {
        if (!IsClipboardFormatAvailable(CF_UNICODETEXT))
            return null;

        if (!OpenClipboard(IntPtr.Zero))
            return null;

        try
        {
            var handle = GetClipboardData(CF_UNICODETEXT);
            if (handle == IntPtr.Zero)
                return null;

            var pointer = GlobalLock(handle);
            if (pointer == IntPtr.Zero)
                return null;

            try
            {
                return Marshal.PtrToStringUni(pointer);
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>
    /// Writes text to the Windows clipboard.
    /// </summary>
    /// <returns>True if the text was successfully written.</returns>
    public static bool SetText(string text)
    {
        if (!OpenClipboard(IntPtr.Zero))
            return false;

        try
        {
            if (!EmptyClipboard())
                return false;

            var bytes = (text.Length + 1) * 2; // +1 for null terminator, *2 for UTF-16
            var hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes);
            if (hMem == IntPtr.Zero)
                return false;

            var ptr = GlobalLock(hMem);
            if (ptr == IntPtr.Zero)
            {
                GlobalFree(hMem);
                return false;
            }

            try
            {
                Marshal.Copy(text.ToCharArray(), 0, ptr, text.Length);
                // Null terminator is already zeroed by GlobalAlloc
            }
            finally
            {
                GlobalUnlock(hMem);
            }

            var result = SetClipboardData(CF_UNICODETEXT, hMem);
            // If SetClipboardData succeeds, it takes ownership of hMem; don't free it
            if (result == IntPtr.Zero)
            {
                GlobalFree(hMem);
                return false;
            }

            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }
}
