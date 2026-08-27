using System;
using System.Collections.Generic;
using Godot;
using Uberkarl.Content;
using Uberkarl.Editor;

namespace Uberkarl {

    /// <summary>The summoned object-type authoring surface: define, rename, toggle collision role, replace graphic, and remove object types.</summary>
    public partial class ObjectSetEditor : Control {

        ObjectSetEditSession session;
        EditableLevel level;
        VBoxContainer listBox;
        ScrollContainer scroll;
        OnScreenKeyboard keyboard;
        FileDialog importDialog;

        readonly FocusMemory focusMemory = new FocusMemory();

        int tileSize = 1;

        string pendingRemoveId;
        string pendingReplaceId;

        /// <summary>Raised after any mutation.</summary>
        public event Action ObjectSetModelChanged;

        /// <summary>Raised when the panel is dismissed.</summary>
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
            BuildImportDialog();
        }

        void BuildLayout() {
            ColorRect backdrop = new ColorRect { Color = new Color(0.05f, 0.06f, 0.08f, 0.75f) };
            EditorLayout.FillParent(backdrop);
            backdrop.MouseFilter = MouseFilterEnum.Stop;
            AddChild(backdrop);

            PanelContainer panel = new PanelContainer { CustomMinimumSize = new Vector2(760f, 420f) };
            EditorLayout.CenterInParent(panel);
            AddChild(panel);

            VBoxContainer root = new VBoxContainer();
            panel.AddChild(root);

            Label title = new Label { Text = "Edit Objects" };
            root.AddChild(title);

            scroll = new ScrollContainer { CustomMinimumSize = new Vector2(740f, 360f), FollowFocus = true };
            root.AddChild(scroll);

            listBox = new VBoxContainer();
            scroll.AddChild(listBox);
        }

        void BuildImportDialog() {
            importDialog = new FileDialog {
                FileMode = FileDialog.FileModeEnum.OpenFile,
                Access = FileDialog.AccessEnum.Filesystem,
                Title = "Import Object Graphic (PNG)",
                Size = new Vector2I(720, 480),
            };
            importDialog.AddFilter("*.png", "PNG Images");
            importDialog.FileSelected += OnGraphicFileSelected;
            AddChild(importDialog);
        }

        /// <summary>Attaches the shared on-screen keyboard the rename affordance summons.</summary>
        public void AttachKeyboard(OnScreenKeyboard onScreenKeyboard) => keyboard = onScreenKeyboard;

        /// <summary>Summons the panel against <paramref name="editSession"/>. <paramref name="editLevel"/> is read-only. <paramref name="levelTileSize"/> is the size a newly imported graphic is scaled to.</summary>
        public void Summon(ObjectSetEditSession editSession, EditableLevel editLevel, int levelTileSize) {
            session = editSession;
            level = editLevel;
            tileSize = levelTileSize;
            pendingRemoveId = null;
            pendingReplaceId = null;
            focusMemory.Reset();
            Visible = true;
            Rebuild();
        }

        void Rebuild() {
            foreach (Node child in listBox.GetChildren())
                child.QueueFree();

            List<List<Control>> rows = new List<List<Control>>();

            Button addButton = new Button { Text = "+ Add Object (import PNG)…" };
            addButton.Pressed += OnAddPressed;
            listBox.AddChild(addButton);
            rows.Add(new List<Control> { addButton });

            if (session != null) {
                foreach (EditableObjectType type in session.Types)
                    rows.Add(BuildTypeRow(type));
            }

            FocusGrid.Contain(rows);
            focusMemory.Track(rows);
            EnsureVisibleOnFocus(rows);
            focusMemory.Restore(rows);
        }

        void EnsureVisibleOnFocus(List<List<Control>> rows) {
            foreach (List<Control> row in rows) {
                foreach (Control control in row) {
                    Control target = control;
                    target.FocusEntered += () => scroll.EnsureControlVisible(target);
                }
            }
        }

