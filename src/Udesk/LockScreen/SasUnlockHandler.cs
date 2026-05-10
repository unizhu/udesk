using Udesk.Input;
using Udesk.Interop;

namespace Udesk.LockScreen;

/// <summary>
/// Unlock handler using SendSAS + SendInput to auto-type Windows login PIN.
/// Requires the one-time Group Policy setting:
///   "Software SASEnabled" = 1 (Enable software Secure Attention Sequence)
/// This allows SendSAS to simulate Ctrl+Alt+Del without running as a service.
/// </summary>
public sealed class SasUnlockHandler : ILockScreenHandler, IDisposable
{
    private readonly IInputController _input;
    private readonly CredentialStore _credentialStore;
    private readonly ILogger<SasUnlockHandler> _logger;
    private bool _disposed;

    public bool HasCredential => _credentialStore.HasCredential;

    public SasUnlockHandler(
        IInputController input,
        CredentialStore credentialStore,
        ILogger<SasUnlockHandler> logger)
    {
        _input = input;
        _credentialStore = credentialStore;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> UnlockAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var credential = _credentialStore.LoadCredential();
        if (credential is null)
        {
            _logger.LogWarning("Cannot unlock: no stored credential found");
            return false;
        }

        _logger.LogInformation("Starting unlock sequence via SendSAS");

        try
        {
            // Step 1: SendSAS to trigger the Ctrl+Alt+Del screen
            NativeMethods.SendSAS(true);
            _logger.LogDebug("SendSAS sent");

            // Step 2: Wait for the unlock screen to appear
            await Task.Delay(800, cancellationToken).ConfigureAwait(false);

            // Step 3: Type the Windows login PIN/password
            _input.TypeText(credential);
            _logger.LogDebug("Credential typed");

            // Step 4: Wait briefly then press Enter
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            _input.KeyDown(VirtualKeyCodes.VK_RETURN);
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            _input.KeyUp(VirtualKeyCodes.VK_RETURN);

            _logger.LogInformation("Unlock sequence completed");
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Unlock sequence cancelled");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unlock sequence failed");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task StoreCredentialAsync(string credential)
    {
        _credentialStore.SaveCredential(credential);
        _logger.LogInformation("Windows login credential stored");
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
