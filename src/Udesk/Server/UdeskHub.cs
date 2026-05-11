using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using Udesk.Capture;
using Udesk.Input;
using Udesk.LockScreen;
using Udesk.Security;

namespace Udesk.Server;

/// <summary>
/// Manages viewer WebSocket connections, input handling, and viewer lifecycle.
/// Kestrel handles HTTP/WebSocket handshake; this handles application protocol.
/// </summary>
public sealed class UdeskHub : IDisposable
{
    private readonly IScreenCapture _capture;
    private readonly IInputController _input;
    private readonly IAuthProvider _authProvider;
    private readonly SleepPreventer _sleepPreventer;
    private readonly LockScreenDetector _lockDetector;
    private readonly ILockScreenHandler _lockHandler;
    private readonly ClipboardSync _clipboardSync;
    private readonly ILogger<UdeskHub> _logger;
    private readonly ConcurrentDictionary<string, ViewerConnection> _viewers = new();

    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public UdeskHub(
        IScreenCapture capture,
        IInputController input,
        IAuthProvider authProvider,
        SleepPreventer sleepPreventer,
        LockScreenDetector lockDetector,
        ILockScreenHandler lockHandler,
        ClipboardSync clipboardSync,
        ILogger<UdeskHub> logger)
    {
        _capture = capture;
        _input = input;
        _authProvider = authProvider;
        _sleepPreventer = sleepPreventer;
        _lockDetector = lockDetector;
        _lockHandler = lockHandler;
        _clipboardSync = clipboardSync;
        _logger = logger;
        _lockDetector.LockStateChanged += OnLockStateChanged;
    }

    /// <summary>
    /// Called by Kestrel middleware for each incoming WebSocket connection.
    /// </summary>
    public async Task HandleConnectionAsync(WebSocket webSocket, CancellationToken cancellationToken)
    {
        var connectionId = Guid.NewGuid().ToString("N")[..8];
        var viewer = new ViewerConnection(webSocket, connectionId);
        _viewers[connectionId] = viewer;

        _sleepPreventer.PreventSleep();
        _logger.LogInformation("Viewer {Id} connected ({Count} total)", connectionId, _viewers.Count);
        Console.WriteLine($"Viewer {connectionId} connected ({_viewers.Count} total)");

        var buffer = new byte[4096];

        try
        {
            // Auth
            var authed = await HandleAuthAsync(viewer, buffer, cancellationToken);
            if (!authed) return;

            // Welcome
            await SendWelcomeAsync(viewer, cancellationToken);

            // Message loop
            await ReceiveLoopAsync(viewer, buffer, cancellationToken);
        }
        catch (WebSocketException)
        {
            // Normal disconnect
        }
        catch (OperationCanceledException)
        {
            // Shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Viewer {Id} error", connectionId);
        }
        finally
        {
            _viewers.TryRemove(connectionId, out _);
            viewer.Dispose();

            if (_viewers.IsEmpty)
                _sleepPreventer.AllowSleep();

            _logger.LogInformation("Viewer {Id} disconnected ({Count} total)", connectionId, _viewers.Count);
            Console.WriteLine($"Viewer {connectionId} disconnected ({_viewers.Count} total)");
        }
    }

