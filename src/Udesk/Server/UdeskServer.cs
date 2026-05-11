using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using Udesk.Capture;
using Udesk.Input;
using Udesk.LockScreen;
using Udesk.Security;

namespace Udesk.Server;

/// <summary>
/// Main server that serves the web viewer and manages WebSocket connections.
/// Uses HttpListener for HTTP and WebSocket communication.
/// </summary>
public sealed class UdeskServer : IDisposable
{
    private readonly IScreenCapture _capture;
    private readonly IInputController _input;
    private readonly IAuthProvider _authProvider;
    private readonly UdeskOptions _options;
    private readonly ILogger<UdeskServer> _logger;
    private readonly ConcurrentDictionary<string, ViewerConnection> _viewers = new();
    private readonly HttpListener _httpListener;
    private readonly SleepPreventer _sleepPreventer;
    private readonly LockScreenDetector _lockDetector;
    private readonly ILockScreenHandler _lockHandler;
    private readonly TlsCertificateManager? _tlsManager;
    private readonly ClipboardSync _clipboardSync;
    private CancellationTokenSource? _serverCts;
    private bool _disposed;
    private bool _screenLocked;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public UdeskServer(
        IScreenCapture capture,
        IInputController input,
        IAuthProvider authProvider,
        UdeskOptions options,
        SleepPreventer sleepPreventer,
        LockScreenDetector lockDetector,
        ILockScreenHandler lockHandler,
        TlsCertificateManager? tlsManager,
        ClipboardSync clipboardSync,
        ILogger<UdeskServer> logger)
    {
        _capture = capture;
        _input = input;
        _authProvider = authProvider;
        _options = options;
        _sleepPreventer = sleepPreventer;
        _logger = logger;
        _httpListener = new HttpListener();

        // Wire lock state change to broadcast
        _lockDetector = lockDetector;
        _lockHandler = lockHandler;
        _tlsManager = tlsManager;
        _clipboardSync = clipboardSync;
        _lockDetector.LockStateChanged += OnLockStateChanged;
    }

    /// <summary>
    /// Starts the HTTP and WebSocket server.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var scheme = _options.EnableTls ? "https" : "http";
        var wildcardPrefix = $"{scheme}://+:{_options.Port}/";
        var localhostPrefix = $"{scheme}://localhost:{_options.Port}/";

        // Try wildcard (LAN access) first, fall back to localhost (loopback only)
        _httpListener.Prefixes.Clear();
        _httpListener.Prefixes.Add(wildcardPrefix);

        try
        {
            _httpListener.Start();
        }
        catch (HttpListenerException)
        {
            // Wildcard requires admin or netsh urlacl — fall back to localhost
            _httpListener.Prefixes.Clear();
            _httpListener.Prefixes.Add(localhostPrefix);
            try
            {
                _httpListener.Start();
            }
            catch (HttpListenerException ex2)
            {
                _logger.LogCritical(ex2, "Failed to start server on port {Port}. Port may be in use.", _options.Port);
                throw;
            }
            _logger.LogWarning("Running without admin: listening on localhost only (loopback). For LAN access, run as admin or: netsh http add urlacl url={Prefix} user=Everyone", wildcardPrefix);
        }

        _serverCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        if (_options.EnableTls)
        {
            _logger.LogInformation("Udesk server listening with TLS on port {Port}", _options.Port);
        }
        else
        {
            _logger.LogInformation("Udesk server listening on port {Port}", _options.Port);
        }

        // Start frame broadcast loop
        var broadcastTask = BroadcastFramesAsync(_serverCts.Token);

        // Start lock screen polling (every 2 seconds)
        var lockPollTask = PollLockStateAsync(_serverCts.Token);

        // Start clipboard polling (every 1 second)
        var clipboardPollTask = PollClipboardAsync(_serverCts.Token);

