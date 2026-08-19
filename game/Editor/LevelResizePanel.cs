using System;
using System.Collections.Generic;
using Godot;
using Uberkarl.Editor;
using Uberkarl.Editor.Input;

namespace Uberkarl {

    /// <summary>
    /// The summoned, gamepad-first level-resize surface (DiVoid #7550): set the level's width/height via
    /// two +/- steppers, then Apply. Reuses the <see cref="PackageBrowser"/>/<see cref="LayerManagerPanel"/>
    /// scaffolding verbatim — full-rect dim backdrop, centered panel, grab-focus-on-summon (deferred, so it
    /// wins the race against <c>LevelEditor.CloseMenu</c>'s synchronous canvas focus grab), <c>ui_cancel</c>
    /// closes with nothing applied. It holds no resize logic itself: Apply calls
    /// <see cref="LevelEditSession.Resize"/> directly, then raises <see cref="LevelModelChanged"/> so the
    /// controller re-snapshots the canvas from the model's new truth — the panel never touches the canvas
    /// or the level builder.
    ///
    /// Unlike the layer panel's Scroll stepper (DiVoid #7512), each dimension stepper here occupies its own
    /// row with no sibling control to its left/right — so there is no spatial-nav conflict to resolve, and
    /// left/right can adjust a LOCAL pending value directly, with no separate enter-edit-mode gesture. Both
    /// steppers stay purely local (the session is untouched) until Apply reads their current values and
    /// resizes in one atomic call — a resize is a single width+height operation, never two independent
    /// dimension changes. Apply requires a second confirm press when the pending size would crop a painted
    /// cell on any layer (<see cref="EditableLevel.WouldDropPaintedCells"/>), mirroring the layer manager's
    /// "Confirm Delete?" two-press pattern — reused here rather than reinvented, since resize (like layer
    /// delete) is not undoable this increment.
    /// </summary>
    public partial class LevelResizePanel : Control {

        LevelEditSession session;
        VBoxContainer listBox;
        Label currentSizeLabel;
        DimensionStepper widthStepper;
        DimensionStepper heightStepper;
        Button applyButton;

        bool pendingConfirm;
        int lastFocusedRow;

        /// <summary>Raised after a successful resize: "refresh the canvas + status."</summary>
        public event Action LevelModelChanged;

        /// <summary>Raised when the panel is dismissed (<c>ui_cancel</c>).</summary>
        public event Action Closed;

        /// <summary>True while the panel is summoned.</summary>
        public bool IsOpen => Visible;

        public override void _Ready() {
            EditorLayout.FillParent(this);
            MouseFilter = MouseFilterEnum.Stop;
            FocusMode = FocusModeEnum.All;
            Visible = false;
            ZIndex = 100;
            BuildLayout();
        }

        void BuildLayout() {
            ColorRect backdrop = new ColorRect { Color = new Color(0.05f, 0.06f, 0.08f, 0.75f) };
            EditorLayout.FillParent(backdrop);
            backdrop.MouseFilter = MouseFilterEnum.Stop;
            AddChild(backdrop);

            PanelContainer panel = new PanelContainer { CustomMinimumSize = new Vector2(420f, 260f) };
            EditorLayout.CenterInParent(panel);
            AddChild(panel);

            VBoxContainer root = new VBoxContainer();
            panel.AddChild(root);

            Label title = new Label { Text = "Resize Level" };
            root.AddChild(title);

            currentSizeLabel = new Label();
            currentSizeLabel.AddThemeColorOverride("font_color", EditorTheme.TextDim);
            root.AddChild(currentSizeLabel);

            root.AddChild(new HSeparator());

            listBox = new VBoxContainer();
            root.AddChild(listBox);
        }

        /// <summary>Summon the panel against <paramref name="editSession"/>, seeding both steppers from the level's current size.</summary>
        public void Summon(LevelEditSession editSession) {
            session = editSession;
            pendingConfirm = false;
            lastFocusedRow = 0;
            Visible = true;
            Rebuild();
        }

