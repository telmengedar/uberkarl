using System;
using System.Collections.Generic;
using Godot;
using Uberkarl.Packages;

namespace Uberkarl {

    /// <summary>
    /// The summoned, gamepad-first browser that replaces the file-system <c>FileDialog</c> for both
    /// loading and saving a level (DiVoid #7470 for load, #7552 for the file-browser look + save flow). It
    /// holds only the source abstraction and the opaque summaries/handles it hands back — no file, ZIP, or
    /// path knowledge, and no edit logic. Every step is a vertical, scrollable list of focus-chained
    /// <see cref="Button"/>s (mirroring <see cref="LevelEditor"/>'s toolbar focus-containment): a focused
    /// button activates on <c>ui_accept</c> on gamepad, keyboard, or a mouse click; <c>ui_cancel</c> (or the
    /// header's Back/Close button, for mouse users) steps back a level of the flow or closes it entirely.
    ///
    /// <b>Load mode</b> (<see cref="SummonLoad"/>): packages → select → that package's levels → select →
    /// <see cref="ResourceChosen"/>. Unchanged in shape from v1, upgraded to the shared list styling +
    /// back-nav below.
    ///
    /// <b>Save mode</b> (<see cref="SummonSave"/>): packages (plus a leading "+ New package…" entry) →
    /// pick an existing package, or type a new one's name via the attached <see cref="OnScreenKeyboard"/>
    /// (with a same-name collision folded into a confirm-overwrite step rather than silently minting a
    /// distinct file — <see cref="PackageSaveTargetResolver"/>) → type the level's name → <see cref="SaveRequested"/>.
    /// The keyboard is a separate summoned overlay (<see cref="AttachKeyboard"/>, exactly like
    /// <see cref="LayerManagerPanel"/>'s rename affordance) that sits on top of whichever list step is
    /// current without the browser itself changing step underneath it.
    /// </summary>
    public partial class PackageBrowser : Control {

        enum Mode { Load, Save }
        enum Step { Packages, Resources, ConfirmOverwrite, SaveResources }

        // Which situation the current ConfirmOverwrite step is resolving — a same-name "+ New package…"
        // collision (confirm folds back into the save-resources step, since the target became an existing
        // package) or an explicitly-picked existing level resource (confirm proceeds straight to naming).
        enum ConfirmKind { NewPackageNameCollision, ResourceOverwrite }

        IPackageSource source;
        Mode mode;
        Step step;
        ConfirmKind confirmKind;
        IReadOnlyList<PackageSummary> packages = Array.Empty<PackageSummary>();
        IReadOnlyList<ResourceSummary> resources = Array.Empty<ResourceSummary>();
        PackageHandle selectedPackage;
        string selectedPackageName = string.Empty;
        string pendingNewPackageName;
        string currentLevelName = string.Empty;
        string collisionName = string.Empty;
        // Set when the author explicitly picked an existing level resource to overwrite (the save-resources
        // step's list, not "+ New level…") — carried through to the emitted PackageSaveTarget so the level
        // attaches to this exact path rather than deriving a fresh one (DiVoid #7571/#7572).
        ResourcePath? pendingOverwriteResource;

        OnScreenKeyboard keyboard;

        Label titleLabel;
        Button closeButton;
        ScrollContainer scroll;
        VBoxContainer listBox;
        Label emptyLabel;

        /// <summary>Raised in load mode when a resource is chosen: the package it lives in and its in-package path.</summary>
        public event Action<PackageHandle, ResourcePath> ResourceChosen;

        /// <summary>Raised in save mode once a target package and a level name have both been settled.</summary>
        public event Action<PackageSaveTarget> SaveRequested;

        /// <summary>Raised when the browser is dismissed without completing either flow.</summary>
        public event Action Cancelled;

        /// <summary>True while the browser is summoned.</summary>
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

            PanelContainer panel = new PanelContainer { CustomMinimumSize = new Vector2(480f, 420f) };
            panel.SetAnchorsPreset(LayoutPreset.Center);
            AddChild(panel);

            VBoxContainer root = new VBoxContainer();
            panel.AddChild(root);

            HBoxContainer header = new HBoxContainer();
            root.AddChild(header);

            titleLabel = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            titleLabel.AddThemeColorOverride("font_color", EditorTheme.Accent);
            header.AddChild(titleLabel);

            // A mouse-clickable mirror of ui_cancel's back/close semantics — gamepad/keyboard already have
            // B/Esc, but a file-browser needs an on-screen affordance for a mouse-only user too.
            closeButton = new Button { Text = "✕ Close" };
            closeButton.Pressed += HandleCancel;
            NodePath self = new NodePath(".");
            closeButton.FocusNeighborLeft = self;
            closeButton.FocusNeighborRight = self;
            closeButton.FocusNeighborTop = self;
            closeButton.FocusNeighborBottom = self;
            header.AddChild(closeButton);

