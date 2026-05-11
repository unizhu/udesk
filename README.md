# udesk

Lightweight remote desktop for Windows — single exe, no admin, browser viewer.

## Features

- 🖥️ Browser-based viewer (no app install needed — works on macOS Safari/Chrome)
- 🔒 Optional PIN authentication on connect
- 🖱️ Full mouse & keyboard control
- 📋 Bidirectional clipboard sync
- 🖥️ Multi-monitor support with switcher
- 🔐 TLS with auto-generated self-signed certificate
- 🚀 Single self-contained exe (~15 MB), no admin privileges required

## Quick Start

### Download

Grab the latest release from [GitHub Releases](https://github.com/unizhu/udesk/releases).

### Run

```powershell
# Basic — starts on http://localhost:8080
udesk.exe

# With PIN protection
udesk.exe --pin 1234

# With TLS (auto-generates self-signed cert)
udesk.exe --tls

# Custom port
udesk.exe --port 9090

# All options
udesk.exe --pin 1234 --tls --port 9090 --fps 10 --quality 60
```

### Connect

1. Open `http://<windows-ip>:8080` in your browser (or `https://` if using `--tls`)
2. If PIN was set, enter it when prompted
3. You're in!

> **LAN only by default.** Use `--tls` for encryption. For internet access, use a VPN or SSH tunnel.

## CLI Options

| Flag | Default | Description |
|------|---------|-------------|
| `--port` | `8080` | HTTP/HTTPS listen port |
| `--pin` | none | 4–8 digit PIN (optional, memory-only) |
| `--tls` | off | Enable HTTPS with auto-generated self-signed cert |
| `--fps` | `5` | Capture frame rate |
| `--quality` | `40` | JPEG quality (1–100) |
| `--monitor` | `0` | Default monitor index (0-based) |

## How It Works

```
┌─────────────┐       WebSocket        ┌──────────────────┐
│   Browser   │◄─────── JPEG ─────────►│  udesk.exe       │
│  (macOS/    │  mouse/keyboard/clip   │  (Windows host)  │
│   any OS)   │                        │  GDI capture +   │
└─────────────┘                        │  SendInput       │
                                       └──────────────────┘
```

- **Capture**: GDI screen capture at configurable resolution/FPS/quality
- **Transport**: WebSocket binary frames for screen data, JSON text frames for control
- **Input**: `SendInput` Win32 API for mouse/keyboard injection
- **Auth**: Memory-only PIN with max 3 attempts, auto-disconnect on failure

## Building from Source

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Build

```powershell
cd src/Udesk
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

Output: `src/Udesk/bin/Release/net8.0-windows/win-x64/publish/udesk.exe`

### Cross-compile for ARM64

```powershell
dotnet publish -c Release -r win-arm64 --self-contained -p:PublishSingleFile=true
```

## Security Notes

- No admin privileges required
- PIN is stored in memory only — never persisted to disk
- TLS uses auto-generated self-signed certificates (stored at `~/.udesk/cert.pfx`)
- For production use, consider replacing the self-signed cert with a proper one
- Lock screen detection & unlock requires Group Policy: **Computer Configuration → Windows Settings → Security Settings → Local Policies → Security Options → "Disable or enable software Secure Attention Sequence" → Enabled for services**

## System Requirements

- **Host**: Windows 10+ (AMD64 or ARM64)
- **Viewer**: Any modern browser (Safari, Chrome, Firefox, Edge)
- **Network**: LAN or VPN access to the host

## License

[AGPLv3](LICENSE)
