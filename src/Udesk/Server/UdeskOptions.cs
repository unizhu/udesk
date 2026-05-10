namespace Udesk.Server;

/// <summary>
/// Configuration options for the Udesk server.
/// </summary>
public sealed class UdeskOptions
{
    /// <summary>Port to listen on (default 8080).</summary>
    public int Port { get; init; } = 8080;

    /// <summary>Target frames per second (default 5).</summary>
    public int Fps { get; init; } = 5;

    /// <summary>JPEG quality 1-100 (default 40).</summary>
    public int JpegQuality { get; init; } = 40;

    /// <summary>Scale factor for captured frames (default 0.5 = 50%).</summary>
    public double ScaleFactor { get; init; } = 0.5;

    /// <summary>Optional PIN code for viewer authentication. Null = no PIN required.</summary>
    public string? Pin { get; init; }
}
