using System;
using System.Collections.Generic;
using Godot;
using Uberkarl.Editor;

namespace Uberkarl {

    /// <summary>
    /// The summoned, gamepad-first layer-management surface: create, delete, reorder layers and edit
    /// each layer's <c>collision</c>/<c>scrollSpeed</c>/<c>repeat</c> properties. Reuses the
    /// <see cref="PackageBrowser"/> scaffolding verbatim — full-rect dim backdrop, centered panel,
    /// grab-focus-on-summon (deferred, so it wins the race against <c>LevelEditor.EndMenu</c>'s
    /// synchronous canvas focus grab), <c>ui_cancel</c> closes. It holds no edit logic: every button
    /// calls the <see cref="LevelEditSession"/> directly (it holds the session reference, exactly as the
    /// browser holds its source), then rebuilds its own rows from the model's current truth and raises
    /// <see cref="LayerModelChanged"/> so the controller re-snapshots the canvas — the panel never
    /// touches the canvas or the level builder.
    ///
    /// Layout is one flat vertical focus chain — "+ Add Layer", then per layer (top = back, matching
    /// array/draw order): a header (=set active), a Collision toggle, a Scroll stepper, a Repeat toggle,
    /// Move up/down, and Delete — with every control's left/right focus neighbour pinned to itself so a
    /// stick/D-pad aim cannot bounce focus onto the canvas underneath. The Scroll stepper is the one
    /// control that consumes <c>ui_left</c>/<c>ui_right</c> itself (safe precisely because horizontal
    /// focus movement is pinned away). Delete requires a confirm press (layer ops are not undoable this
    /// increment, so an accidental delete would lose a whole painted layer).
    /// </summary>
    public partial class LayerManagerPanel : Control {

        LevelEditSession session;
        VBoxContainer listBox;

        int activeLayerIndex;
        int pendingDeleteIndex = -1;
        int lastFocusedIndex;

        /// <summary>Raised after any mutation (add/delete/move/property-set): "refresh the canvas + status."</summary>
        public event Action LayerModelChanged;

        /// <summary>Raised when the author picks a layer to paint on (a header press, or the layer a reorder/add/delete leaves active) — parity with the Layers radial pick.</summary>
        public event Action<int> ActiveLayerChosen;

        /// <summary>Raised when the panel is dismissed (<c>ui_cancel</c>).</summary>
        public event Action Closed;

        /// <summary>True while the panel is summoned.</summary>
        public bool IsOpen => Visible;

        public override void _Ready() {
            SetAnchorsPreset(LayoutPreset.FullRect);
            MouseFilter = MouseFilterEnum.Stop;
            FocusMode = FocusModeEnum.All;
            Visible = false;
            ZIndex = 100;
            BuildLayout();
        }

        void BuildLayout() {
            ColorRect backdrop = new ColorRect { Color = new Color(0.05f, 0.06f, 0.08f, 0.75f) };
            backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
            backdrop.MouseFilter = MouseFilterEnum.Stop;
            AddChild(backdrop);

            PanelContainer panel = new PanelContainer { CustomMinimumSize = new Vector2(520f, 420f) };
            panel.SetAnchorsPreset(LayoutPreset.Center);
            AddChild(panel);

            VBoxContainer root = new VBoxContainer();
            panel.AddChild(root);

            Label title = new Label { Text = "Manage Layers" };
            root.AddChild(title);

            ScrollContainer scroll = new ScrollContainer { CustomMinimumSize = new Vector2(500f, 360f) };
            root.AddChild(scroll);

            listBox = new VBoxContainer();
            scroll.AddChild(listBox);
        }

        /// <summary>Summon the panel against <paramref name="editSession"/>, highlighting <paramref name="activeLayer"/> as the current paint target.</summary>
        public void Summon(LevelEditSession editSession, int activeLayer) {
            session = editSession;
            activeLayerIndex = activeLayer;
            pendingDeleteIndex = -1;
            lastFocusedIndex = 0;
            Visible = true;
            Rebuild();
        }

        void Rebuild() {
            foreach (Node child in listBox.GetChildren())
                child.QueueFree();

            List<Control> chain = new List<Control>();

            Button addButton = new Button { Text = "+ Add Layer" };
            addButton.Pressed += OnAddPressed;
            listBox.AddChild(addButton);
            chain.Add(addButton);

            if (session != null) {
                IReadOnlyList<EditableLayer> layers = session.Level.Layers;
                for (int i = 0; i < layers.Count; i++)
                    BuildLayerRow(i, layers[i], chain);
            }

            ContainVerticalFocus(chain);

            int restore = Math.Clamp(lastFocusedIndex, 0, chain.Count - 1);
            if (chain.Count > 0)
                chain[restore].CallDeferred(Control.MethodName.GrabFocus);
            else
                CallDeferred(Control.MethodName.GrabFocus);
        }

