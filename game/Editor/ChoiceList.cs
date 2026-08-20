using System;
using System.Collections.Generic;
using Godot;

namespace Uberkarl {

    /// <summary>
    /// A summoned, gamepad/keyboard/mouse-first scrollable list of focus-chained rows, driven per step
    /// through <see cref="Open"/> by a caller that decides what a row means and what dismissal does.
    /// </summary>
    public partial class ChoiceList : Control {

        Label titleLabel;
        Button closeButton;
        ScrollContainer scroll;
        VBoxContainer listBox;
        Label emptyLabel;

        Action<int> onChosen;
        Action onDismissRequested;

        /// <summary>
        /// Polled before acting on a dismissal request; while it returns true, dismissal is ignored. Scoped
        /// per <see cref="Open"/> call (set from its <c>dismissSuppressed</c> parameter, defaulting to none)
        /// rather than attached once — the list has more than one driver, and a predicate set by one must
        /// not leak into a summon owned by another.
        /// </summary>
        public Func<bool> DismissSuppressed { get; private set; }

        /// <summary>True while the list is summoned.</summary>
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

            PanelContainer panel = new PanelContainer { CustomMinimumSize = new Vector2(480f, 420f) };
            EditorLayout.CenterInParent(panel);
            AddChild(panel);

            VBoxContainer root = new VBoxContainer();
            panel.AddChild(root);

            HBoxContainer header = new HBoxContainer();
            root.AddChild(header);

            titleLabel = new Label {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            titleLabel.AddThemeColorOverride("font_color", EditorTheme.Accent);
            header.AddChild(titleLabel);

            closeButton = new Button();
            closeButton.Pressed += RequestDismiss;
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

        /// <summary>Summons the list.</summary>
        /// <param name="title">header title text</param>
        /// <param name="closeText">header close/back button text</param>
        /// <param name="count">number of rows to build</param>
        /// <param name="rowAt">builds the row at a given index</param>
        /// <param name="emptyMessage">shown in place of the list when <paramref name="count"/> is zero</param>
        /// <param name="onChosen">fires with the picked row's index</param>
        /// <param name="onDismissRequested">fires on <c>ui_cancel</c> or the header button</param>
        /// <param name="dismissSuppressed">polled before acting on a dismissal request for this summon only; defaults to none</param>
        public void Open(string title, string closeText, int count, Func<int, ChoiceListRow> rowAt, string emptyMessage, Action<int> onChosen, Action onDismissRequested, Func<bool> dismissSuppressed = null) {
            Visible = true;
            titleLabel.Text = title;
            closeButton.Text = closeText;
            this.onChosen = onChosen;
            this.onDismissRequested = onDismissRequested;
            DismissSuppressed = dismissSuppressed;
            PopulateList(count, rowAt, emptyMessage);
        }

        void PopulateList(int count, Func<int, ChoiceListRow> rowAt, string emptyMessage) {
            foreach (Node child in listBox.GetChildren())
                child.QueueFree();

            emptyLabel.Visible = count == 0;
            emptyLabel.Text = emptyMessage;
            scroll.Visible = count > 0;

            Action<int> chosen = onChosen;
            List<Button> buttons = new List<Button>(count);
            for (int i = 0; i < count; i++) {
                int index = i;
                ChoiceListRow text = rowAt(i);

                HBoxContainer row = new HBoxContainer();
                listBox.AddChild(row);

                Button button = new Button {
                    Text = text.Primary,
                    Icon = text.Icon,
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    Alignment = HorizontalAlignment.Left,
                };
                button.Pressed += () => chosen?.Invoke(index);
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

        void RequestDismiss() => onDismissRequested?.Invoke();

        public override void _GuiInput(InputEvent @event) {
            if (!Visible || (DismissSuppressed?.Invoke() ?? false))
                return;

            if (@event.IsActionPressed("ui_cancel")) {
                AcceptEvent();
                RequestDismiss();
            }
        }

        public override void _UnhandledInput(InputEvent @event) {
            if (!Visible || @event.IsEcho() || (DismissSuppressed?.Invoke() ?? false))
                return;

            if (@event.IsActionPressed("ui_cancel")) {
                RequestDismiss();
                GetViewport().SetInputAsHandled();
            }
        }
    }
}
