using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Udesk.Capture;
using Udesk.Input;
using Udesk.LockScreen;
using Udesk.Security;
using Udesk.Server;

namespace Udesk;

/// <summary>
/// Udesk — lightweight remote desktop for Windows.
/// No admin privileges, no drivers, no external dependencies.
/// Browser-based viewer (Safari/Chrome on macOS).
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

        using var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((_, services) =>
            {
                services.AddSingleton(options);
                services.AddSingleton<IScreenCapture>(sp =>
                    new GdiScreenCapture(
                        options.Fps,
                        options.JpegQuality,
                        options.ScaleFactor,
                        sp.GetRequiredService<ILogger<GdiScreenCapture>>(),
                        options.MonitorIndex));
                services.AddSingleton<IInputController, SendInputController>();
                services.AddSingleton<IAuthProvider>(sp =>
                    new PinAuthProvider(
                        options.Pin,
                        sp.GetRequiredService<ILogger<PinAuthProvider>>()));
                services.AddSingleton<SleepPreventer>();
                services.AddSingleton<CredentialStore>();
                services.AddSingleton<LockScreenDetector>();
                services.AddSingleton<TlsCertificateManager>();
                services.AddSingleton<ClipboardSync>();
                services.AddSingleton<ILockScreenHandler>(sp =>
                    new SasUnlockHandler(
                        sp.GetRequiredService<IInputController>(),
                        sp.GetRequiredService<CredentialStore>(),
                        sp.GetRequiredService<ILogger<SasUnlockHandler>>()));
                services.AddSingleton<UdeskServer>(sp =>
                    new UdeskServer(
                        sp.GetRequiredService<IScreenCapture>(),
                        sp.GetRequiredService<IInputController>(),
                        sp.GetRequiredService<IAuthProvider>(),
                        sp.GetRequiredService<UdeskOptions>(),
                        sp.GetRequiredService<SleepPreventer>(),
                        sp.GetRequiredService<LockScreenDetector>(),
                        sp.GetRequiredService<ILockScreenHandler>(),
                        options.EnableTls ? sp.GetRequiredService<TlsCertificateManager>() : null,
                        sp.GetRequiredService<ClipboardSync>(),
                        sp.GetRequiredService<ILogger<UdeskServer>>()));
                services.AddHostedService<UdeskHostedService>();
            })
            .ConfigureLogging(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Information);
            })
            .Build();

        var logger = host.Services.GetRequiredService<ILogger<UdeskHostedService>>();
        logger.LogInformation("Udesk starting on port {Port}, FPS: {Fps}, Quality: {Quality}%",
            options.Port, options.Fps, options.JpegQuality);

        if (options.Pin is not null)
        {
            logger.LogInformation("PIN protection enabled");
        }

        if (options.EnableTls)
        {
            logger.LogInformation("TLS enabled with self-signed certificate");
        }

        try
        {
            await host.RunAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Udesk terminated unexpectedly");
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
