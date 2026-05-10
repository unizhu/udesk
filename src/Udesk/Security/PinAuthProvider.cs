using System.Security.Cryptography;

namespace Udesk.Security;

/// <summary>
/// PIN-based authentication provider.
/// The PIN is stored in memory only (never persisted to disk).
/// Supports max 3 failed attempts before temporary lockout.
/// Thread-safe for concurrent viewer connections.
/// </summary>
public sealed class PinAuthProvider : IAuthProvider
{
    private readonly string? _pin;
    private readonly ILogger<PinAuthProvider> _logger;
    private int _failedAttempts;
    private long _lockoutUntilTicks; // DateTime.UtcNow.Ticks

    public bool RequiresPin => _pin is not null;

    public PinAuthProvider(string? pin, ILogger<PinAuthProvider> logger)
    {
        if (pin is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pin);
            if (pin.Length is < 4 or > 8)
                throw new ArgumentException("PIN must be 4-8 characters", nameof(pin));

            _pin = pin;
        }

        _logger = logger;
    }

    public Task<bool> ValidatePinAsync(string? pin)
    {
        // No PIN required
        if (_pin is null)
        {
            _logger.LogDebug("No PIN required, authenticating viewer");
            return Task.FromResult(true);
        }

        // Check lockout (atomic read)
        var lockoutTicks = Interlocked.Read(ref _lockoutUntilTicks);
        if (lockoutTicks > 0 && DateTime.UtcNow.Ticks < lockoutTicks)
        {
            var lockoutEnd = new DateTime(lockoutTicks, DateTimeKind.Utc);
            _logger.LogWarning("Viewer authentication rejected: locked out until {LockoutEnd}", lockoutEnd);
            return Task.FromResult(false);
        }

        // Constant-time comparison to prevent timing attacks
        if (pin is null || !CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(pin),
            System.Text.Encoding.UTF8.GetBytes(_pin)))
        {
            var attempts = Interlocked.Increment(ref _failedAttempts);
            _logger.LogWarning("Invalid PIN attempt #{Count}", attempts);

            if (attempts >= 3)
            {
                var lockoutEnd = DateTime.UtcNow.AddMinutes(5).Ticks;
                Interlocked.Exchange(ref _lockoutUntilTicks, lockoutEnd);
                _logger.LogWarning("Too many failed attempts. Locked out for 5 minutes");
            }

            return Task.FromResult(false);
        }

        // Success — reset counters
        Interlocked.Exchange(ref _failedAttempts, 0);
        Interlocked.Exchange(ref _lockoutUntilTicks, 0);
        _logger.LogInformation("Viewer authenticated successfully");
        return Task.FromResult(true);
    }
}