        void Rebuild() {
            foreach (Node child in listBox.GetChildren())
                child.QueueFree();

            currentSizeLabel.Text = $"Current size: {session.Level.Width} x {session.Level.Height}";

            widthStepper = new DimensionStepper("Width", session.Level.Width);
            widthStepper.Adjusted += OnStepperAdjusted;
            listBox.AddChild(widthStepper);

            heightStepper = new DimensionStepper("Height", session.Level.Height);
            heightStepper.Adjusted += OnStepperAdjusted;
            listBox.AddChild(heightStepper);

            applyButton = new Button { Text = "Apply" };
            applyButton.Pressed += OnApplyPressed;
            listBox.AddChild(applyButton);

            List<List<Control>> rows = new List<List<Control>> {
                new List<Control> { widthStepper },
                new List<Control> { heightStepper },
                new List<Control> { applyButton },
            };
            FocusGrid.Contain(rows);
            TrackFocusPosition(rows);

            int restoreRow = Math.Clamp(lastFocusedRow, 0, rows.Count - 1);
            rows[restoreRow][0].CallDeferred(Control.MethodName.GrabFocus);
        }

        // Mirrors LayerManagerPanel.TrackFocusPosition: records which row last held focus so a Rebuild()
        // (only triggered here by a successful Apply) restores focus to roughly the same spot rather than
        // always snapping back to the Width stepper.
        void TrackFocusPosition(List<List<Control>> rows) {
            for (int r = 0; r < rows.Count; r++) {
                int capturedRow = r;
                rows[r][0].FocusEntered += () => lastFocusedRow = capturedRow;
            }
        }

        // A stepper's value changed — the pending width/height combo is different now, so any prior
        // "would drop painted cells" confirm state no longer necessarily applies to it. Reset the gate
        // rather than let a stale confirm carry over to a combo that was never checked.
        void OnStepperAdjusted() {
            if (!pendingConfirm)
                return;
            pendingConfirm = false;
            applyButton.Text = "Apply";
        }

        void OnApplyPressed() {
            int newWidth = widthStepper.Value;
            int newHeight = heightStepper.Value;
            if (newWidth == session.Level.Width && newHeight == session.Level.Height)
                return; // nothing to apply

            if (session.Level.WouldDropPaintedCells(newWidth, newHeight) && !pendingConfirm) {
                pendingConfirm = true;
                applyButton.Text = "Confirm Resize? (crops painted tiles)";
                return;
            }

            pendingConfirm = false;
            bool happened = session.Resize(newWidth, newHeight);
            if (happened) {
                GD.Print($"LevelResizePanel: resized level to {newWidth}x{newHeight}.");
                LevelModelChanged?.Invoke();
            }
            Rebuild();
        }

        public override void _GuiInput(InputEvent @event) {
            if (!Visible)
                return;

            if (@event.IsActionPressed("ui_cancel")) {
                AcceptEvent();
                Close();
            }
        }

        // Belt-and-suspenders close path — see LayerManagerPanel._UnhandledInput for why this is needed:
        // Godot delivers a keyboard/action GUI input event only to the exact focused Control, so ui_cancel
        // pressed while a row button holds focus never reaches _GuiInput above; it falls through to
        // unhandled input instead, where this catches it before LevelEditor's own _UnhandledInput (which
        // would otherwise no-op while a modal is open, per its resizePanel.IsOpen guard).
        public override void _UnhandledInput(InputEvent @event) {
            if (!Visible || @event.IsEcho())
                return;

            if (@event.IsActionPressed("ui_cancel")) {
                Close();
                GetViewport().SetInputAsHandled();
            }
        }

        void Close() {
            Visible = false;
            pendingConfirm = false;
            Closed?.Invoke();
        }