            root.AddChild(new HSeparator());

            scroll = new ScrollContainer { CustomMinimumSize = new Vector2(460f, 340f) };
            root.AddChild(scroll);

            listBox = new VBoxContainer();
            scroll.AddChild(listBox);

            emptyLabel = new Label { Visible = false };
            emptyLabel.AddThemeColorOverride("font_color", EditorTheme.TextDim);
            root.AddChild(emptyLabel);
        }

        /// <summary>
        /// Attaches the shared <see cref="OnScreenKeyboard"/> the save flow's naming steps summon (DiVoid
        /// #7513). Called once by <see cref="LevelEditor"/> alongside construction, exactly like
        /// <see cref="LayerManagerPanel.AttachKeyboard"/>.
        /// </summary>
        public void AttachKeyboard(OnScreenKeyboard onScreenKeyboard) => keyboard = onScreenKeyboard;

        /// <summary>Summon the browser in load mode against <paramref name="packageSource"/>, showing its packages.</summary>
        public void SummonLoad(IPackageSource packageSource) {
            source = packageSource;
            mode = Mode.Load;
            Visible = true;
            packages = source.ListPackages();
            ShowPackages();
        }

        /// <summary>
        /// Summon the browser in save mode against <paramref name="packageSource"/>, seeding the eventual
        /// level-name prompt with <paramref name="currentName"/> (the level's existing name, so re-saving
        /// under the same name is a single confirm rather than retyping it).
        /// </summary>
        public void SummonSave(IPackageSource packageSource, string currentName) {
            source = packageSource;
            mode = Mode.Save;
            currentLevelName = currentName ?? string.Empty;
            Visible = true;
            packages = source.ListPackages();
            ShowPackages();
        }

        // ----- packages step (shared by both modes; save mode prepends "+ New package…") -----

        void ShowPackages() {
            step = Step.Packages;
            closeButton.Text = "✕ Close";
            titleLabel.Text = mode == Mode.Load ? "Open Package" : "Save To Package";
            pendingOverwriteResource = null;

            int newRowCount = mode == Mode.Save ? 1 : 0;
            string empty = mode == Mode.Load
                ? "No packages in the content folder."
                : "No packages yet — choose “+ New package…” to create one.";
            PopulateList(packages.Count + newRowCount, PackageRow, empty, OnPackageRowChosen);
        }

        RowText PackageRow(int index) {
            if (mode == Mode.Save && index == 0)
                return new RowText("+ New package…", string.Empty);

            PackageSummary summary = packages[mode == Mode.Save ? index - 1 : index];
            string itemWord = summary.ResourceCount == 1 ? "item" : "items";
            return new RowText(summary.Name, $"v{summary.Version} · {summary.ResourceCount} {itemWord}");
        }

        void OnPackageRowChosen(int index) {
            if (mode == Mode.Load) {
                if (index < 0 || index >= packages.Count)
                    return;
                selectedPackage = packages[index].Handle;
                selectedPackageName = packages[index].Name;
                ShowResources();
                return;
            }

            if (index == 0) {
                OpenNewPackageNameKeyboard();
                return;
            }

            int packageIndex = index - 1;
            if (packageIndex < 0 || packageIndex >= packages.Count)
                return;

            selectedPackage = packages[packageIndex].Handle;
            selectedPackageName = packages[packageIndex].Name;
            pendingNewPackageName = null;
            pendingOverwriteResource = null;
            ShowSaveResources(); // an existing package: pick which level resource to save as (DiVoid #7571/#7572)
        }

        // ----- load mode: resources step -----

        void ShowResources() {
            IReadOnlyList<ResourceSummary> contents;
            try {
                contents = source.GetContents(selectedPackage);
            } catch (Exception exception) {
                GD.PrintErr($"PackageBrowser: {exception.GetType().Name}: {exception.Message}");
                ShowPackages();
                return;
            }

            List<ResourceSummary> levels = new List<ResourceSummary>();
            foreach (ResourceSummary entry in contents)
                if (entry.Kind == ResourceKind.Level)
                    levels.Add(entry);
            resources = levels;

            step = Step.Resources;
            closeButton.Text = "← Back";
            titleLabel.Text = $"{selectedPackageName} — Select Level";
            PopulateList(resources.Count, ResourceRow, "No loadable resources in this package.", OnResourceChosen);
        }

        RowText ResourceRow(int index) {
            ResourceSummary summary = resources[index];
            return new RowText(summary.DisplayName, FormatBytes(summary.ByteLength));
        }

