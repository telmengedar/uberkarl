using NUnit.Framework;
using Uberkarl.Editor.Input;

namespace Uberkarl.Editor.Tests;

/// <summary>
/// Covers the on-screen keyboard's engine-agnostic core (DiVoid #7513): <see cref="TextEntryEditor"/>
/// (insert/backspace/caps/commit/cancel) and <see cref="OnScreenKeyboardLayout"/>/<see cref="KeyboardKey"/>
/// (the character grid data + shift-display rules). No Godot dependency — the Godot glue
/// (<c>game/Editor/OnScreenKeyboard.cs</c>) only renders this and routes presses into it.
/// </summary>
[TestFixture]
public sealed class OnScreenKeyboardTests
{
    // ----- TextEntryEditor -----

    [Test]
    public void Ctor_SeedsBufferFromInitialText()
    {
        var editor = new TextEntryEditor("Layer 1");
        Assert.That(editor.Text, Is.EqualTo("Layer 1"));
    }

    [Test]
    public void Ctor_NullInitialText_SeedsEmptyBuffer()
    {
        var editor = new TextEntryEditor(null);
        Assert.That(editor.Text, Is.EqualTo(string.Empty));
    }

    [Test]
    public void Insert_AppendsLiteralCharacter()
    {
        var editor = new TextEntryEditor("ab");
        editor.Insert('c');
        Assert.That(editor.Text, Is.EqualTo("abc"));
    }

