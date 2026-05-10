# udesk — Implementation Plan

## Overview

**udesk** is a lightweight Windows remote desktop tool. Single exe, no admin, no drivers, browser-based viewer.

- **Language**: C# 12 / .NET 8
- **Target**: Windows 10/11 amd64
- **Dependencies**: Zero external NuGet packages
- **Output**: Single self-contained exe (~15MB)

---

## Architecture

```
Browser (any device)
   |  WebSocket (ws://host:port/ws)
   v
udesk.exe
   |-- Capture Layer (GDI CopyFromScreen -> resize -> JPEG)
   |-- Input Layer (SendInput: mouse + keyboard)
   |-- Lock Screen (SetThreadExecutionState + SendSAS + auto-type)
   |-- Web Server (HttpListener + WebSocket)
   +-- Security (password auth + DPAPI credential store)
```

---

## Phase 1 — Core Screen + Input + Viewer (MVP)

**Goal**: See the remote screen, move mouse, click, type. Over LAN.

### 1.1 Project Scaffold

- [ ] Create solution: `udesk.sln` with single project `src/Udesk.csproj`
- [ ] `.csproj` config: `net8.0-windows`, AOT-ready, single-file publish, analyzers on
- [ ] `.editorconfig` for formatting rules
- [ ] `.gitignore` for .NET (bin, obj, .vs, user settings)
- [ ] README.md with build instructions

### 1.2 P/Invoke Interop Layer (`Udesk.Interop`)

- [ ] `NativeMethods.cs` — LibraryImport source-generated P/Invoke:
  - `user32.dll`: `SendInput`, `SetCursorPos`, `GetCursorPos`, `mouse_event`, `GetSystemMetrics`
  - `kernel32.dll`: `SetThreadExecutionState`, `GetLastError`
  - `sas.dll`: `SendSAS`
  - `gdi32.dll`: (if needed for raw GDI fallback)
- [ ] `NativeTypes.cs` — INPUT, MOUSEINPUT, KEYBDINPUT, HARDWAREINPUT structs with exact LayoutKind.Sequential and CharSet
- [ ] `SafeHandles.cs` — SafeHandle derivatives for any native resources

### 1.3 Screen Capture (`Udesk.Capture`)

- [ ] `IScreenCapture.cs` — interface: `StartAsync(CancellationToken)`, `StopAsync()`, event `FrameCaptured`
- [ ] `GdiScreenCapture.cs` — implementation:
  1. Get primary screen bounds via `System.Windows.Forms.Screen.PrimaryScreen`
  2. Create offscreen Bitmap at target resolution (50% of native)
  3. Use `Graphics.CopyFromScreen` to capture
  4. Save to JPEG (quality=40) via `System.Drawing.Imaging`
  5. Return `byte[]` via `Channel<Frame>` (producer/consumer)
- [ ] `Frame.cs` — record: `byte[] Data`, `int Width`, `int Height`, `long Timestamp`
- [ ] Framerate control: configurable (default 5 FPS), sleep between captures
- [ ] Dirty region detection (optional Phase 2): compare previous frame, skip if identical

### 1.4 Input Controller (`Udesk.Input`)

- [ ] `IInputController.cs` — interface: `MouseMove(x, y)`, `MouseClick(x, y, button)`, `MouseScroll(delta)`, `KeyDown(key)`, `KeyUp(key)`, `TypeText(text)`
- [ ] `SendInputController.cs` — implementation:
  - Mouse: `SetCursorPos` + construct `INPUT` with `MOUSEINPUT` + `SendInput`
  - Keyboard: construct `INPUT` with `KEYBDINPUT` (VK codes) + `SendInput`
  - Text: convert characters to VK codes + shift state, send key-down/key-up pairs
- [ ] `VirtualKeyCodes.cs` — VK_* constants mapping (from WinUser.h)

### 1.5 Web Server (`Udesk.Server`)

