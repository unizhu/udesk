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

        // Start capture in background
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
                Console.WriteLine($"[ERROR] Screen capture failed: {ex.Message}");
                _logger.LogError(ex, "Screen capture failed");
            }
        }, token);

        // Start server in background — errors are logged with Console.WriteLine for visibility
        _ = Task.Run(async () =>
        {
            try
            {
                Console.WriteLine("Starting HTTP server...");
                await _server.StartAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Server failed: {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException is not null)
                    Console.WriteLine($"[ERROR] Inner: {ex.InnerException.Message}");
                _logger.LogError(ex, "Server failed");
            }
        }, token);

        Console.WriteLine("Udesk hosted service started");
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
