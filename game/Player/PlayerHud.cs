using Godot;

namespace Uberkarl {

    /// <summary>
    /// Minimal health readout (bar + numeric value) for the play/playtest overlay, added as a
    /// <see cref="CanvasLayer"/> child by <see cref="PlayRuntimeBuilder.Populate"/>.
    /// </summary>
    public partial class PlayerHud : CanvasLayer {

        const int Margin = 12;
        const int BarWidth = 160;
        const int BarHeight = 14;

        Player player;
        ProgressBar bar;
        Label label;

        /// <summary>Binds the player whose health this HUD polls.</summary>
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

        public override void _Process(double delta) {
            if (player is null || bar is null)
                return;

            bar.MaxValue = player.MaxHealth;
            bar.Value = player.Health;
            label.Text = $"{Mathf.CeilToInt((float)player.Health)} / {Mathf.CeilToInt((float)player.MaxHealth)}";
        }
    }
}
