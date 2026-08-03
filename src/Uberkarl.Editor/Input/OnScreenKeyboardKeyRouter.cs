namespace Uberkarl.Editor.Input;

/// <summary>What the on-screen keyboard's physical-typing path should do with a routed physical key.</summary>
public enum OnScreenKeyboardCommand
{
    /// <summary>Not a routed key — leave it to the normal grid/typing dispatch (backspace, printable
    /// characters, or — for gamepad A / mouse click — activating whatever key currently has focus).</summary>
    None,

    /// <summary>Commit the whole buffer (Done), regardless of which grid key currently has focus.</summary>
    Commit,

    /// <summary>Cancel and discard the buffer (Cancel), regardless of which grid key currently has focus.</summary>
    Cancel,

    /// <summary>Insert a literal space into the buffer, regardless of which grid key currently has focus —
    /// DiVoid #7513 follow-up: physical Space is bound to <c>ui_accept</c> project-wide, so without this
    /// interception it both types a space (via the physical-typing path) AND activates whatever grid key
    /// currently has focus (double action). See <c>game/Editor/OnScreenKeyboard._Input</c>.</summary>
    InsertSpace,
}

/// <summary>
/// Pure, engine-agnostic decision for <see cref="OnScreenKeyboardCommand"/>: PR #19 playtest feedback was
/// that a physical Enter/Return must always commit the on-screen keyboard's buffer and a physical Escape
/// must always cancel it — <b>regardless of which grid key currently has focus</b>. The same family of bug
/// applies to physical Space (DiVoid #7513 follow-up): it is bound to <c>ui_accept</c> just like Enter, so
/// it must always insert a literal space rather than also activating the focused grid key. All three are
/// deliberately distinct from a gamepad A-button press or a mouse click landing on a grid key, both of which
/// correctly continue to TYPE that key (see <c>game/Editor/OnScreenKeyboard.OnKeyPressed</c>); this router
/// only ever sees the raw physical-key facts the Godot glue extracts from an <c>InputEventKey</c>, never the
/// device that produced a <c>ui_accept</c>/<c>ui_cancel</c> action, so it cannot be reached by gamepad/mouse
/// input at all — the glue only calls it for <c>InputEventKey</c>.
/// </summary>
public static class OnScreenKeyboardKeyRouter
{
    /// <summary>
    /// Resolves the command for a physical key press given whether it was Enter/Return (<paramref name="isEnter"/>,
    /// true for either the main Enter or the numpad Enter), Escape (<paramref name="isEscape"/>), or Space
    /// (<paramref name="isSpace"/>). Enter takes priority over Escape, which takes priority over Space, if
    /// somehow more than one is set (they never are for a single real key press — the caller passes flags
    /// derived from one <c>Keycode</c>, so at most one is ever true).
    /// </summary>
    public static OnScreenKeyboardCommand Resolve(bool isEnter, bool isEscape, bool isSpace)
    {
        if (isEnter)
            return OnScreenKeyboardCommand.Commit;
        if (isEscape)
            return OnScreenKeyboardCommand.Cancel;
        if (isSpace)
            return OnScreenKeyboardCommand.InsertSpace;
        return OnScreenKeyboardCommand.None;
    }
}