        void OnResourceChosen(int index) {
            if (index < 0 || index >= resources.Count)
                return;

            PackageHandle handle = selectedPackage;
            ResourcePath path = resources[index].Path;
            Close();
            ResourceChosen?.Invoke(handle, path);
        }

        static string FormatBytes(long byteLength) =>
            byteLength < 1024 ? $"{byteLength} B" : $"{byteLength / 1024.0:0.#} KB";

        // ----- save mode: pick a level resource within the target package (DiVoid #7571/#7572) -----

        // Mirrors the "+ New package…" pattern already shipped for the package step: once an EXISTING
        // package is the target, its existing level resources are offered alongside "+ New level…" —
        // picking one replaces it (one confirm, reusing ConfirmOverwrite below); picking "+ New level…"
        // names a brand-new resource. A "+ New package…" target skips this step entirely (no resources
        // exist yet in an archive that has not been created).
        void ShowSaveResources() {
            IReadOnlyList<ResourceSummary> contents;
            try {
                contents = source.GetContents(selectedPackage);
            } catch (Exception exception) {
                GD.PrintErr($"PackageBrowser: {exception.GetType().Name}: {exception.Message}");
                ShowPackages();
                return;
            }

            List<ResourceSummary> levels = new List<ResourceSummary>();
            foreach (ResourceSummary entry in contents)
                if (entry.Kind == ResourceKind.Level)
                    levels.Add(entry);
            resources = levels;

            step = Step.SaveResources;
            closeButton.Text = "← Back";
            titleLabel.Text = $"{selectedPackageName} — Save Level";
            // Unlike the load-mode resources step, "+ New level…" is always offered — an empty package is
            // a perfectly normal save target, never a dead end.
            PopulateList(resources.Count + 1, SaveResourceRow, string.Empty, OnSaveResourceChosen);
        }

        RowText SaveResourceRow(int index) {
            if (index == 0)
                return new RowText("+ New level…", string.Empty);

            ResourceSummary summary = resources[index - 1];
            return new RowText(summary.DisplayName, FormatBytes(summary.ByteLength));
        }

        void OnSaveResourceChosen(int index) {
            if (index == 0) {
                pendingOverwriteResource = null;
                OpenLevelNameKeyboard();
                return;
            }

            int resourceIndex = index - 1;
            if (resourceIndex < 0 || resourceIndex >= resources.Count)
                return;

            ResourceSummary picked = resources[resourceIndex];
            pendingOverwriteResource = picked.Path;
            collisionName = picked.DisplayName;
            confirmKind = ConfirmKind.ResourceOverwrite;
            ShowConfirmOverwrite();
        }

        // ----- save mode: naming (via the attached keyboard) + confirm-overwrite -----

        void OpenNewPackageNameKeyboard() {
            if (keyboard == null)
                return;
            keyboard.RequestText("New package name", string.Empty, OnNewPackageNameCommitted);
        }

        void OnNewPackageNameCommitted(string name) {
            if (string.IsNullOrWhiteSpace(name)) {
                ShowPackages(); // nothing typed — bounce back rather than silently doing nothing
                return;
            }

            PackageHandle? collision = PackageSaveTargetResolver.FindCollision(packages, name);
            if (collision is { } handle) {
                selectedPackage = handle;
                selectedPackageName = name.Trim();
                pendingNewPackageName = null;
                collisionName = name.Trim();
                confirmKind = ConfirmKind.NewPackageNameCollision;
                ShowConfirmOverwrite();
            } else {
                pendingNewPackageName = name.Trim();
                pendingOverwriteResource = null;
                OpenLevelNameKeyboard();
            }
        }

        void ShowConfirmOverwrite() {
            step = Step.ConfirmOverwrite;
            closeButton.Text = "← Back";
            titleLabel.Text = confirmKind == ConfirmKind.NewPackageNameCollision
                ? $"A package named “{collisionName}” already exists."
                : $"Overwrite the level “{collisionName}”?";
            PopulateList(2, ConfirmOverwriteRow, string.Empty, OnConfirmOverwriteChosen);
        }

        static RowText ConfirmOverwriteRow(int index) => index == 0
            ? new RowText("Overwrite it", string.Empty)
            : new RowText("Choose a different name", string.Empty);

        void OnConfirmOverwriteChosen(int index) {
            if (index == 0) {
                if (confirmKind == ConfirmKind.NewPackageNameCollision)
                    ShowSaveResources(); // selectedPackage now holds the collided handle — pick its level resource
                else
                    OpenLevelNameKeyboard(); // resource overwrite confirmed — proceed with pendingOverwriteResource set
            } else {
                if (confirmKind == ConfirmKind.NewPackageNameCollision)
                    ShowPackages();
                else
                    ShowSaveResources();
            }
        }

