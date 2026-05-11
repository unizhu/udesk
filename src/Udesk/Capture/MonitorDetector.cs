using System.Runtime.InteropServices;

namespace Udesk.Capture;

/// <summary>
/// Detects connected display monitors using Win32 EnumDisplayMonitors.
/// </summary>
public sealed class MonitorDetector
{
    /// <summary>
    /// Enumerates all connected monitors.
    /// </summary>
    public static List<MonitorInfo> DetectMonitors()
    {
        var monitors = new List<MonitorInfo>();
        var index = 0;

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (hMonitor, hdcMonitor, lprcMonitor, dwData) =>
            {
                var rect = Marshal.PtrToStructure<RECT>(lprcMonitor);
                var monitorInfo = new MONITORINFOEX();
                monitorInfo.Size = (uint)Marshal.SizeOf<MONITORINFOEX>();

                if (GetMonitorInfo(hMonitor, ref monitorInfo))
                {
                    var name = monitorInfo.DeviceName;
                    var isPrimary = (monitorInfo.Flags & 1) != 0; // MONITORINFOF_PRIMARY = 1
                    monitors.Add(new MonitorInfo(
                        Index: index,
                        Name: name,
                        Width: rect.Right - rect.Left,
                        Height: rect.Bottom - rect.Top,
                        X: rect.Left,
                        Y: rect.Top,
                        IsPrimary: isPrimary));
                }

                index++;
                return true;
            }, IntPtr.Zero);

        return monitors;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFOEX
    {
        public uint Size;
        public RECT Monitor;
        public RECT WorkArea;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    // Use DllImport for EnumDisplayMonitors because LibraryImport doesn't support delegate marshalling
    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc,
        IntPtr lprcClip,
        MonitorEnumProc lpfnEnum,
        IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr dwData);
}
