using System;
using System.IO;
using Godot;
using Uberkarl.Content;
using Uberkarl.Packages;

namespace Uberkarl {

    public partial class LevelDisplay : Node2D {

        const string PackagePath = "res://content/sample.pkg";

        public override void _Ready() {
            try {
                byte[] bytes = Godot.FileAccess.GetFileAsBytes(PackagePath);
                if (bytes == null || bytes.Length == 0) {
                    GD.PrintErr($"LevelDisplay: package '{PackagePath}' is missing or empty.");
                    return;
                }

                PackageRegistry registry = new PackageRegistry(PackageReader.Open(new MemoryStream(bytes)));
                try {
                    ResolvedLevel level = LevelLoader.Load(registry, FindLevelReference(registry.Origin));
                    Node2D holder = new Node2D { Name = "LevelHolder" };
                    holder.AddChild(TileMapLevelBuilder.Build(level));
                    AddChild(holder);
                    FitToViewport(holder, level);
                    GD.Print($"LevelDisplay: rendered {level.Width}x{level.Height} level with {level.TileGraphics.Count} tiles across {level.Layers.Count} layers.");
                } finally {
                    registry.Dispose();
                }
            } catch (Exception exception) {
                GD.PrintErr($"LevelDisplay: {exception.GetType().Name}: {exception.Message}");
            }
        }

        static ResourceReference FindLevelReference(Package package) {
            foreach (ResourceEntry entry in package.Manifest.Resources) {
                if (entry.Kind == ResourceKind.Level)
                    return ResourceReference.ToSelf(entry.Path);
            }

            throw new LevelContentException("Package does not contain a level resource.");
        }

        void FitToViewport(Node2D holder, ResolvedLevel level) {
            Vector2 viewport = GetViewportRect().Size;
            Vector2 levelPixels = new Vector2(level.Width * level.TileSize, level.Height * level.TileSize);
            if (levelPixels.X <= 0 || levelPixels.Y <= 0)
                return;

            float scale = Mathf.Min(viewport.X / levelPixels.X, viewport.Y / levelPixels.Y) * 0.85f;
            holder.Scale = new Vector2(scale, scale);
            holder.Position = (viewport - levelPixels * scale) / 2f;
        }
    }
}