    [Test]
    public void Backspace_RemovesLastCharacter_AndReportsItChanged()
    {
        var editor = new TextEntryEditor("abc");

        var changed = editor.Backspace();

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(editor.Text, Is.EqualTo("ab"));
        });
    }

    [Test]
    public void Backspace_OnEmptyBuffer_IsNoOp()
    {
        var editor = new TextEntryEditor(null);

        var changed = editor.Backspace();

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.False);
            Assert.That(editor.Text, Is.EqualTo(string.Empty));
        });
    }

    [Test]
    public void CapsActive_StartsOff()
    {
        var editor = new TextEntryEditor(null);
        Assert.That(editor.CapsActive, Is.False);
    }

    [Test]
    public void ToggleCaps_FlipsState_Repeatedly()
    {
        var editor = new TextEntryEditor(null);

        editor.ToggleCaps();
        Assert.That(editor.CapsActive, Is.True);

        editor.ToggleCaps();
        Assert.That(editor.CapsActive, Is.False);
    }

    [Test]
    public void Type_WhileCapsOff_UsesNormalGlyph()
    {
        var editor = new TextEntryEditor(null);
        editor.Type('q', 'Q');
        Assert.That(editor.Text, Is.EqualTo("q"));
    }

    [Test]
    public void Type_WhileCapsOn_UsesShiftedGlyph()
    {
        var editor = new TextEntryEditor(null);
        editor.ToggleCaps();
        editor.Type('q', 'Q');
        Assert.That(editor.Text, Is.EqualTo("Q"));
    }

    [Test]
    public void Type_DigitRow_CapsOnProducesSymbolNotUppercaseDigit()
    {
        // The digit row's "shifted" glyph is a symbol (1 -> !), not a case change — Type is the same
        // mechanism for both letters and the digit/symbol rows.
        var editor = new TextEntryEditor(null);
        editor.ToggleCaps();
        editor.Type('1', '!');
        Assert.That(editor.Text, Is.EqualTo("!"));
    }

    [Test]
    public void Commit_ReturnsCurrentBufferContents()
    {
        var editor = new TextEntryEditor("Layer 1");
        editor.Backspace();
        editor.Insert('2');

        Assert.That(editor.Commit(), Is.EqualTo("Layer 2"));
    }

    [Test]
    public void Cancel_ReturnsOriginalText_IgnoringAnyEdits()
    {
        var editor = new TextEntryEditor("Layer 1");
        editor.Backspace();
        editor.Backspace();
        editor.Insert('9');
        editor.Insert('9');

        Assert.That(editor.Cancel(), Is.EqualTo("Layer 1"));
    }

    [Test]
    public void FullTypingSequence_InsertBackspaceCapsCommit()
    {
        // Mirrors a real rename: seed "Layer 1", backspace the digit, shift on, type a letter, shift off,
        // type digits, commit.
        var editor = new TextEntryEditor("Layer 1");

        editor.Backspace(); // "Layer "
        editor.ToggleCaps();
        editor.Type('b', 'B'); // "Layer B"
        editor.ToggleCaps();
        editor.Type('2', '@'); // "Layer B2"

        Assert.That(editor.Commit(), Is.EqualTo("Layer B2"));
    }

    // ----- KeyboardKey -----

    [Test]
    public void KeyboardKey_Character_DisplaysNormalWhenCapsOff()
    {
        var key = KeyboardKey.Character('q', 'Q');
        Assert.That(key.DisplayText(capsActive: false), Is.EqualTo("q"));
    }

    [Test]
    public void KeyboardKey_Character_DisplaysShiftedWhenCapsOn()
    {
        var key = KeyboardKey.Character('q', 'Q');
        Assert.That(key.DisplayText(capsActive: true), Is.EqualTo("Q"));
    }

    [Test]
    public void KeyboardKey_ControlKeys_DisplayTheirFixedLabel_RegardlessOfCaps()
    {
        Assert.Multiple(() =>
        {
            Assert.That(KeyboardKey.Space.DisplayText(false), Is.EqualTo("Space"));
            Assert.That(KeyboardKey.Space.DisplayText(true), Is.EqualTo("Space"));
            Assert.That(KeyboardKey.Backspace.DisplayText(true), Is.EqualTo(KeyboardKey.Backspace.Label));
            Assert.That(KeyboardKey.Shift.DisplayText(true), Is.EqualTo("Shift"));
            Assert.That(KeyboardKey.Done.DisplayText(true), Is.EqualTo("Done"));
            Assert.That(KeyboardKey.Cancel.DisplayText(true), Is.EqualTo("Cancel"));
        });
    }

    [Test]
    public void KeyboardKey_ControlKeys_HaveDistinctKinds()
    {
        Assert.Multiple(() =>
        {
            Assert.That(KeyboardKey.Space.Kind, Is.EqualTo(KeyboardKeyKind.Space));
            Assert.That(KeyboardKey.Backspace.Kind, Is.EqualTo(KeyboardKeyKind.Backspace));
            Assert.That(KeyboardKey.Shift.Kind, Is.EqualTo(KeyboardKeyKind.ShiftToggle));
            Assert.That(KeyboardKey.Done.Kind, Is.EqualTo(KeyboardKeyKind.Done));
            Assert.That(KeyboardKey.Cancel.Kind, Is.EqualTo(KeyboardKeyKind.Cancel));
        });
    }

    // ----- OnScreenKeyboardLayout -----

    [Test]
    public void Layout_HasSixRows()
    {
        Assert.That(OnScreenKeyboardLayout.Rows, Has.Count.EqualTo(6));
    }

    [Test]
    public void Layout_DigitRow_HasTenShiftableCharacterKeys()
    {
        var digits = OnScreenKeyboardLayout.Rows[0];
        Assert.That(digits, Has.Count.EqualTo(10));
        foreach (var key in digits)
        {
            Assert.That(key.Kind, Is.EqualTo(KeyboardKeyKind.Character));
            Assert.That(key.Normal, Is.Not.EqualTo(key.Shifted), $"digit '{key.Normal}' should have a distinct shifted symbol.");
        }
    }

    [Test]
    public void Layout_LetterRows_CoverAllTwentySixLettersExactlyOnce()
    {
        var seen = new System.Collections.Generic.HashSet<char>();
        foreach (var row in new[] { OnScreenKeyboardLayout.Rows[1], OnScreenKeyboardLayout.Rows[2], OnScreenKeyboardLayout.Rows[3] })
        {
            foreach (var key in row)
            {
                Assert.That(key.Kind, Is.EqualTo(KeyboardKeyKind.Character));
                Assert.That(key.Shifted, Is.EqualTo(char.ToUpperInvariant(key.Normal)));
                Assert.That(seen.Add(key.Normal), Is.True, $"letter '{key.Normal}' appears more than once.");
            }
        }

        Assert.That(seen, Has.Count.EqualTo(26));
    }

    [Test]
    public void Layout_ActionRow_IsShiftSpaceBackspaceCancelDone_InThatOrder()
    {
        var actions = OnScreenKeyboardLayout.Rows[5];
        Assert.That(actions, Has.Count.EqualTo(5));
        Assert.Multiple(() =>
        {
            Assert.That(actions[0].Kind, Is.EqualTo(KeyboardKeyKind.ShiftToggle));
            Assert.That(actions[1].Kind, Is.EqualTo(KeyboardKeyKind.Space));
            Assert.That(actions[2].Kind, Is.EqualTo(KeyboardKeyKind.Backspace));
            Assert.That(actions[3].Kind, Is.EqualTo(KeyboardKeyKind.Cancel));
            Assert.That(actions[4].Kind, Is.EqualTo(KeyboardKeyKind.Done));
        });
    }

    [Test]
    public void Layout_EveryRowIsNonEmpty()
    {
        foreach (var row in OnScreenKeyboardLayout.Rows)
            Assert.That(row, Is.Not.Empty);
    }

    // ----- OnScreenKeyboardKeyRouter (PR #19 playtest feedback: physical Enter commits, physical Escape
    // cancels, regardless of which grid key has focus — see game/Editor/OnScreenKeyboard._Input) -----

    [Test]
    public void Router_Enter_ResolvesToCommit()
    {
        var command = OnScreenKeyboardKeyRouter.Resolve(isEnter: true, isEscape: false);
        Assert.That(command, Is.EqualTo(OnScreenKeyboardCommand.Commit));
    }

    [Test]
    public void Router_Escape_ResolvesToCancel()
    {
        var command = OnScreenKeyboardKeyRouter.Resolve(isEnter: false, isEscape: true);
        Assert.That(command, Is.EqualTo(OnScreenKeyboardCommand.Cancel));
    }

    [Test]
    public void Router_NeitherEnterNorEscape_ResolvesToNone()
    {
        // The catch-all for every other physical key (letters, backspace, shift, space, arrows...) — those
        // stay on the normal grid/typing dispatch untouched by this router.
        var command = OnScreenKeyboardKeyRouter.Resolve(isEnter: false, isEscape: false);
        Assert.That(command, Is.EqualTo(OnScreenKeyboardCommand.None));
    }

    [Test]
    public void Router_BothFlagsSet_EnterTakesPriority()
    {
        // Defensive only — a single real key press can never set both, since the caller derives each flag
        // from one Keycode comparison. Pins the tie-break so it's a deliberate choice, not accidental.
        var command = OnScreenKeyboardKeyRouter.Resolve(isEnter: true, isEscape: true);
        Assert.That(command, Is.EqualTo(OnScreenKeyboardCommand.Commit));
    }
}
