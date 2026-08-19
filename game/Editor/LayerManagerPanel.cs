using System;
using System.Collections.Generic;
using Godot;
using Uberkarl.Editor;
using Uberkarl.Editor.Input;

namespace Uberkarl {

    /// <summary>
    /// The summoned, gamepad-first layer-management surface: create, delete, reorder layers and edit
    /// each layer's <c>collision</c>/<c>scrollSpeed</c>/<c>repeat</c> properties. Reuses the
    /// <see cref="PackageBrowser"/> scaffolding verbatim — full-rect dim backdrop, centered panel,
    /// grab-focus-on-summon (deferred, so it wins the race against <c>LevelEditor.CloseMenu</c>'s
    /// synchronous canvas focus grab), <c>ui_cancel</c> closes. It holds no edit logic: every button
    /// calls the <see cref="LevelEditSession"/> directly (it holds the session reference, exactly as the
    /// browser holds its source), then rebuilds its own rows from the model's current truth and raises
    /// <see cref="LayerModelChanged"/> so the controller re-snapshots the canvas — the panel never
    /// touches the canvas or the level builder.
    ///
    /// Layout is a 2D control grid (Toni's playtest fix, DiVoid #7512): "+ Add Layer" is row 0, then per
    /// layer (top = back, matching array/draw order) a row of a header (=set active), a Collision toggle,
    /// a Scroll stepper, a Repeat toggle, Move up/down, and Delete. <b>Up/down moves to the same column
    /// in the adjacent row; left/right moves within the row</b> — real spatial neighbours built from the
    /// actual layout by <see cref="FocusGrid"/>, not one flat vertical chain — with every grid edge pinned
    /// to itself so a stick/D-pad aim cannot bounce focus onto the canvas underneath. The Scroll stepper no
    /// longer consumes left/right just because it is focused: it must first be entered via confirm
    /// (<c>ui_accept</c>), at which point it alone owns left/right to step the preset ladder until a second
    /// confirm commits or <c>ui_cancel</c> reverts — see <see cref="ScrollStepper"/>. Delete requires a
    /// confirm press (layer ops are not undoable this increment, so an accidental delete would lose a
    /// whole painted layer).
    ///
    /// Activating a row's header/name cell (<c>ui_accept</c> or a click — DiVoid #7513 for the keyboard,
    /// Toni's PR #19 playtest feedback for this wiring) summons the shared <see cref="OnScreenKeyboard"/>
    /// (attached via <see cref="AttachKeyboard"/>) seeded with the layer's current name; Done applies the
    /// new name via <see cref="LevelEditSession.RenameLayer"/>, Cancel leaves the model untouched. There is
    /// no separate Rename button: picking the active layer to paint on is already the Layers radial's job,
    /// so inside this management panel the header/name cell is free to mean exactly one thing.
    /// </summary>
    public partial class LayerManagerPanel : Control {

        LevelEditSession session;
        VBoxContainer listBox;
        OnScreenKeyboard keyboard;

        int activeLayerIndex;
        int pendingDeleteIndex = -1;
        int lastFocusedRow;
        int lastFocusedCol;

        /// <summary>Raised after any mutation (add/delete/move/property-set): "refresh the canvas + status."</summary>
        public event Action LayerModelChanged;

        /// <summary>Raised when a mutation (add/move/delete) leaves a different layer active — parity with the Layers radial pick. The header/name cell no longer raises this itself (PR #19 feedback): picking the active layer is the Layers radial's job, not this management panel's.</summary>
        public event Action<int> ActiveLayerChosen;

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

            PanelContainer panel = new PanelContainer { CustomMinimumSize = new Vector2(920f, 420f) };
            EditorLayout.CenterInParent(panel);
            AddChild(panel);

            VBoxContainer root = new VBoxContainer();
            panel.AddChild(root);

            Label title = new Label { Text = "Manage Layers" };
            root.AddChild(title);

            ScrollContainer scroll = new ScrollContainer { CustomMinimumSize = new Vector2(900f, 360f) };
            root.AddChild(scroll);

            listBox = new VBoxContainer();
            scroll.AddChild(listBox);
        }

