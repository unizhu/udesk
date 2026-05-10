namespace Udesk.Interop;

/// <summary>
/// Virtual key code constants from WinUser.h.
/// </summary>
internal static class VirtualKeyCodes
{
    public const ushort VK_LBUTTON = 0x01;
    public const ushort VK_RBUTTON = 0x02;
    public const ushort VK_CANCEL = 0x03;
    public const ushort VK_MBUTTON = 0x04;
    public const ushort VK_BACK = 0x08;
    public const ushort VK_TAB = 0x09;
    public const ushort VK_CLEAR = 0x0C;
    public const ushort VK_RETURN = 0x0D;
    public const ushort VK_SHIFT = 0x10;
    public const ushort VK_CONTROL = 0x11;
    public const ushort VK_MENU = 0x12;
    public const ushort VK_PAUSE = 0x13;
    public const ushort VK_CAPITAL = 0x14;
    public const ushort VK_ESCAPE = 0x1B;
    public const ushort VK_SPACE = 0x20;
    public const ushort VK_PRIOR = 0x21;
    public const ushort VK_NEXT = 0x22;
    public const ushort VK_END = 0x23;
    public const ushort VK_HOME = 0x24;
    public const ushort VK_LEFT = 0x25;
    public const ushort VK_UP = 0x26;
    public const ushort VK_RIGHT = 0x27;
    public const ushort VK_DOWN = 0x28;
    public const ushort VK_SNAPSHOT = 0x2C;
    public const ushort VK_INSERT = 0x2D;
    public const ushort VK_DELETE = 0x2E;
    public const ushort VK_LWIN = 0x5B;
    public const ushort VK_RWIN = 0x5C;
    public const ushort VK_NUMPAD0 = 0x60;
    public const ushort VK_NUMPAD1 = 0x61;
    public const ushort VK_NUMPAD2 = 0x62;
    public const ushort VK_NUMPAD3 = 0x63;
    public const ushort VK_NUMPAD4 = 0x64;
    public const ushort VK_NUMPAD5 = 0x65;
    public const ushort VK_NUMPAD6 = 0x66;
    public const ushort VK_NUMPAD7 = 0x67;
    public const ushort VK_NUMPAD8 = 0x68;
    public const ushort VK_NUMPAD9 = 0x69;
    public const ushort VK_MULTIPLY = 0x6A;
    public const ushort VK_ADD = 0x6B;
    public const ushort VK_SEPARATOR = 0x6C;
    public const ushort VK_SUBTRACT = 0x6D;
    public const ushort VK_DECIMAL = 0x6E;
    public const ushort VK_DIVIDE = 0x6F;
    public const ushort VK_F1 = 0x70;
    public const ushort VK_F2 = 0x71;
    public const ushort VK_F3 = 0x72;
    public const ushort VK_F4 = 0x73;
    public const ushort VK_F5 = 0x74;
    public const ushort VK_F6 = 0x75;
    public const ushort VK_F7 = 0x76;
    public const ushort VK_F8 = 0x77;
    public const ushort VK_F9 = 0x78;
    public const ushort VK_F10 = 0x79;
    public const ushort VK_F11 = 0x7A;
    public const ushort VK_F12 = 0x7B;
    public const ushort VK_NUMLOCK = 0x90;
    public const ushort VK_SCROLL = 0x91;
    public const ushort VK_LSHIFT = 0xA0;
    public const ushort VK_RSHIFT = 0xA1;
    public const ushort VK_LCONTROL = 0xA2;
    public const ushort VK_RCONTROL = 0xA3;
    public const ushort VK_LMENU = 0xA4;
    public const ushort VK_RMENU = 0xA5;
    public const ushort VK_OEM_1 = 0xBA;       // ;:
    public const ushort VK_OEM_PLUS = 0xBB;     // =+
    public const ushort VK_OEM_COMMA = 0xBC;    // ,<
    public const ushort VK_OEM_MINUS = 0xBD;    // -_
    public const ushort VK_OEM_PERIOD = 0xBE;   // .>
    public const ushort VK_OEM_2 = 0xBF;        // /?
    public const ushort VK_OEM_3 = 0xC0;        // `~
    public const ushort VK_OEM_4 = 0xDB;        // [{
    public const ushort VK_OEM_5 = 0xDC;        // \|
    public const ushort VK_OEM_6 = 0xDD;        // ]}
    public const ushort VK_OEM_7 = 0xDE;        // '"

    /// <summary>
    /// Map a JavaScript keyCode or character to a VK code.
    /// </summary>
    public static ushort FromKeyCode(int keyCode)
    {
        // JS keyCodes mostly match VK codes for letters and digits
        if (keyCode is >= 0x30 and <= 0x39) return (ushort)keyCode; // 0-9
        if (keyCode is >= 0x41 and <= 0x5A) return (ushort)keyCode; // A-Z

        return keyCode switch
        {
            8 => VK_BACK,
            9 => VK_TAB,
            13 => VK_RETURN,
            16 => VK_SHIFT,
            17 => VK_CONTROL,
            18 => VK_MENU,
            19 => VK_PAUSE,
            20 => VK_CAPITAL,
            27 => VK_ESCAPE,
            32 => VK_SPACE,
            33 => VK_PRIOR,
            34 => VK_NEXT,
            35 => VK_END,
            36 => VK_HOME,
            37 => VK_LEFT,
            38 => VK_UP,
            39 => VK_RIGHT,
            40 => VK_DOWN,
            44 => VK_SNAPSHOT,
            45 => VK_INSERT,
            46 => VK_DELETE,
            91 => VK_LWIN,
            93 => VK_RWIN,
            112 => VK_F1,
            113 => VK_F2,
            114 => VK_F3,
            115 => VK_F4,
            116 => VK_F5,
            117 => VK_F6,
            118 => VK_F7,
            119 => VK_F8,
            120 => VK_F9,
            121 => VK_F10,
            122 => VK_F11,
            123 => VK_F12,
            144 => VK_NUMLOCK,
            145 => VK_SCROLL,
            186 => VK_OEM_1,
            187 => VK_OEM_PLUS,
            188 => VK_OEM_COMMA,
            189 => VK_OEM_MINUS,
            190 => VK_OEM_PERIOD,
            191 => VK_OEM_2,
            192 => VK_OEM_3,
            219 => VK_OEM_4,
            220 => VK_OEM_5,
            221 => VK_OEM_6,
            222 => VK_OEM_7,
            _ => (ushort)keyCode,
        };
    }
}
