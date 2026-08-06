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

        /// <summary>Half the player's collision-box size in pixels -- the seam the behavior runtime's tile/trigger contact detection uses to compute the player's world AABB (game/Behavior/BehaviorRuntime.cs), so contact geometry stays a single source of truth with the actual collision shape below.</summary>
        public static readonly Vector2 CollisionHalfExtents = new Vector2(6f, 12f);

        public override void _Ready() {
            Health = MaxHealth;

            CollisionShape2D collider = new CollisionShape2D {
                Shape = new RectangleShape2D { Size = CollisionHalfExtents * 2f },
            };
            AddChild(collider);

            Polygon2D marker = new Polygon2D {
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

        /// <summary>Applies a behavior script's <c>player.hurt(amount)</c> intent (design #7704 §8.1). Clamped at zero; no death/respawn transition in P1 (design Q-4, open).</summary>
        public void Hurt(double amount) {
            Health = Math.Max(0, Health - amount);
            GD.Print($"Player: hurt {amount} -> health {Health}/{MaxHealth}");
        }

        /// <summary>Applies a behavior script's <c>player.heal(amount)</c> intent (design #7704 §8.1). Clamped at <see cref="MaxHealth"/>.</summary>
        public void Heal(double amount) {
            Health = Math.Min(MaxHealth, Health + amount);
            GD.Print($"Player: heal {amount} -> health {Health}/{MaxHealth}");
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
        }
    }
}
