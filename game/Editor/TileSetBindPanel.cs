using System;
using System.Collections.Generic;
using Godot;
using Uberkarl.Editor;
using Uberkarl.Packages;

namespace Uberkarl {

    /// <summary>
    /// The summoned "bind a different shared tile set" list (DiVoid #7551 Phase 1b, design #7580): picks
    /// among the <c>tileset</c> resources already in the level's CURRENT package and rebinds the level to
    /// whichever one is chosen — the affordance that makes "many levels reference the same tileset" an
    /// author-reachable action, not just a save-time accident. Reuses <see cref="PackageBrowser"/>'s list
    /// scaffolding (full-rect dim backdrop, centered panel, vertical focus-chained <see cref="Button"/>
    /// rows, <c>ui_cancel</c> back/close) at the scope this one job needs, rather than extending the
    /// already-intricate load/save state machine in <see cref="PackageBrowser"/> itself.
    ///
    /// Scoped to the CURRENT package only (Phase 1 simplicity, matching <see cref="EditableLevelReader"/>'s
    /// existing same-package restriction on a level's tile set) — cross-package binding is not built.
    ///
    /// <b>Always opens</b> (DiVoid #7551 bugfix): the panel must give feedback on every "Bind Tileset…"
    /// press — the bindable siblings, the "no other tile sets" empty state (<see cref="Summon"/>, both
    /// already handled before the bug), OR, when the level isn't attached to a browsable package yet, the
    /// <see cref="SummonUnavailable"/> explanation. It must never silently do nothing.
    /// </summary>
    public partial class TileSetBindPanel : Control {

        const string NoSiblingsMessage = "No other tile sets in this package.";

        IPackageSource source;
        PackageHandle packageHandle;
        ResourceReference currentReference;
        IReadOnlyList<ResourceSummary> tileSets = Array.Empty<ResourceSummary>();

        Label titleLabel;
        Button closeButton;
        ScrollContainer scroll;
        VBoxContainer listBox;
        Label emptyLabel;

        /// <summary>Raised once a different tile set is chosen: the reference to bind, and the loaded, editable tile set itself.</summary>
        public event Action<ResourceReference, EditableTileSet> TileSetChosen;

        /// <summary>Raised when the panel is dismissed without choosing.</summary>
        public event Action Cancelled;

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

            PanelContainer panel = new PanelContainer { CustomMinimumSize = new Vector2(480f, 380f) };
            panel.SetAnchorsPreset(LayoutPreset.Center);
            AddChild(panel);

            VBoxContainer root = new VBoxContainer();
            panel.AddChild(root);

            HBoxContainer header = new HBoxContainer();
            root.AddChild(header);

            titleLabel = new Label { Text = "Bind Tileset", SizeFlagsHorizontal = SizeFlags.ExpandFill };
            titleLabel.AddThemeColorOverride("font_color", EditorTheme.Accent);
            header.AddChild(titleLabel);

            closeButton = new Button { Text = "✕ Close" };
            closeButton.Pressed += HandleCancel;
            NodePath self = new NodePath(".");
            closeButton.FocusNeighborLeft = self;
            closeButton.FocusNeighborRight = self;
            closeButton.FocusNeighborTop = self;
            closeButton.FocusNeighborBottom = self;
            header.AddChild(closeButton);

            root.AddChild(new HSeparator());

            scroll = new ScrollContainer { CustomMinimumSize = new Vector2(460f, 300f) };
            root.AddChild(scroll);

            listBox = new VBoxContainer();
            scroll.AddChild(listBox);

            emptyLabel = new Label { Visible = false, Text = NoSiblingsMessage };
            emptyLabel.AddThemeColorOverride("font_color", EditorTheme.TextDim);
            root.AddChild(emptyLabel);
        }