    /// <summary>
    /// Broadcasts a JPEG frame to all authenticated viewers.
    /// </summary>
    public void BroadcastFrame(byte[] frameData)
    {
        if (_viewers.IsEmpty) return;

        foreach (var kvp in _viewers)
        {
            if (kvp.Value.Authenticated)
            {
                _ = kvp.Value.SendFrameAsync(frameData, CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// Broadcasts a text JSON message to all authenticated viewers.
    /// </summary>
    public void BroadcastText(string json)
    {
        if (_viewers.IsEmpty) return;

        foreach (var kvp in _viewers)
        {
            if (kvp.Value.Authenticated)
            {
                _ = kvp.Value.SendTextAsync(json, CancellationToken.None);
            }
        }
    }

    public int ViewerCount => _viewers.Count;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lockDetector.LockStateChanged -= OnLockStateChanged;

        foreach (var viewer in _viewers.Values)
            viewer.Dispose();
        _viewers.Clear();
    }

    // === Private methods ===

    private async Task<bool> HandleAuthAsync(ViewerConnection viewer, byte[] buffer, CancellationToken ct)
    {
        // If no PIN required, auto-authenticate
        if (_authProvider.RequiresPin is false)
        {
            viewer.Authenticated = true;
            return true;
        }

        // Wait for auth message (with timeout)
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            var result = await viewer.ReceiveAsync(buffer, timeoutCts.Token);
            if (result.MessageType == WebSocketMessageType.Close)
                return false;

            var json = System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count);
            var authMsg = JsonSerializer.Deserialize<Protocol.AuthRequest>(json, JsonOptions);

            if (authMsg is null || !await _authProvider.ValidatePinAsync(authMsg.Pin))
            {
                var failed = JsonSerializer.Serialize(new Protocol.AuthFailedMessage
                {
                    Reason = authMsg is null ? "Invalid message" : "Invalid PIN"
                }, JsonOptions);
                await viewer.SendTextAsync(failed, ct);
                return false;
            }

            viewer.Authenticated = true;
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task SendWelcomeAsync(ViewerConnection viewer, CancellationToken ct)
    {
        var welcome = new Protocol.WelcomeMessage
        {
            ScreenWidth = _capture.ScreenWidth,
            ScreenHeight = _capture.ScreenHeight,
            CaptureWidth = _capture.CaptureWidth,
            CaptureHeight = _capture.CaptureHeight,
            ActiveMonitorIndex = _capture.ActiveMonitorIndex,
            Monitors = _capture.Monitors.ToList()
        };
        var json = JsonSerializer.Serialize(welcome, JsonOptions);
        await viewer.SendTextAsync(json, ct);
    }

    private async Task ReceiveLoopAsync(ViewerConnection viewer, byte[] buffer, CancellationToken ct)
    {
        while (!viewer.CancellationToken.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            var result = await viewer.ReceiveAsync(buffer, ct);

            if (result.MessageType == WebSocketMessageType.Close)
                break;

            var json = System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count);
            await HandleMessageAsync(viewer, json, ct);
        }
    }

    private async Task HandleMessageAsync(ViewerConnection viewer, string json, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(json);
        var type = doc.RootElement.GetProperty("type").GetString();

        switch (type)
        {
            case "mouse":
                HandleMouse(doc);
                break;
            case "keyboard":
                HandleKeyboard(doc);
                break;
            case "text":
                HandleText(doc);
                break;
            case "unlock":
                await HandleUnlockAsync(viewer, ct);
                break;
            case "store_credential":
                await HandleStoreCredentialAsync(doc, viewer, ct);
                break;
            case "switch_monitor":
                await HandleSwitchMonitorAsync(doc);
                break;
            case "clipboard":
                HandleClipboard(doc);
                break;
        }
    }

    private void HandleMouse(JsonDocument doc)
    {
        var root = doc.RootElement;
        var x = root.GetProperty("x").GetInt32();
        var y = root.GetProperty("y").GetInt32();
        var button = root.GetProperty("button").GetString() ?? "left";
        var action = root.GetProperty("action").GetString() ?? "move";
        var delta = root.TryGetProperty("delta", out var d) ? d.GetInt32() : 0;

        switch (action)
        {
            case "move":
                _input.MouseMove(x, y);
                break;
            case "down" or "up":
                _input.MouseClick(x, y, button, action);
                break;
            case "scroll":
                _input.MouseScroll(delta);
                break;
        }
    }

    private void HandleKeyboard(JsonDocument doc)
    {
        var root = doc.RootElement;
        var keyCode = root.GetProperty("keyCode").GetInt32();
        var action = root.GetProperty("action").GetString() ?? "down";
        switch (action)
        {
            case "down":
                _input.KeyDown(keyCode);
                break;
            case "up":
                _input.KeyUp(keyCode);
                break;
        }
    }

    private void HandleText(JsonDocument doc)
    {
        var root = doc.RootElement;
        var value = root.GetProperty("value").GetString() ?? "";
        _input.TypeText(value);
    }

    private async Task HandleUnlockAsync(ViewerConnection viewer, CancellationToken ct)
    {
        var result = await _lockHandler.UnlockAsync(ct);
        var msg = JsonSerializer.Serialize(new Protocol.UnlockResultMessage
        {
            Success = result,
            HasCredential = _lockHandler.HasCredential
        }, JsonOptions);
        await viewer.SendTextAsync(msg, ct);
    }

    private async Task HandleStoreCredentialAsync(JsonDocument doc, ViewerConnection viewer, CancellationToken ct)
    {
        var credential = doc.RootElement.GetProperty("credential").GetString() ?? "";
        await _lockHandler.StoreCredentialAsync(credential);
        var msg = JsonSerializer.Serialize(new Protocol.UnlockResultMessage
        {
            Success = true,
            HasCredential = true
        }, JsonOptions);
        await viewer.SendTextAsync(msg, ct);
    }

    private async Task HandleSwitchMonitorAsync(JsonDocument doc)
    {
        var monitorIndex = doc.RootElement.GetProperty("monitorIndex").GetInt32();
        _capture.SwitchMonitor(monitorIndex);

        var msg = JsonSerializer.Serialize(new Protocol.MonitorChangedMessage
        {
            ActiveMonitorIndex = _capture.ActiveMonitorIndex,
            ScreenWidth = _capture.ScreenWidth,
            ScreenHeight = _capture.ScreenHeight,
            CaptureWidth = _capture.CaptureWidth,
            CaptureHeight = _capture.CaptureHeight
        }, JsonOptions);
        BroadcastText(msg);
    }

    private void HandleClipboard(JsonDocument doc)
    {
        var text = doc.RootElement.GetProperty("text").GetString() ?? "";
        _clipboardSync.SetFromViewer(text);
    }

    private void OnLockStateChanged(bool locked)
    {
        var msg = JsonSerializer.Serialize(new Protocol.LockStateMessage { Locked = locked }, JsonOptions);
        BroadcastText(msg);
    }
}
