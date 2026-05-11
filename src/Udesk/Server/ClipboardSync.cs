using System.Text.Json;
using Udesk.Interop;

namespace Udesk.Server;

/// <summary>
/// Manages bidirectional clipboard synchronization between the host and viewers.
/// Polls the Windows clipboard for changes and broadcasts to viewers.
/// Receives clipboard text from viewers and sets the Windows clipboard.
/// </summary>
public sealed class ClipboardSync
{
    private readonly ILogger<ClipboardSync> _logger;
    private string _lastClipboardText = string.Empty;
    private readonly object _lock = new();

    public ClipboardSync(ILogger<ClipboardSync> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Polls the Windows clipboard for changes.
    /// Returns the clipboard text if it changed, null otherwise.
    /// </summary>
    public string? PollClipboardChange()
    {
        try
        {
            var text = ClipboardInterop.GetText();
            if (text is null) return null;

            lock (_lock)
            {
                if (text == _lastClipboardText) return null;
                _lastClipboardText = text;
                return text;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read clipboard");
            return null;
        }
    }

    /// <summary>
    /// Sets the Windows clipboard from a viewer's clipboard content.
    /// Avoids echo by updating the last-known text.
    /// </summary>
    public void SetFromViewer(string text)
    {
        try
        {
            lock (_lock)
            {
                _lastClipboardText = text;
            }

            ClipboardInterop.SetText(text);
            _logger.LogDebug("Clipboard synced from viewer ({Length} chars)", text.Length);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set clipboard from viewer");
        }
    }
}