        /// <summary>Summon the panel, listing every <c>tileset</c> resource in <paramref name="handle"/>'s package other than <paramref name="current"/>.</summary>
        public void Summon(IPackageSource packageSource, PackageHandle handle, ResourceReference current) {
            source = packageSource;
            packageHandle = handle;
            currentReference = current;
            Visible = true;

            List<ResourceSummary> found = new List<ResourceSummary>();
            try {
                found = TileSetBindAvailability.SelectBindableSiblings(source.GetContents(handle), current);
            } catch (Exception exception) {
                GD.PrintErr($"TileSetBindPanel: {exception.GetType().Name}: {exception.Message}");
            }
            tileSets = found;

            emptyLabel.Text = NoSiblingsMessage;
            Rebuild();
        }

        /// <summary>
        /// Summon the panel in a "can't list siblings" state — e.g. the level under edit is not (yet)
        /// attached to a browsable package (DiVoid #7551 bugfix, Toni 2026-08-xx: "bind tileset does
        /// nothing"). Previously <c>LevelEditor</c> simply skipped calling <see cref="Summon"/> at all in
        /// this case, so "Bind Tileset…" silently did nothing — no panel, no feedback, indistinguishable
        /// from the action not being wired up at all. Every "Bind Tileset…" press now opens SOMETHING.
        /// </summary>
        public void SummonUnavailable(string reason) {
            source = null;
            packageHandle = default;
            currentReference = default;
            tileSets = Array.Empty<ResourceSummary>();
            Visible = true;

            emptyLabel.Text = reason;
            Rebuild();
        }

        void Rebuild() {
            foreach (Node child in listBox.GetChildren())
                child.QueueFree();

            emptyLabel.Visible = tileSets.Count == 0;
            scroll.Visible = tileSets.Count > 0;

            List<Button> buttons = new List<Button>(tileSets.Count);
            for (int i = 0; i < tileSets.Count; i++) {
                int index = i;
                ResourceSummary summary = tileSets[i];

                Button button = new Button {
                    Text = summary.DisplayName,
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    Alignment = HorizontalAlignment.Left,
                };
                button.Pressed += () => OnChosen(index);
                listBox.AddChild(button);
                buttons.Add(button);
            }

            NodePath self = new NodePath(".");
            for (int i = 0; i < buttons.Count; i++) {
                Button button = buttons[i];
                button.FocusNeighborLeft = self;
                button.FocusNeighborRight = self;
                button.FocusNeighborTop = i > 0 ? button.GetPathTo(buttons[i - 1]) : self;
                button.FocusNeighborBottom = i < buttons.Count - 1 ? button.GetPathTo(buttons[i + 1]) : self;
                button.FocusNext = self;
                button.FocusPrevious = self;
            }

            if (buttons.Count > 0)
                buttons[0].CallDeferred(Control.MethodName.GrabFocus);
            else
                CallDeferred(Control.MethodName.GrabFocus);
        }

        void OnChosen(int index) {
            if (index < 0 || index >= tileSets.Count)
                return;

            try {
                using Package package = source.Open(packageHandle);
                ResourceReference reference = ResourceReference.ToSelf(tileSets[index].Path);
                EditableTileSet tileSet = EditableTileSetReader.FromPackage(package, reference);
                Close();
                TileSetChosen?.Invoke(reference, tileSet);
            } catch (Exception exception) {
                GD.PrintErr($"TileSetBindPanel: {exception.GetType().Name}: {exception.Message}");
                Close();
                Cancelled?.Invoke();
            }
        }

        void HandleCancel() {
            Close();
            Cancelled?.Invoke();
        }

        void Close() => Visible = false;

        public override void _GuiInput(InputEvent @event) {
            if (!Visible)
                return;

            if (@event.IsActionPressed("ui_cancel")) {
                AcceptEvent();
                HandleCancel();
            }
        }

        public override void _UnhandledInput(InputEvent @event) {
            if (!Visible || @event.IsEcho())
                return;

            if (@event.IsActionPressed("ui_cancel")) {
                HandleCancel();
                GetViewport().SetInputAsHandled();
            }
        }
    }
}
