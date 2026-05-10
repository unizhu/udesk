using System.Security.Cryptography;

namespace Udesk.LockScreen;

/// <summary>
/// Stores Windows login credentials encrypted with DPAPI.
/// The credential file is stored at ~/.udesk/credentials.dat (CurrentUser scope).
/// Only the same Windows user on the same machine can decrypt it.
/// </summary>
public sealed class CredentialStore
{
    private readonly string _credentialPath;
    private readonly ILogger<CredentialStore> _logger;

    public bool HasCredential => File.Exists(_credentialPath);

    public CredentialStore(ILogger<CredentialStore> logger)
    {
        _logger = logger;
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var udeskDir = Path.Combine(homeDir, ".udesk");
        Directory.CreateDirectory(udeskDir);
        _credentialPath = Path.Combine(udeskDir, "credentials.dat");
    }

    /// <summary>
    /// Saves a Windows login credential, encrypted with DPAPI.
    /// </summary>
    /// <param name="credential">The PIN or password to store.</param>
    public void SaveCredential(string credential)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credential);

        var plaintext = System.Text.Encoding.UTF8.GetBytes(credential);

        // DPAPI encrypt with CurrentUser scope — tied to this Windows user account
        var encrypted = ProtectedData.Protect(
            plaintext,
            optionalEntropy: null,
            scope: DataProtectionScope.CurrentUser);

        // Write with restricted permissions (user-only)
        File.WriteAllBytes(_credentialPath, encrypted);
        _logger.LogInformation("Credential saved to {Path} (DPAPI encrypted)", _credentialPath);
    }

    /// <summary>
    /// Loads and decrypts the stored credential.
    /// </summary>
    /// <returns>The decrypted credential, or null if not found or decryption fails.</returns>
    public string? LoadCredential()
    {
        if (!File.Exists(_credentialPath))
        {
            return null;
        }

        try
        {
            var encrypted = File.ReadAllBytes(_credentialPath);
            var plaintext = ProtectedData.Unprotect(
                encrypted,
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser);

            return System.Text.Encoding.UTF8.GetString(plaintext);
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(ex, "Failed to decrypt stored credential — may be corrupted or from different user");
            return null;
        }
    }

    /// <summary>
    /// Deletes the stored credential file.
    /// </summary>
    public void DeleteCredential()
    {
        if (File.Exists(_credentialPath))
        {
            File.Delete(_credentialPath);
            _logger.LogInformation("Credential deleted from {Path}", _credentialPath);
        }
    }
}
