# udesk — Implementation Plan

## Overview

**udesk** is a lightweight Windows remote desktop tool. Single exe, no admin, no drivers.
Viewer runs in **any browser** (macOS Safari, Chrome, Firefox, mobile) — zero install.

- **Language**: C# 12 / .NET 8
- **Target**: Windows 10/11 amd64 (host), any modern browser (viewer)
- **Dependencies**: Zero external NuGet packages
- **Output**: Single self-contained exe (~15MB)

## User Experience Flow

```
1. Windows user runs: udesk.exe --pin 123456
   (or no --pin for open access on LAN)

2. macOS user opens Safari/Chrome:
   http://192.168.1.100:8080

3. If PIN is set:
   - Browser shows PIN entry dialog
   - User enters PIN -> validated -> screen streaming starts

4. If no PIN:
   - Screen streaming starts immediately

5. User sees Windows desktop, moves mouse, types, clicks
   - All input sent over WebSocket
   - Screen updates as JPEG frames at 5-10 FPS

6. Multiple devices can connect simultaneously (optional)
```

---

## Architecture

```
Browser (macOS Safari/Chrome, iOS, Android)
   |  WebSocket (ws://host:port/ws)
   v
udesk.exe (Windows, no admin)
   |-- Capture Layer (GDI CopyFromScreen -> resize -> JPEG)
   |-- Input Layer (SendInput: mouse + keyboard)
   |-- Lock Screen (SetThreadExecutionState + SendSAS + auto-type)
   |-- Web Server (HttpListener + WebSocket)
   +-- Security (optional PIN + DPAPI credential store)
```

---

## Phase 1 — Core Screen + Input + Viewer (MVP)

**Goal**: See the remote screen from macOS browser, move mouse, click, type.

### 1.1 Project Scaffold

- [ ] Create solution: `udesk.sln` with single project `src/Udesk/Udesk.csproj`
- [ ] `.csproj` config: `net8.0-windows`, AOT-ready, single-file publish, analyzers on
- [ ] `.editorconfig` for formatting rules (already created)
- [ ] `.gitignore` for .NET (already created)
- [ ] README.md with build instructions

### 1.2 P/Invoke Interop Layer (`Udesk.Interop`)

- [ ] `NativeMethods.cs` — LibraryImport source-generated P/Invoke:
  - `user32.dll`: `SendInput`, `SetCursorPos`, `GetCursorPos`, `mouse_event`, `GetSystemMetrics`
  - `kernel32.dll`: `SetThreadExecutionState`
  - `sas.dll`: `SendSAS`
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

### 1.4 Input Controller (`Udesk.Input`)

- [ ] `IInputController.cs` — interface: `MouseMove(x, y)`, `MouseClick(x, y, button, action)`, `MouseScroll(delta)`, `KeyDown(key)`, `KeyUp(key)`, `TypeText(text)`
- [ ] `SendInputController.cs` — implementation:
  - Mouse: `SetCursorPos` + construct `INPUT` with `MOUSEINPUT` + `SendInput`
  - Keyboard: construct `INPUT` with `KEYBDINPUT` (VK codes) + `SendInput`
  - Text: convert characters to VK codes + shift state, send key-down/key-up pairs
- [ ] `VirtualKeyCodes.cs` — VK_* constants mapping (from WinUser.h)

### 1.5 Web Server (`Udesk.Server`)

- [ ] `IWebServer.cs` — interface: `StartAsync(CancellationToken)`, `StopAsync()`
- [ ] `HttpListenerServer.cs` — implementation:
  - Serves embedded HTML viewer page on `GET /` (no files on disk, all in-memory)
  - Accepts WebSocket on `GET /ws`
  - WebSocket connection lifecycle:
    ```
    1. Client connects via ws://host:port/ws
    2. Server checks if PIN is configured:
       - No PIN: send {"type":"connected"}, start streaming immediately
       - PIN set: send {"type":"auth_required"}, wait for client response
    3. Client sends {"type":"auth","pin":"123456"}
    4. Server validates PIN:
       - OK: send {"type":"connected"}, start streaming
       - Wrong: send {"type":"auth_failed"}, close after 3 failures
    5. Streaming: server sends JPEG binary frames, receives input JSON
    6. Disconnect: clean up resources
    ```
  - WebSocket message protocol:
    ```
    Client -> Host (control):
      { "type": "auth", "pin": "123456" }
      { "type": "ping" }

    Client -> Host (input, after auth):
      { "type": "mouse_move", "x": 100, "y": 200 }
      { "type": "mouse_click", "x": 100, "y": 200, "button": "left", "action": "down" }
      { "type": "mouse_click", "x": 100, "y": 200, "button": "left", "action": "up" }
      { "type": "mouse_scroll", "delta": 120 }
      { "type": "key_down", "key": 65 }
      { "type": "key_up", "key": 65 }
      { "type": "type_text", "text": "hello" }

    Host -> Client:
      Text frame: { "type": "auth_required" }
      Text frame: { "type": "connected", "width": 960, "height": 540 }
      Text frame: { "type": "auth_failed", "attempts_left": 2 }
      Text frame: { "type": "status", "screen": "locked" }
      Binary frame: JPEG bytes (starts with 0xFF 0xD8)
    ```