- [ ] `IWebServer.cs` — interface: `StartAsync(CancellationToken)`, `StopAsync()`
- [ ] `HttpListenerServer.cs` — implementation:
  - Serves embedded HTML viewer page on `GET /`
  - Accepts WebSocket on `GET /ws`
  - WebSocket message protocol (JSON):
    ```
    Client -> Host (input):
      { "type": "mouse_move", "x": 100, "y": 200 }
      { "type": "mouse_click", "x": 100, "y": 200, "button": "left" }
      { "type": "mouse_scroll", "delta": 120 }
      { "type": "key_down", "key": 65 }
      { "type": "key_up", "key": 65 }
      { "type": "type_text", "text": "hello" }

    Host -> Client (screen):
      Binary frame: JPEG bytes (0xFF 0xD8 header)
      Text frame: { "type": "status", "screen": "locked" }
    ```
- [ ] `ViewerHtml.cs` — embedded HTML/JS/CSS as a const string:
  - Canvas element for JPEG display
  - Mouse event listeners → JSON → WebSocket send
  - Keyboard event listeners → JSON → WebSocket send
  - JPEG blob receive → draw to canvas via Image + requestAnimationFrame

### 1.6 Security (`Udesk.Security`)

- [ ] `ConnectionAuth.cs` — SHA256 password hash verification
- [ ] Handshake: client sends password hash on connect, server verifies before streaming

### 1.7 Entry Point (`Program.cs`)

- [ ] Parse CLI args: `--password`, `--port`, `--fps`, `--quality`
- [ ] Build DI container (ServiceCollection)
- [ ] Wire: Capture → Server (frame streaming), Server (input) → InputController
- [ ] Start capture thread + web server
- [ ] Graceful shutdown on Ctrl+C

### 1.8 Testing

- [ ] Unit test: JPEG encoding produces valid output
- [ ] Unit test: VirtualKeyCodes mapping completeness
- [ ] Unit test: input JSON message parsing
- [ ] Manual test: run on Windows, open browser, see screen, click

**Estimated time**: 2-3 days

---

## Phase 2 — Lock Screen Handling

**Goal**: Prevent screen timeout, and when locked, unlock remotely.

### 2.1 Prevent Sleep

- [ ] `SleepPreventer.cs` — calls `SetThreadExecutionState(ES_CONTINUOUS | ES_DISPLAY_REQUIRED | ES_SYSTEM_REQUIRED)` in a background loop every 60 seconds
- [ ] Restores previous state on shutdown

### 2.2 Lock Screen Detection

- [ ] `LockScreenDetector.cs` — detects if session is locked:
  - P/Invoke `OpenInputDesktop()` — if fails, session is locked
  - Or WMI query `Win32_LogonSession` logon type comparison
- [ ] Sends `{ "type": "status", "screen": "locked" }` to viewer

### 2.3 Unlock Handler

- [ ] `ILockScreenHandler.cs` — interface: `UnlockAsync()`, `SetCredentials(pin)`
- [ ] `SasUnlockHandler.cs`:
  1. Call `SendSAS(TRUE)` — simulates Ctrl+Alt+Del
  2. Wait 500ms for unlock screen to appear
  3. Resume capture (to see the PIN input field)
  4. Type stored PIN via `SendInput`
  5. Press Enter
  6. Wait for desktop to appear, resume normal streaming

### 2.4 Credential Storage

- [ ] `CredentialStore.cs`:
  - Encrypt PIN/password with `System.Security.Cryptography.ProtectedData` (DPAPI)
  - Store in `~/.udesk/credentials.dat` (CurrentUser scope)
  - Only decryptable by same Windows user on same machine

### 2.5 Viewer UI Update

- [ ] Show "Screen Locked" overlay with "Unlock" button
- [ ] First time: prompt for PIN, store locally
- [ ] Subsequent: click "Unlock" → auto-unlock sequence

**Estimated time**: 1 day

---

## Phase 3 — Polish & Production

### 3.1 TLS Support

- [ ] Auto-generate self-signed certificate on first run
- [ ] Store in `~/.udesk/cert.pfx`
- [ ] Configure `HttpListener` with HTTPS binding
- [ ] Viewer redirects HTTP → HTTPS

### 3.2 Multi-Monitor

- [ ] Detect all monitors via `Screen.AllScreens`
- [ ] Viewer: monitor selector dropdown
- [ ] Capture: switch between monitors

### 3.3 Clipboard Sync

- [ ] P/Invoke clipboard APIs: `OpenClipboard`, `GetClipboardData`, `SetClipboardData`
- [ ] WebSocket messages: `{ "type": "clipboard", "text": "..." }`
- [ ] Viewer: Ctrl+C / Ctrl+V syncs across machines

