using System;
using Godot;
using Uberkarl.Behavior;
using Uberkarl.Editor.Input;

namespace Uberkarl {

    /// <summary>
    /// The summoned, gamepad-first behavior assignment surface (design #8049 M4, #8525 §12): step one
    /// reuses the shared <see cref="ChoiceList"/> to pick an applicable predefined; step two tunes each of
    /// its parameters here, one at a time. Both steps are driven entirely by the engine-free
    /// <see cref="BehaviorAssignmentPicker"/> — this panel only renders its current step and forwards input to it.
    /// </summary>
    public partial class BehaviorAssignmentPanel : Control {

        ChoiceList choiceList;
        Label parameterLabel;
        readonly AnalogStepGate analogGate = new AnalogStepGate();

        BehaviorAssignmentPicker picker;

        /// <summary>Raised once, on a successful pick, with the assembled binding.</summary>
        public event Action<BehaviorBinding> Assigned;

        /// <summary>Raised when the assignment is cancelled from either step.</summary>
        public event Action Cancelled;

        /// <summary>True while the parameter-tuning step is summoned. Step one lives on the shared <see cref="ChoiceList"/> and is covered by its own <c>IsOpen</c>.</summary>
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

            PanelContainer panel = new PanelContainer { CustomMinimumSize = new Vector2(420f, 120f) };
            EditorLayout.CenterInParent(panel);
            AddChild(panel);

            VBoxContainer root = new VBoxContainer();
            panel.AddChild(root);

            parameterLabel = new Label();
            parameterLabel.AddThemeColorOverride("font_color", EditorTheme.Accent);
            root.AddChild(parameterLabel);

            Label hint = new Label { Text = "left/right adjust, accept commits, cancel aborts" };
            hint.AddThemeColorOverride("font_color", EditorTheme.TextDim);
            root.AddChild(hint);
        }

        /// <summary>Attaches the shared <see cref="ChoiceList"/> step one uses. Called once by <see cref="LevelEditor"/> alongside construction.</summary>
        public void AttachChoiceList(ChoiceList list) => choiceList = list;

        /// <summary>Summons the picker for <paramref name="subjectKind"/> — opens the shared list for step one.</summary>
        public void Summon(BehaviorSubjectKind subjectKind) {
            picker = new BehaviorAssignmentPicker(subjectKind);
            choiceList.Open($"Assign Behavior — {SubjectLabel(subjectKind)}", "✕ Cancel", picker.ApplicablePredefineds.Count, PredefinedRow,
                EmptyMessage(subjectKind), OnPredefinedChosen, OnPredefinedListDismissed);
        }

        /// <summary>The subject kind's display label for the picker title.</summary>
        static string SubjectLabel(BehaviorSubjectKind subjectKind) => subjectKind switch {
            BehaviorSubjectKind.Tile => "Tile",
            BehaviorSubjectKind.Trigger => "Trigger",
            BehaviorSubjectKind.Object => "Object",
            BehaviorSubjectKind.LevelScript => "Level Script",
            _ => subjectKind.ToString(),
        };

        /// <summary>The picker's empty-list message for <paramref name="subjectKind"/>.</summary>
        static string EmptyMessage(BehaviorSubjectKind subjectKind) => subjectKind == BehaviorSubjectKind.LevelScript
            ? "No predefined behaviors apply to this subject. A custom level script becomes authorable in M5."
            : "No predefined behaviors apply to this subject.";

        ChoiceListRow PredefinedRow(int index) => new ChoiceListRow(picker.ApplicablePredefineds[index].Label, string.Empty);

        void OnPredefinedChosen(int index) {
            choiceList.Hide();
            if (!picker.SelectPredefined(index)) {
                Cancelled?.Invoke();
                return;
            }

            if (picker.Stage == BehaviorAssignmentStage.Complete)
                Finish();
            else
                OpenParameterStep();
        }

        void OnPredefinedListDismissed() {
            choiceList.Hide();
            Cancelled?.Invoke();
        }

        void OpenParameterStep() {
            Visible = true;
            analogGate.Prime(Godot.Input.IsActionPressed("ui_left"), Godot.Input.IsActionPressed("ui_right"));
            RedrawParameter();
            CallDeferred(Control.MethodName.GrabFocus);
        }

        void RedrawParameter() {
            parameterLabel.Text = $"{picker.Selected.Label} — {picker.CurrentParameter.Name}: < {picker.CurrentParameterPendingValue:0.##} >";
        }

        void Adjust(int direction) {
            if (picker.AdjustCurrentParameter(direction))
                RedrawParameter();
        }

        void Commit() {
            picker.CommitCurrentParameter();
            if (picker.Stage == BehaviorAssignmentStage.Complete)
                Finish();
            else
                RedrawParameter();
        }

        public override void _GuiInput(InputEvent @event) {
            if (!Visible)
                return;

            if (@event is InputEventJoypadMotion motion && motion.Axis == JoyAxis.LeftX) {
                int step = analogGate.Poll(Godot.Input.IsActionPressed("ui_left"), Godot.Input.IsActionPressed("ui_right"));
                AcceptEvent();
                if (step != 0)
                    Adjust(step);
            } else if (@event.IsActionPressed("ui_left")) {
                AcceptEvent();
                Adjust(-1);
            } else if (@event.IsActionPressed("ui_right")) {
                AcceptEvent();
                Adjust(+1);
            } else if (@event.IsActionPressed("ui_accept")) {
                AcceptEvent();
                Commit();
            } else if (@event.IsActionPressed("ui_cancel")) {
                AcceptEvent();
                Abort();
            }
        }

        void Abort() {
            picker.Cancel();
            Visible = false;
            Cancelled?.Invoke();
        }

        void Finish() {
            BehaviorBinding result = picker.Result;
            Visible = false;
            Assigned?.Invoke(result);
        }
    }
}