        void BuildLayerRow(int index, EditableLayer layer, List<Control> chain) {
            HBoxContainer row = new HBoxContainer();
            listBox.AddChild(row);

            bool editable = LayerPropertyRules.Editable(layer.Collision);

            Button header = new Button {
                Text = index == activeLayerIndex ? $"> {layer.Name}" : layer.Name,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            header.AddThemeColorOverride("font_color", index == activeLayerIndex ? EditorTheme.Accent : EditorTheme.Text);
            header.Pressed += () => OnHeaderPressed(index);
            row.AddChild(header);
            chain.Add(header);

            Button collisionToggle = new Button {
                Text = layer.Collision ? "Collision: On" : "Collision: Off",
                ToggleMode = true,
                ButtonPressed = layer.Collision,
            };
            collisionToggle.Pressed += () => OnCollisionPressed(index, collisionToggle);
            row.AddChild(collisionToggle);
            chain.Add(collisionToggle);

            ScrollStepper stepper = new ScrollStepper();
            row.AddChild(stepper);
            stepper.Configure($"Scroll {layer.ScrollSpeed:0.00}x" + (editable ? string.Empty : " (locked)"), !editable);
            stepper.Stepped += direction => OnScrollStepped(index, direction);
            chain.Add(stepper);

            Button repeatToggle = new Button {
                Text = layer.Repeat ? "Repeat: On" : "Repeat: Off",
                ToggleMode = true,
                ButtonPressed = layer.Repeat,
                Disabled = !editable,
            };
            repeatToggle.Pressed += () => OnRepeatPressed(index, repeatToggle);
            row.AddChild(repeatToggle);
            chain.Add(repeatToggle);

            Button moveUp = new Button { Text = "Move ↑" };
            moveUp.Pressed += () => OnMovePressed(index, -1);
            row.AddChild(moveUp);
            chain.Add(moveUp);

            Button moveDown = new Button { Text = "Move ↓" };
            moveDown.Pressed += () => OnMovePressed(index, +1);
            row.AddChild(moveDown);
            chain.Add(moveDown);

            Button delete = new Button { Text = pendingDeleteIndex == index ? "Confirm Delete?" : "Delete" };
            delete.Pressed += () => OnDeletePressed(index);
            row.AddChild(delete);
            chain.Add(delete);

            if (!editable) {
                Label hint = new Label { Text = "  collision layers are world-locked and non-repeating" };
                hint.AddThemeColorOverride("font_color", EditorTheme.TextDim);
                listBox.AddChild(hint);
            }
        }

        // Vertical focus chain, ends and every horizontal side pinned to self — the same technique
        // PackageBrowser.ContainListFocus uses, generalised beyond plain buttons to whatever control kind
        // sits at each step (including the ScrollStepper, which alone wants ui_left/ui_right for itself).
        static void ContainVerticalFocus(List<Control> chain) {
            NodePath self = new NodePath(".");
            for (int i = 0; i < chain.Count; i++) {
                Control control = chain[i];
                control.FocusNeighborLeft = self;
                control.FocusNeighborRight = self;
                control.FocusNeighborTop = i > 0 ? control.GetPathTo(chain[i - 1]) : self;
                control.FocusNeighborBottom = i < chain.Count - 1 ? control.GetPathTo(chain[i + 1]) : self;
                control.FocusNext = self;
                control.FocusPrevious = self;
            }
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

        void OnHeaderPressed(int index) {
            pendingDeleteIndex = -1;
            activeLayerIndex = index;
            ActiveLayerChosen?.Invoke(index);
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

        void OnScrollStepped(int index, int direction) {
            pendingDeleteIndex = -1;
            LayerEditResult result = session.StepScrollSpeed(index, direction);
            if (result.Happened)
                LayerModelChanged?.Invoke();
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
            if (!Visible)
                return;

            if (@event.IsActionPressed("ui_cancel")) {
                AcceptEvent();
                Close();
            }
        }

        void Close() {
            Visible = false;
            pendingDeleteIndex = -1;
            Closed?.Invoke();
        }

        // The scroll-speed preset stepper: a small self-drawn focusable Control (not a Button) so it can
        // safely consume ui_left/ui_right itself to step the value without fighting Button's own input
        // handling. Locked (collision on) it still sits in the focus chain — greyed, and its input is
        // inert — rather than being skipped, so navigating past it never traps focus on an unfocusable
        // neighbour.
        sealed partial class ScrollStepper : Control {

            public event Action<int> Stepped;

            string label = string.Empty;
            bool locked;

            public override void _Ready() {
                FocusMode = FocusModeEnum.All;
                MouseFilter = MouseFilterEnum.Stop;
                CustomMinimumSize = new Vector2(150f, 32f);
                NodePath self = new NodePath(".");
                FocusNeighborLeft = self;
                FocusNeighborRight = self;
                FocusEntered += QueueRedraw;
                FocusExited += QueueRedraw;
            }

            public void Configure(string text, bool isLocked) {
                label = text;
                locked = isLocked;
                QueueRedraw();
            }

            public override void _GuiInput(InputEvent @event) {
                if (@event.IsActionPressed("ui_left")) {
                    AcceptEvent();
                    if (!locked)
                        Stepped?.Invoke(-1);
                } else if (@event.IsActionPressed("ui_right")) {
                    AcceptEvent();
                    if (!locked)
                        Stepped?.Invoke(+1);
                } else if (@event is InputEventMouseButton button && button.Pressed) {
                    GrabFocus();
                    AcceptEvent();
                }
            }

            public override void _Draw() {
                Rect2 rect = new Rect2(Vector2.Zero, Size);
                Color back = locked ? new Color(0.14f, 0.15f, 0.17f, 0.6f) : EditorTheme.PanelRaised;
                DrawRect(rect, back);
                if (HasFocus())
                    DrawRect(rect, EditorTheme.Accent, false, 2f);

                Font font = GetThemeDefaultFont();
                int fontSize = GetThemeDefaultFontSize();
                Color textColor = locked ? EditorTheme.TextDim : EditorTheme.Text;
                DrawString(font, new Vector2(8f, Size.Y * 0.5f + 5f), label,
                    HorizontalAlignment.Left, Size.X - 16f, fontSize, textColor);
            }
        }
    }
}
