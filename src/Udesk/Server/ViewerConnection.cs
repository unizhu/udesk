using System.Net.WebSockets;

namespace Udesk.Server;

/// <summary>
/// Represents a connected viewer client.
/// </summary>
internal sealed class ViewerConnection : IDisposable
{
    private readonly WebSocket _webSocket;
    private readonly CancellationTokenSource _cts;
    private bool _disposed;

    public string ConnectionId { get; }
    public bool Authenticated { get; set; }
    public DateTime ConnectedAt { get; }

    public ViewerConnection(WebSocket webSocket, string connectionId)
    {
        _webSocket = webSocket;
        ConnectionId = connectionId;
        _cts = new CancellationTokenSource();
        ConnectedAt = DateTime.UtcNow;
        Authenticated = false;
    }

    public CancellationToken CancellationToken => _cts.Token;

    /// <summary>
    /// Sends binary data (JPEG frame) to the viewer.
    /// </summary>
    public async Task SendFrameAsync(byte[] data, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_webSocket.State != WebSocketState.Open)
            return;

        try
        {
            await _webSocket.SendAsync(data, WebSocketMessageType.Binary, true, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (WebSocketException)
        {
            // Connection likely closed; disconnect
            Disconnect();
        }
    }

    /// <summary>
    /// Sends a text message (JSON status) to the viewer.
    /// </summary>
    public async Task SendTextAsync(string json, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_webSocket.State != WebSocketState.Open)
            return;

        try
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            await _webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (WebSocketException)
        {
            Disconnect();
        }
    }

    /// <summary>
    /// Receives a message from the viewer.
    /// </summary>
    public async Task<WebSocketReceiveResult> ReceiveAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken)
            .ConfigureAwait(false);
    }

    public void Disconnect()
    {
        if (!_disposed)
        {
            _cts.Cancel();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
    }
}
