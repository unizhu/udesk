namespace Udesk.Input;

/// <summary>
/// Interface for simulating mouse and keyboard input on the remote desktop.
/// </summary>
public interface IInputController
{
    /// <summary>Moves the cursor to the specified coordinates.</summary>
    void MouseMove(int x, int y);

    /// <summary>Performs a mouse button action at the specified coordinates.</summary>
    /// <param name="x">X coordinate on the remote desktop.</param>
    /// <param name="y">Y coordinate on the remote desktop.</param>
    /// <param name="button">Mouse button: "left", "right", "middle".</param>
    /// <param name="action">Action: "down" or "up".</param>
    void MouseClick(int x, int y, string button, string action);

    /// <summary>Scrolls the mouse wheel.</summary>
    /// <param name="delta">Scroll amount. Positive = up, negative = down. Typically 120 per notch.</param>
    void MouseScroll(int delta);

    /// <summary>Presses a key down.</summary>
    /// <param name="keyCode">Virtual key code.</param>
    void KeyDown(int keyCode);

    /// <summary>Releases a key.</summary>
    /// <param name="keyCode">Virtual key code.</param>
    void KeyUp(int keyCode);

    /// <summary>Types a text string character by character.</summary>
    /// <param name="text">Text to type.</param>
    void TypeText(string text);
}
