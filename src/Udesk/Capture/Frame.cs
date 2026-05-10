namespace Udesk.Capture;

/// <summary>
/// Represents a captured screen frame as a JPEG-encoded image.
/// </summary>
/// <param name="Data">JPEG-encoded image bytes.</param>
/// <param name="Width">Width of the captured image in pixels.</param>
/// <param name="Height">Height of the captured image in pixels.</param>
/// <param name="TimestampMs">Unix timestamp in milliseconds when the frame was captured.</param>
public sealed record Frame(byte[] Data, int Width, int Height, long TimestampMs);
