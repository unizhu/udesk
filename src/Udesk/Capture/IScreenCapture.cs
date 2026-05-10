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
}
