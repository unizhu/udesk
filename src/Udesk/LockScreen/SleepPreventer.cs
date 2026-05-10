using System.Runtime.InteropServices;
using Udesk.Interop;

namespace Udesk.LockScreen;

/// <summary>
/// Prevents the Windows session from locking or going to sleep.
/// Uses SetThreadExecutionState — no admin privileges required.
/// When the screen timeout does occur, provides capability to unlock.
/// </summary>
public sealed class SleepPreventer : IDisposable
{
    private readonly ILogger<SleepPreventer> _logger;
    private bool _preventActive;
    private bool _disposed;

    public SleepPreventer(ILogger<SleepPreventer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Prevents the display from turning off and the system from sleeping.
    /// Must be called periodically (or with ES_CONTINUOUS) to keep alive.
    /// </summary>
    public void PreventSleep()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_preventActive) return;

        var result = NativeMethods.SetThreadExecutionState(
            EXECUTION_STATE.ES_CONTINUOUS |
            EXECUTION_STATE.ES_DISPLAY_REQUIRED |
            EXECUTION_STATE.ES_SYSTEM_REQUIRED);

        if (result == 0)
        {
            _logger.LogWarning("SetThreadExecutionState failed — cannot prevent sleep");
            return;
        }

        _preventActive = true;
        _logger.LogInformation("Sleep prevention enabled (display + system)");
    }

    /// <summary>
    /// Allows the system to resume normal sleep behavior.
    /// </summary>
    public void AllowSleep()
    {
        if (!_preventActive || _disposed) return;

        var result = NativeMethods.SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
        if (result == 0)
        {
            _logger.LogWarning("SetThreadExecutionState failed — cannot restore sleep");
            return;
        }

        _preventActive = false;
        _logger.LogInformation("Sleep prevention disabled — normal sleep restored");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        AllowSleep();
    }
}
