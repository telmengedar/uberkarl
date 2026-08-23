using System;
using System.Collections.Generic;
using Godot;
using Uberkarl.Behavior;
using Uberkarl.Editor;
using Uberkarl.Editor.Input;
using Uberkarl.Packages;

namespace Uberkarl {

    /// <summary>The summoned, gamepad-first behavior assignment surface.</summary>
    public partial class BehaviorAssignmentPanel : Control {

        ChoiceList choiceList;
        OnScreenKeyboard keyboard;
        Label parameterLabel;
        readonly AnalogStepGate analogGate = new AnalogStepGate();

        BehaviorAssignmentPicker picker;
        Func<string, bool> pendingSlugTaken;
        string subjectName;

        /// <summary>Raised once, on a successful pick, with the assembled binding.</summary>
        public event Action<BehaviorBinding> Assigned;

        /// <summary>Raised when the assignment is cancelled from any step.</summary>
        public event Action Cancelled;

        /// <summary>True while the parameter-tuning step is summoned.</summary>
        public bool IsOpen => Visible;

        /// <summary>The just-finished pick's minted script path, or <c>null</c> when nothing was minted.</summary>
        public ResourcePath? MintedScriptPath { get; private set; }

        /// <summary>The starter template text seeded alongside <see cref="MintedScriptPath"/>.</summary>
        public string MintedScriptSource { get; private set; }

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

        /// <summary>Attaches the shared <see cref="ChoiceList"/> step one uses.</summary>
        public void AttachChoiceList(ChoiceList list) => choiceList = list;

        /// <summary>Attaches the shared <see cref="OnScreenKeyboard"/> the new-script naming step uses.</summary>
        public void AttachKeyboard(OnScreenKeyboard onScreenKeyboard) => keyboard = onScreenKeyboard;

        /// <summary>Summons the picker for <paramref name="subjectKind"/> over <paramref name="existingScripts"/>, checking a committed new-script name against <paramref name="isNewScriptSlugTaken"/>. <paramref name="subjectName"/>, when given, is shown in the picker title.</summary>
        public void Summon(BehaviorSubjectKind subjectKind, string subjectName, IReadOnlyList<ResourcePath> existingScripts, Func<string, bool> isNewScriptSlugTaken) {
            picker = new BehaviorAssignmentPicker(subjectKind, existingScripts);
            this.subjectName = subjectName;
            pendingSlugTaken = isNewScriptSlugTaken;
            MintedScriptPath = null;
            MintedScriptSource = null;
            OpenChoiceList();
        }

        void OpenChoiceList() {
            choiceList.Open($"Assign Behavior — {BehaviorSubjectLabel.Format(picker.SubjectKind, subjectName)}", "✕ Cancel", picker.Choices.Count, ChoiceRow,
                "No behaviors available.", OnChoiceChosen, OnChoiceListDismissed);
        }

        ChoiceListRow ChoiceRow(int index) {
            BehaviorAssignmentChoice choice = picker.Choices[index];
            string prefix = choice.Kind == BehaviorAssignmentChoiceKind.ExistingScript ? "▸ " : string.Empty;
            return new ChoiceListRow(prefix + choice.Label, string.Empty);
        }

        void OnChoiceChosen(int index) {
            if (!picker.SelectChoice(index)) {
                choiceList.Hide();
                Cancelled?.Invoke();
                return;
            }

            switch (picker.Stage) {
                case BehaviorAssignmentStage.Complete:
                    choiceList.Hide();
                    Finish();
                    break;
                case BehaviorAssignmentStage.EditingParameter:
                    choiceList.Hide();
                    OpenParameterStep();
                    break;
                case BehaviorAssignmentStage.NamingNewScript:
                    OpenNamingStep();
                    break;
            }
        }

        void OnChoiceListDismissed() {
            choiceList.Hide();
            Cancelled?.Invoke();
        }

        void OpenNamingStep() => keyboard.RequestText("New script name", string.Empty, OnNewScriptNameCommitted, OnNewScriptNameCancelled);

        void OnNewScriptNameCommitted(string name) {
            if (!picker.CreateNewScript(name, pendingSlugTaken)) {
                OpenNamingStep();
                return;
            }

            choiceList.Hide();
            Finish();
        }

        void OnNewScriptNameCancelled() => picker.CancelNewScriptNaming();

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
            MintedScriptPath = picker.MintedScriptPath;
            MintedScriptSource = picker.MintedScriptSource;
            Visible = false;
            Assigned?.Invoke(result);
        }
    }
}
