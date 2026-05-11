namespace Udesk.Capture;

/// <summary>
/// Information about a display monitor.
/// </summary>
/// <param name="Index">Zero-based monitor index.</param>
/// <param name="Name">Monitor name/identifier.</param>
/// <param name="Width">Native width in pixels.</param>
/// <param name="Height">Native height in pixels.</param>
/// <param name="X">X offset relative to primary monitor.</param>
/// <param name="Y">Y offset relative to primary monitor.</param>
/// <param name="IsPrimary">Whether this is the primary monitor.</param>
public sealed record MonitorInfo(
    int Index,
    string Name,
    int Width,
    int Height,
    int X,
    int Y,
    bool IsPrimary);
