using System.Runtime.InteropServices;
using System.Text;
using Udesk.Interop;

namespace Udesk.Input;

/// <summary>
/// Input controller using Win32 SendInput for mouse and keyboard simulation.
/// Runs in user mode — no admin privileges required.
/// </summary>
public sealed class SendInputController : IInputController
{
    private readonly ILogger<SendInputController> _logger;

    public SendInputController(ILogger<SendInputController> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public void MouseMove(int x, int y)
    {
        if (!NativeMethods.SetCursorPos(x, y))
        {
            _logger.LogWarning("SetCursorPos failed for ({X}, {Y})", x, y);
        }
    }

    /// <inheritdoc />
    public void MouseClick(int x, int y, string button, string action)
    {
        // First move to position
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
        var needsShift = ch != upper; // lowercase letters need shift check via actual char

        // Check if this character requires Shift to be held
        if (IsShiftRequired(ch))
        {
            // Press Shift
            SendSingleInput(CreateKeyboardInput(VirtualKeyCodes.VK_SHIFT, KeyboardFlags.KEYDOWN));
        }

        // Press and release the key
        SendSingleInput(CreateKeyboardInput(vk, KeyboardFlags.KEYDOWN));
        Thread.Sleep(10); // Small delay between down and up
        SendSingleInput(CreateKeyboardInput(vk, KeyboardFlags.KEYUP));

        if (IsShiftRequired(ch))
        {
            // Release Shift
            SendSingleInput(CreateKeyboardInput(VirtualKeyCodes.VK_SHIFT, KeyboardFlags.KEYUP));
        }
    }

    private static bool IsShiftRequired(char ch)
    {
        // Characters that need Shift to type on a standard US keyboard
        return ch is '~' or '!' or '@' or '#' or '$' or '%' or '^' or '&' or '*' or '('
            or ')' or '_' or '+' or '{' or '}' or '|' or ':' or '"' or '<' or '>' or '?'
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
