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
    private int _activeMonitorIndex;
    private MonitorInfo? _activeMonitor;
    private List<MonitorInfo> _monitors = [];
    private volatile bool _monitorChanged;
    private byte[] _lastFrameHash = [];

    public ChannelReader<Frame> FrameReader => _frameChannel.Reader;
    public int CaptureWidth => _captureWidth;
    public int CaptureHeight => _captureHeight;
    public int ScreenWidth { get; private set; }
    public int ScreenHeight { get; private set; }
    public IReadOnlyList<MonitorInfo> Monitors => _monitors.AsReadOnly();
    public int ActiveMonitorIndex => _activeMonitorIndex;

    /// <summary>
    /// Creates a new GDI screen capture instance.
    /// </summary>
    /// <param name="fps">Target frames per second (default 5).</param>
    /// <param name="jpegQuality">JPEG quality 1-100 (default 40).</param>
    /// <param name="scaleFactor">Scale factor for output resolution (default 0.5 = 50%).</param>
    /// <param name="monitorIndex">Monitor index to capture (null = primary).</param>
    /// <param name="logger">Logger instance.</param>
    public GdiScreenCapture(int fps, int jpegQuality, double scaleFactor, ILogger<GdiScreenCapture> logger, int? monitorIndex = null)
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

        // Detect monitors and select requested or primary
        _monitors = MonitorDetector.DetectMonitors();
        _activeMonitorIndex = monitorIndex ?? _monitors.FindIndex(m => m.IsPrimary);
        if (_activeMonitorIndex < 0 || _activeMonitorIndex >= _monitors.Count) _activeMonitorIndex = 0;

        // Initialize screen dimensions eagerly so they're available before StartAsync
        UpdateScreenDimensions();
    }

    /// <inheritdoc />
    public void SwitchMonitor(int monitorIndex)
    {
        if (monitorIndex < 0 || monitorIndex >= _monitors.Count) return;
        if (monitorIndex == _activeMonitorIndex) return;

        _activeMonitorIndex = monitorIndex;
        _monitorChanged = true;
        _logger.LogInformation("Switching capture to monitor {Index}: {Name}", monitorIndex, _monitors[monitorIndex].Name);
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

                // Frame diffing: skip identical frames to save bandwidth
                var currentHash = ComputeHash(frame.Data);
                if (HashEquals(currentHash, _lastFrameHash))
                {
                    // Frame unchanged, skip sending
                    var skipMs = Math.Max(0, intervalMs - (int)sw.ElapsedMilliseconds);
                    if (skipMs > 0) await Task.Delay(skipMs, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                _lastFrameHash = currentHash;
                await _frameChannel.Writer.WriteAsync(frame, cancellationToken).ConfigureAwait(false);

                if (_frameCount <= 3 || _frameCount % 300 == 0)
                {
                    _logger.LogInformation("Frame #{Count} captured: {Width}x{Height}, {Size} bytes",
                        _frameCount, frame.Width, frame.Height, frame.Data.Length);
                }

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
            catch (System.ComponentModel.Win32Exception ex)
            {
                // Handle is invalid (error 6) — screen locked, blanked, or secure desktop active.
                // This is transient; retry without spamming error logs.
                _logger.LogDebug(ex, "Screen capture temporarily unavailable (handle invalid), retrying...");
                _lastFrameHash = []; // Force send next frame once screen returns
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
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
        // Check if monitor was switched
        if (_monitorChanged)
        {
            _monitorChanged = false;
            UpdateScreenDimensions();
            _logger.LogInformation("Monitor switched to {W}x{H} at ({X},{Y})", ScreenWidth, ScreenHeight, _activeMonitor?.X ?? 0, _activeMonitor?.Y ?? 0);
            return false; // Signal that bitmap needs to be recreated
        }

        // Check if screen dimensions changed (monitor resolution change)
        var currentWidth = _activeMonitor?.Width ?? NativeMethods.GetSystemMetrics(SystemMetrics.SM_CXSCREEN);
        var currentHeight = _activeMonitor?.Height ?? NativeMethods.GetSystemMetrics(SystemMetrics.SM_CYSCREEN);
        if (currentWidth != ScreenWidth || currentHeight != ScreenHeight)
        {
            UpdateScreenDimensions();
            _logger.LogInformation("Screen resolution changed to {W}x{H}", ScreenWidth, ScreenHeight);
            return false;
        }

        // Capture from the active monitor's offset
        var offsetX = _activeMonitor?.X ?? 0;
        var offsetY = _activeMonitor?.Y ?? 0;
        graphics.CopyFromScreen(offsetX, offsetY, 0, 0, new Size(_captureWidth, _captureHeight), CopyPixelOperation.SourceCopy);
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
        // Re-detect monitors in case of hot-plug
        _monitors = MonitorDetector.DetectMonitors();
        if (_activeMonitorIndex >= _monitors.Count)
        {
            _activeMonitorIndex = 0;
        }

        _activeMonitor = _activeMonitorIndex < _monitors.Count ? _monitors[_activeMonitorIndex] : null;
        ScreenWidth = _activeMonitor?.Width ?? NativeMethods.GetSystemMetrics(SystemMetrics.SM_CXSCREEN);
        ScreenHeight = _activeMonitor?.Height ?? NativeMethods.GetSystemMetrics(SystemMetrics.SM_CYSCREEN);
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

    /// <summary>
    /// Computes a fast 64-bit hash of a byte span using xxHash-like algorithm.
    /// Avoids full SHA256 for performance — we just need collision resistance for frame diffing.
    /// </summary>
    private static byte[] ComputeHash(byte[] data)
    {
        // Simple FNV-1a 64-bit hash — fast and good enough for frame diffing
        const ulong FnvOffset = 14695981039346656037;
        const ulong FnvPrime = 1099511628211;
        ulong hash = FnvOffset;

        // Sample every 64th byte for speed (JPEG frames can be 10-100KB)
        var step = Math.Max(1, data.Length / 1024);
        for (var i = 0; i < data.Length; i += step)
        {
            hash ^= data[i];
            hash *= FnvPrime;
        }

        // Include last byte for completeness
        if (data.Length > 0) hash ^= data[data.Length - 1];

        return BitConverter.GetBytes(hash);
    }

    private static bool HashEquals(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i]) return false;
        }
        return true;
    }
}
