using System;
using System.Collections.Generic;
using Godot;
using Uberkarl.Packages;

namespace Uberkarl {

    /// <summary>
    /// The summoned, gamepad-first two-step browser that replaces the file-system <c>FileDialog</c> for
    /// loading a level: step 1 lists the packages an <see cref="IPackageSource"/> offers, step 2 lists
    /// the selected package's loadable (level) resources. It holds only the source abstraction and the
    /// opaque summaries/handles it hands back — no file, ZIP, or path knowledge, and no edit logic. Each
    /// step is a vertical list of focus-chained <see cref="Button"/>s (mirroring
    /// <see cref="LevelEditor"/>'s toolbar focus-containment) so a focused button activates on
    /// <c>ui_accept</c> on gamepad, keyboard, or a mouse click; <c>ui_cancel</c> steps back (step 2) or
    /// closes (step 1), handled here because the buttons themselves do not react to it.
    /// </summary>
    public partial class PackageBrowser : Control {

        enum Step { Packages, Resources }

        IPackageSource source;
        Step step;
        IReadOnlyList<PackageSummary> packages = Array.Empty<PackageSummary>();
        IReadOnlyList<ResourceSummary> resources = Array.Empty<ResourceSummary>();
        PackageHandle selectedPackage;

        Label titleLabel;
        ScrollContainer scroll;
        VBoxContainer listBox;
        Label emptyLabel;

        /// <summary>Raised when a resource is chosen: the package it lives in and its in-package path.</summary>
        public event Action<PackageHandle, ResourcePath> ResourceChosen;

        /// <summary>Raised when the browser is dismissed without choosing a resource.</summary>
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

            PanelContainer panel = new PanelContainer { CustomMinimumSize = new Vector2(420f, 360f) };
            panel.SetAnchorsPreset(LayoutPreset.Center);
            AddChild(panel);

            VBoxContainer root = new VBoxContainer();
            panel.AddChild(root);

            titleLabel = new Label();
            root.AddChild(titleLabel);

            scroll = new ScrollContainer { CustomMinimumSize = new Vector2(400f, 300f) };
            root.AddChild(scroll);

            listBox = new VBoxContainer();
            scroll.AddChild(listBox);

            emptyLabel = new Label { Visible = false };
            root.AddChild(emptyLabel);
        }

        /// <summary>Summon the browser against <paramref name="packageSource"/>, showing its packages.</summary>
        public void Summon(IPackageSource packageSource) {
            source = packageSource;
            Visible = true;
            packages = source.ListPackages();
            ShowPackages();
        }

        void ShowPackages() {
            step = Step.Packages;
            titleLabel.Text = "Open Package";
            PopulateList(packages.Count, index => $"{packages[index].Name} ({packages[index].Version})",
                "No packages in the content folder.");
        }

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
            titleLabel.Text = "Select Level";
            PopulateList(resources.Count, index => resources[index].DisplayName,
                "No loadable resources in this package.");
        }

        void PopulateList(int count, Func<int, string> labelAt, string emptyMessage) {
            foreach (Node child in listBox.GetChildren())
                child.QueueFree();

            emptyLabel.Visible = count == 0;
            emptyLabel.Text = emptyMessage;
            scroll.Visible = count > 0;

            List<Button> buttons = new List<Button>(count);
            for (int i = 0; i < count; i++) {
                int index = i;
                Button button = new Button { Text = labelAt(i) };
                button.Pressed += () => OnItemChosen(index);
                listBox.AddChild(button);
                buttons.Add(button);
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

        void OnItemChosen(int index) {
            if (step == Step.Packages) {
                if (index < 0 || index >= packages.Count)
                    return;
                selectedPackage = packages[index].Handle;
                ShowResources();
            } else {
                if (index < 0 || index >= resources.Count)
                    return;
                PackageHandle handle = selectedPackage;
                ResourcePath path = resources[index].Path;
                Close();
                ResourceChosen?.Invoke(handle, path);
            }
        }

        void Close() {
            Visible = false;
        }

        public override void _GuiInput(InputEvent @event) {
            if (!Visible)
                return;

            if (@event.IsActionPressed("ui_cancel")) {
                AcceptEvent();
                if (step == Step.Resources)
                    ShowPackages();
                else {
                    Close();
                    Cancelled?.Invoke();
                }
            }
        }
    }
}
