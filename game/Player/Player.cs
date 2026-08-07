using System;
using Godot;

namespace Uberkarl {

    /// <summary>
    /// A minimal platformer controller: gravity, left/right movement, a jump, and health with i-frames
    /// and death/respawn, driven by the "move_left" / "move_right" / "jump" input actions.
    /// </summary>
    public partial class Player : CharacterBody2D {

        /// <summary>Horizontal move speed in px/s.</summary>
        [Export] public float MoveSpeed { get; set; } = 90f;

        /// <summary>Initial upward jump velocity in px/s.</summary>
        [Export] public float JumpSpeed { get; set; } = 330f;

        /// <summary>Downward acceleration in px/s^2 applied while airborne.</summary>
        [Export] public float Gravity { get; set; } = 900f;

        /// <summary>Starting/maximum health.</summary>
        [Export] public double MaxHealth { get; set; } = 100;

        /// <summary>Current health, clamped to [0, <see cref="MaxHealth"/>].</summary>
        public double Health { get; private set; }

        /// <summary>Seconds remaining on the post-hit invulnerability window.</summary>
        public double InvulnerabilityRemaining { get; private set; }

        /// <summary>True while the post-hit invulnerability window is active.</summary>
        public bool IsInvulnerable => InvulnerabilityRemaining > 0;

        /// <summary>Raised when health crosses from positive into zero.</summary>
        public event Action Died;

        /// <summary>Half the player's collision-box size in pixels.</summary>
        public static readonly Vector2 CollisionHalfExtents = new Vector2(6f, 12f);

        const double BlinkIntervalSeconds = 0.1;

        Polygon2D marker;

        public override void _Ready() {
            Health = MaxHealth;

            CollisionShape2D collider = new CollisionShape2D {
                Shape = new RectangleShape2D { Size = CollisionHalfExtents * 2f },
            };
            AddChild(collider);

            marker = new Polygon2D {
                Color = new Color(0.97f, 0.85f, 0.18f),
                Polygon = new Vector2[] {
                    new Vector2(-CollisionHalfExtents.X, -CollisionHalfExtents.Y),
                    new Vector2(CollisionHalfExtents.X, -CollisionHalfExtents.Y),
                    new Vector2(CollisionHalfExtents.X, CollisionHalfExtents.Y),
                    new Vector2(-CollisionHalfExtents.X, CollisionHalfExtents.Y),
                },
            };
            AddChild(marker);
        }

        /// <summary>Applies a behavior script's <c>player.hurt(amount)</c> intent, honoring <see cref="IsInvulnerable"/>, and raises <see cref="Died"/> at zero health.</summary>
        public void Hurt(double amount) {
            PlayerHealth.HurtResult result = PlayerHealth.Hurt(Health, InvulnerabilityRemaining, amount);
            if (!result.Applied) {
                GD.Print($"Player: hurt {amount} absorbed by invulnerability ({InvulnerabilityRemaining:0.00}s remaining)");
                return;
            }

            Health = result.Health;
            InvulnerabilityRemaining = result.InvulnerabilityRemaining;
            GD.Print($"Player: hurt {amount} -> health {Health}/{MaxHealth}");

            if (result.Died) {
                GD.Print("Player: health reached 0 -> respawning");
                Died?.Invoke();
            }
        }

        /// <summary>Applies a behavior script's <c>player.heal(amount)</c> intent, clamped at <see cref="MaxHealth"/>.</summary>
        public void Heal(double amount) {
            Health = PlayerHealth.Heal(Health, MaxHealth, amount);
            GD.Print($"Player: heal {amount} -> health {Health}/{MaxHealth}");
        }

        /// <summary>Resets the player to <paramref name="position"/> at full health with a fresh invulnerability window.</summary>
        public void Respawn(Vector2 position) {
            Position = position;
            Velocity = Vector2.Zero;
            Health = MaxHealth;
            InvulnerabilityRemaining = PlayerHealth.InvulnerabilityDurationSeconds;
            if (marker != null)
                marker.Visible = true;
            GD.Print($"Player: respawned at {position} -> health {Health}/{MaxHealth}");
        }

        public override void _PhysicsProcess(double delta) {
            Vector2 velocity = Velocity;

            if (!IsOnFloor())
                velocity.Y += Gravity * (float)delta;

            if (IsOnFloor() && Input.IsActionJustPressed("jump"))
                velocity.Y = -JumpSpeed;

            float direction = Input.GetAxis("move_left", "move_right");
            velocity.X = direction * MoveSpeed;

            Velocity = velocity;
            MoveAndSlide();

            TickInvulnerability(delta);
        }

        void TickInvulnerability(double delta) {
            if (InvulnerabilityRemaining <= 0) {
                if (marker != null && !marker.Visible)
                    marker.Visible = true;
                return;
            }

            InvulnerabilityRemaining = PlayerHealth.TickInvulnerability(InvulnerabilityRemaining, delta);
            if (marker != null)
                marker.Visible = (int)(InvulnerabilityRemaining / BlinkIntervalSeconds) % 2 == 0;
        }
    }
}
