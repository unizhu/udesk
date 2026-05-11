using Microsoft.Extensions.Hosting;
using Udesk.Capture;

namespace Udesk.Server;

/// <summary>
/// Hosted service that manages the Udesk server lifecycle.
/// Starts capture + server, handles graceful shutdown.
/// </summary>
internal sealed class UdeskHostedService : IHostedService, IDisposable
{
    private readonly IScreenCapture _capture;
    private readonly UdeskServer _server;
    private readonly ILogger<UdeskHostedService> _logger;
    private CancellationTokenSource? _cts;

    public UdeskHostedService(
        IScreenCapture capture,
        UdeskServer server,
        ILogger<UdeskHostedService> logger)
    {
        _capture = capture;
        _server = server;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _cts.Token;

        // Start capture and server concurrently
        _ = Task.Run(async () =>
        {
            try
            {
                await _capture.StartAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Screen capture failed");
            }
        }, token);

        _ = Task.Run(async () =>
        {
            try
            {
                await _server.StartAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Server failed");
            }
        }, token);

        _logger.LogInformation("Udesk hosted service started");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Udesk hosted service stopping...");
        _cts?.Cancel();
        _server.Stop();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _cts?.Dispose();
    }
}
