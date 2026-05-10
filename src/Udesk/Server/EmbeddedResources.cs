namespace Udesk.Server;

/// <summary>
/// Provides the embedded HTML viewer page.
/// The viewer is a single-page HTML with Canvas 2D rendering, WebSocket communication,
/// and coordinate mapping. Compatible with Safari/Chrome on macOS.
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
body { background: #1a1a2e; color: #eee; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; display: flex; flex-direction: column; align-items: center; min-height: 100vh; }
#status { padding: 12px 20px; font-size: 14px; width: 100%; text-align: center; background: #16213e; border-bottom: 1px solid #0f3460; }
#status.connected { color: #4ecca3; }
#status.disconnected { color: #e94560; }
#pin-overlay { position: fixed; top: 0; left: 0; width: 100%; height: 100%; background: rgba(0,0,0,0.85); display: flex; align-items: center; justify-content: center; z-index: 100; }
#pin-box { background: #16213e; padding: 30px 40px; border-radius: 12px; text-align: center; }
#pin-box h2 { margin-bottom: 16px; font-weight: 500; }
#pin-input { padding: 10px 16px; font-size: 18px; border: 2px solid #0f3460; border-radius: 8px; background: #1a1a2e; color: #eee; width: 200px; text-align: center; letter-spacing: 6px; }
#pin-input:focus { outline: none; border-color: #4ecca3; }
#pin-submit { margin-top: 16px; padding: 10px 30px; font-size: 16px; background: #0f3460; color: #eee; border: none; border-radius: 8px; cursor: pointer; }
#pin-submit:hover { background: #4ecca3; }
#pin-error { color: #e94560; margin-top: 10px; font-size: 13px; display: none; }
canvas { max-width: 100vw; max-height: calc(100vh - 50px); cursor: crosshair; image-rendering: auto; display: block; }
</style>
</head>
<body>
<div id="status" class="disconnected">Connecting...</div>
<div id="pin-overlay" style="display:none">
  <div id="pin-box">
    <h2>🔒 Enter PIN</h2>
    <input type="password" id="pin-input" maxlength="8" autocomplete="off" autofocus>
    <br>
    <button id="pin-submit">Connect</button>
    <div id="pin-error">Invalid PIN. Try again.</div>
  </div>
</div>
<canvas id="screen"></canvas>
<script>
(function() {
  const canvas = document.getElementById('screen');
  const ctx = canvas.getContext('2d');
  const statusEl = document.getElementById('status');
  const pinOverlay = document.getElementById('pin-overlay');
  const pinInput = document.getElementById('pin-input');
  const pinSubmit = document.getElementById('pin-submit');
  const pinError = document.getElementById('pin-error');

  let ws = null;
  let needsPin = false;
  let lastPin = '';
  let scaleRatio = { x: 1, y: 1 };

  function connect() {
    const proto = location.protocol === 'https:' ? 'wss:' : 'ws:';
    ws = new WebSocket(proto + '//' + location.host + '/ws');
    ws.binaryType = 'arraybuffer';

    ws.onopen = () => {
      statusEl.textContent = 'Connected. Authenticating...';
      statusEl.className = 'connected';
      // Send auth (with or without PIN)
      const pin = lastPin || pinInput.value || '';
      ws.send(JSON.stringify({ type: 'auth', pin: pin }));
    };

    ws.onmessage = (e) => {
      if (e.data instanceof ArrayBuffer) {
        // Binary = JPEG frame
        const blob = new Blob([e.data], { type: 'image/jpeg' });
        const url = URL.createObjectURL(blob);
        const img = new Image();
        img.onload = () => {
          canvas.width = img.width;
          canvas.height = img.height;
          ctx.drawImage(img, 0, 0);
          URL.revokeObjectURL(url);
        };
        img.src = url;
      } else {
        // Text = JSON message
        const msg = JSON.parse(e.data);
        handleMessage(msg);
      }
    };

    ws.onclose = () => {
      statusEl.textContent = 'Disconnected. Reconnecting...';
      statusEl.className = 'disconnected';
      if (needsPin || !lastPin) {
        pinOverlay.style.display = 'flex';
      }
      setTimeout(connect, 3000);
    };

    ws.onerror = () => {};
  }

  function handleMessage(msg) {
    switch (msg.type) {
      case 'welcome':
        statusEl.textContent = 'Connected — ' + msg.capture_width + 'x' + msg.capture_height;
        pinOverlay.style.display = 'none';
        lastPin = pinInput.value;
        needsPin = false;
        break;
      case 'auth_failed':
        needsPin = true;
        pinError.style.display = 'block';
        pinInput.value = '';
        pinInput.focus();
        if (ws) { ws.close(); ws = null; }
        break;
      case 'status':
        statusEl.textContent = 'Connected — ' + (msg.viewer_count || 1) + ' viewer(s)';
        break;
    }
  }

  // Mouse events
  canvas.addEventListener('mousedown', (e) => {
    e.preventDefault();
    const pos = canvasCoords(e);
    sendMouse(pos.x, pos.y, buttonName(e), 'down');
  });

  canvas.addEventListener('mouseup', (e) => {
    e.preventDefault();
    const pos = canvasCoords(e);
    sendMouse(pos.x, pos.y, buttonName(e), 'up');
  });

  canvas.addEventListener('mousemove', (e) => {
    const pos = canvasCoords(e);
    sendMouse(pos.x, pos.y, 'left', 'move');
  });

  canvas.addEventListener('wheel', (e) => {
    e.preventDefault();
    const pos = canvasCoords(e);
    send({ type: 'mouse', x: pos.x, y: pos.y, button: 'left', action: 'scroll', delta: -e.deltaY });
  }, { passive: false });

  canvas.addEventListener('contextmenu', (e) => e.preventDefault());

  // Touch events (basic single-touch support)
  canvas.addEventListener('touchstart', (e) => {
    e.preventDefault();
    const t = e.touches[0];
    const pos = canvasCoords(t);
    sendMouse(pos.x, pos.y, 'left', 'down');
  }, { passive: false });

  canvas.addEventListener('touchend', (e) => {
    e.preventDefault();
    const t = e.changedTouches[0];
    const pos = canvasCoords(t);
    sendMouse(pos.x, pos.y, 'left', 'up');
  }, { passive: false });

  // Keyboard events
  document.addEventListener('keydown', (e) => {
    if (e.target === pinInput) return;
    e.preventDefault();
    send({ type: 'keyboard', keyCode: e.keyCode, action: 'down' });
  });

  document.addEventListener('keyup', (e) => {
    if (e.target === pinInput) return;
    e.preventDefault();
    send({ type: 'keyboard', keyCode: e.keyCode, action: 'up' });
  });

  function canvasCoords(e) {
    const rect = canvas.getBoundingClientRect();
    return {
      x: Math.round(e.clientX - rect.left),
      y: Math.round(e.clientY - rect.top)
    };
  }

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
  pinSubmit.addEventListener('click', () => { connect(); });
  pinInput.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') { connect(); }
  });

  // Auto-connect; if server requires PIN, overlay will show after auth_failed
  connect();
})();
</script>
</body>
</html>
""";
        return System.Text.Encoding.UTF8.GetBytes(html);
    }
}