- [ ] `ViewerHtml.cs` — embedded HTML/JS/CSS as a const string:
  - **Works on macOS Safari, Chrome, Firefox** — no plugins, no install, no app
  - Responsive layout: scales to any screen size (MacBook, iPad, phone)
  - **PIN entry dialog**: centered overlay, numeric input, Enter to submit
  - **Canvas element** for JPEG display (auto-scaled with CSS `object-fit: contain`)
  - Mouse event listeners (move, down, up, wheel) -> JSON -> WebSocket send
    - Coordinate mapping: browser canvas coords -> remote desktop coords (scale ratio)
  - Keyboard event listeners (keydown, keyup) -> JSON -> WebSocket send
    - Prevents default browser behavior for special keys (Backspace, Tab, F5, etc.)
  - JPEG binary frame receive:
    ```js
    ws.onmessage = (event) => {
      if (event.data instanceof Blob) {
        const url = URL.createObjectURL(event.data);
        img.src = url;
        img.onload = () => {
          ctx.drawImage(img, 0, 0);
          URL.revokeObjectURL(url);
        };
      } else {
        handleMessage(JSON.parse(event.data));
      }
    };
    ```
  - Safari compatibility: use `Blob` + `URL.createObjectURL` (Safari handles this well)
  - Connection status indicator (green/red dot)
  - Fullscreen toggle button (Fullscreen API)
  - Touch support for mobile (touchstart/touchmove/touchend -> mouse events)
  - Viewport meta tag: `<meta name="viewport" content="width=device-width, initial-scale=1">`

### 1.6 Security (`Udesk.Security`)

- [ ] `ConnectionAuth.cs` — PIN code verification:
  - PIN is a short numeric code (4-8 digits, e.g. "123456")
  - **Optional**: if `--pin` not provided at startup, no auth required (open LAN access)
  - Stored in memory only (never persisted to disk for connection PIN)
  - Constant-time comparison to prevent timing attacks
  - Max 3 failed attempts, then disconnect that client
- [ ] `SessionManager.cs` — track connected clients:
  - One WebSocket = one session
  - Each session has state: `AwaitingAuth` -> `Connected` -> `Disconnected`
  - Optional: limit concurrent sessions (default: 1)

### 1.7 Entry Point (`Program.cs`)

- [ ] Parse CLI args:
  - `--pin` (optional, 4-8 digit code for browser clients to enter)
  - `--port` (default 8080)
  - `--fps` (default 5)
  - `--quality` (JPEG quality, default 40)
- [ ] Build DI container (ServiceCollection)
- [ ] Wire: Capture -> Server (frame streaming), Server (input) -> InputController
- [ ] Start capture thread + web server
- [ ] Print access URL to console: `udesk running at http://192.168.1.100:8080`
- [ ] Graceful shutdown on Ctrl+C

### 1.8 Testing

- [ ] Unit test: JPEG encoding produces valid output
- [ ] Unit test: VirtualKeyCodes mapping completeness
- [ ] Unit test: input JSON message parsing
- [ ] Unit test: PIN auth logic (correct PIN, wrong PIN, max attempts)
- [ ] Manual test: run on Windows, open macOS Safari, see screen, click

**Estimated time**: 2-3 days

---

## Phase 2 — Lock Screen Handling

**Goal**: Prevent screen timeout, and when locked, unlock remotely from browser.

### 2.1 Prevent Sleep

- [ ] `SleepPreventer.cs` — calls `SetThreadExecutionState(ES_CONTINUOUS | ES_DISPLAY_REQUIRED | ES_SYSTEM_REQUIRED)` in a background loop every 60 seconds
- [ ] Restores previous state on shutdown

### 2.2 Lock Screen Detection

- [ ] `LockScreenDetector.cs` — detects if session is locked:
  - P/Invoke `OpenInputDesktop()` — if fails, session is locked
  - Or WMI query `Win32_LogonSession` logon type comparison
- [ ] Sends `{ "type": "status", "screen": "locked" }` to all connected browsers

### 2.3 Unlock Handler

- [ ] `ILockScreenHandler.cs` — interface: `UnlockAsync()`, `SetCredentials(pin)`
- [ ] `SasUnlockHandler.cs`:
  1. Call `SendSAS(TRUE)` — simulates Ctrl+Alt+Del
  2. Wait 500ms for unlock screen to appear
  3. Resume capture (to see the PIN input field)
  4. Type stored Windows login PIN via `SendInput`
  5. Press Enter
  6. Wait for desktop to appear, resume normal streaming

