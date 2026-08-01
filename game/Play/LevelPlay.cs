using System;
using System.IO;
using Godot;
using Uberkarl.Content;
using Uberkarl.Packages;

namespace Uberkarl {

    /// <summary>
    /// The playable scene root. Loads the sample level, builds its tile layers (collision enabled
    /// per layer, parallax layers wrapped in <see cref="Parallax2D"/>), spawns the player at the
    /// level's default spawn, and gives the player a <see cref="Camera2D"/> that follows within the
    /// level bounds. Mirrors <see cref="LevelDisplay"/> but adds the player, physics, and scrolling.
    /// </summary>
    public partial class LevelPlay : Node2D {

        const string PackagePath = "res://content/sample.pkg";
        const float CameraZoom = 3f;
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
                    Player player = SpawnPlayer(level);
                    AttachCamera(player, level);
                    GD.Print($"LevelPlay: playable {level.Width}x{level.Height} level, " +
                        $"{level.CollidingTileIds.Count} solid tile ids across {level.Layers.Count} layers.");
                } finally {
                    registry.Dispose();
                }
            } catch (Exception exception) {
                GD.PrintErr($"LevelPlay: {exception.GetType().Name}: {exception.Message}");
            }
        }

        Player SpawnPlayer(ResolvedLevel level) {
            Vector2I start = level.DefaultSpawnPosition is { } cell
                ? new Vector2I(cell.X, cell.Y)
                : FallbackStart;

            Player player = new Player {
                Name = "Player",
                // Centre horizontally in the cell; sit near the top of the cell so gravity settles
                // the body onto whatever solid tile is below it (proving gravity + collision).
                Position = new Vector2(start.X * level.TileSize + level.TileSize / 2f, start.Y * level.TileSize),
            };
            AddChild(player);
            return player;
        }

        // The camera is a child of the player, so it follows automatically. Limits from the level
        // bounds clamp it at the edges so it never scrolls past them; light smoothing softens the
        // follow. Parallax2D layers read this current camera to compute their scroll.
        void AttachCamera(Player player, ResolvedLevel level) {
            Camera2D camera = new Camera2D {
                Name = "Camera",
                Zoom = new Vector2(CameraZoom, CameraZoom),
                LimitLeft = 0,
                LimitTop = 0,
                LimitRight = level.Width * level.TileSize,
                LimitBottom = level.Height * level.TileSize,
                PositionSmoothingEnabled = true,
                PositionSmoothingSpeed = 8f,
            };
            player.AddChild(camera);
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
