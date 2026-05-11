namespace Udesk.Server;

/// <summary>
/// Provides the embedded HTML viewer page.
/// Windows-style PIN login, Canvas 2D rendering, WebSocket communication,
/// and proper coordinate mapping for mouse events.
/// Compatible with Safari/Chrome on macOS.
/// </summary>
internal static class EmbeddedResources
{
    private static readonly Lazy<byte[]> ViewerHtml = new(GenerateViewerHtml);

    public static byte[] GetViewerHtml() => ViewerHtml.Value;

    private static byte[] GenerateViewerHtml()
    {
        var html = """
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>Udesk Remote Desktop</title>
<style>
* { margin: 0; padding: 0; box-sizing: border-box; }
body { background: #000; color: #fff; font-family: 'Segoe UI', -apple-system, BlinkMacSystemFont, sans-serif; display: flex; flex-direction: column; align-items: center; min-height: 100vh; overflow: hidden; }
#status { padding: 8px 20px; font-size: 13px; width: 100%; text-align: center; background: #1a1a1a; border-bottom: 1px solid #333; z-index: 10; }
#status.connected { color: #4cc2ff; }
#status.disconnected { color: #ff605c; }

/* Windows-style PIN overlay */
#pin-overlay { position: fixed; top: 0; left: 0; width: 100%; height: 100%; background: #000; display: flex; align-items: center; justify-content: center; z-index: 100; flex-direction: column; }
#pin-wallpaper { position: absolute; top: 0; left: 0; width: 100%; height: 100%; background: linear-gradient(135deg, #1a1a2e 0%, #16213e 40%, #0f3460 100%); filter: blur(0px); }
#pin-content { position: relative; z-index: 1; text-align: center; }
#pin-avatar { width: 120px; height: 120px; border-radius: 50%; background: #0078d4; margin: 0 auto 16px; display: flex; align-items: center; justify-content: center; }
#pin-avatar svg { width: 64px; height: 64px; fill: #fff; }
#pin-username { font-size: 24px; font-weight: 300; margin-bottom: 24px; color: #fff; }
#pin-field { position: relative; display: inline-block; }
#pin-input { padding: 12px 44px 12px 16px; font-size: 18px; border: 2px solid #555; border-radius: 4px; background: rgba(255,255,255,0.05); color: #fff; width: 260px; text-align: center; letter-spacing: 8px; outline: none; transition: border-color 0.2s; }
#pin-input:focus { border-color: #0078d4; }
#pin-input::placeholder { color: #666; letter-spacing: normal; font-size: 14px; }
#pin-submit-icon { position: absolute; right: 8px; top: 50%; transform: translateY(-50%); background: #0078d4; border: none; border-radius: 50%; width: 32px; height: 32px; cursor: pointer; display: flex; align-items: center; justify-content: center; transition: background 0.2s; }
#pin-submit-icon:hover { background: #1a8ae6; }
#pin-submit-icon svg { width: 18px; height: 18px; fill: #fff; }
#pin-error { color: #ff605c; margin-top: 12px; font-size: 14px; display: none; animation: shake 0.4s; }
#pin-hint { color: #888; margin-top: 20px; font-size: 13px; }
@keyframes shake { 0%,100%{transform:translateX(0)} 25%{transform:translateX(-8px)} 75%{transform:translateX(8px)} }

canvas { max-width: 100vw; max-height: calc(100vh - 40px); cursor: default; image-rendering: auto; display: block; }

/* Lock screen overlay - also Windows style */
#lock-overlay { position: fixed; top: 0; left: 0; width: 100%; height: 100%; background: rgba(0,0,0,0.85); display: none; align-items: center; justify-content: center; z-index: 90; flex-direction: column; }
#lock-box { text-align: center; }
#lock-box h2 { margin-bottom: 12px; font-size: 24px; font-weight: 300; }
#lock-box p { color: #aaa; margin-bottom: 16px; font-size: 14px; }
.lock-btn { margin: 6px; padding: 10px 28px; font-size: 14px; border: none; border-radius: 4px; cursor: pointer; transition: background 0.2s; }
#btn-unlock { background: #0078d4; color: #fff; }
#btn-unlock:hover { background: #1a8ae6; }
#btn-set-cred { background: transparent; color: #aaa; border: 1px solid #555; }
#btn-set-cred:hover { background: rgba(255,255,255,0.05); }
#cred-section { margin-top: 16px; display: none; }
#cred-input { padding: 10px 16px; font-size: 16px; border: 2px solid #555; border-radius: 4px; background: rgba(255,255,255,0.05); color: #fff; width: 220px; text-align: center; letter-spacing: 4px; }
#cred-input:focus { outline: none; border-color: #0078d4; }
#unlock-error { color: #ff605c; margin-top: 10px; font-size: 13px; display: none; }

#monitor-bar { display:none; background:#1a1a1a; padding:4px 8px; text-align:center; font-size:13px; }
#monitor-select { background:#111; color:#fff; border:1px solid #444; border-radius:4px; padding:2px 8px; font-size:13px; }
</style>
</head>
<body>
<div id="status" class="disconnected">Connecting...</div>
<div id="monitor-bar">Monitor: <select id="monitor-select"></select></div>
<div id="pin-overlay" style="display:none">
  <div id="pin-wallpaper"></div>
  <div id="pin-content">
    <div id="pin-avatar">
      <svg viewBox="0 0 24 24"><path d="M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z"/></svg>
    </div>
    <div id="pin-username">Remote Desktop</div>
    <div id="pin-field">
      <input type="password" id="pin-input" maxlength="8" placeholder="PIN" autocomplete="off" autofocus>
      <button id="pin-submit-icon" title="Connect">
        <svg viewBox="0 0 24 24"><path d="M12 4l-1.41 1.41L16.17 11H4v2h12.17l-5.58 5.59L12 20l8-8z"/></svg>
      </button>
    </div>
    <div id="pin-error">The PIN is incorrect. Try again.</div>
    <div id="pin-hint"></div>
  </div>
</div>
<canvas id="screen"></canvas>
<div id="lock-overlay">
  <div id="lock-box">
    <h2>🔒 Locked</h2>
    <p>The remote Windows session is locked.</p>
    <div id="cred-section">
      <p>Enter your Windows PIN/password:</p>
      <input type="password" id="cred-input" maxlength="32" placeholder="Windows PIN">
    </div>
    <div>
      <button class="lock-btn" id="btn-unlock">Unlock</button>
      <button class="lock-btn" id="btn-set-cred">Set Credential</button>
    </div>
    <div id="unlock-error"></div>
  </div>
</div>
<script>
(function() {
  const canvas = document.getElementById('screen');
  const ctx = canvas.getContext('2d');
  const statusEl = document.getElementById('status');
  const pinOverlay = document.getElementById('pin-overlay');
  const pinInput = document.getElementById('pin-input');
  const pinSubmitIcon = document.getElementById('pin-submit-icon');
  const pinError = document.getElementById('pin-error');
  const pinHint = document.getElementById('pin-hint');

  let ws = null;
  let needsPin = false;
  let savedPin = '';
  let screenW = 0, screenH = 0;  // native screen resolution
  let captureW = 0, captureH = 0; // capture resolution (scaled)
  let frameCount = 0;  // debug: count received frames

  function connect() {
    const proto = location.protocol === 'https:' ? 'wss:' : 'ws:';
    ws = new WebSocket(proto + '//' + location.host + '/ws');
    ws.binaryType = 'arraybuffer';

    ws.onopen = () => {
      statusEl.textContent = 'Connected. Authenticating...';
      statusEl.className = 'connected';
      // Send auth with saved PIN or current input
      const pin = savedPin || pinInput.value || '';
      ws.send(JSON.stringify({ type: 'auth', pin: pin }));
    };

    ws.onmessage = (e) => {
      if (e.data instanceof ArrayBuffer) {
        // Binary = JPEG frame
        frameCount++;
        if (frameCount <= 3 || frameCount % 100 === 0) {
          console.log('Frame #' + frameCount + ': ' + e.data.byteLength + ' bytes');
        }
        const blob = new Blob([e.data], { type: 'image/jpeg' });
        createImageBitmap(blob).then(bmp => {
          // Set canvas size to capture resolution only when it changes
          if (canvas.width !== bmp.width || canvas.height !== bmp.height) {
            canvas.width = bmp.width;
            canvas.height = bmp.height;
          }
          ctx.drawImage(bmp, 0, 0);
          bmp.close();
          statusEl.textContent = 'Connected — ' + canvas.width + 'x' + canvas.height + ' #' + frameCount;
        }).catch(err => {
          // Fallback: use Image + Blob URL
          console.warn('createImageBitmap failed, using fallback:', err);
          const url = URL.createObjectURL(blob);
          const img = new Image();
          img.onload = () => {
            if (canvas.width !== img.width || canvas.height !== img.height) {
              canvas.width = img.width;
              canvas.height = img.height;
            }
            ctx.drawImage(img, 0, 0);
            URL.revokeObjectURL(url);
            statusEl.textContent = 'Connected — ' + canvas.width + 'x' + canvas.height + ' #' + frameCount;
          };
          img.src = url;
        });
      } else {
        const msg = JSON.parse(e.data);
        handleMessage(msg);
      }
    };

    ws.onclose = () => {
      statusEl.textContent = 'Disconnected. Reconnecting...';
      statusEl.className = 'disconnected';
      if (needsPin) {
        // DON'T auto-reconnect — wait for user to enter PIN and submit
        pinOverlay.style.display = 'flex';
        pinError.style.display = 'none';
        pinInput.focus();
      } else {
        // No PIN required, auto-reconnect after 3 seconds
        setTimeout(connect, 3000);
      }
    };

    ws.onerror = () => {};
  }

  function handleMessage(msg) {
    switch (msg.type) {
      case 'welcome':
        statusEl.textContent = 'Connected — ' + msg.captureWidth + 'x' + msg.captureHeight;
        pinOverlay.style.display = 'none';
        needsPin = false;
        // Save screen dimensions for coordinate mapping
        screenW = msg.screenWidth;
        screenH = msg.screenHeight;
        captureW = msg.captureWidth;
        captureH = msg.captureHeight;
        updateMonitorSelector(msg.monitors, msg.activeMonitorIndex);
        break;
      case 'auth_failed':
        needsPin = true;
        savedPin = '';  // Clear saved PIN since it was wrong
        pinError.style.display = 'block';
        // DON'T clear pinInput.value — let user see and fix what they typed
        pinInput.value = '';
        pinInput.focus();
        if (ws) { ws.close(); ws = null; }
        break;
      case 'status':
        statusEl.textContent = 'Connected — ' + (msg.viewerCount || 1) + ' viewer(s)';
        break;
      case 'lock_state':
        document.getElementById('lock-overlay').style.display = msg.locked ? 'flex' : 'none';
        if (!msg.locked) {
          document.getElementById('cred-section').style.display = 'none';
          document.getElementById('unlock-error').style.display = 'none';
        }
        break;
      case 'unlock_result':
        if (!msg.success) {
          const errEl = document.getElementById('unlock-error');
          errEl.textContent = msg.error || 'Unlock failed';
          errEl.style.display = 'block';
          if (!msg.hasCredential) showCredInput();
        }
        break;
      case 'monitor_changed':
        statusEl.textContent = 'Connected — ' + msg.captureWidth + 'x' + msg.captureHeight + ' (Monitor ' + (msg.activeMonitorIndex + 1) + ')';
        screenW = msg.screenWidth;
        screenH = msg.screenHeight;
        captureW = msg.captureWidth;
        captureH = msg.captureHeight;
        updateMonitorSelector(null, msg.activeMonitorIndex);
        break;
      case 'clipboard':
        if (navigator.clipboard && navigator.clipboard.writeText) {
          navigator.clipboard.writeText(msg.text).catch(() => {});
        }
        break;
    }
  }

  /**
   * Maps canvas display coordinates to screen coordinates.
   * canvas.getBoundingClientRect() = CSS display size (may be scaled)
   * canvas.width/height = capture resolution
   * screenW/screenH = native screen resolution
   */
  function screenCoords(e) {
    const rect = canvas.getBoundingClientRect();
    const displayX = e.clientX - rect.left;
    const displayY = e.clientY - rect.top;
    // Display → Capture space
    const captureX = displayX * (canvas.width / rect.width);
    const captureY = displayY * (canvas.height / rect.height);
    // Capture → Screen space
    const sx = Math.round(captureX * (screenW / captureW));
    const sy = Math.round(captureY * (screenH / captureH));
    return { x: sx, y: sy };
  }

  // Mouse events
  canvas.addEventListener('mousedown', (e) => {
    e.preventDefault();
    const pos = screenCoords(e);
    sendMouse(pos.x, pos.y, buttonName(e), 'down');
  });

  canvas.addEventListener('mouseup', (e) => {
    e.preventDefault();
    const pos = screenCoords(e);
    sendMouse(pos.x, pos.y, buttonName(e), 'up');
  });

  canvas.addEventListener('mousemove', (e) => {
    const pos = screenCoords(e);
    sendMouse(pos.x, pos.y, 'left', 'move');
  });

  canvas.addEventListener('wheel', (e) => {
    e.preventDefault();
    const pos = screenCoords(e);
    send({ type: 'mouse', x: pos.x, y: pos.y, button: 'left', action: 'scroll', delta: -e.deltaY });
  }, { passive: false });

  canvas.addEventListener('contextmenu', (e) => e.preventDefault());

  // Touch events
  canvas.addEventListener('touchstart', (e) => {
    e.preventDefault();
    const t = e.touches[0];
    const pos = screenCoords(t);
    sendMouse(pos.x, pos.y, 'left', 'down');
  }, { passive: false });

  canvas.addEventListener('touchend', (e) => {
    e.preventDefault();
    const t = e.changedTouches[0];
    const pos = screenCoords(t);
    sendMouse(pos.x, pos.y, 'left', 'up');
  }, { passive: false });

  // Keyboard events
  document.addEventListener('keydown', (e) => {
    if (e.target === pinInput || e.target === document.getElementById('cred-input')) return;
    if (e.ctrlKey && e.key === 'v') {
      if (navigator.clipboard && navigator.clipboard.readText) {
        navigator.clipboard.readText().then(text => {
          if (text) send({ type: 'clipboard', text: text });
        }).catch(() => {});
      }
    }
    e.preventDefault();
    send({ type: 'keyboard', keyCode: e.keyCode, action: 'down' });
  });

  document.addEventListener('keyup', (e) => {
    if (e.target === pinInput || e.target === document.getElementById('cred-input')) return;
    e.preventDefault();
    send({ type: 'keyboard', keyCode: e.keyCode, action: 'up' });
  });

  function buttonName(e) {
    if (e.button === 0) return 'left';
    if (e.button === 1) return 'middle';
    if (e.button === 2) return 'right';
    return 'left';
  }

  function sendMouse(x, y, button, action) {
    send({ type: 'mouse', x, y, button, action, delta: 0 });
  }

  function send(obj) {
    if (ws && ws.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify(obj));
    }
  }

  // PIN submit
  pinSubmitIcon.addEventListener('click', () => {
    savedPin = pinInput.value;
    connect();
  });
  pinInput.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') {
      savedPin = pinInput.value;
      connect();
    }
  });

  // Lock screen controls
  document.getElementById('btn-unlock').addEventListener('click', () => {
    send({ type: 'unlock' });
    document.getElementById('unlock-error').style.display = 'none';
  });

  document.getElementById('btn-set-cred').addEventListener('click', () => {
    showCredInput();
  });

  document.getElementById('cred-input').addEventListener('keydown', (e) => {
    if (e.key === 'Enter') {
      const cred = document.getElementById('cred-input').value;
      if (cred) {
        send({ type: 'store_credential', credential: cred });
        document.getElementById('cred-section').style.display = 'none';
        document.getElementById('cred-input').value = '';
        setTimeout(() => send({ type: 'unlock' }), 300);
      }
    }
  });

  function showCredInput() {
    document.getElementById('cred-section').style.display = 'block';
    document.getElementById('cred-input').focus();
  }

  function updateMonitorSelector(monitors, activeIndex) {
    const select = document.getElementById('monitor-select');
    const bar = document.getElementById('monitor-bar');
    if (monitors && monitors.length > 1) {
      select.innerHTML = '';
      monitors.forEach((m, i) => {
        const opt = document.createElement('option');
        opt.value = i;
        opt.textContent = (i + 1) + '. ' + m.name + ' (' + m.width + 'x' + m.height + (m.isPrimary ? ' \u2605' : '') + ')';
        if (i === activeIndex) opt.selected = true;
        select.appendChild(opt);
      });
      bar.style.display = 'block';
    } else if (monitors && monitors.length <= 1) {
      bar.style.display = 'none';
    }
    if (activeIndex !== undefined) select.value = activeIndex;
  }

  document.getElementById('monitor-select').addEventListener('change', (e) => {
    send({ type: 'switch_monitor', monitorIndex: parseInt(e.target.value) });
  });

  // Auto-connect
  connect();
})();
</script>
</body>
</html>
""";
        return System.Text.Encoding.UTF8.GetBytes(html);
    }
}
