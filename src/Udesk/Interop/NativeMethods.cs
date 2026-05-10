namespace Udesk.Interop;

/// <summary>
/// P/Invoke declarations for Win32 native methods.
/// Uses LibraryImport (source-generated) for AOT compatibility.
/// </summary>
internal static partial class NativeMethods
{
    // === user32.dll ===

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetCursorPos(int x, int y);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [LibraryImport("user32.dll")]
    internal static partial int GetSystemMetrics(int nIndex);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetCursorPos(out POINT lpPoint);

    // === kernel32.dll ===

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);

    // === sas.dll ===

    [LibraryImport("sas.dll")]
    internal static partial void SendSAS([MarshalAs(UnmanagedType.Bool)] bool asUser);
}

/// <summary>
/// Execution state flags for SetThreadExecutionState.
/// </summary>
[Flags]
internal enum EXECUTION_STATE : uint
{
    /// <summary>Enables away mode. Must be combined with ES_CONTINUOUS.</summary>
    ES_AWAYMODE_REQUIRED = 0x00000040,

    /// <summary>Informs the system that the state being set should remain in effect until the next call.</summary>
    ES_CONTINUOUS = 0x80000000,

    /// <summary>Forces the display to be on by resetting the display idle timer.</summary>
    ES_DISPLAY_REQUIRED = 0x00000002,

    /// <summary>Forces the system to be in the working state by resetting the system idle timer.</summary>
    ES_SYSTEM_REQUIRED = 0x00000001,

    /// <summary>Allows the system to wait for user input.</summary>
    ES_USER_PRESENT = 0x00000004,
}

/// <summary>Point structure for GetCursorPos.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X;
    public int Y;
}

/// <summary>INPUT structure for SendInput.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct INPUT
{
    public uint type;
    public InputUnion Data;
}

[StructLayout(LayoutKind.Explicit)]
internal struct InputUnion
{
    [FieldOffset(0)]
    public MOUSEINPUT mi;

    [FieldOffset(0)]
    public KEYBDINPUT ki;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MOUSEINPUT
{
    public int dx;
    public int dy;
    public uint mouseData;
    public uint dwFlags;
    public uint time;
    public IntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct KEYBDINPUT
{
    public ushort wVk;
    public ushort wScan;
    public uint dwFlags;
    public uint time;
    public IntPtr dwExtraInfo;
}

/// <summary>SendInput type constants.</summary>
internal static class InputType
{
    public const uint MOUSE = 0;
    public const uint KEYBOARD = 1;
}

/// <summary>Mouse event flags.</summary>
internal static class MouseFlags
{
    public const uint MOVE = 0x0001;
    public const uint LEFTDOWN = 0x0002;
    public const uint LEFTUP = 0x0004;
    public const uint RIGHTDOWN = 0x0008;
    public const uint RIGHTUP = 0x0010;
    public const uint MIDDLEDOWN = 0x0020;
    public const uint MIDDLEUP = 0x0040;
    public const uint WHEEL = 0x0800;
    public const uint VIRTUALDESK = 0x4000;
    public const uint ABSOLUTE = 0x8000;
}

/// <summary>Keyboard event flags.</summary>
internal static class KeyboardFlags
{
    public const uint KEYDOWN = 0x0000;
    public const uint KEYUP = 0x0002;
    public const uint EXTENDEDKEY = 0x0001;
    public const uint UNICODE = 0x0004;
    public const uint SCANCODE = 0x0008;
}

/// <summary>GetSystemMetrics index constants.</summary>
internal static class SystemMetrics
{
    public const int SM_CXSCREEN = 0;
    public const int SM_CYSCREEN = 1;
    public const int SM_CMONITORS = 80;
}
