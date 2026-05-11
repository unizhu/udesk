using Udesk.Capture;
using Udesk.Security;

namespace Udesk.Server;

/// <summary>
/// Background service that starts screen capture, reads frames from the channel,
/// polls clipboard changes, and broadcasts everything via UdeskHub.
/// Kestrel manages HTTP/WebSocket; this manages the capture pipeline.
/// </summary>
public sealed class CaptureHostedService : BackgroundService
{
    private readonly UdeskHub _hub;
    private readonly IScreenCapture _capture;
    private readonly ClipboardSync _clipboardSync;
    private readonly ILogger<CaptureHostedService> _logger;
    private int _frameCount;

    public CaptureHostedService(
        UdeskHub hub,
        IScreenCapture capture,
        ClipboardSync clipboardSync,
        ILogger<CaptureHostedService> logger)
    {
        _hub = hub;
        _capture = capture;
        _clipboardSync = clipboardSync;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Capture service starting");

        try
        {
            // Start the capture pipeline
            await _capture.StartAsync(stoppingToken);

            // Read frames from the channel and broadcast
            await foreach (var frame in _capture.FrameReader.ReadAllAsync(stoppingToken))
            {
                // Poll clipboard every ~10 frames
                if (++_frameCount % 10 == 0)
                {
                    var clipboardText = _clipboardSync.PollClipboardChange();
                    if (clipboardText is not null)
                    {
                        var json = System.Text.Json.JsonSerializer.Serialize(
                            new Protocol.ClipboardMessage { Text = clipboardText });
                        _hub.BroadcastText(json);
                    }
                }

                if (_hub.ViewerCount is 0) continue;
                _hub.BroadcastFrame(frame.Data);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Capture service error");
        }
        finally
        {
            _logger.LogInformation("Capture service stopped");
        }
    }
}