        void OpenLevelNameKeyboard() {
            if (keyboard == null)
                return;

            string target = pendingNewPackageName ?? selectedPackageName;
            // An explicitly-picked existing resource seeds the keyboard with ITS name (confirming/tweaking
            // the spelling of the slot just picked), not whatever name the currently-open level happens to
            // carry — the pick itself is the intent (design #7572 decision 4).
            string seed = pendingOverwriteResource != null ? collisionName : currentLevelName;
            keyboard.RequestText($"Level name — saving into “{target}”", seed, OnLevelNameCommitted);
        }

        void OnLevelNameCommitted(string name) {
            if (string.IsNullOrWhiteSpace(name)) {
                ShowPackages(); // no name typed — back to picking a target rather than saving blank
                return;
            }

            PackageSaveTarget target = pendingNewPackageName != null
                ? new PackageSaveTarget(null, pendingNewPackageName, name.Trim(), null)
                : new PackageSaveTarget(selectedPackage, null, name.Trim(), pendingOverwriteResource);

            Close();
            SaveRequested?.Invoke(target);
        }

        // ----- shared list rendering -----

        readonly record struct RowText(string Primary, string Secondary);

        void PopulateList(int count, Func<int, RowText> rowAt, string emptyMessage, Action<int> onChosen) {
            foreach (Node child in listBox.GetChildren())
                child.QueueFree();

            emptyLabel.Visible = count == 0;
            emptyLabel.Text = emptyMessage;
            scroll.Visible = count > 0;

            List<Button> buttons = new List<Button>(count);
            for (int i = 0; i < count; i++) {
                int index = i;
                RowText text = rowAt(i);

                HBoxContainer row = new HBoxContainer();
                listBox.AddChild(row);

                Button button = new Button {
                    Text = text.Primary,
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    Alignment = HorizontalAlignment.Left,
                };
                button.Pressed += () => onChosen(index);
                row.AddChild(button);
                buttons.Add(button);

                if (!string.IsNullOrEmpty(text.Secondary)) {
                    Label meta = new Label { Text = text.Secondary, VerticalAlignment = VerticalAlignment.Center };
                    meta.AddThemeColorOverride("font_color", EditorTheme.TextDim);
                    row.AddChild(meta);
                }
            }
            ContainListFocus(buttons);

            if (buttons.Count > 0)
                buttons[0].CallDeferred(Control.MethodName.GrabFocus);
            else
                CallDeferred(Control.MethodName.GrabFocus);
        }

        // Vertical focus chain, ends and every horizontal side pinned to self — the same technique
        // LevelEditor.ContainToolbarFocus uses to stop a stick/D-pad aim from bouncing focus off the
        // list onto whatever sits underneath the browser.
        static void ContainListFocus(List<Button> buttons) {
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
        }

        // ----- back-navigation / close -----

        // The step this browser is on decides what "cancel" (ui_cancel, or the header's Back/Close button)
        // means: one level back out of Resources/SaveResources/ConfirmOverwrite into Packages (a resource-
        // overwrite confirm steps back to the save-resources list instead, mirroring its "no" branch
        // above), or a full close from Packages itself. The level-name/new-package-name keyboard prompts
        // are a separate overlay (OnScreenKeyboard) that owns its own cancel — this browser's step never
        // changes while the keyboard is open, so its own cancel handling below is guarded on the keyboard
        // being closed.
        void HandleCancel() {
            switch (step) {
                case Step.Resources:
                case Step.SaveResources:
                    ShowPackages();
                    break;
                case Step.ConfirmOverwrite:
                    if (confirmKind == ConfirmKind.ResourceOverwrite)
                        ShowSaveResources();
                    else
                        ShowPackages();
                    break;
                default:
                    Close();
                    Cancelled?.Invoke();
                    break;
            }
        }

        void Close() {
            Visible = false;
            pendingNewPackageName = null;
            pendingOverwriteResource = null;
        }

        public override void _GuiInput(InputEvent @event) {
            if (!Visible || (keyboard != null && keyboard.IsOpen))
                return;

            if (@event.IsActionPressed("ui_cancel")) {
                AcceptEvent();
                HandleCancel();
            }
        }

        // Belt-and-suspenders close path, exactly as LayerManagerPanel/OnScreenKeyboard: a row Button (not
        // the browser) almost always holds focus, so ui_cancel pressed there never reaches _GuiInput above
        // — it falls through to unhandled input, where this catches it instead.
        public override void _UnhandledInput(InputEvent @event) {
            if (!Visible || @event.IsEcho() || (keyboard != null && keyboard.IsOpen))
                return;

            if (@event.IsActionPressed("ui_cancel")) {
                HandleCancel();
                GetViewport().SetInputAsHandled();
            }
        }
    }
}
