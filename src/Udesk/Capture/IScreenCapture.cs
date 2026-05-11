namespace Udesk.Capture;

/// <summary>
/// Interface for screen capture implementations.
/// </summary>
public interface IScreenCapture : IDisposable
{
    /// <summary>
    /// Starts capturing screen frames at the configured framerate.
    /// Frames are produced to an internal channel for consumption.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to stop capturing.</param>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets the channel reader for consuming captured frames.
    /// </summary>
    ChannelReader<Frame> FrameReader { get; }

    /// <summary>
    /// Gets the current screen width in pixels (native, before resize).
    /// </summary>
    int ScreenWidth { get; }

    /// <summary>
    /// Gets the current screen height in pixels (native, before resize).
    /// </summary>
    int ScreenHeight { get; }

    /// <summary>
    /// Gets the capture width in pixels (after scaling).
    /// </summary>
    int CaptureWidth { get; }

    /// <summary>
    /// Gets the capture height in pixels (after scaling).
    /// </summary>
    int CaptureHeight { get; }

    /// <summary>
    /// Gets the list of available monitors.
    /// </summary>
    IReadOnlyList<MonitorInfo> Monitors { get; }

    /// <summary>
    /// Gets the index of the currently captured monitor.
    /// </summary>
    int ActiveMonitorIndex { get; }

    /// <summary>
    /// Switches capture to a different monitor.
    /// </summary>
    /// <param name="monitorIndex">Zero-based monitor index.</param>
    void SwitchMonitor(int monitorIndex);
}
