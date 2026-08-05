using Godot;

namespace Uberkarl {

    /// <summary>
    /// Minimal health readout (bar + numeric value) for the play/playtest overlay (DiVoid #7743 -- making
    /// the P1 hurt/heal intent plumbing visible: "I see the spike but wouldn't know how to actually
    /// playtest the behaviors"). Added as a <see cref="CanvasLayer"/> child by the shared
    /// <see cref="PlayRuntimeBuilder.Populate"/>, so standalone play (<see cref="LevelPlay"/>) and editor
    /// playtest (<see cref="PlaytestOverlay"/>) show it identically (design #7704 C-4) -- same reasoning as
    /// <see cref="BehaviorRuntime"/> being wired in that one shared place. A <see cref="CanvasLayer"/> (like
    /// <c>PlayRuntimeBuilder.AddBackgroundFill</c>'s backdrop) draws in screen space, so the HUD stays
    /// pinned to a screen corner regardless of the following camera.
    /// </summary>
    public partial class PlayerHud : CanvasLayer {

        const int Margin = 12;
        const int BarWidth = 160;
        const int BarHeight = 14;

        Player player;
        ProgressBar bar;
        Label label;

        /// <summary>Binds the player whose health this HUD polls. Must be called once, any time relative to
        /// <see cref="_Ready"/> -- reading only starts on the next <see cref="_Process"/>.</summary>
        public void Configure(Player boundPlayer) => player = boundPlayer;

        public override void _Ready() {
            Control container = new Control { Name = "HudRoot", Position = new Vector2(Margin, Margin) };
            AddChild(container);

            bar = new ProgressBar {
                Name = "HealthBar",
                Size = new Vector2(BarWidth, BarHeight),
                MinValue = 0,
                ShowPercentage = false,
            };
            container.AddChild(bar);

            label = new Label {
                Name = "HealthLabel",
                Position = new Vector2(0, BarHeight + 2),
            };
            container.AddChild(label);
        }

        // A read-only poll, not event-driven (DiVoid #7743) -- Player.Health/MaxHealth have no change
        // notification of their own, and a per-frame read of two doubles is cheap enough that adding one
        // isn't worth the extra wiring at this scope.
        public override void _Process(double delta) {
            if (player is null || bar is null)
                return;

            bar.MaxValue = player.MaxHealth;
            bar.Value = player.Health;
            label.Text = $"{Mathf.CeilToInt((float)player.Health)} / {Mathf.CeilToInt((float)player.MaxHealth)}";
        }
    }
}