        try
        {
            while (!_serverCts.Token.IsCancellationRequested)
            {
                var httpContext = await _httpListener.GetContextAsync().ConfigureAwait(false);

                if (httpContext.Request.IsWebSocketRequest)
                {
                    await HandleWebSocketAsync(httpContext).ConfigureAwait(false);
                }
                else
                {
                    await HandleHttpRequestAsync(httpContext).ConfigureAwait(false);
                }
            }
        }
        catch (HttpListenerException) when (_serverCts.Token.IsCancellationRequested)
        {
            // Normal shutdown
        }
        finally
        {
            await Task.WhenAll(broadcastTask, lockPollTask, clipboardPollTask).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Stops the server gracefully.
    /// </summary>
    public void Stop()
    {
        _serverCts?.Cancel();
        _httpListener.Stop();
    }

    private async Task HandleHttpRequestAsync(HttpListenerContext context)
    {
        var path = context.Request.Url?.AbsolutePath ?? "/";

        if (path is "/" or "/index.html")
        {
            await ServeViewerPageAsync(context).ConfigureAwait(false);
        }
        else if (path is "/cert.pem")
        {
            await ServeCertificateAsync(context).ConfigureAwait(false);
        }
        else
        {
            context.Response.StatusCode = 404;
            context.Response.Close();
        }
    }

    private async Task ServeViewerPageAsync(HttpListenerContext context)
    {
        var html = EmbeddedResources.GetViewerHtml();
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = html.Length;
        await context.Response.OutputStream.WriteAsync(html, _serverCts?.Token ?? CancellationToken.None)
            .ConfigureAwait(false);
        context.Response.Close();
    }

    private async Task ServeCertificateAsync(HttpListenerContext context)
    {
        var pem = _tlsManager?.GetCertificatePem();
        if (pem is null)
        {
            context.Response.StatusCode = 404;
            context.Response.Close();
            return;
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(pem);
        context.Response.ContentType = "application/x-pem-file";
        context.Response.ContentLength64 = bytes.Length;
        context.Response.Headers.Add("Content-Disposition", "attachment; filename=udesk-cert.pem");
        await context.Response.OutputStream.WriteAsync(bytes, _serverCts?.Token ?? CancellationToken.None)
            .ConfigureAwait(false);
        context.Response.Close();
    }

    private async Task HandleWebSocketAsync(HttpListenerContext context)
    {
        var wsContext = await context.AcceptWebSocketAsync(null).ConfigureAwait(false);
        var connectionId = Guid.NewGuid().ToString("N")[..8];
        var connection = new ViewerConnection(wsContext.WebSocket, connectionId);

        _viewers[connectionId] = connection;
        _logger.LogInformation("Viewer {Id} connected. Total viewers: {Count}", connectionId, _viewers.Count);

        // Prevent sleep while viewers are connected
        if (_viewers.Count == 1)
        {
            _sleepPreventer.PreventSleep();
        }

        try
        {
            await HandleViewerMessagesAsync(connection).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Viewer {Id} disconnected with error", connectionId);
        }
        finally
        {
            _viewers.TryRemove(connectionId, out _);
            connection.Dispose();
            _logger.LogInformation("Viewer {Id} disconnected. Total viewers: {Count}", connectionId, _viewers.Count);

            // Allow sleep when no viewers remain
            if (_viewers.IsEmpty)
            {
                _sleepPreventer.AllowSleep();
            }
        }
    }

    private async Task HandleViewerMessagesAsync(ViewerConnection connection)
    {
        var buffer = new byte[4096];
        var jsonOptions = JsonOptions;

        while (!connection.CancellationToken.IsCancellationRequested)
        {
            var result = await connection.ReceiveAsync(buffer, connection.CancellationToken).ConfigureAwait(false);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                connection.Disconnect();
                break;
            }

            if (result.MessageType == WebSocketMessageType.Text)
            {
                var json = System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count);
                await ProcessViewerMessageAsync(connection, json, jsonOptions).ConfigureAwait(false);
            }
        }
    }

