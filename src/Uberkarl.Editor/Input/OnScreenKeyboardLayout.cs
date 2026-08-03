namespace Uberkarl.Editor.Input;

/// <summary>
/// The on-screen keyboard's fixed character grid (DiVoid #7513) — a deliberately simple QWERTY-ish
/// layout (letters + digits + a small punctuation set) plus the five control keys, expressed as pure
/// data so it is unit-testable and reusable by the Godot glue (<c>game/Editor/OnScreenKeyboard.cs</c>)
/// without depending on it. Rows are ragged on purpose (the digit/letter/symbol/action rows are
/// different widths) — <see cref="FocusGrid"/> (in <c>game/Editor</c>) already handles ragged rows by
/// clamping the vertical step to the shorter row, exactly as it does for <c>LayerManagerPanel</c>.
/// </summary>
public static class OnScreenKeyboardLayout
{
    private const string DigitsPlain = "1234567890";
    private const string DigitsShifted = "!@#$%^&*()";

    private const string SymbolsPlain = "-.,'";
    private const string SymbolsShifted = "_:;\"";

    /// <summary>
    /// The grid, top row first: digits, three QWERTY letter rows, a small punctuation row, then the
    /// action row (Shift, Space, Backspace, Cancel, Done).
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<KeyboardKey>> Rows { get; } = BuildRows();

    private static IReadOnlyList<IReadOnlyList<KeyboardKey>> BuildRows()
    {
        return new IReadOnlyList<KeyboardKey>[]
        {
            ShiftablePairRow(DigitsPlain, DigitsShifted),
            LetterRow("qwertyuiop"),
            LetterRow("asdfghjkl"),
            LetterRow("zxcvbnm"),
            ShiftablePairRow(SymbolsPlain, SymbolsShifted),
            new[] { KeyboardKey.Shift, KeyboardKey.Space, KeyboardKey.Backspace, KeyboardKey.Cancel, KeyboardKey.Done },
        };
    }

    private static IReadOnlyList<KeyboardKey> LetterRow(string lettersLowercase)
    {
        var row = new KeyboardKey[lettersLowercase.Length];
        for (var i = 0; i < lettersLowercase.Length; i++)
        {
            var lower = lettersLowercase[i];
            row[i] = KeyboardKey.Character(lower, char.ToUpperInvariant(lower));
        }

        return row;
    }

    private static IReadOnlyList<KeyboardKey> ShiftablePairRow(string plain, string shifted)
    {
        if (plain.Length != shifted.Length)
            throw new ArgumentException("Plain/shifted rows must be the same length.", nameof(shifted));

        var row = new KeyboardKey[plain.Length];
        for (var i = 0; i < plain.Length; i++)
            row[i] = KeyboardKey.Character(plain[i], shifted[i]);

        return row;
    }
}