### 2.4 Credential Storage

- [ ] `CredentialStore.cs`:
  - Encrypt Windows login PIN/password with `System.Security.Cryptography.ProtectedData` (DPAPI)
  - Store in `~/.udesk/credentials.dat` (CurrentUser scope)
  - Only decryptable by same Windows user on same machine

### 2.5 Viewer UI Update

- [ ] Show "Screen Locked" overlay with "Unlock" button
- [ ] First time: prompt for Windows login PIN/password, store encrypted via DPAPI
- [ ] Subsequent: click "Unlock" -> auto-unlock sequence (SendSAS + auto-type PIN)
- [ ] Note: This is the *Windows login PIN*, separate from the *connection PIN* (--pin)

**Estimated time**: 1 day

---

## Phase 3 — Polish & Production

### 3.1 TLS Support

- [ ] Auto-generate self-signed certificate on first run
- [ ] Store in `~/.udesk/cert.pfx`
- [ ] Configure `HttpListener` with HTTPS binding
- [ ] Viewer redirects HTTP -> HTTPS
- [ ] macOS Safari: show "trust this certificate" instructions

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
|       |   +-- SessionManager.cs
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
|       +-- AuthTests.cs
+-- .editorconfig
+-- .gitignore
+-- publish.bat
```

---

## Key Technical Decisions

| Decision | Choice | Why |
|---|---|---|
| Language | C# 12 / .NET 8 | Best Win32 interop, zero-dep BCL, single-file publish |
| Screen capture | GDI CopyFromScreen | Simplest, zero dependencies, works without admin |
| Video codec | JPEG (no video codec) | Simplest, no external deps, low quality OK per requirement |
| Transport | WebSocket over HTTP | Browser-native on ALL devices, bidirectional, low latency |
| HTTP server | HttpListener | Built-in .NET, no dependencies, works on all Windows versions |
| Viewer | Embedded HTML/JS in exe | No files on disk, works on macOS Safari/Chrome/Firefox, zero install |
| Input | SendInput (P/Invoke) | Standard user-mode input injection, no admin |
| Lock unlock | SendSAS + auto-type | One-time GPO setup, no admin at runtime |
| Connection auth | Optional PIN (--pin flag) | Simple, no accounts, no database, memory-only |
| Credential storage | DPAPI ProtectedData | Built-in Windows encryption, tied to user account |
| DI | Microsoft.Extensions.DependencyInjection | Built-in .NET, lightweight |
| Logging | Microsoft.Extensions.Logging | Built-in, structured logging |

---

## Browser Compatibility (Viewer)

| Browser | Platform | Status | Notes |
|---|---|---|---|
| Safari 17+ | macOS, iOS | Primary target | Blob URL works, WebSocket stable |
| Chrome 120+ | macOS, Windows, Linux, Android | Primary target | Best WebSocket performance |
| Firefox 120+ | macOS, Windows, Linux | Supported | Standard API support |
| Edge 120+ | Windows | Supported | Chromium-based, same as Chrome |
| Safari iOS | iPhone, iPad | Supported | Touch events mapped to mouse |

All browsers support: WebSocket, Canvas 2D, Blob URL, Fullscreen API, Touch Events.

---

## Build & Run

```bash
# Build
cd src
dotnet build

# Run (development) — no PIN, open access
dotnet run -- --port 8080

# Run — with PIN protection
dotnet run -- --pin 123456 --port 8080

# Publish (single-file, self-contained)
dotnet publish Udesk/Udesk.csproj -c Release -r win-x64 \
  /p:PublishSingleFile=true --self-contained true -o ../../publish

# Run published
./publish/Udesk.exe --pin 123456 --port 8080
```

---

## Risks & Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| SendSAS GPO not configured | Cannot auto-unlock | Detect and show clear instructions in browser |
| UAC dialog blocks input | Cannot click "Yes" on UAC | Document limitation, suggest running apps unelevated |
| High DPI scaling | Capture coordinates mismatch | Use DPI-aware coordinates, test on 150%/200% |
| Windows Defender blocks exe | Cannot run | Sign with self-signed cert or add exclusion |
| Safari WebSocket limits | Connection drops | Implement auto-reconnect in viewer JS |
| Multiple users same machine | Session confusion | Only capture current user's session |

---

## Timeline

| Phase | Scope | Time |
|---|---|---|
| Phase 1 | Core: capture + input + browser viewer + PIN | 2-3 days |
| Phase 2 | Lock screen + Windows credential storage | 1 day |
| Phase 3 | TLS + multi-monitor + clipboard | 1-2 days |
| **Total MVP** | **Working remote desktop** | **3-5 days** |
