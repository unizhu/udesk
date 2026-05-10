namespace Udesk.Security;

/// <summary>
/// Interface for viewer authentication.
/// </summary>
public interface IAuthProvider
{
    /// <summary>
    /// Returns true if PIN authentication is required.
    /// </summary>
    bool RequiresPin { get; }

    /// <summary>
    /// Validates the provided PIN. If no PIN is required, always returns true.
    /// </summary>
    /// <param name="pin">The PIN provided by the viewer, or null if none was sent.</param>
    /// <returns>True if authentication succeeds.</returns>
    Task<bool> ValidatePinAsync(string? pin);
}
