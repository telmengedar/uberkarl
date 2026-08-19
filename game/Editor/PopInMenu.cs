using System;
using Godot;
using Uberkarl.Editor.Input;

namespace Uberkarl {

    /// <summary>
    /// The pop-in / hold-to-reveal radial menu overlay. It is a thin front-end: it takes a device-neutral
    /// <see cref="RadialMenuModel"/> (built by the controller from current editor state), draws its wedges
    /// around a centre point, tracks the live aim direction the controller feeds it (stick / arrows / mouse
    /// offset), and on commit raises the highlighted wedge's <see cref="MenuOutcome"/> — which the
    /// controller dispatches onto the editor's existing operations. It owns no edit logic and no selection
    /// state; the geometry and the aim→outcome routing live in the engine-agnostic core it delegates to.
    /// While open it holds focus so the canvas grid cursor stands still and the directional inputs steer the
    /// wheel instead of the cursor.
    /// </summary>
    public partial class PopInMenu : Control {

        const float WedgeRadius = 96f;   // distance from centre to each wedge chip
        const float ChipRadius = 30f;    // radius of a wedge chip
        const float IconSize = 34f;
        const float HitInnerRadius = ChipRadius;
        const float HitOuterRadius = WedgeRadius + ChipRadius;

        RadialMenuModel model;
        Func<int, Texture2D> iconProvider;
        Vector2 centerGlobal;
        int highlighted = -1;

        /// <summary>Raised when a wedge is committed (aim released/confirmed over a wedge).</summary>
        public event Action<MenuOutcome> Chosen;

        /// <summary>Raised when the menu is dismissed without a selection (an explicit cancel, or a commit with nothing highlighted).</summary>
        public event Action Cancelled;

        /// <summary>True while the menu is popped in.</summary>
        public bool IsOpen => Visible;

        public override void _Ready() {
            SetAnchorsPreset(LayoutPreset.FullRect);
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
        /// its texture; pass null for text-only menus.</summary>
        public void Open(RadialMenuModel menu, Vector2 centerGlobalPosition, Func<int, Texture2D> icons = null) {
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
            ApplyHighlight(model.IndexAt(direction.X, direction.Y));
        }

        /// <summary>Feed the current mouse offset from the menu centre, using the positional (pixel-radius) hit test.</summary>
        public void SetPositionalAim(Vector2 offset) {
            if (model == null)
                return;
            ApplyHighlight(RadialGeometry.PositionalIndexAt(offset.X, offset.Y, model.Count, HitInnerRadius, HitOuterRadius));
        }

        /// <summary>Steps the highlighted wedge by one position, wrapping — the latched menu's discrete directional stepping.</summary>
        public void StepHighlight(int direction) {
            if (model == null || model.Count == 0)
                return;
            ApplyHighlight(RadialHighlight.Step(highlighted, model.Count, direction));
        }

        /// <summary>Commit the currently highlighted wedge (or dismiss if the aim is on the neutral centre).</summary>
        public void Commit() {
            if (!Visible)
                return;
            MenuOutcome? outcome = model?.OutcomeAt(highlighted);
            Close();
            if (outcome is { } chosen)
                Chosen?.Invoke(chosen);
            else
                Cancelled?.Invoke();
        }

        /// <summary>Dismiss without committing.</summary>
        public void Cancel() {
            if (!Visible)
                return;
            Close();
            Cancelled?.Invoke();
        }

        void Close() {
            Visible = false;
            model = null;
            iconProvider = null;
            highlighted = -1;
        }

        void ApplyHighlight(int next) {
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
                Commit();
                AcceptEvent();
                return;
            }

            // Explicit confirm (gamepad A / Enter-Space via the paint action) and cancel (Esc / erase action).
            if (@event.IsActionPressed(EditorActionMap.NameOf(EditorAction.Paint)) || @event.IsActionPressed("ui_accept")) {
                Commit();
                AcceptEvent();
            } else if (@event.IsActionPressed(EditorActionMap.NameOf(EditorAction.Erase)) || @event.IsActionPressed("ui_cancel")) {
                Cancel();
                AcceptEvent();
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
            DrawCircle(center, WedgeRadius + ChipRadius + 8f, new Color(0.05f, 0.06f, 0.08f, 0.55f));

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

                RadialMenuItem item = model.Items[i];
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
