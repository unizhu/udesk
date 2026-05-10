using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using Udesk.Capture;
using Udesk.Input;
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
    private CancellationTokenSource? _serverCts;
    private bool _disposed;

    public UdeskServer(
        IScreenCapture capture,
        IInputController input,
        IAuthProvider authProvider,
        UdeskOptions options,
        ILogger<UdeskServer> logger)
    {
        _capture = capture;
        _input = input;
        _authProvider = authProvider;
        _options = options;
        _logger = logger;
        _httpListener = new HttpListener();
    }

    /// <summary>
    /// Starts the HTTP and WebSocket server.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var prefix = $"http://+:{_options.Port}/";
        _httpListener.Prefixes.Clear();
        _httpListener.Prefixes.Add(prefix);
        _httpListener.Start();

        _serverCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _logger.LogInformation("Udesk server listening on port {Port}", _options.Port);

        // Start frame broadcast loop
        var broadcastTask = BroadcastFramesAsync(_serverCts.Token);

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
            await broadcastTask.ConfigureAwait(false);
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

    private async Task HandleWebSocketAsync(HttpListenerContext context)
    {
        var wsContext = await context.AcceptWebSocketAsync(null).ConfigureAwait(false);
        var connectionId = Guid.NewGuid().ToString("N")[..8];
        var connection = new ViewerConnection(wsContext.WebSocket, connectionId);

        _viewers[connectionId] = connection;
        _logger.LogInformation("Viewer {Id} connected. Total viewers: {Count}", connectionId, _viewers.Count);

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
        }
    }

    private async Task HandleViewerMessagesAsync(ViewerConnection connection)
    {
        var buffer = new byte[4096];
        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

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
                    };
                    await connection.SendTextAsync(
                        JsonSerializer.Serialize(welcome), connection.CancellationToken).ConfigureAwait(false);
                    _logger.LogInformation("Viewer {Id} authenticated", connection.ConnectionId);
                }
                else
                {
                    var failed = new Protocol.AuthFailedMessage { Reason = "Invalid PIN" };
                    await connection.SendTextAsync(
                        JsonSerializer.Serialize(failed), connection.CancellationToken).ConfigureAwait(false);
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
        }
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
