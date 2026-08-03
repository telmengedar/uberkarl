using System;
using System.IO;
using Godot;
using Uberkarl.Content;
using Uberkarl.Packages;

namespace Uberkarl {

    /// <summary>
    /// The playable scene root. Loads the sample level and runs it through the shared
    /// <see cref="PlayRuntimeBuilder"/> (tile layers with collision/parallax, background fill, player at
    /// the level's default spawn, following camera). Mirrors <see cref="LevelDisplay"/> but adds the
    /// player, physics, and scrolling. The level editor's playtest overlay runs the same builder against a
    /// snapshot of the level currently being edited instead of loading this fixed sample.
    /// </summary>
    public partial class LevelPlay : Node2D {

        const string PackagePath = "res://content/sample.pkg";

        public override void _Ready() {
            try {
                byte[] bytes = Godot.FileAccess.GetFileAsBytes(PackagePath);
                if (bytes == null || bytes.Length == 0) {
                    GD.PrintErr($"LevelPlay: package '{PackagePath}' is missing or empty.");
                    return;
                }

                PackageRegistry registry = new PackageRegistry(PackageReader.Open(new MemoryStream(bytes)));
                try {
                    ResolvedLevel level = LevelLoader.Load(registry, FindLevelReference(registry.Origin));
                    PlayRuntimeBuilder.Populate(this, level);
                    GD.Print($"LevelPlay: playable {level.Width}x{level.Height} level, " +
                        $"{level.CollidingTileIds.Count} solid tile ids across {level.Layers.Count} layers.");
                } finally {
                    registry.Dispose();
                }
            } catch (Exception exception) {
                GD.PrintErr($"LevelPlay: {exception.GetType().Name}: {exception.Message}");
            }
        }

        static ResourceReference FindLevelReference(Package package) {
            foreach (ResourceEntry entry in package.Manifest.Resources) {
                if (entry.Kind == ResourceKind.Level)
                    return ResourceReference.ToSelf(entry.Path);
            }

            throw new LevelContentException("Package does not contain a level resource.");
        }
    }
}