        /// <summary>
        /// Attaches the shared <see cref="OnScreenKeyboard"/> the Rename button summons (DiVoid #7513).
        /// Called once by <see cref="LevelEditor"/> alongside construction, exactly like the panel itself
        /// is wired — the panel holds the reference and calls <see cref="OnScreenKeyboard.RequestText"/>
        /// directly, the same way it already holds <c>session</c> and calls it directly.
        /// </summary>
        public void AttachKeyboard(OnScreenKeyboard onScreenKeyboard) => keyboard = onScreenKeyboard;

        /// <summary>Summon the panel against <paramref name="editSession"/>, highlighting <paramref name="activeLayer"/> as the current paint target.</summary>
        public void Summon(LevelEditSession editSession, int activeLayer) {
            session = editSession;
            activeLayerIndex = activeLayer;
            pendingDeleteIndex = -1;
            lastFocusedRow = 0;
            lastFocusedCol = 0;
            Visible = true;
            Rebuild();
        }

        void Rebuild() {
            foreach (Node child in listBox.GetChildren())
                child.QueueFree();

            List<List<Control>> rows = new List<List<Control>>();

            Button addButton = new Button { Text = "+ Add Layer" };
            addButton.Pressed += OnAddPressed;
            listBox.AddChild(addButton);
            rows.Add(new List<Control> { addButton });

            if (session != null) {
                IReadOnlyList<EditableLayer> layers = session.Level.Layers;
                for (int i = 0; i < layers.Count; i++)
                    rows.Add(BuildLayerRow(i, layers[i]));
            }

            FocusGrid.Contain(rows);
            TrackFocusPosition(rows);

            int restoreRow = Math.Clamp(lastFocusedRow, 0, rows.Count - 1);
            int restoreCol = Math.Clamp(lastFocusedCol, 0, rows[restoreRow].Count - 1);
            rows[restoreRow][restoreCol].CallDeferred(Control.MethodName.GrabFocus);
        }

        // Records which grid cell last held focus (FocusEntered), so a Rebuild() triggered by a mutation
        // can restore focus to roughly the same spot instead of always snapping back to "+ Add Layer" —
        // every mutation rebuilds the whole row list from scratch, which would otherwise discard focus
        // entirely.
        void TrackFocusPosition(List<List<Control>> rows) {
            for (int r = 0; r < rows.Count; r++) {
                for (int c = 0; c < rows[r].Count; c++) {
                    int capturedRow = r;
                    int capturedCol = c;
                    rows[r][c].FocusEntered += () => {
                        lastFocusedRow = capturedRow;
                        lastFocusedCol = capturedCol;
                    };
                }
            }
        }

