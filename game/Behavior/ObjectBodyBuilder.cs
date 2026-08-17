using Godot;
using Uberkarl.Content;

namespace Uberkarl {

    /// <summary>
    /// Builds the free-moving Godot body for a resolved object placement (DiVoid #7863, design #7704 §9.4):
    /// an <see cref="AnimatableBody2D"/> for <see cref="ObjectCollisionRole.Solid"/> (blocks and carries the
    /// player — a moving platform) or an <see cref="Area2D"/> sensor for <see cref="ObjectCollisionRole.Passthrough"/>
    /// (detects contact but never blocks — a jump-block). Spawned at the placement's grid cell, then free.
    /// </summary>
    public static class ObjectBodyBuilder {

        public static Node2D Build(ResolvedObjectPlacement placement, int tileSize) {
            CollisionObject2D body = placement.CollisionRole == ObjectCollisionRole.Solid
                ? new AnimatableBody2D { Name = NodeName(placement.Name) }
                : new Area2D { Name = NodeName(placement.Name) };

            body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(tileSize, tileSize) } });
            body.AddChild(new Sprite2D { Texture = LoadTexture(placement) });

            Node2D node = (Node2D)body;
            node.Position = new Vector2(
                placement.Cell.X * tileSize + tileSize / 2f,
                placement.Cell.Y * tileSize + tileSize / 2f);
            return node;
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
