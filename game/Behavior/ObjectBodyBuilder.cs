using Godot;
using Uberkarl.Content;

namespace Uberkarl {

    /// <summary>
    /// Builds the free-moving Godot body for a resolved object placement (DiVoid #7863, design #7704 §9.4):
    /// an <see cref="AnimatableBody2D"/> for <see cref="ObjectCollisionRole.Solid"/> (blocks and carries the
    /// player — a moving platform) or an <see cref="Area2D"/> sensor for <see cref="ObjectCollisionRole.Passthrough"/>
    /// (detects contact but never blocks — a jump-block). Spawned at the placement's grid cell, then free.
    /// A solid body carries a child sensor as well, because a body that blocks the player can never overlap them
    /// and would otherwise be invisible to the contact sweep (DiVoid #8237).
    /// </summary>
    public static class ObjectBodyBuilder {

        /// <summary>Node name of the child sensor a solid body carries; see <see cref="ContactSensor"/>.</summary>
        public const string ContactSensorName = "ContactSensor";

        /// <summary>Pixels the solid body's sensor extends past its collision shape, so a player resting against the surface still registers.</summary>
        const float SensorMargin = 1f;

        public static Node2D Build(ResolvedObjectPlacement placement, int tileSize) {
            bool solid = placement.CollisionRole == ObjectCollisionRole.Solid;
            CollisionObject2D body = solid
                ? new AnimatableBody2D { Name = NodeName(placement.Name) }
                : new Area2D { Name = NodeName(placement.Name) };

            body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(tileSize, tileSize) } });
            body.AddChild(new Sprite2D { Texture = LoadTexture(placement) });

            if (solid)
                body.AddChild(BuildContactSensor(tileSize));

            Node2D node = (Node2D)body;
            node.Position = new Vector2(
                placement.Cell.X * tileSize + tileSize / 2f,
                placement.Cell.Y * tileSize + tileSize / 2f);
            return node;
        }

        /// <summary>
        /// The <see cref="Area2D"/> that reports player contact for <paramref name="body"/>: a passthrough body is
        /// its own sensor, a solid body carries one as a child. Null for a body that has neither.
        /// </summary>
        public static Area2D ContactSensor(Node2D body) => body as Area2D ?? body.GetNodeOrNull<Area2D>(ContactSensorName);

        static Area2D BuildContactSensor(int tileSize) {
            var sensor = new Area2D { Name = ContactSensorName };
            float size = tileSize + SensorMargin * 2;
            sensor.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(size, size) } });
            return sensor;
        }

        static ImageTexture LoadTexture(ResolvedObjectPlacement placement) {
            Image image = new Image();
            Error status = image.LoadPngFromBuffer(placement.Graphic);
            if (status != Error.Ok)
                throw new LevelContentException($"Object '{placement.Name}' graphic is not a readable PNG (Godot error {status}).");
            return ImageTexture.CreateFromImage(image);
        }

        static string NodeName(string name) => string.IsNullOrEmpty(name) ? "Object" : name;
    }
}
