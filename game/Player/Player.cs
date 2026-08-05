using System;
using Godot;

namespace Uberkarl {

    /// <summary>
    /// A minimal, readable platformer controller: gravity, left/right movement, and a jump,
    /// driven by the "move_left" / "move_right" / "jump" input actions. Collides against the
    /// collision-enabled tile layers via a rectangular collision shape. Built in code so the playable
    /// scene stays a single script-on-Node2D with no hand-authored sub-scene.
    /// </summary>
    public partial class Player : CharacterBody2D {

        /// <summary>Horizontal move speed in px/s. Editor-adjustable; a seam for per-level and script-driven overrides later.</summary>
        [Export] public float MoveSpeed { get; set; } = 90f;

        /// <summary>Initial upward jump velocity in px/s. Editor-adjustable; a seam for per-level and script-driven overrides later.</summary>
        [Export] public float JumpSpeed { get; set; } = 330f;

        /// <summary>Downward acceleration in px/s^2 applied while airborne. Editor-adjustable; a seam for per-level and script-driven overrides later.</summary>
        [Export] public float Gravity { get; set; } = 900f;

        /// <summary>Starting/maximum health (DiVoid #7738 -- the minimal health model <c>IPlayerFacade.Hurt</c>/<c>Heal</c> intents apply against; design #7704 §15 Q-4 leaves the full death/respawn model open, not built here).</summary>
        [Export] public double MaxHealth { get; set; } = 100;

        /// <summary>Current health, clamped to [0, <see cref="MaxHealth"/>]. Applied by the behavior runtime's drained <c>HurtIntent</c>/<c>HealIntent</c> (game/Behavior/BehaviorRuntime.cs) -- never mutated directly by a script (design #7704 §8.5, intent buffer).</summary>
        public double Health { get; private set; }

        /// <summary>Seconds remaining on the post-hit invulnerability window (DiVoid #7743, design #7704 §15 Q-4). While positive, <see cref="Hurt"/> is a no-op -- this is what keeps continuous hazard contact from chain-draining health every physics frame.</summary>
        public double InvulnerabilityRemaining { get; private set; }

        /// <summary>True while the post-hit invulnerability window (<see cref="InvulnerabilityRemaining"/>) is active.</summary>
        public bool IsInvulnerable => InvulnerabilityRemaining > 0;

        /// <summary>Raised the instant health crosses from positive into zero (DiVoid #7743). Purely a host/glue signal -- not a behavior facade event (design #7704 §15 Q-4 leaves a scriptable <c>onDeath</c> hook for later); <see cref="BehaviorRuntime"/> is the sole subscriber and drives the respawn.</summary>
        public event Action Died;

        /// <summary>Half the player's collision-box size in pixels -- the seam the behavior runtime's tile/trigger contact detection uses to compute the player's world AABB (game/Behavior/BehaviorRuntime.cs), so contact geometry stays a single source of truth with the actual collision shape below.</summary>
        public static readonly Vector2 CollisionHalfExtents = new Vector2(6f, 12f);

        // Hurt-feedback blink cadence while invulnerable (DiVoid #7743) -- a simple visibility toggle on the
        // code-built marker sprite is the whole "damage flash": no sprite asset to flash a tint against yet,
        // and a blink reads clearly as "you got hit, you're briefly safe" without new art.
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

        /// <summary>Applies a behavior script's <c>player.hurt(amount)</c> intent (design #7704 §8.1). No-op while <see cref="IsInvulnerable"/> (DiVoid #7743 i-frames). Otherwise clamped at zero, starts a fresh invulnerability window, and raises <see cref="Died"/> the instant health first reaches zero.</summary>
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

        /// <summary>Applies a behavior script's <c>player.heal(amount)</c> intent (design #7704 §8.1). Clamped at <see cref="MaxHealth"/>. Unlike <see cref="Hurt"/>, never gated by invulnerability (DiVoid #7743 -- i-frames only protect against damage).</summary>
        public void Heal(double amount) {
            Health = PlayerHealth.Heal(Health, MaxHealth, amount);
            GD.Print($"Player: heal {amount} -> health {Health}/{MaxHealth}");
        }

        /// <summary>Resets the player to <paramref name="position"/> at full health with a fresh invulnerability window (DiVoid #7743 death -&gt; respawn; the window guards against instantly re-dying if the spawn cell sits near a hazard). Called by <see cref="BehaviorRuntime"/> in response to <see cref="Died"/> -- the level's named spawn cell lookup lives there, not here (Player owns health, not level geometry).</summary>
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

        // Advances the i-frame countdown and drives the blink flash while it's active (DiVoid #7743); once
        // it lapses the marker is forced back to visible so a hit that ends mid-blink never leaves the
        // player invisible.
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
