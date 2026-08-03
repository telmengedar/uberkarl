namespace Uberkarl.Editor.Input;

/// <summary>What a single on-screen keyboard key does when activated.</summary>
public enum KeyboardKeyKind
{
    /// <summary>Types <see cref="KeyboardKey.Normal"/> or <see cref="KeyboardKey.Shifted"/> (per caps state).</summary>
    Character,

    /// <summary>Types a literal space.</summary>
    Space,

    /// <summary>Removes the last typed character.</summary>
    Backspace,

    /// <summary>Toggles caps/shift for every <see cref="Character"/> key.</summary>
    ShiftToggle,

    /// <summary>Commits the buffer and closes the keyboard.</summary>
    Done,

    /// <summary>Discards the buffer and closes the keyboard.</summary>
    Cancel,
}

/// <summary>
/// One key of the on-screen keyboard's character grid — pure data (no Godot type), so the grid layout
/// itself is unit-testable and the Godot <c>Control</c> glue only has to render it and route presses to
/// <see cref="TextEntryEditor"/>. A <see cref="KeyboardKeyKind.Character"/> key carries both its
/// unshifted and shifted glyph (e.g. <c>q</c>/<c>Q</c>, <c>1</c>/<c>!</c>) so one Shift toggle governs
/// letters and the digit row's symbol variants uniformly; every other kind carries a fixed <see cref="Label"/>.
/// </summary>
public readonly record struct KeyboardKey(KeyboardKeyKind Kind, char Normal = '\0', char Shifted = '\0', string Label = "")
{
    public static KeyboardKey Character(char normal, char shifted) => new(KeyboardKeyKind.Character, normal, shifted);

    public static readonly KeyboardKey Space = new(KeyboardKeyKind.Space, ' ', ' ', "Space");
    public static readonly KeyboardKey Backspace = new(KeyboardKeyKind.Backspace, Label: "⌫ Back");
    public static readonly KeyboardKey Shift = new(KeyboardKeyKind.ShiftToggle, Label: "Shift");
    public static readonly KeyboardKey Done = new(KeyboardKeyKind.Done, Label: "Done");
    public static readonly KeyboardKey Cancel = new(KeyboardKeyKind.Cancel, Label: "Cancel");

    /// <summary>The glyph to draw on the key given the keyboard's current caps state.</summary>
    public string DisplayText(bool capsActive) =>
        Kind == KeyboardKeyKind.Character ? (capsActive ? Shifted : Normal).ToString() : Label;
}
