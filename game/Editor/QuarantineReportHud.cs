using Godot;
using Uberkarl.Editor;

namespace Uberkarl {

    /// <summary>
    /// Displays a live playtest run's quarantine verdict, polling a bound <see cref="BehaviorRuntime"/>'s
    /// retained <see cref="Uberkarl.Behavior.QuarantinedSubject"/> records rather than subscribing to
    /// <c>BehaviorScheduler.Quarantined</c>, so init-time quarantines — already recorded by the time this
    /// node is bound — are never missed. Added to the play-world subtree by <see cref="PlaytestOverlay"/>,
    /// so it dies with the run.
    /// </summary>
    public partial class QuarantineReportHud : CanvasLayer {

        const int ReportLayer = 2;

        BehaviorRuntime runtime;
        Label label;
        int lastRenderedCount;

        /// <summary>Binds the runtime this HUD polls. Must be called before this node enters the tree.</summary>
        public void Configure(BehaviorRuntime boundRuntime) => runtime = boundRuntime;

        public override void _Ready() {
            Layer = ReportLayer;

            label = new Label { Name = "QuarantineReportLabel", Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
            EditorLayout.PinBottom(label);
            AddChild(label);
        }

        public override void _Process(double delta) {
            int count = runtime.Quarantines.Count;
            if (count <= lastRenderedCount)
                return;

            lastRenderedCount = count;
            label.Text = QuarantineReportText.Format(runtime.Quarantines);
            label.Visible = true;
        }
    }
}
