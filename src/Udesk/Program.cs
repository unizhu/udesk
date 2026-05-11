using Udesk.Capture;
using Udesk.Input;
using Udesk.LockScreen;
using Udesk.Security;
using Udesk.Server;
using System.Text;

namespace Udesk;

/// <summary>
/// Udesk — lightweight remote desktop for Windows.
/// No admin privileges, no drivers, no external dependencies.
/// Browser-based viewer (Safari/Chrome on macOS).
/// Uses Kestrel (raw TCP sockets) — binds 0.0.0.0 without admin.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var options = ParseArgs(args);

        Console.WriteLine($"udesk starting — port: {options.Port}, fps: {options.Fps}, quality: {options.JpegQuality}%");
        if (options.Pin is not null) Console.WriteLine($"PIN: enabled ({options.Pin.Length} digits)");
        if (options.EnableTls) Console.WriteLine("TLS: enabled");
        Console.WriteLine($"Monitor: {(options.MonitorIndex?.ToString() ?? "primary")}");
        Console.WriteLine();

        // Pass empty args to avoid ASP.NET Core parsing our custom flags
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());

        // Suppress default ASP.NET Core logging noise
        builder.Logging.SetMinimumLevel(LogLevel.Information);
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.Hosting", LogLevel.Warning);

        // Configure Kestrel — bind 0.0.0.0 (all interfaces, no admin needed)
        builder.WebHost.ConfigureKestrel(kestrelOptions =>
        {
            kestrelOptions.Listen(System.Net.IPAddress.Any, options.Port, listenOptions =>
            {
                if (options.EnableTls)
                {
                    var certManager = new TlsCertificateManager(
                        LoggerFactory.Create(b => b.AddConsole()).CreateLogger<TlsCertificateManager>());
                    var cert = certManager.GetOrCreateCertificate();
                    if (cert is not null)
                        listenOptions.UseHttps(cert);
                }
            });
        });

        // Register services
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<IScreenCapture>(sp =>
            new GdiScreenCapture(
                options.Fps,
                options.JpegQuality,
                options.ScaleFactor,
                sp.GetRequiredService<ILogger<GdiScreenCapture>>(),
                options.MonitorIndex));
        builder.Services.AddSingleton<IInputController, SendInputController>();
        builder.Services.AddSingleton<IAuthProvider>(sp =>
            new PinAuthProvider(
                options.Pin,
                sp.GetRequiredService<ILogger<PinAuthProvider>>()));
        builder.Services.AddSingleton<SleepPreventer>();
        builder.Services.AddSingleton<CredentialStore>();
        builder.Services.AddSingleton<LockScreenDetector>();
        builder.Services.AddSingleton<TlsCertificateManager>();
        builder.Services.AddSingleton<ClipboardSync>();
        builder.Services.AddSingleton<ILockScreenHandler>(sp =>
            new SasUnlockHandler(
                sp.GetRequiredService<IInputController>(),
                sp.GetRequiredService<CredentialStore>(),
                sp.GetRequiredService<ILogger<SasUnlockHandler>>()));
        builder.Services.AddSingleton<UdeskHub>();
        builder.Services.AddHostedService<CaptureHostedService>();

        var app = builder.Build();

        // WebSocket middleware
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

        // Serve viewer HTML page
        app.MapGet("/", (HttpContext context) =>
        {
            var html = EmbeddedResources.GetViewerHtml();
            return Results.Bytes(html, "text/html", Encoding.UTF8);
        });

        // Serve TLS certificate for download
        app.MapGet("/cert.pem", (HttpContext context) =>
        {
            if (!options.EnableTls) return Results.NotFound();
            var certManager = context.RequestServices.GetRequiredService<TlsCertificateManager>();
            var pem = certManager.GetCertificatePem();
            return Results.Text(pem ?? "", "application/x-pem-file");
        });

        // WebSocket endpoint
        app.Map("/ws", async (HttpContext context) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                return;
            }

            var webSocket = await context.WebSockets.AcceptWebSocketAsync();
            var hub = context.RequestServices.GetRequiredService<UdeskHub>();
            await hub.HandleConnectionAsync(webSocket, context.RequestAborted);
        });

        var listenUrl = $"{(options.EnableTls ? "https" : "http")}://0.0.0.0:{options.Port}/";
        Console.WriteLine($"Binding to: {listenUrl}");
        Console.WriteLine();
        Console.WriteLine($"Udesk ready — open {(options.EnableTls ? "https" : "http")}://<windows-ip>:{options.Port}/ in your browser");
        Console.WriteLine("Press Ctrl+C to stop");
        Console.WriteLine();

        try
        {
            await app.RunAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FATAL] {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException is not null)
                Console.WriteLine($"[FATAL] Inner: {ex.InnerException.Message}");
            return 1;
        }

        return 0;
    }

    private static UdeskOptions ParseArgs(string[] args)
    {
        var options = new UdeskOptions();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--port" or "-p":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var port))
                        options = options with { Port = Math.Clamp(port, 1, 65535) };
                    break;
                case "--fps" or "-f":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var fps))
                        options = options with { Fps = Math.Clamp(fps, 1, 30) };
                    break;
                case "--quality" or "-q":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var quality))
                        options = options with { JpegQuality = Math.Clamp(quality, 1, 100) };
                    break;
                case "--pin":
                    if (i + 1 < args.Length)
                    {
                        var pin = args[++i];
                        if (pin.Length is >= 4 and <= 8)
                            options = options with { Pin = pin };
                    }
                    break;
                case "--tls":
                    options = options with { EnableTls = true };
                    break;
                case "--monitor":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var mon))
                        options = options with { MonitorIndex = mon };
                    break;
                case "--help" or "-h":
                    Console.WriteLine("Udesk — Lightweight Remote Desktop");
                    Console.WriteLine();
                    Console.WriteLine("Usage: udesk [options]");
                    Console.WriteLine();
                    Console.WriteLine("Options:");
                    Console.WriteLine("  --port, -p <port>      Port to listen on (default: 8080)");
                    Console.WriteLine("  --fps, -f <fps>        Target frames per second (default: 5)");
                    Console.WriteLine("  --quality, -q <1-100>  JPEG quality (default: 40)");
                    Console.WriteLine("  --pin <4-8 digits>     Optional PIN for viewer authentication");
                    Console.WriteLine("  --tls                  Enable HTTPS with self-signed certificate");
                    Console.WriteLine("  --monitor <index>      Monitor index to capture (0-based)");
                    Console.WriteLine("  --help, -h             Show this help");
                    Environment.Exit(0);
                    break;
            }
        }

        return options;
    }
}