    private async Task ProcessViewerMessageAsync(
        ViewerConnection connection,
        string json,
        JsonSerializerOptions jsonOptions)
    {
        using var doc = JsonDocument.Parse(json);
        var type = doc.RootElement.GetProperty("type").GetString() ?? "";

        switch (type)
        {
            case "auth":
                var authMsg = JsonSerializer.Deserialize<Protocol.AuthRequest>(json, jsonOptions);
                if (authMsg is null) break;

                if (await _authProvider.ValidatePinAsync(authMsg.Pin).ConfigureAwait(false))
                {
                    connection.Authenticated = true;
                    var welcome = new Protocol.WelcomeMessage
                    {
                        ScreenWidth = _capture.ScreenWidth,
                        ScreenHeight = _capture.ScreenHeight,
                        CaptureWidth = (int)(_capture.ScreenWidth * _options.ScaleFactor),
                        CaptureHeight = (int)(_capture.ScreenHeight * _options.ScaleFactor),
                        ActiveMonitorIndex = _capture.ActiveMonitorIndex,
                        Monitors = _capture.Monitors.ToList()
                    };
                    await connection.SendTextAsync(
                        JsonSerializer.Serialize(welcome, JsonOptions), connection.CancellationToken).ConfigureAwait(false);
                    _logger.LogInformation("Viewer {Id} authenticated", connection.ConnectionId);
                }
                else
                {
                    var failed = new Protocol.AuthFailedMessage { Reason = "Invalid PIN" };
                    await connection.SendTextAsync(
                        JsonSerializer.Serialize(failed, JsonOptions), connection.CancellationToken).ConfigureAwait(false);
                    connection.Disconnect();
                }
                break;

            case "mouse":
                if (!connection.Authenticated) break;
                var mouseMsg = JsonSerializer.Deserialize<Protocol.MouseEvent>(json, jsonOptions);
                if (mouseMsg is null) break;
                ProcessMouseEvent(mouseMsg);
                break;

            case "keyboard":
                if (!connection.Authenticated) break;
                var keyMsg = JsonSerializer.Deserialize<Protocol.KeyboardEvent>(json, jsonOptions);
                if (keyMsg is null) break;
                ProcessKeyboardEvent(keyMsg);
                break;

            case "text":
                if (!connection.Authenticated) break;
                var textMsg = JsonSerializer.Deserialize<Protocol.TextEvent>(json, jsonOptions);
                if (textMsg is null) break;
                _input.TypeText(textMsg.Value);
                break;

            case "unlock":
                if (!connection.Authenticated) break;
                _ = HandleUnlockAsync(connection, connection.CancellationToken);
                break;

            case "store_credential":
                if (!connection.Authenticated) break;
                var credMsg = JsonSerializer.Deserialize<Protocol.StoreCredentialRequest>(json, jsonOptions);
                if (credMsg is null) break;
                await _lockHandler.StoreCredentialAsync(credMsg.Credential).ConfigureAwait(false);
                break;

            case "switch_monitor":
                if (!connection.Authenticated) break;
                var monMsg = JsonSerializer.Deserialize<Protocol.SwitchMonitorRequest>(json, jsonOptions);
                if (monMsg is null) break;
                _capture.SwitchMonitor(monMsg.MonitorIndex);
                // Notify all viewers of the monitor change
                var monChanged = new Protocol.MonitorChangedMessage
                {
                    ActiveMonitorIndex = _capture.ActiveMonitorIndex,
                    ScreenWidth = _capture.ScreenWidth,
                    ScreenHeight = _capture.ScreenHeight,
                    CaptureWidth = (int)(_capture.ScreenWidth * _options.ScaleFactor),
                    CaptureHeight = (int)(_capture.ScreenHeight * _options.ScaleFactor)
                };
                await BroadcastToAllAsync(JsonSerializer.Serialize(monChanged, JsonOptions), CancellationToken.None).ConfigureAwait(false);
                break;

            case "clipboard":
                if (!connection.Authenticated) break;
                var clipMsg = JsonSerializer.Deserialize<Protocol.ClipboardUpdateRequest>(json, jsonOptions);
                if (clipMsg is null) break;
                _clipboardSync.SetFromViewer(clipMsg.Text);
                break;
        }
    }

