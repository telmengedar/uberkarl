using System;
using Godot;
using Uberkarl.Editor.Input;

namespace Uberkarl {

    /// <summary>
    /// The pop-in / hold-to-reveal radial menu overlay. It is a passive front-end: it takes a
    /// device-neutral <see cref="MenuModel"/> (built by the controller from current editor state),
    /// draws its wedges around a centre point, and tracks the live aim direction the controller feeds it
    /// (stick / arrows / mouse offset). A click, a confirm, or a cancel only records intent —
    /// <see cref="ConsumeResolveRequest"/> / <see cref="ConsumeCancelRequest"/> / <see cref="CurrentOutcome"/>
    /// — polled once a frame by the controller, which owns the session that decides whether that intent
    /// actually closes the menu; only the controller ever calls <see cref="Close"/>. It owns no edit logic
    /// and no selection state; the geometry and the aim→outcome routing live in the engine-agnostic core it
    /// delegates to. While open it holds focus so the canvas grid cursor stands still and the directional
    /// inputs steer the wheel instead of the cursor.
    /// </summary>
    public partial class PopInMenu : Control {

        const float WedgeRadius = 96f;   // distance from centre to each wedge chip
        const float ChipRadius = 30f;    // radius of a wedge chip
        const float IconSize = 34f;
        const float HitInnerRadius = ChipRadius;
        const float HitOuterRadius = WedgeRadius + ChipRadius;

        /// <summary>Radius from the menu centre a placement clamp must keep clear of the viewport edge.</summary>
        public const float OuterMargin = WedgeRadius + ChipRadius + 8f;

        enum AimSource { None, Pointer, Directional }

        MenuModel model;
        Func<int, Texture2D> iconProvider;
        Vector2 centerGlobal;
        int highlighted = -1;
        AimSource highlightSource = AimSource.None;
        bool resolveRequested;
        bool cancelRequested;

        /// <summary>True while the menu is popped in.</summary>
        public bool IsOpen => Visible;

        /// <summary>The outcome the currently highlighted wedge would commit, or null with nothing highlighted.</summary>
        public MenuOutcome? CurrentOutcome => model?.OutcomeAt(highlighted);

        /// <summary>True while the current highlight was set by the pointer (mouse hover) rather than by a
        /// directional (stick/D-pad/arrows) reading — what <see cref="MenuAimArbitration.Resolve"/> needs to
        /// decide whether a neutral directional reading may clear the highlight.</summary>
        public bool HasPointerHighlight => highlighted >= 0 && highlightSource == AimSource.Pointer;

        public override void _Ready() {
            EditorLayout.FillParent(this);
            MouseFilter = MouseFilterEnum.Stop; // eat input under the wheel while it is open
            FocusMode = FocusModeEnum.All;
            // Pin every focus neighbour to self so a held stick / D-pad aim (which also fires Godot's
            // ui_left/right/up/down focus navigation) cannot bounce focus off the wheel onto the canvas
            // underneath while the menu is open — that bounce is what let the grid cursor move under the
            // radial. Focus stays here so the wheel's own confirm/cancel (_GuiInput) also keeps working.
            NodePath self = new NodePath(".");
            FocusNeighborLeft = self;
            FocusNeighborRight = self;
            FocusNeighborTop = self;
            FocusNeighborBottom = self;
            FocusNext = self;
            FocusPrevious = self;
            Visible = false;
            ZIndex = 100;
        }

        /// <summary>Pop the menu in around <paramref name="centerGlobalPosition"/>, rendering
        /// <paramref name="menu"/>. For a tile menu, <paramref name="icons"/> maps a wedge's tile index to
        /// its texture; pass null for text-only menus. See <see cref="MenuCatalog.EnforceRadialCap"/>.</summary>
        public void Open(MenuModel menu, Vector2 centerGlobalPosition, Func<int, Texture2D> icons = null) {
            MenuCatalog.EnforceRadialCap(menu);
            model = menu;
            iconProvider = icons;
            centerGlobal = centerGlobalPosition;
            highlighted = -1;
            Visible = true;
            GrabFocus();
            QueueRedraw();
        }

        /// <summary>Feed the current directional aim (stick/D-pad/arrows; screen convention +X right, +Y down). A direction inside the neutral centre highlights nothing.</summary>
        public void SetAim(Vector2 direction) {
            if (model == null)
                return;
            ApplyHighlight(model.IndexAt(direction.X, direction.Y), AimSource.Directional);
        }

        /// <summary>Feed the current mouse offset from the menu centre, using the positional (pixel-radius) hit test.</summary>
        public void SetPositionalAim(Vector2 offset) {
            if (model == null)
                return;
            ApplyHighlight(RadialGeometry.PositionalIndexAt(offset.X, offset.Y, model.Count, HitInnerRadius, HitOuterRadius), AimSource.Pointer);
        }

        /// <summary>Steps the highlighted wedge by one position, wrapping — the latched menu's discrete directional stepping.</summary>
        public void StepHighlight(int direction) {
            if (model == null || model.Count == 0)
                return;
            ApplyHighlight(RadialHighlight.Step(highlighted, model.Count, direction), AimSource.Directional);
        }

        /// <summary>Clears the highlight outright — the Transient-phase "neutral directional reading with no
        /// pointer highlight to protect" case (<see cref="MenuAimArbitration.AimAction.ClearHighlight"/>).</summary>
        public void ClearHighlight() => ApplyHighlight(-1, AimSource.None);

