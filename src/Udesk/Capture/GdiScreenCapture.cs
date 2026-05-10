using System.Diagnostics;
using Udesk.Interop;

namespace Udesk.Capture;

/// <summary>
/// GDI-based screen capture using CopyFromScreen.
/// Captures at a reduced resolution and encodes to JPEG for minimal bandwidth.
/// </summary>
public sealed class GdiScreenCapture : IScreenCapture
{
    private readonly int _fps;
    private readonly int _jpegQuality;
    private readonly double _scaleFactor;
    private readonly Channel<Frame> _frameChannel;
    private readonly ILogger<GdiScreenCapture> _logger;
    private readonly ImageCodecInfo _jpegCodec;
    private readonly EncoderParameters _encoderParams;
    private int _captureWidth;
    private int _captureHeight;

    public ChannelReader<Frame> FrameReader => _frameChannel.Reader;
    public int ScreenWidth { get; private set; }
    public int ScreenHeight { get; private set; }

    /// <summary>
    /// Creates a new GDI screen capture instance.
    /// </summary>
    /// <param name="fps">Target frames per second (default 5).</param>
    /// <param name="jpegQuality">JPEG quality 1-100 (default 40).</param>
    /// <param name="scaleFactor">Scale factor for output resolution (default 0.5 = 50%).</param>
    /// <param name="logger">Logger instance.</param>
    public GdiScreenCapture(int fps, int jpegQuality, double scaleFactor, ILogger<GdiScreenCapture> logger)
    {
        _fps = Math.Clamp(fps, 1, 30);
        _jpegQuality = Math.Clamp(jpegQuality, 1, 100);
        _scaleFactor = Math.Clamp(scaleFactor, 0.1, 1.0);
        _frameChannel = Channel.CreateBounded<Frame>(new BoundedChannelOptions(2)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = true,
        });
        _logger = logger;

        _jpegCodec = ImageCodecInfo.GetImageDecoders()
            .First(c => c.FormatID == ImageFormat.Jpeg.Guid);

        _encoderParams = new EncoderParameters(1)
        {
            Param = [new EncoderParameter(Encoder.Quality, _jpegQuality)]
        };

        // Initialize screen dimensions eagerly so they're available before StartAsync
        UpdateScreenDimensions();
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        UpdateScreenDimensions();
        _logger.LogInformation(
            "Screen capture started: native {NativeW}x{NativeH}, capture {CaptureW}x{CaptureH}, {Fps} FPS, quality {Quality}",
            ScreenWidth, ScreenHeight, _captureWidth, _captureHeight, _fps, _jpegQuality);

        var intervalMs = 1000 / _fps;
        var bitmap = new Bitmap(_captureWidth, _captureHeight);
        var graphics = Graphics.FromImage(bitmap);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                if (!CaptureFrame(graphics, bitmap))
                {
                    // Resolution changed — recreate bitmap and graphics
                    graphics.Dispose();
                    bitmap.Dispose();
                    bitmap = new Bitmap(_captureWidth, _captureHeight);
                    graphics = Graphics.FromImage(bitmap);
                    continue;
                }
                sw.Stop();

                var frame = FrameFromBitmap(bitmap);
                await _frameChannel.Writer.WriteAsync(frame, cancellationToken).ConfigureAwait(false);

                var delayMs = Math.Max(0, intervalMs - (int)sw.ElapsedMilliseconds);
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error capturing screen frame");
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            }
        }

        _frameChannel.Writer.TryComplete();
        graphics.Dispose();
        bitmap.Dispose();
        _logger.LogInformation("Screen capture stopped");
    }

    private bool CaptureFrame(Graphics graphics, Bitmap bitmap)
    {
        // Check if screen dimensions changed (monitor resolution change)
        var currentWidth = NativeMethods.GetSystemMetrics(SystemMetrics.SM_CXSCREEN);
        var currentHeight = NativeMethods.GetSystemMetrics(SystemMetrics.SM_CYSCREEN);

        if (currentWidth != ScreenWidth || currentHeight != ScreenHeight)
        {
            UpdateScreenDimensions();
            _logger.LogInformation("Screen resolution changed to {W}x{H}", ScreenWidth, ScreenHeight);
            return false; // Signal that bitmap needs to be recreated
        }

        graphics.CopyFromScreen(0, 0, 0, 0, new Size(_captureWidth, _captureHeight), CopyPixelOperation.SourceCopy);
        return true;
    }

    private Frame FrameFromBitmap(Image bitmap)
    {
        using var ms = new MemoryStream(64 * 1024); // 64KB initial capacity
        bitmap.Save(ms, _jpegCodec, _encoderParams);
        return new Frame(
            Data: ms.ToArray(),
            Width: _captureWidth,
            Height: _captureHeight,
            TimestampMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );
    }

    private void UpdateScreenDimensions()
    {
        ScreenWidth = NativeMethods.GetSystemMetrics(SystemMetrics.SM_CXSCREEN);
        ScreenHeight = NativeMethods.GetSystemMetrics(SystemMetrics.SM_CYSCREEN);
        _captureWidth = (int)(ScreenWidth * _scaleFactor);
        _captureHeight = (int)(ScreenHeight * _scaleFactor);

        // Ensure minimum dimensions
        _captureWidth = Math.Max(320, _captureWidth);
        _captureHeight = Math.Max(240, _captureHeight);
    }

    public void Dispose()
    {
        _frameChannel.Writer.TryComplete();
        _encoderParams.Dispose();
    }
}
