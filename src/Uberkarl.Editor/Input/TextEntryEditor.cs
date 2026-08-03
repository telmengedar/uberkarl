namespace Uberkarl.Editor.Input;

/// <summary>
/// Engine-agnostic text-buffer + caps state for the on-screen keyboard (DiVoid #7513): the recurring
/// gamepad blocker is that layer rename, level naming, and package Save-As all need a way to type a
/// string with no physical keyboard. This is the core one input-agnostic text-entry primitive builds
/// on — a mutable buffer seeded from a starting value, appended to one character at a time (no
/// interior-cursor editing; a rename/filename field never needs mid-string insertion, and the "keep it
/// simple" mandate rules out building one), plus a <see cref="CapsActive"/> toggle the on-screen
/// keyboard's Shift key flips so its OWN key presses (mouse/gamepad/keyboard activating a grid button)
/// insert the upper-case/shifted glyph. A physical keyboard's own Shift key already produces the
/// correctly-cased Unicode character at the OS level, so real typing bypasses <see cref="Type"/>
/// entirely and calls <see cref="Insert"/> directly with whatever character the key event carries.
///
/// <see cref="Commit"/> and <see cref="Cancel"/> mirror <see cref="SteppedValueEditor{T}"/>'s
/// enter/commit/cancel shape: the buffer is free to mutate as the author types, and only
/// <see cref="Commit"/> hands back the value the caller should actually apply — <see cref="Cancel"/>
/// hands back the original, untouched value, so a caller that always applies "the returned string" never
/// needs a separate cancelled-branch.
/// </summary>
public sealed class TextEntryEditor
{
    private readonly string originalText;
    private readonly System.Text.StringBuilder buffer;

    public TextEntryEditor(string? initialText)
    {
        originalText = initialText ?? string.Empty;
        buffer = new System.Text.StringBuilder(originalText);
    }

    /// <summary>The buffer's current contents (the in-progress typed text).</summary>
    public string Text => buffer.ToString();

    /// <summary>True while the on-screen keyboard's Shift/Caps key is toggled on.</summary>
    public bool CapsActive { get; private set; }

    /// <summary>Flips <see cref="CapsActive"/> — the on-screen Shift/Caps key's whole job.</summary>
    public void ToggleCaps() => CapsActive = !CapsActive;

    /// <summary>
    /// Appends a literal character exactly as given — the path a physical keyboard's key event (whose
    /// Unicode is already correctly cased/shifted by the OS) and the Space key both use.
    /// </summary>
    public void Insert(char character) => buffer.Append(character);

    /// <summary>
    /// Appends <paramref name="normal"/> or <paramref name="shifted"/> depending on <see cref="CapsActive"/>
    /// — the on-screen keyboard's character keys call this so one Shift toggle governs every key's case
    /// (letters) or symbol (digit row) uniformly.
    /// </summary>
    public void Type(char normal, char shifted) => buffer.Append(CapsActive ? shifted : normal);

    /// <summary>Removes the last character. No-op (returns <c>false</c>) on an empty buffer.</summary>
    public bool Backspace()
    {
        if (buffer.Length == 0)
            return false;

        buffer.Length -= 1;
        return true;
    }

    /// <summary>Ends editing, handing back the buffer's final contents for the caller to apply.</summary>
    public string Commit() => Text;

    /// <summary>Ends editing, handing back the original (pre-edit) text — the caller applies this exactly
    /// as it would a commit, so cancelling never requires touching the model at all.</summary>
    public string Cancel() => originalText;
}