        List<Control> BuildTypeRow(EditableObjectType type) {
            HBoxContainer row = new HBoxContainer();
            listBox.AddChild(row);

            List<Control> columns = new List<Control>();
            string id = type.Definition.Id;

            TextureRect thumb = new TextureRect {
                CustomMinimumSize = new Vector2(28f, 28f),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                Texture = LoadTexture(type.Graphic),
            };
            row.AddChild(thumb);

            string label = string.IsNullOrEmpty(type.Definition.Name) ? $"Object #{id}" : $"{type.Definition.Name} ({id})";
            Button nameButton = new Button { Text = label, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            nameButton.Pressed += () => OnRenamePressed(id);
            row.AddChild(nameButton);
            columns.Add(nameButton);

            Button roleButton = new Button { Text = type.Definition.CollisionRole == ObjectCollisionRole.Solid ? "Solid" : "Passthrough" };
            roleButton.Pressed += () => OnToggleCollisionRolePressed(id);
            row.AddChild(roleButton);
            columns.Add(roleButton);

            Button replaceButton = new Button { Text = "Replace PNG…" };
            replaceButton.Pressed += () => OnReplaceGraphicPressed(id);
            row.AddChild(replaceButton);
            columns.Add(replaceButton);

            int placementCount = level != null ? level.CountPlacementsOfType(session.Reference, id) : 0;
            Button removeButton;
            if (placementCount > 0) {
                removeButton = new Button { Text = $"In use ×{placementCount}" };
                removeButton.Pressed += () => OnRemoveInUsePressed(id, placementCount);
            } else {
                removeButton = new Button { Text = pendingRemoveId == id ? "Confirm Remove?" : "Remove" };
                removeButton.Pressed += () => OnRemovePressed(id);
            }
            row.AddChild(removeButton);
            columns.Add(removeButton);

            return columns;
        }

        void OnAddPressed() {
            ClearPendingConfirms();
            importDialog.PopupCentered();
        }

        void OnReplaceGraphicPressed(string id) {
            ClearPendingConfirms();
            pendingReplaceId = id;
            importDialog.PopupCentered();
        }

        void OnGraphicFileSelected(string path) {
            if (session == null)
                return;

            byte[] bytes = Godot.FileAccess.GetFileAsBytes(path);
            if (bytes == null || bytes.Length == 0) {
                GD.PrintErr($"ObjectSetEditor: could not read '{path}'.");
                return;
            }

            Image probe = new Image();
            if (probe.LoadPngFromBuffer(bytes) != Error.Ok) {
                GD.PrintErr($"ObjectSetEditor: '{path}' is not a readable PNG.");
                return;
            }

            int sourceWidth = probe.GetWidth();
            int sourceHeight = probe.GetHeight();
            if (TileGraphicImport.NeedsResize(sourceWidth, sourceHeight, tileSize)) {
                probe.Resize(tileSize, tileSize, Image.Interpolation.Lanczos);
                bytes = probe.SavePngToBuffer();
                GD.Print($"ObjectSetEditor: '{path}' was {sourceWidth}x{sourceHeight}, scaled to {tileSize}x{tileSize} to fill the object.");
            }

            string replacing = pendingReplaceId;
            pendingReplaceId = null;

            if (replacing == null) {
                string id = session.AddType(bytes, ObjectCollisionRole.Solid);
                GD.Print($"ObjectSetEditor: imported object #{id} from '{path}'.");
            } else if (session.ReplaceGraphic(replacing, bytes)) {
                GD.Print($"ObjectSetEditor: replaced object #{replacing}'s graphic from '{path}'.");
            } else {
                GD.PrintErr($"ObjectSetEditor: could not replace object #{replacing}'s graphic (it no longer exists).");
            }

            ObjectSetModelChanged?.Invoke();
            Rebuild();
        }

        void OnRenamePressed(string id) {
            if (session == null || keyboard == null)
                return;

            ClearPendingConfirms();
            EditableObjectType type = Find(id);
            string currentName = type?.Definition.Name ?? string.Empty;
            keyboard.RequestText($"Rename object #{id}", currentName, newName => ApplyRename(id, newName));
        }

        void ApplyRename(string id, string newName) {
            if (session.RenameType(id, newName)) {
                GD.Print($"ObjectSetEditor: renamed object #{id} to '{newName}'.");
                ObjectSetModelChanged?.Invoke();
            }
            Rebuild();
        }

        void OnToggleCollisionRolePressed(string id) {
            ClearPendingConfirms();
            EditableObjectType type = Find(id);
            if (type == null)
                return;

            ObjectCollisionRole next = type.Definition.CollisionRole == ObjectCollisionRole.Solid
                ? ObjectCollisionRole.Passthrough
                : ObjectCollisionRole.Solid;
            if (session.SetCollisionRole(id, next)) {
                GD.Print($"ObjectSetEditor: object #{id} collision role set to {next}.");
                ObjectSetModelChanged?.Invoke();
            }
            Rebuild();
        }

        void OnRemovePressed(string id) {
            if (pendingRemoveId != id) {
                ClearPendingConfirms();
                pendingRemoveId = id;
                Rebuild();
                return;
            }

            ClearPendingConfirms();
            if (session.RemoveType(id)) {
                GD.Print($"ObjectSetEditor: removed object #{id}.");
                ObjectSetModelChanged?.Invoke();
            }
            Rebuild();
        }

        static void OnRemoveInUsePressed(string id, int count) =>
            GD.Print($"ObjectSetEditor: object #{id} is in use by {count} placement(s) in this level; remove them first.");

        void ClearPendingConfirms() {
            pendingRemoveId = null;
            pendingReplaceId = null;
        }

        EditableObjectType Find(string id) {
            if (session == null)
                return null;
            foreach (EditableObjectType type in session.Types)
                if (type.Definition.Id == id)
                    return type;
            return null;
        }

        static ImageTexture LoadTexture(byte[] png) {
            Image image = new Image();
            if (image.LoadPngFromBuffer(png) != Error.Ok)
                return null;
            return ImageTexture.CreateFromImage(image);
        }

        public override void _GuiInput(InputEvent @event) {
            if (!Visible || (keyboard != null && keyboard.IsOpen) || (importDialog != null && importDialog.Visible))
                return;

            if (@event.IsActionPressed("ui_cancel")) {
                AcceptEvent();
                Close();
            }
        }

        public override void _UnhandledInput(InputEvent @event) {
            if (!Visible || @event.IsEcho() || (keyboard != null && keyboard.IsOpen) || (importDialog != null && importDialog.Visible))
                return;

            if (@event.IsActionPressed("ui_cancel")) {
                Close();
                GetViewport().SetInputAsHandled();
            }
        }

        void Close() {
            Visible = false;
            pendingRemoveId = null;
            pendingReplaceId = null;
            Closed?.Invoke();
        }
    }
}