        // A small self-drawn focusable Control (not a Button) so left/right can adjust its value directly
        // rather than fighting Button's own input handling — safe here (unlike the layer panel's Scroll
        // stepper) because each instance is the only column in its row, so left/right has no spatial-nav
        // duty to preserve.
        sealed partial class DimensionStepper : Control {

            readonly string label;
            // Edge-triggers analog-stick left/right so a held stick steps once per deflection like the
            // D-pad, instead of every motion frame (DiVoid #7576) — see AnalogStepGate.
            readonly AnalogStepGate analogGate = new();

            /// <summary>Raised whenever the value changes via a stepper press.</summary>
            public event Action Adjusted;

            public int Value { get; private set; }

            public DimensionStepper(string label, int initialValue) {
                this.label = label;
                Value = initialValue;
            }

            public override void _Ready() {
                FocusMode = FocusModeEnum.All;
                MouseFilter = MouseFilterEnum.Stop;
                CustomMinimumSize = new Vector2(260f, 32f);
                FocusEntered += OnFocusEntered;
                FocusExited += QueueRedraw;
            }

            // Re-baseline the analog gate to the stick's CURRENT position every time this stepper (re)gains
            // focus — including the very first grab right after the panel opens. This is what fixes "opened
            // the Resize panel with the stick still deflected from aiming the radial": if the stick is
            // already pushed left/right at that moment, priming records it as the baseline rather than a
            // fresh edge, so nothing steps until the stick returns to neutral and is pushed again — the same
            // one-press-one-step feel the D-pad already has.
            void OnFocusEntered() {
                analogGate.Prime(Godot.Input.IsActionPressed("ui_left"), Godot.Input.IsActionPressed("ui_right"));
                QueueRedraw();
            }

            public override void _GuiInput(InputEvent @event) {
                if (@event is InputEventMouseButton button && button.Pressed) {
                    GrabFocus();
                    AcceptEvent();
                    return;
                }

                // Analog stick (left-stick horizontal axis): route through the edge-trigger gate rather than
                // reacting to the raw ui_left/ui_right pressed state directly, which Godot reports true on
                // essentially every frame the stick stays deflected (unlike a D-pad button press, which it
                // never echoes) — see AnalogStepGate's doc comment. Other axes (vertical stick) fall through
                // unhandled so up/down focus navigation between the width/height rows still works.
                if (@event is InputEventJoypadMotion motion && motion.Axis == JoyAxis.LeftX) {
                    int step = analogGate.Poll(Godot.Input.IsActionPressed("ui_left"), Godot.Input.IsActionPressed("ui_right"));
                    AcceptEvent();
                    if (step != 0)
                        Adjust(step);
                    return;
                }

                if (@event.IsActionPressed("ui_left")) {
                    AcceptEvent();
                    Adjust(-1);
                } else if (@event.IsActionPressed("ui_right")) {
                    AcceptEvent();
                    Adjust(+1);
                }
                // ui_accept/ui_cancel fall through unaccepted: ui_cancel bubbles to the panel's own
                // _UnhandledInput and closes it, exactly as on every other control in the grid.
            }

            void Adjust(int direction) {
                int stepped = GridDimensionRules.Step(Value, direction);
                if (stepped == Value)
                    return;
                Value = stepped;
                QueueRedraw();
                Adjusted?.Invoke();
            }

            public override void _Draw() {
                Rect2 rect = new Rect2(Vector2.Zero, Size);
                DrawRect(rect, EditorTheme.PanelRaised);
                if (HasFocus())
                    DrawRect(rect, EditorTheme.Accent, false, 2f);

                Font font = GetThemeDefaultFont();
                int fontSize = GetThemeDefaultFontSize();
                DrawString(font, new Vector2(8f, Size.Y * 0.5f + 5f), $"◄ {label}: {Value} ►",
                    HorizontalAlignment.Left, Size.X - 16f, fontSize, EditorTheme.Text);
            }
        }
    }
}