        /// <summary>Reads and clears whether a commit-style interaction (left-click, gamepad A, Enter/Space) happened since the last poll.</summary>
        public bool ConsumeResolveRequest() {
            bool requested = resolveRequested;
            resolveRequested = false;
            return requested;
        }

        /// <summary>Reads and clears whether an explicit cancel (gamepad B, Esc, the erase action) happened since the last poll.</summary>
        public bool ConsumeCancelRequest() {
            bool requested = cancelRequested;
            cancelRequested = false;
            return requested;
        }

        /// <summary>Tears the popped-in menu down; called only by the controller, once its session has decided to close.</summary>
        internal void Close() {
            Visible = false;
            model = null;
            iconProvider = null;
            highlighted = -1;
            highlightSource = AimSource.None;
            resolveRequested = false;
            cancelRequested = false;
        }

        void ApplyHighlight(int next, AimSource source) {
            highlightSource = next >= 0 ? source : AimSource.None;
            if (next != highlighted) {
                highlighted = next;
                QueueRedraw();
            }
        }

        public override void _GuiInput(InputEvent @event) {
            if (!Visible)
                return;

            if (@event is InputEventMouseMotion motion) {
                HoverAt(motion.Position);
                return;
            }

            if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click) {
                HoverAt(click.Position);
                resolveRequested = true;
                AcceptEvent();
                return;
            }

            // Explicit confirm (gamepad A / Enter-Space via the paint action) and cancel (Esc / erase action).
            if (@event.IsActionPressed(EditorActionMap.NameOf(EditorAction.Paint)) || @event.IsActionPressed("ui_accept")) {
                resolveRequested = true;
                AcceptEvent();
            } else if (@event.IsActionPressed(EditorActionMap.NameOf(EditorAction.Erase)) || @event.IsActionPressed("ui_cancel")) {
                cancelRequested = true;
                AcceptEvent();
            }
        }

        public override void _UnhandledInput(InputEvent @event) {
            if (!Visible || @event.IsEcho())
                return;

            if (@event.IsActionPressed(EditorActionMap.NameOf(EditorAction.Erase)) || @event.IsActionPressed("ui_cancel")) {
                cancelRequested = true;
                GetViewport().SetInputAsHandled();
            }
        }

        /// <summary>Updates the highlighted wedge from a mouse position local to this control, using the positional (pixel-radius) hit test — the latched menu's mouse hover.</summary>
        void HoverAt(Vector2 localPosition) {
            if (model == null)
                return;
            Vector2 offset = localPosition - (centerGlobal - GlobalPosition);
            SetPositionalAim(offset);
        }

        public override void _Draw() {
            if (model == null)
                return;

            Vector2 center = centerGlobal - GlobalPosition;
            int count = model.Count;

            // Soft focus disc behind the wheel — keeps the canvas readable underneath (the whole area stays
            // the edit canvas) while giving the wedges a backdrop to read against.
            DrawCircle(center, OuterMargin, new Color(0.05f, 0.06f, 0.08f, 0.55f));

            Font font = GetThemeDefaultFont();
            int fontSize = GetThemeDefaultFontSize();

            DrawString(font, center + new Vector2(-WedgeRadius, -ChipRadius - 14f), model.Title,
                HorizontalAlignment.Center, WedgeRadius * 2f, fontSize, new Color(1f, 0.85f, 0.2f));
            string hub = highlighted >= 0 && highlighted < count ? model.Items[highlighted].Label : "release to cancel";
            DrawString(font, center + new Vector2(-WedgeRadius, 4f), hub,
                HorizontalAlignment.Center, WedgeRadius * 2f, fontSize - 2, new Color(0.75f, 0.78f, 0.85f));

            for (int i = 0; i < count; i++) {
                (double dx, double dy) = RadialGeometry.WedgeDirection(i, count);
                Vector2 pos = center + new Vector2((float)dx, (float)dy) * WedgeRadius;
                bool active = i == highlighted;

                // Pointer from the hub to the highlighted wedge.
                if (active)
                    DrawLine(center, pos, new Color(1f, 0.85f, 0.2f, 0.6f), 2f);

                Color chipFill = active ? new Color(1f, 0.85f, 0.2f, 0.9f) : new Color(0.16f, 0.18f, 0.22f, 0.95f);
                DrawCircle(pos, ChipRadius, chipFill);
                DrawArc(pos, ChipRadius, 0f, Mathf.Tau, 24, new Color(1f, 0.85f, 0.2f, active ? 1f : 0.4f), active ? 2.5f : 1.5f);

                MenuItem item = model.Items[i];
                Texture2D icon = iconProvider?.Invoke(item.Outcome.Index);
                if (icon != null && item.Outcome.Kind == MenuOutcomeKind.SelectTile) {
                    Rect2 iconRect = new Rect2(pos - new Vector2(IconSize, IconSize) / 2f, new Vector2(IconSize, IconSize));
                    DrawTextureRect(icon, iconRect, false);
                } else {
                    Color textColor = active ? new Color(0.08f, 0.08f, 0.1f) : new Color(0.85f, 0.88f, 0.92f);
                    DrawString(font, pos + new Vector2(-ChipRadius, 4f), item.Label,
                        HorizontalAlignment.Center, ChipRadius * 2f, fontSize - 3, textColor);
                }
            }
        }
    }
}
