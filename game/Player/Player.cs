using Godot;

namespace Uberkarl {

    /// <summary>
    /// A minimal, readable platformer controller: gravity, left/right movement, and a jump,
    /// driven by the "move_left" / "move_right" / "jump" input actions. Collides against the
    /// main tile layer via a rectangular collision shape. Built in code so the playable scene
    /// stays a single script-on-Node2D with no hand-authored sub-scene.
    /// </summary>
    public partial class Player : CharacterBody2D {

        const float MoveSpeed = 90f;      // px/s horizontal
        const float JumpSpeed = 330f;     // px/s initial upward jump velocity
        const float Gravity = 900f;       // px/s^2

        static readonly Vector2 BodyHalfExtents = new Vector2(6f, 12f);

        public override void _Ready() {
            CollisionShape2D collider = new CollisionShape2D {
                Shape = new RectangleShape2D { Size = BodyHalfExtents * 2f },
            };
            AddChild(collider);

            Polygon2D marker = new Polygon2D {
                Color = new Color(0.97f, 0.85f, 0.18f),
                Polygon = new Vector2[] {
                    new Vector2(-BodyHalfExtents.X, -BodyHalfExtents.Y),
                    new Vector2(BodyHalfExtents.X, -BodyHalfExtents.Y),
                    new Vector2(BodyHalfExtents.X, BodyHalfExtents.Y),
                    new Vector2(-BodyHalfExtents.X, BodyHalfExtents.Y),
                },
            };
            AddChild(marker);
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
