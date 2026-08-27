using System;
using Godot;
using Uberkarl.Behavior;
using Uberkarl.Editor;
using Uberkarl.Packages;

namespace Uberkarl {

    /// <summary>The summoned script source-text editor: a name header, a multi-line text field, and a validation footer.</summary>
    public partial class ScriptSourceEditor : Control {

        const float ValidationDebounceSeconds = 0.5f;

        Label nameLabel;
        TextEdit textEdit;
        Label footerLabel;
        Timer validationTimer;

        ResourcePath path;
        BehaviorScriptRole role;

        /// <summary>Raised once, on close, with the edited script's path and the buffer's final text. Every close commits — there is no discard branch.</summary>
        public event Action<ResourcePath, string> Closed;

        /// <summary>True while the surface is summoned.</summary>
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

            PanelContainer panel = new PanelContainer { CustomMinimumSize = new Vector2(720f, 480f) };
            EditorLayout.CenterInParent(panel);
            AddChild(panel);

            VBoxContainer root = new VBoxContainer();
            panel.AddChild(root);

            HBoxContainer header = new HBoxContainer();
            root.AddChild(header);

            nameLabel = new Label {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            nameLabel.AddThemeColorOverride("font_color", EditorTheme.Accent);
            header.AddChild(nameLabel);

            NodePath self = new NodePath(".");
            Button closeButton = new Button { Text = "✕ Close", FocusMode = FocusModeEnum.None };
            closeButton.Pressed += Close;
            closeButton.FocusNeighborLeft = self;
            closeButton.FocusNeighborRight = self;
            closeButton.FocusNeighborTop = self;
            closeButton.FocusNeighborBottom = self;
            closeButton.FocusNext = self;
            closeButton.FocusPrevious = self;
            header.AddChild(closeButton);

            root.AddChild(new HSeparator());

            textEdit = new TextEdit { CustomMinimumSize = new Vector2(700f, 380f), SizeFlagsVertical = SizeFlags.ExpandFill };
            textEdit.FocusNeighborLeft = self;
            textEdit.FocusNeighborRight = self;
            textEdit.FocusNeighborTop = self;
            textEdit.FocusNeighborBottom = self;
            textEdit.FocusNext = self;
            textEdit.FocusPrevious = self;
            textEdit.TextChanged += OnTextChanged;
            root.AddChild(textEdit);

            footerLabel = new Label { TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
            footerLabel.AddThemeColorOverride("font_color", EditorTheme.TextDim);
            root.AddChild(footerLabel);

            validationTimer = new Timer { OneShot = true, WaitTime = ValidationDebounceSeconds };
            validationTimer.Timeout += RunValidation;
            AddChild(validationTimer);
        }

        /// <summary>Summons the editor over <paramref name="source"/> at <paramref name="scriptPath"/>, showing <paramref name="displayName"/> in the header and validating under <paramref name="scriptRole"/>'s budget.</summary>
        public void Summon(ResourcePath scriptPath, string displayName, string source, BehaviorScriptRole scriptRole) {
            path = scriptPath;
            role = scriptRole;
            nameLabel.Text = displayName;
            validationTimer.Stop();
            textEdit.Text = source ?? string.Empty;
            Visible = true;
            RunValidation();
            textEdit.CallDeferred(Control.MethodName.GrabFocus);
        }

        void OnTextChanged() => validationTimer.Start();

        void RunValidation() {
            string reason = BehaviorSourceValidator.Validate(textEdit.Text, role);
            footerLabel.Text = ScriptValidationFooterText.Format(reason);
        }

        void Close() {
            string source = textEdit.Text;
            Visible = false;
            validationTimer.Stop();
            Closed?.Invoke(path, source);
        }

        public override void _GuiInput(InputEvent @event) {
            if (!Visible)
                return;

            if (@event.IsActionPressed("ui_cancel")) {
                AcceptEvent();
                Close();
            }
        }

        public override void _UnhandledInput(InputEvent @event) {
            if (!Visible || @event.IsEcho())
                return;

            if (@event.IsActionPressed("ui_cancel")) {
                Close();
                GetViewport().SetInputAsHandled();
            }
        }
    }
}
