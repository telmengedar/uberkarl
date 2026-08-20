using System;
using System.Collections.Generic;
using Godot;
using Uberkarl.Editor.Input;

namespace Uberkarl {

    /// <summary>
    /// The reusable gamepad/keyboard/mouse text-entry primitive (DiVoid #7513): a summoned <see cref="Control"/>
    /// showing a grid of character keys (<see cref="OnScreenKeyboardLayout"/>) navigated by D-pad/stick via
    /// <see cref="FocusGrid"/>, with a live buffer preview at the top. <see cref="RequestText"/> is the whole
    /// public surface — <c>RequestText(prompt, initial, onCommit)</c> — so any future caller (Save-As naming
    /// #7552, tile naming #7551 — NOT wired here, per task scope) can summon it without knowing anything
    /// about layers. <see cref="LayerManagerPanel"/>'s rename affordance is the first (and, per task scope,
    /// only) caller.
    ///
    /// Reuses the summoned-panel scaffolding verbatim — full-rect dim backdrop, centered panel, grab-focus-
    /// on-summon, <c>ui_cancel</c> discards — exactly as <see cref="PackageBrowser"/>/<see cref="LayerManagerPanel"/>
    /// already do. Godot's <c>DisplayServer.virtual_keyboard_show</c> is mobile-only (checked per task —
    /// desktop, our target, has no OS on-screen keyboard), hence this custom in-engine one.
    ///
    /// Three input paths, all live simultaneously:
    /// <list type="bullet">
    /// <item>Gamepad/keyboard grid navigation: <see cref="FocusGrid"/> navigates the grid, <c>ui_accept</c>
    /// activates the focused key (mirrors every other summoned panel — Button.Pressed fires identically
    /// regardless of which device triggered it: gamepad A or a mouse click on a grid key both correctly TYPE
    /// that key).</item>
    /// <item>Mouse: a Button click is a Button click — no special-casing needed.</item>
    /// <item>Physical keyboard, typing directly: <see cref="_UnhandledInput"/> reads raw <c>InputEventKey</c>
    /// Unicode/Backspace, independent of which grid key currently has focus. A real key's Unicode is already
    /// correctly cased by the OS, so this bypasses <see cref="TextEntryEditor.Type"/>/<see cref="TextEntryEditor.CapsActive"/>
    /// entirely — those exist only for the on-screen Shift key.</item>
    /// </list>
    /// <b>Physical Enter/Return, Escape, and Space</b> (PR #19 playtest feedback, plus a Space follow-up) are
    /// the exceptions routed ahead of all three paths above, in <see cref="_Input"/>: Enter/Return always
    /// <b>commits</b> the whole buffer, Escape always <b>cancels</b> it, and Space always <b>inserts a literal
    /// space</b> into the buffer, regardless of which grid key currently has focus — not "activate whatever's
    /// focused," the way gamepad A/mouse click on a grid key correctly still behaves. Godot's own
    /// <c>ui_accept</c> binding includes both Enter and Space (so a focused Button would otherwise consume it
    /// first and merely activate itself); <see cref="_Input"/> runs before Godot's GUI dispatch, so
    /// intercepting the raw <c>InputEventKey</c> there and marking it handled pre-empts that. The
    /// commit/cancel/insert-space decision itself is <see cref="OnScreenKeyboardKeyRouter.Resolve"/>, a pure,
    /// engine-agnostic function unit-tested without Godot.
    /// </summary>
    public partial class OnScreenKeyboard : Control {

        Label promptLabel;
        Label bufferLabel;
        VBoxContainer rowsBox;

        TextEntryEditor editor;
        Action<string> pendingCommit;
        Control focusToRestore;

        int lastFocusedRow;
        int lastFocusedCol;

        /// <summary>True while the keyboard is summoned.</summary>
        public bool IsOpen => Visible;

        public override void _Ready() {
            EditorLayout.FillParent(this);
            MouseFilter = MouseFilterEnum.Stop;
            FocusMode = FocusModeEnum.All;
            Visible = false;
            ZIndex = 150;
            BuildLayout();
        }

