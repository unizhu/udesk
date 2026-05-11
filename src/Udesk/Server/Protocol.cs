using Udesk.Capture;

namespace Udesk.Server;

/// <summary>
/// Protocol messages exchanged between the Windows host and browser viewers.
/// Binary WebSocket frames = JPEG screen data.
/// Text WebSocket frames = JSON control messages.
/// </summary>
internal static class Protocol
{
    // === Server → Viewer messages ===

    /// <summary>
    /// Sent after successful authentication.
    /// </summary>
    public sealed class WelcomeMessage
    {
        public string Type => "welcome";
        public required int ScreenWidth { get; init; }
        public required int ScreenHeight { get; init; }
        public required int CaptureWidth { get; init; }
        public required int CaptureHeight { get; init; }
        public required int ActiveMonitorIndex { get; init; }
        public required List<MonitorInfo> Monitors { get; init; }
    }

    /// <summary>
    /// Sent when authentication fails.
    /// </summary>
    public sealed class AuthFailedMessage
    {
        public string Type => "auth_failed";
        public required string Reason { get; init; }
    }

    /// <summary>
    /// Sent periodically as a status heartbeat.
    /// </summary>
    public sealed class StatusMessage
    {
        public string Type => "status";
        public required long TimestampMs { get; init; }
        public required int ViewerCount { get; init; }
    }

    /// <summary>
    /// Sent when the screen lock state changes.
    /// </summary>
    public sealed class LockStateMessage
    {
        public string Type => "lock_state";
        public required bool Locked { get; init; }
    }

    /// <summary>
    /// Sent when an unlock request completes (success or failure).
    /// </summary>
    public sealed class UnlockResultMessage
    {
        public string Type => "unlock_result";
        public required bool Success { get; init; }
        public required bool HasCredential { get; init; }
        public string? Error { get; init; }
    }

    // === Viewer → Server messages ===

    /// <summary>
    /// First message from a viewer — authenticates with the optional PIN.
    /// </summary>
    public sealed class AuthRequest
    {
        public string Type => "auth";
        public string? Pin { get; init; }
    }

    /// <summary>
    /// Mouse event from the viewer.
    /// </summary>
    public sealed class MouseEvent
    {
        public string Type => "mouse";
        public required int X { get; init; }
        public required int Y { get; init; }
        public required string Button { get; init; } // left, right, middle
        public required string Action { get; init; } // down, up, move, scroll
        public int Delta { get; init; } // scroll amount
    }

    /// <summary>
    /// Keyboard event from the viewer.
    /// </summary>
    public sealed class KeyboardEvent
    {
        public string Type => "keyboard";
        public required int KeyCode { get; init; }
        public required string Action { get; init; } // down, up
    }

    /// <summary>
    /// Text input event from the viewer.
    /// </summary>
    public sealed class TextEvent
    {
        public string Type => "text";
        public required string Value { get; init; }
    }

    /// <summary>
    /// Request to unlock the remote session.
    /// </summary>
    public sealed class UnlockRequest
    {
        public string Type => "unlock";
    }

    /// <summary>
    /// Store Windows login credential for auto-unlock.
    /// </summary>
    public sealed class StoreCredentialRequest
    {
        public string Type => "store_credential";
        public required string Credential { get; init; }
    }

    /// <summary>
    /// Request to switch the active monitor.
    /// </summary>
    public sealed class SwitchMonitorRequest
    {
        public string Type => "switch_monitor";
        public required int MonitorIndex { get; init; }
    }

    /// <summary>
    /// Sent to all viewers when the active monitor changes.
    /// </summary>
    public sealed class MonitorChangedMessage
    {
        public string Type => "monitor_changed";
        public required int ActiveMonitorIndex { get; init; }
        public required int ScreenWidth { get; init; }
        public required int ScreenHeight { get; init; }
        public required int CaptureWidth { get; init; }
        public required int CaptureHeight { get; init; }
    }

    // === Clipboard messages ===

    /// <summary>
    /// Server → Viewer: clipboard content from the host.
    /// </summary>
    public sealed class ClipboardMessage
    {
        public string Type => "clipboard";
        public required string Text { get; init; }
    }

    /// <summary>
    /// Viewer → Server: clipboard content from the viewer.
    /// </summary>
    public sealed class ClipboardUpdateRequest
    {
        public string Type => "clipboard";
        public required string Text { get; init; }
    }
}