        List<Control> BuildLayerRow(int index, EditableLayer layer) {
            HBoxContainer row = new HBoxContainer();
            listBox.AddChild(row);

            List<Control> columns = new List<Control>();
            bool editable = LayerPropertyRules.Editable(layer.Collision);

            Button header = new Button {
                Text = index == activeLayerIndex ? $"> {layer.Name}" : layer.Name,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            header.AddThemeColorOverride("font_color", index == activeLayerIndex ? EditorTheme.Accent : EditorTheme.Text);
            header.Pressed += () => OnRenamePressed(index);
            row.AddChild(header);
            columns.Add(header);

            Button collisionToggle = new Button {
                Text = layer.Collision ? "Collision: On" : "Collision: Off",
                ToggleMode = true,
                ButtonPressed = layer.Collision,
            };
            collisionToggle.Pressed += () => OnCollisionPressed(index, collisionToggle);
            row.AddChild(collisionToggle);
            columns.Add(collisionToggle);

            ScrollStepper stepper = new ScrollStepper();
            row.AddChild(stepper);
            stepper.Configure(layer.ScrollSpeed, !editable);
            stepper.Committed += value => OnScrollCommitted(index, value);
            columns.Add(stepper);

            // Intentionally NOT Button.Disabled: Godot's default ui_down/ui_up focus navigation cannot
            // traverse THROUGH a disabled Control — it silently traps focus there instead of advancing to
            // the next chain member. The real gating already lives server-side (LevelEditSession.SetRepeat
            // no-ops while collision is on); here we only grey the label so the lock reads visually while
            // keeping the control a normal, navigable stop in the grid.
            Button repeatToggle = new Button {
                Text = layer.Repeat ? "Repeat: On" : "Repeat: Off",
                ToggleMode = true,
                ButtonPressed = layer.Repeat,
            };
            if (!editable)
                repeatToggle.AddThemeColorOverride("font_color", EditorTheme.TextDim);
            repeatToggle.Pressed += () => OnRepeatPressed(index, repeatToggle);
            row.AddChild(repeatToggle);
            columns.Add(repeatToggle);

            Button moveUp = new Button { Text = "Move ↑" };
            moveUp.Pressed += () => OnMovePressed(index, -1);
            row.AddChild(moveUp);
            columns.Add(moveUp);

            Button moveDown = new Button { Text = "Move ↓" };
            moveDown.Pressed += () => OnMovePressed(index, +1);
            row.AddChild(moveDown);
            columns.Add(moveDown);

            Button delete = new Button { Text = pendingDeleteIndex == index ? "Confirm Delete?" : "Delete" };
            delete.Pressed += () => OnDeletePressed(index);
            row.AddChild(delete);
            columns.Add(delete);

            if (!editable) {
                Label hint = new Label { Text = "  collision layers are world-locked and non-repeating" };
                hint.AddThemeColorOverride("font_color", EditorTheme.TextDim);
                listBox.AddChild(hint);
            }

            return columns;
        }

        void OnAddPressed() {
            pendingDeleteIndex = -1;
            LayerEditResult result = session.AddLayer();
            GD.Print($"LayerManagerPanel: added layer {result.LayerIndex}.");
            activeLayerIndex = result.LayerIndex;
            ActiveLayerChosen?.Invoke(result.LayerIndex);
            LayerModelChanged?.Invoke();
            Rebuild();
        }

        // The row's header/name cell (DiVoid #7513 for the keyboard itself; PR #19 playtest feedback for
        // wiring it here instead of a separate Rename button) opens the shared keyboard seeded with the
        // layer's current name. It intentionally does NOT set the active layer any more — that's the Layers
        // radial's job, per Toni: "selecting the layer in layer management does not really make sense to
        // me." Nothing happens without a keyboard attached (defensive; LevelEditor always attaches one) or a
        // session. Cancel simply never invokes the callback below — no model call to undo.
        void OnRenamePressed(int index) {
            if (session == null || keyboard == null)
                return;

            pendingDeleteIndex = -1;
            string currentName = session.Level.Layers[index].Name;
            keyboard.RequestText($"Rename '{currentName}'", currentName, newName => ApplyRename(index, newName));
        }

        void ApplyRename(int index, string newName) {
            LayerEditResult result = session.RenameLayer(index, newName);
            if (result.Happened) {
                GD.Print($"LayerManagerPanel: renamed layer {index} to '{newName}'.");
                LayerModelChanged?.Invoke();
            }
            Rebuild();
        }

        void OnCollisionPressed(int index, Button toggle) {
            pendingDeleteIndex = -1;
            LayerEditResult result = session.SetCollision(index, toggle.ButtonPressed);
            if (result.Happened) {
                GD.Print($"LayerManagerPanel: layer {index} collision set to {toggle.ButtonPressed}.");
                LayerModelChanged?.Invoke();
            }
            Rebuild();
        }

        void OnScrollCommitted(int index, float value) {
            pendingDeleteIndex = -1;
            LayerEditResult result = session.SetScrollSpeed(index, value);
            if (result.Happened) {
                GD.Print($"LayerManagerPanel: layer {index} scroll speed set to {value:0.00}x.");
                LayerModelChanged?.Invoke();
            }
            Rebuild();
        }

        void OnRepeatPressed(int index, Button toggle) {
            pendingDeleteIndex = -1;
            LayerEditResult result = session.SetRepeat(index, toggle.ButtonPressed);
            if (result.Happened) {
                GD.Print($"LayerManagerPanel: layer {index} repeat set to {toggle.ButtonPressed}.");
                LayerModelChanged?.Invoke();
            }
            Rebuild();
        }

        void OnMovePressed(int index, int direction) {
            pendingDeleteIndex = -1;
            LayerEditResult result = session.MoveLayer(index, direction);
            if (result.Happened) {
                GD.Print($"LayerManagerPanel: moved layer {index} to {result.LayerIndex}.");
                activeLayerIndex = result.LayerIndex;
                ActiveLayerChosen?.Invoke(result.LayerIndex);
                LayerModelChanged?.Invoke();
            }
            Rebuild();
        }

        void OnDeletePressed(int index) {
            if (pendingDeleteIndex != index) {
                pendingDeleteIndex = index;
                Rebuild();
                return;
            }

            pendingDeleteIndex = -1;
            LayerEditResult result = session.DeleteLayer(index);
            if (result.Happened) {
                GD.Print($"LayerManagerPanel: deleted layer {index}.");
                activeLayerIndex = result.LayerIndex;
                ActiveLayerChosen?.Invoke(result.LayerIndex);
                LayerModelChanged?.Invoke();
            } else {
                GD.Print("LayerManagerPanel: cannot delete the last layer.");
            }
            Rebuild();
        }

        public override void _GuiInput(InputEvent @event) {
            if (!Visible || (keyboard != null && keyboard.IsOpen))
                return;

            if (@event.IsActionPressed("ui_cancel")) {
                AcceptEvent();
                Close();
            }
        }

        // Belt-and-suspenders close path. _GuiInput only ever reaches the exact focused Control — Godot
        // does not bubble a keyboard/action GUI event up through ancestor Controls the way a mouse event's
        // hit-test chain does. Since the panel's chain almost always has a Button (not the panel itself)
        // focused, ui_cancel pressed on that Button never reaches the _GuiInput override above; it falls
        // through to unhandled input instead, where this catches it and marks it handled before it can
        // reach LevelEditor's own _UnhandledInput (which would otherwise treat it as a no-op while a modal
        // is open, per its layerManager.IsOpen guard). The Scroll stepper consumes ui_cancel itself while
        // it is mid-edit (exit edit mode without closing the panel), so this only ever sees ui_cancel that
        // was NOT claimed by an in-progress edit.
        public override void _UnhandledInput(InputEvent @event) {
            if (!Visible || @event.IsEcho() || (keyboard != null && keyboard.IsOpen))
                return;

            if (@event.IsActionPressed("ui_cancel")) {
                Close();
                GetViewport().SetInputAsHandled();
            }
        }

        void Close() {
            Visible = false;
            pendingDeleteIndex = -1;
            Closed?.Invoke();
        }

        // The scroll-speed preset stepper: a small self-drawn focusable Control (not a Button) so it can
        // gate ui_left/ui_right behind an explicit edit mode instead of fighting Button's own input
        // handling. Merely focused, it is inert to left/right — those fall through unaccepted so
        // FocusGrid's spatial neighbours handle them (Toni's playtest fix: left/right must stay free for
        // navigation until the author deliberately enters edit mode). ui_accept enters edit mode (visually
        // highlighted) starting from the committed value; while editing, left/right step a LOCAL pending
        // value via SteppedValueEditor/ScrollSpeedLadder — the model is untouched until a second ui_accept
        // commits it (raising Committed); ui_cancel while editing discards the pending value and reverts to
        // the committed display with no model call at all. Locked (collision on) it still sits in the grid
        // — greyed, and its input (including entering edit mode) is inert — rather than being skipped, so
        // navigating past it never traps focus on an unfocusable neighbour.
        sealed partial class ScrollStepper : Control {

            /// <summary>Raised once, on a successful commit, with the final absolute scroll speed to apply to the model.</summary>
            public event Action<float> Committed;

            readonly SteppedValueEditor<float> edit = new(ScrollSpeedLadder.Step);
            // Edge-triggers analog-stick left/right while editing so a held stick steps once per
            // deflection like the D-pad, instead of every motion frame (DiVoid #7576) — see AnalogStepGate.
            readonly AnalogStepGate analogGate = new();

            float committedValue;
            bool locked;

            public override void _Ready() {
                FocusMode = FocusModeEnum.All;
                MouseFilter = MouseFilterEnum.Stop;
                CustomMinimumSize = new Vector2(150f, 32f);
                FocusEntered += QueueRedraw;
                FocusExited += QueueRedraw;
            }

            public void Configure(float scrollSpeed, bool isLocked) {
                committedValue = scrollSpeed;
                locked = isLocked;
                QueueRedraw();
            }

            public override void _GuiInput(InputEvent @event) {
                if (@event is InputEventMouseButton button && button.Pressed) {
                    GrabFocus();
                    AcceptEvent();
                    return;
                }

                // Locked stays a normal, focusable grid stop (mouse click above still works) but every
                // value-editing gesture is inert — collision governs scroll speed while it is on.
                if (locked)
                    return;

                if (@event.IsActionPressed("ui_accept")) {
                    AcceptEvent();
                    if (edit.IsEditing) {
                        edit.TryCommit(out float value);
                        committedValue = value;
                        Committed?.Invoke(value);
                    } else {
                        edit.Enter(committedValue);
                        // Re-baseline the analog gate to the stick's CURRENT position right as editing
                        // starts (the edit-mode boundary is this control's equivalent of the resize panel's
                        // "just gained focus" moment) — a stick still deflected from navigating here becomes
                        // the baseline, not a fresh edge, so it steps only once released and re-deflected.
                        analogGate.Prime(Godot.Input.IsActionPressed("ui_left"), Godot.Input.IsActionPressed("ui_right"));
                    }
                    QueueRedraw();
                } else if (edit.IsEditing && @event is InputEventJoypadMotion motion && motion.Axis == JoyAxis.LeftX) {
                    // Analog stick: edge-triggered discrete step via AnalogStepGate — see DimensionStepper
                    // for why raw ui_left/ui_right pressed-state alone fires every motion frame while a
                    // D-pad button does not. Other axes fall through unhandled so spatial nav still works.
                    int step = analogGate.Poll(Godot.Input.IsActionPressed("ui_left"), Godot.Input.IsActionPressed("ui_right"));
                    AcceptEvent();
                    if (step != 0) {
                        edit.Adjust(step);
                        QueueRedraw();
                    }
                } else if (edit.IsEditing && @event.IsActionPressed("ui_left")) {
                    AcceptEvent();
                    edit.Adjust(-1);
                    QueueRedraw();
                } else if (edit.IsEditing && @event.IsActionPressed("ui_right")) {
                    AcceptEvent();
                    edit.Adjust(+1);
                    QueueRedraw();
                } else if (edit.IsEditing && @event.IsActionPressed("ui_cancel")) {
                    AcceptEvent();
                    edit.Cancel();
                    QueueRedraw();
                }
                // Not editing: ui_left/ui_right/ui_cancel fall through unaccepted — ui_left/right resolve to
                // FocusGrid's spatial neighbours (spatial nav), ui_cancel bubbles to the panel's own
                // _UnhandledInput and closes it, exactly as on every other control in the grid.
            }

            public override void _Draw() {
                Rect2 rect = new Rect2(Vector2.Zero, Size);
                Color panelRaised = EditorTheme.PanelRaised;
                Color editingBack = new Color(panelRaised.R + 0.10f, panelRaised.G + 0.10f, panelRaised.B + 0.10f);
                Color back = locked ? new Color(0.14f, 0.15f, 0.17f, 0.6f) : edit.IsEditing ? editingBack : panelRaised;
                DrawRect(rect, back);

                if (edit.IsEditing)
                    DrawRect(rect, EditorTheme.Accent, false, 3f);
                else if (HasFocus())
                    DrawRect(rect, EditorTheme.Accent, false, 2f);

                Font font = GetThemeDefaultFont();
                int fontSize = GetThemeDefaultFontSize();
                Color textColor = locked ? EditorTheme.TextDim : EditorTheme.Text;
                string label = locked
                    ? $"Scroll {committedValue:0.00}x (locked)"
                    : edit.IsEditing ? $"◄ {edit.PendingValue:0.00}x ►" : $"Scroll {committedValue:0.00}x";
                DrawString(font, new Vector2(8f, Size.Y * 0.5f + 5f), label,
                    HorizontalAlignment.Left, Size.X - 16f, fontSize, textColor);
            }
        }
    }
}