        void BuildLayout() {
            ColorRect backdrop = new ColorRect { Color = new Color(0.05f, 0.06f, 0.08f, 0.85f) };
            EditorLayout.FillParent(backdrop);
            backdrop.MouseFilter = MouseFilterEnum.Stop;
            AddChild(backdrop);

            PanelContainer panel = new PanelContainer { CustomMinimumSize = new Vector2(640f, 380f) };
            EditorLayout.CenterInParent(panel);
            AddChild(panel);

            VBoxContainer root = new VBoxContainer();
            panel.AddChild(root);

            promptLabel = new Label();
            root.AddChild(promptLabel);

            bufferLabel = new Label();
            bufferLabel.AddThemeColorOverride("font_color", EditorTheme.Accent);
            root.AddChild(bufferLabel);

            rowsBox = new VBoxContainer();
            root.AddChild(rowsBox);
        }

        /// <summary>
        /// Summon the keyboard seeded with <paramref name="initialText"/>, showing <paramref name="prompt"/>
        /// above the buffer. <paramref name="onCommit"/> fires with the final text on Done; Cancel (or
        /// <c>ui_cancel</c>) closes without calling it at all — the reusable text-input primitive the task
        /// asks for. Remembers whatever control currently holds focus so it can be restored once the
        /// keyboard closes, regardless of who summoned it.
        /// </summary>
        public void RequestText(string prompt, string initialText, Action<string> onCommit) {
            editor = new TextEntryEditor(initialText);
            pendingCommit = onCommit;
            promptLabel.Text = prompt;
            focusToRestore = GetViewport()?.GuiGetFocusOwner();
            lastFocusedRow = 0;
            lastFocusedCol = 0;

            Visible = true;
            RebuildKeys();
            RefreshBuffer();
        }

        void RebuildKeys() {
            foreach (Node child in rowsBox.GetChildren())
                child.QueueFree();

            List<List<Control>> grid = new List<List<Control>>();
            foreach (IReadOnlyList<KeyboardKey> sourceRow in OnScreenKeyboardLayout.Rows) {
                HBoxContainer rowBox = new HBoxContainer();
                rowsBox.AddChild(rowBox);

                List<Control> row = new List<Control>(sourceRow.Count);
                foreach (KeyboardKey key in sourceRow) {
                    Button button = new Button { Text = key.DisplayText(editor.CapsActive) };
                    if (key.Kind == KeyboardKeyKind.ShiftToggle) {
                        button.ToggleMode = true;
                        button.ButtonPressed = editor.CapsActive;
                    }
                    button.Pressed += () => OnKeyPressed(key);
                    rowBox.AddChild(button);
                    row.Add(button);
                }
                grid.Add(row);
            }

            FocusGrid.Contain(grid);
            TrackFocusPosition(grid);

            int restoreRow = Math.Clamp(lastFocusedRow, 0, grid.Count - 1);
            int restoreCol = Math.Clamp(lastFocusedCol, 0, grid[restoreRow].Count - 1);
            grid[restoreRow][restoreCol].CallDeferred(Control.MethodName.GrabFocus);
        }

        // Same technique as LayerManagerPanel.TrackFocusPosition: a Shift press rebuilds every key's label
        // (case flips), which would otherwise always snap focus back to the grid's first cell.
        void TrackFocusPosition(List<List<Control>> grid) {
            for (int r = 0; r < grid.Count; r++) {
                for (int c = 0; c < grid[r].Count; c++) {
                    int capturedRow = r;
                    int capturedCol = c;
                    grid[r][c].FocusEntered += () => {
                        lastFocusedRow = capturedRow;
                        lastFocusedCol = capturedCol;
                    };
                }
            }
        }

        void OnKeyPressed(KeyboardKey key) {
            switch (key.Kind) {
                case KeyboardKeyKind.Character:
                    editor.Type(key.Normal, key.Shifted);
                    RefreshBuffer();
                    break;
                case KeyboardKeyKind.Space:
                    editor.Insert(' ');
                    RefreshBuffer();
                    break;
                case KeyboardKeyKind.Backspace:
                    editor.Backspace();
                    RefreshBuffer();
                    break;
                case KeyboardKeyKind.ShiftToggle:
                    editor.ToggleCaps();
                    RebuildKeys();
                    break;
                case KeyboardKeyKind.Done:
                    Commit();
                    break;
                case KeyboardKeyKind.Cancel:
                    CancelEntry();
                    break;
            }
        }

        void RefreshBuffer() => bufferLabel.Text = editor.Text + "_";

        void Commit() {
            string result = editor.Commit();
            Action<string> commit = pendingCommit;
            Close();
            commit?.Invoke(result);
        }

        void CancelEntry() => Close();