    private async Task HandleUnlockAsync(ViewerConnection connection, CancellationToken cancellationToken)
    {
        if (!_lockHandler.HasCredential)
        {
            var noCred = new Protocol.UnlockResultMessage
            {
                Success = false,
                HasCredential = false,
                Error = "No credential stored. Please provide Windows login PIN first."
            };
            await connection.SendTextAsync(JsonSerializer.Serialize(noCred, JsonOptions), cancellationToken).ConfigureAwait(false);
            return;
        }

        var success = await _lockHandler.UnlockAsync(cancellationToken).ConfigureAwait(false);
        var result = new Protocol.UnlockResultMessage
        {
            Success = success,
            HasCredential = true,
            Error = success ? null : "Unlock sequence failed. Check Group Policy: Enable software SASEnabled."
        };
        await connection.SendTextAsync(JsonSerializer.Serialize(result, JsonOptions), cancellationToken).ConfigureAwait(false);
    }

    private void ProcessMouseEvent(Protocol.MouseEvent msg)
    {
        // Scale coordinates from capture size to screen size
        var captureWidth = (int)(_capture.ScreenWidth * _options.ScaleFactor);
        var captureHeight = (int)(_capture.ScreenHeight * _options.ScaleFactor);
        var scaleX = captureWidth > 0 ? (double)_capture.ScreenWidth / captureWidth : 1.0;
        var scaleY = captureHeight > 0 ? (double)_capture.ScreenHeight / captureHeight : 1.0;
        var screenX = (int)(msg.X * scaleX);
        var screenY = (int)(msg.Y * scaleY);

        switch (msg.Action)
        {
            case "move":
                _input.MouseMove(screenX, screenY);
                break;
            case "down":
            case "up":
                _input.MouseClick(screenX, screenY, msg.Button, msg.Action);
                break;
            case "scroll":
                _input.MouseScroll(msg.Delta);
                break;
        }
    }

    private void ProcessKeyboardEvent(Protocol.KeyboardEvent msg)
    {
        switch (msg.Action)
        {
            case "down":
                _input.KeyDown(msg.KeyCode);
                break;
            case "up":
                _input.KeyUp(msg.KeyCode);
                break;
        }
    }

    private async Task PollLockStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                _lockDetector.Poll();
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    private async Task PollClipboardAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                if (_viewers.IsEmpty) continue;

                var changedText = _clipboardSync.PollClipboardChange();
                if (changedText is null) continue;

                var msg = new Protocol.ClipboardMessage { Text = changedText };
                var json = JsonSerializer.Serialize(msg, JsonOptions);
                await BroadcastToAllAsync(json, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    private void OnLockStateChanged(bool locked)
    {
        _screenLocked = locked;
        var msg = new Protocol.LockStateMessage { Locked = locked };
        var json = JsonSerializer.Serialize(msg, JsonOptions);

        foreach (var viewer in _viewers.Values.Where(v => v.Authenticated))
        {
            _ = viewer.SendTextAsync(json, CancellationToken.None);
        }
    }

    private async Task BroadcastToAllAsync(string json, CancellationToken cancellationToken)
    {
        var viewers = _viewers.Values.Where(v => v.Authenticated).ToList();
        if (viewers.Count == 0) return;

        var tasks = viewers.Select(v => v.SendTextAsync(json, cancellationToken));
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task BroadcastFramesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var frame in _capture.FrameReader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                var viewers = _viewers.Values.Where(v => v.Authenticated).ToList();
                if (viewers.Count == 0) continue;

                var tasks = viewers.Select(v => v.SendFrameAsync(frame.Data, cancellationToken));
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();

        foreach (var viewer in _viewers.Values)
        {
            viewer.Dispose();
        }
        _viewers.Clear();
        _httpListener.Close();
    }
}