### 3.4 Performance Tuning

- [ ] Option: DXGI Desktop Duplication as alternative capture (dirty regions, GPU-accelerated)
- [ ] Adaptive quality: reduce FPS/resolution when bandwidth is limited
- [ ] Frame diffing: skip identical frames, only send changes

**Estimated time**: 1-2 days

---

## File Structure

```
udesk/
+-- AGENTS.md
+-- README.md
+-- docs/
|   +-- PLAN.md                 (this file)
|   +-- ARCHITECTURE.md         (detailed architecture diagrams, Phase 1+)
+-- src/
|   +-- Udesk.sln
|   +-- Udesk/
|       +-- Udesk.csproj
|       +-- Program.cs
|       +-- Capture/
|       |   +-- IScreenCapture.cs
|       |   +-- GdiScreenCapture.cs
|       |   +-- Frame.cs
|       +-- Input/
|       |   +-- IInputController.cs
|       |   +-- SendInputController.cs
|       |   +-- VirtualKeyCodes.cs
|       +-- LockScreen/
|       |   +-- ILockScreenHandler.cs
|       |   +-- SasUnlockHandler.cs
|       |   +-- LockScreenDetector.cs
|       |   +-- SleepPreventer.cs
|       +-- Server/
|       |   +-- IWebServer.cs
|       |   +-- HttpListenerServer.cs
|       |   +-- ViewerHtml.cs
|       +-- Security/
|       |   +-- ConnectionAuth.cs
|       |   +-- CredentialStore.cs
|       +-- Interop/
|           +-- NativeMethods.cs
|           +-- NativeTypes.cs
|           +-- SafeHandles.cs
+-- tests/
|   +-- Udesk.Tests/
|       +-- Udesk.Tests.csproj
|       +-- CaptureTests.cs
|       +-- InputTests.cs
|       +-- ServerTests.cs
+-- .editorconfig
+-- .gitignore
+-- publish.bat
```

---

## Key Technical Decisions

| Decision | Choice | Why |
|---|---|---|
| Language | C# 12 / .NET 8 | Best Win32 interop story, zero-dep BCL, single-file publish |
| Screen capture | GDI CopyFromScreen | Simplest, zero dependencies, works without admin |
| Video codec | JPEG (no video codec) | Simplest, no external deps, low quality OK per requirement |
| Transport | WebSocket over HTTP | Browser-native, bidirectional, low latency |
| HTTP server | HttpListener | Built-in .NET, no dependencies |
| Input | SendInput (P/Invoke) | Standard user-mode input injection, no admin |
| Lock unlock | SendSAS + auto-type | One-time GPO setup, no admin at runtime |
| Credential storage | DPAPI ProtectedData | Built-in Windows encryption, tied to user account |
| DI | Microsoft.Extensions.DependencyInjection | Built-in .NET, lightweight |
| Logging | Microsoft.Extensions.Logging | Built-in, structured logging |
| AOT | NativeAOT (optional) | Smaller, faster startup, but limited reflection |

---

## Build & Run

```bash
# Build
cd src
dotnet build

# Run (development)
dotnet run -- --password mysecret --port 8080

# Publish (single-file, self-contained)
dotnet publish Udesk/Udesk.csproj -c Release -r win-x64 /p:PublishSingleFile=true --self-contained true -o ../../publish

# Run published
./publish/Udesk.exe --password mysecret --port 8080
```

---

## Risks & Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| SendSAS GPO not configured | Cannot auto-unlock | Detect and show clear error message with setup instructions |
| UAC dialog blocks input | Cannot click "Yes" on UAC | Document limitation, suggest running target apps unelevated |
| High DPI scaling | Capture coordinates mismatch | Use DPI-aware coordinates, test on 150%/200% scaling |
| Windows Defender blocks exe | Cannot run | Sign with self-signed cert or add exclusion |
| Multiple users on same machine | Session confusion | Only capture current user's session |

---

## Timeline

| Phase | Scope | Time |
|---|---|---|
| Phase 1 | Core: capture + input + web viewer | 2-3 days |
| Phase 2 | Lock screen + credentials | 1 day |
| Phase 3 | TLS + multi-monitor + clipboard | 1-2 days |
| **Total MVP** | **Working remote desktop** | **3-5 days** |