        void Close() {
            Visible = false;
            pendingCommit = null;
            focusToRestore?.CallDeferred(Control.MethodName.GrabFocus);
            focusToRestore = null;
        }

        // Physical Enter/Return → commit, physical Escape → cancel (PR #19 playtest feedback), physical
        // Space → insert a literal space (DiVoid #7513 follow-up) — all regardless of which grid key
        // currently has focus. Must run in _Input, NOT _UnhandledInput: Godot's GUI dispatch (which happens
        // between _Input and _UnhandledInput) lets a focused Button consume ui_accept itself first — Enter
        // AND Space are both bound to ui_accept project-wide, so by the time _UnhandledInput would see them,
        // the focused Button has already "activated" (typed/toggled/etc.) instead of committing/inserting a
        // space. Without this interception, physical Space both typed a space (via _UnhandledInput's Unicode
        // path below) AND activated the focused grid key — Godot's GUI dispatch consuming ui_accept does not
        // set the viewport's "handled" flag that _UnhandledInput checks, so both fired. Intercepting the raw
        // InputEventKey here, before that GUI dispatch, and marking it handled pre-empts the Button entirely.
        // Only InputEventKey is inspected, so this can never fire for a gamepad A-button (an
        // InputEventJoypadButton) or a mouse click (an InputEventMouseButton) — both of those still
        // correctly TYPE the focused grid key via OnKeyPressed (including the on-screen Space key), unaffected.
        // Escape is routed the same way, through the same OnScreenKeyboardKeyRouter decision, even though it
        // also happens to fall through to _UnhandledInput's own ui_cancel check below (Buttons never consume
        // ui_cancel) — handling it here keeps all three routed physical keys next to each other and behind
        // one engine-agnostic, unit-tested rule.
        public override void _Input(InputEvent @event) {
            if (!Visible || @event.IsEcho() || @event is not InputEventKey key || !key.Pressed)
                return;

            OnScreenKeyboardCommand command = OnScreenKeyboardKeyRouter.Resolve(
                isEnter: key.Keycode == Key.Enter || key.Keycode == Key.KpEnter,
                isEscape: key.Keycode == Key.Escape,
                isSpace: key.Keycode == Key.Space);

            switch (command) {
                case OnScreenKeyboardCommand.Commit:
                    Commit();
                    GetViewport().SetInputAsHandled();
                    break;
                case OnScreenKeyboardCommand.Cancel:
                    CancelEntry();
                    GetViewport().SetInputAsHandled();
                    break;
                case OnScreenKeyboardCommand.InsertSpace:
                    editor.Insert(' ');
                    RefreshBuffer();
                    GetViewport().SetInputAsHandled();
                    break;
            }
        }

        // Physical keyboard: type directly into the buffer, independent of which grid key has focus. Only
        // Backspace and printable Unicode are claimed here — everything else (arrows/ui_accept) is left to
        // the normal focus/Button dispatch so grid navigation keeps working unimpeded. ui_cancel is handled
        // here too (belt-and-suspenders, same as LayerManagerPanel/PackageBrowser): this is the path a
        // gamepad B press falls through to (Buttons never consume ui_cancel); a physical Escape is normally
        // already intercepted earlier by _Input above, so by the time it would reach here it is already
        // marked handled and this branch is effectively gamepad-B-only in practice.
        public override void _UnhandledInput(InputEvent @event) {
            if (!Visible || @event.IsEcho())
                return;

            if (@event.IsActionPressed("ui_cancel")) {
                CancelEntry();
                GetViewport().SetInputAsHandled();
                return;
            }

            if (@event is not InputEventKey key || !key.Pressed)
                return;

            if (key.Keycode == Key.Backspace) {
                editor.Backspace();
                RefreshBuffer();
                GetViewport().SetInputAsHandled();
            } else if (key.Unicode != 0 && !char.IsControl((char)key.Unicode)) {
                editor.Insert((char)key.Unicode);
                RefreshBuffer();
                GetViewport().SetInputAsHandled();
            }
        }

        // Belt-and-suspenders close path, exactly as LayerManagerPanel/PackageBrowser: a Button (not the
        // panel) almost always holds focus, so ui_cancel pressed there never reaches _GuiInput here — it
        // falls through to unhandled input, where this closes the keyboard without committing.
        public override void _GuiInput(InputEvent @event) {
            if (!Visible)
                return;

            if (@event.IsActionPressed("ui_cancel")) {
                AcceptEvent();
                CancelEntry();
            }
        }
    }
}
