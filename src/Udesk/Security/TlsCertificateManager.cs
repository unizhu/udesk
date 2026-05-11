using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Udesk.Security;

/// <summary>
/// Manages TLS certificate generation and binding for HTTPS support.
/// Auto-generates a self-signed certificate on first run, stored at ~/.udesk/cert.pfx.
/// Binds the certificate to the HTTP listener port via netsh or HTTP.sys.
/// </summary>
public sealed class TlsCertificateManager
{
    private readonly ILogger<TlsCertificateManager> _logger;
    private readonly string _certPath;
    private readonly string _certDir;

    public TlsCertificateManager(ILogger<TlsCertificateManager> logger)
    {
        _logger = logger;
        _certDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".udesk");
        Directory.CreateDirectory(_certDir);
        _certPath = Path.Combine(_certDir, "cert.pfx");
    }

    /// <summary>
    /// Gets or creates a self-signed TLS certificate.
    /// Returns the loaded X509Certificate2, or null if not available.
    /// </summary>
    public X509Certificate2? GetOrCreateCertificate()
    {
        // Try loading existing certificate
        if (File.Exists(_certPath))
        {
            try
            {
                var cert = new X509Certificate2(_certPath, string.Empty, X509KeyStorageFlags.PersistKeySet);
                if (cert.NotAfter > DateTime.UtcNow)
                {
                    _logger.LogInformation("Loaded existing TLS certificate (expires {Expiry})", cert.NotAfter);
                    return cert;
                }

                _logger.LogInformation("TLS certificate expired, regenerating");
                cert.Dispose();
                File.Delete(_certPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load existing certificate, regenerating");
                try { File.Delete(_certPath); } catch { /* ignore */ }
            }
        }

        // Generate new self-signed certificate
        return GenerateSelfSignedCertificate();
    }

    /// <summary>
    /// Gets the PEM-encoded certificate for download by the viewer.
    /// </summary>
    public string? GetCertificatePem()
    {
        if (!File.Exists(_certPath)) return null;

        try
        {
            using var cert = new X509Certificate2(_certPath, string.Empty);
            return cert.ExportCertificatePem();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Binds the SSL certificate to the given port for HttpListener.
    /// Requires admin or a one-time netsh command.
    /// Returns true if binding succeeded.
    /// </summary>
    public bool BindSslCertificate(int port, X509Certificate2 certificate)
    {
        try
        {
            // Use HTTP.sys certificate binding via netsh
            var thumbprint = certificate.Thumbprint;
            var appId = typeof(TlsCertificateManager).Assembly.GetName().Name ?? "udesk";

            // First, try to delete any existing binding
            RunNetsh($"http delete sslcert ipport=0.0.0.0:{port}");

            // Add new binding
            var result = RunNetsh(
                $"http add sslcert ipport=0.0.0.0:{port} " +
                $"certhash={thumbprint} " +
                $"appid={{{Guid.NewGuid()}}}");

            if (result)
            {
                _logger.LogInformation("SSL certificate bound to port {Port}", port);
                return true;
            }

            _logger.LogWarning("Failed to bind SSL certificate via netsh. " +
                "Run manually: netsh http add sslcert ipport=0.0.0.0:{Port} certhash={Thumbprint} appid={{ANY-GUID}}",
                port, thumbprint);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SSL certificate binding failed (admin required)");
            return false;
        }
    }

    private X509Certificate2? GenerateSelfSignedCertificate()
    {
        try
        {
            var subjectName = $"CN=udesk-{Environment.MachineName}";
            using var rsa = RSA.Create(2048);

            var request = new CertificateRequest(
                subjectName,
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            // Add Subject Alternative Names for local network access
            var sanBuilder = new SubjectAlternativeNameBuilder();
            sanBuilder.AddDnsName(Environment.MachineName);
            sanBuilder.AddIpAddress(System.Net.IPAddress.Loopback);
            sanBuilder.AddIpAddress(System.Net.IPAddress.IPv6Loopback);

            // Add common LAN IPs
            foreach (var ip in GetLocalIpAddresses())
            {
                sanBuilder.AddIpAddress(ip);
            }

            request.CertificateExtensions.Add(sanBuilder.Build());

            // Basic constraints: CA=false, end entity
            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(certificateAuthority: false, pathLengthConstraint: 0, critical: true));

            // Enhanced key usage: Server Authentication
            request.CertificateExtensions.Add(
                new X509EnhancedKeyUsageExtension(
                    new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, critical: true));

            // Key usage: Digital Signature, Key Encipherment
            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                    critical: true));

            // Valid for 1 year
            var cert = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddYears(1));

            // Export and save as PFX (no password)
            var pfxBytes = cert.Export(X509ContentType.Pfx, string.Empty);
            File.WriteAllBytes(_certPath, pfxBytes);

            _logger.LogInformation("Generated new self-signed TLS certificate at {Path} (valid 1 year)", _certPath);
            return cert;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate self-signed certificate");
            return null;
        }
    }

    private static List<System.Net.IPAddress> GetLocalIpAddresses()
    {
        var ips = new List<System.Net.IPAddress>();
        try
        {
            var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    ips.Add(ip);
                }
            }
        }
        catch
        {
            // Ignore DNS resolution failures
        }
        return ips;
    }

    private bool RunNetsh(string arguments)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null) return false;

            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
