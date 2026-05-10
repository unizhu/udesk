namespace Udesk.LockScreen;

/// <summary>
/// Interface for handling lock screen unlock operations.
/// </summary>
public interface ILockScreenHandler
{
    /// <summary>
    /// Attempts to unlock the Windows session.
    /// Uses SendSAS to simulate Ctrl+Alt+Del, then types stored credentials.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if unlock sequence was initiated successfully.</returns>
    Task<bool> UnlockAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stores the Windows login credential for auto-unlock.
    /// </summary>
    /// <param name="credential">PIN or password for Windows login.</param>
    Task StoreCredentialAsync(string credential);

    /// <summary>
    /// Returns true if a credential has been stored for auto-unlock.
    /// </summary>
    bool HasCredential { get; }
}
