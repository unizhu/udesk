using System.Runtime.InteropServices;
using Udesk.Interop;

namespace Udesk.LockScreen;

/// <summary>
/// Detects whether the Windows session is locked (Secure Desktop active).
/// Uses OpenInputDesktop — if it fails or returns IntPtr.Zero,
/// the session is on the Secure Desktop (locked).
/// </summary>
public sealed class LockScreenDetector : IDisposable
{
    private readonly ILogger<LockScreenDetector> _logger;
    private bool _disposed;
    private bool _lastKnownLocked;

    /// <summary>Fired when lock state changes.</summary>
    public event Action<bool>? LockStateChanged;

    public LockScreenDetector(ILogger<LockScreenDetector> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets the current lock state. Returns true if the screen is locked.
    /// </summary>
    public bool IsLocked => DetectLockState();

    /// <summary>
    /// Polls the lock state and fires event if changed.
    /// Call this periodically (e.g. every 2 seconds).
    /// </summary>
    public void Poll()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var locked = DetectLockState();
        if (locked != _lastKnownLocked)
        {
            _lastKnownLocked = locked;
            _logger.LogInformation("Session lock state changed: {State}", locked ? "Locked" : "Unlocked");
            LockStateChanged?.Invoke(locked);
        }
    }

    private bool DetectLockState()
    {
        // OpenInputDesktop returns a handle to the desktop currently receiving input.
        // When the screen is locked, the Secure Desktop is active and OpenInputDesktop
        // will fail (return IntPtr.Zero) for non-service processes.
        var hDesktop = NativeMethods.OpenInputDesktop(
            0,
            false,
            DesktopAccessRights.DESKTOP_READOBJECTS);

        if (hDesktop == IntPtr.Zero)
        {
            // Could not open input desktop — session is likely locked
            return true;
        }

        // Successfully opened — close the handle and report not locked
        NativeMethods.CloseDesktop(hDesktop);
        return false;
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
