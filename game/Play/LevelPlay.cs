using System;
using System.IO;
using Godot;
using Uberkarl.Content;
using Uberkarl.Packages;

namespace Uberkarl {

    /// <summary>
    /// The playable scene root. Loads the sample level, builds its tile layers (with collision on
    /// the main layer), spawns the player at the level's player-start, and frames the level with a
    /// static camera. Mirrors <see cref="LevelDisplay"/> but adds the player and physics.
    /// </summary>
    public partial class LevelPlay : Node2D {

        const string PackagePath = "res://content/sample.pkg";
        static readonly Vector2I FallbackStart = new Vector2I(1, 1);

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
                    AddChild(TileMapLevelBuilder.Build(level));
                    SpawnPlayer(level);
                    AddCamera(level);
                    GD.Print($"LevelPlay: playable {level.Width}x{level.Height} level, " +
                        $"{level.CollidingTileIds.Count} solid tile ids across {level.Layers.Count} layers.");
                } finally {
                    registry.Dispose();
                }
            } catch (Exception exception) {
                GD.PrintErr($"LevelPlay: {exception.GetType().Name}: {exception.Message}");
            }
        }

        void SpawnPlayer(ResolvedLevel level) {
            Vector2I start = level.PlayerStart is { } cell
                ? new Vector2I(cell.X, cell.Y)
                : FallbackStart;

            Player player = new Player {
                Name = "Player",
                // Centre horizontally in the cell; sit near the top of the cell so gravity settles
                // the body onto whatever solid tile is below it (proving gravity + collision).
                Position = new Vector2(start.X * level.TileSize + level.TileSize / 2f, start.Y * level.TileSize),
            };
            AddChild(player);
        }

        void AddCamera(ResolvedLevel level) {
            Camera2D camera = new Camera2D {
                Name = "Camera",
                Position = new Vector2(level.Width * level.TileSize / 2f, level.Height * level.TileSize / 2f),
                Zoom = new Vector2(3f, 3f),
            };
            AddChild(camera);
            camera.MakeCurrent();
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
