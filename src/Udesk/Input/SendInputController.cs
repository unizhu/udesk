using System.Runtime.InteropServices;
using System.Text;
using Udesk.Interop;

namespace Udesk.Input;

/// <summary>
/// Input controller using Win32 SendInput for mouse and keyboard simulation.
/// Uses MOUSEEVENTF_ABSOLUTE for reliable mouse positioning.
/// Runs in user mode — no admin privileges required.
/// </summary>
public sealed class SendInputController : IInputController
{
    private readonly ILogger<SendInputController> _logger;

    // Cache screen dimensions for coordinate normalization
    private int _screenWidth;
    private int _screenHeight;
    private DateTime _lastScreenCheck;

    public SendInputController(ILogger<SendInputController> logger)
    {
        _logger = logger;
        RefreshScreenDimensions();
    }

    /// <inheritdoc />
    public void MouseMove(int x, int y)
    {
        // Use absolute positioning via SendInput for consistency
        var (normX, normY) = Normalize(x, y);
        var input = new INPUT
        {
            type = InputType.MOUSE,
            Data = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = normX,
                    dy = normY,
                    mouseData = 0,
                    dwFlags = MouseFlags.MOVE | MouseFlags.ABSOLUTE,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                }
            }
        };

        SendSingleInput(input);
    }

    /// <inheritdoc />
    public void MouseClick(int x, int y, string button, string action)
    {
        // Move to position first
        MouseMove(x, y);

        var flags = button.ToLowerInvariant() switch
        {
            "left" => action == "down" ? MouseFlags.LEFTDOWN : MouseFlags.LEFTUP,
            "right" => action == "down" ? MouseFlags.RIGHTDOWN : MouseFlags.RIGHTUP,
            "middle" => action == "down" ? MouseFlags.MIDDLEDOWN : MouseFlags.MIDDLEUP,
            _ => action == "down" ? MouseFlags.LEFTDOWN : MouseFlags.LEFTUP,
        };

        var input = new INPUT
        {
            type = InputType.MOUSE,
            Data = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = 0,
                    dy = 0,
                    mouseData = 0,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                }
            }
        };

        SendSingleInput(input);
    }

    /// <inheritdoc />
    public void MouseScroll(int delta)
    {
        var input = new INPUT
        {
            type = InputType.MOUSE,
            Data = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = 0,
                    dy = 0,
                    mouseData = (uint)delta,
                    dwFlags = MouseFlags.WHEEL,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                }
            }
        };

        SendSingleInput(input);
    }

    /// <inheritdoc />
    public void KeyDown(int keyCode)
    {
        var vk = VirtualKeyCodes.FromKeyCode(keyCode);
        var input = CreateKeyboardInput(vk, KeyboardFlags.KEYDOWN);
        SendSingleInput(input);
    }

    /// <inheritdoc />
    public void KeyUp(int keyCode)
    {
        var vk = VirtualKeyCodes.FromKeyCode(keyCode);
        var input = CreateKeyboardInput(vk, KeyboardFlags.KEYUP);
        SendSingleInput(input);
    }

    /// <inheritdoc />
    public void TypeText(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        foreach (var ch in text)
        {
            if (ch is >= ' ' and <= '~')
            {
                TypeAsciiCharacter(ch);
            }
            else
            {
                _logger.LogDebug("Skipping unsupported character: U+{Code:X4}", (int)ch);
            }
        }
    }

    private void TypeAsciiCharacter(char ch)
    {
        var upper = char.ToUpperInvariant(ch);
        var vk = (ushort)upper;

        if (IsShiftRequired(ch))
        {
            SendSingleInput(CreateKeyboardInput(VirtualKeyCodes.VK_SHIFT, KeyboardFlags.KEYDOWN));
        }

        SendSingleInput(CreateKeyboardInput(vk, KeyboardFlags.KEYDOWN));
        Thread.Sleep(10);
        SendSingleInput(CreateKeyboardInput(vk, KeyboardFlags.KEYUP));

        if (IsShiftRequired(ch))
        {
            SendSingleInput(CreateKeyboardInput(VirtualKeyCodes.VK_SHIFT, KeyboardFlags.KEYUP));
        }
    }

    private static bool IsShiftRequired(char ch)
    {
        return ch is '~' or '!' or '@' or '#' or '$' or '%' or '^' or '&' or '*' or '('
            or ')' or '_' or '+' or '{' or '}' or '|' or ':' or '\"' or '<' or '>' or '?'
            || (ch is >= 'A' and <= 'Z');
    }

    private static INPUT CreateKeyboardInput(ushort vk, uint flags)
    {
        return new INPUT
        {
            type = InputType.KEYBOARD,
            Data = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                }
            }
        };
    }

    /// <summary>
    /// Normalizes screen pixel coordinates to absolute coordinates (0-65535).
    /// SendInput with MOUSEEVENTF_ABSOLUTE uses this range.
    /// </summary>
    private (int normX, int normY) Normalize(int x, int y)
    {
        RefreshScreenDimensions();

        // Per MSDN: dx = (x * 65535) / (screenWidth - 1)
        var normX = _screenWidth > 1 ? (int)((long)x * 65535 / (_screenWidth - 1)) : 0;
        var normY = _screenHeight > 1 ? (int)((long)y * 65535 / (_screenHeight - 1)) : 0;

        normX = Math.Clamp(normX, 0, 65535);
        normY = Math.Clamp(normY, 0, 65535);

        return (normX, normY);
    }

    /// <summary>
    /// Refreshes cached screen dimensions periodically (every 5 seconds).
    /// Avoids calling GetSystemMetrics on every mouse event.
    /// </summary>
    private void RefreshScreenDimensions()
    {
        if ((DateTime.UtcNow - _lastScreenCheck).TotalSeconds < 5) return;

        _screenWidth = NativeMethods.GetSystemMetrics(SystemMetrics.SM_CXSCREEN);
        _screenHeight = NativeMethods.GetSystemMetrics(SystemMetrics.SM_CYSCREEN);
        _lastScreenCheck = DateTime.UtcNow;
    }

    private void SendSingleInput(INPUT input)
    {
        var inputs = new INPUT[] { input };
        var result = NativeMethods.SendInput(1, inputs, Marshal.SizeOf<INPUT>());
        if (result == 0)
        {
            var error = Marshal.GetLastPInvokeError();
            _logger.LogWarning("SendInput failed with error {Error}", error);
        }
    }
}
