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

        // Camera position-smoothing speed: a fast smooth — crisp follow that still eases the last few
        // pixels rather than snapping 1:1. This is the tuning seam for future camera scripting
        // (deadzone / look-ahead / zoom transitions); no scripting is built here.
        const float CameraSmoothingSpeed = 20f;

        // The back CanvasLayer index for the background fill: negative so it always draws behind the
        // level's layer-0 world content, and (being on a CanvasLayer) it does not scroll with the camera.
        const int BackgroundLayerIndex = -100;

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
                    AddBackgroundFill(level);
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

        // Renders the level's optional solid background fill behind every layer. A full-rect ColorRect
        // on a back CanvasLayer covers the whole viewport regardless of camera position and does not
        // scroll with the world, so a finite parallax layer's edge never hard-cuts to the clear colour.
        // When the level declares no backgroundColor, nothing is added and the viewport clear colour
        // shows through, exactly as before.
        void AddBackgroundFill(ResolvedLevel level) {
            if (level.BackgroundColor is not { } fill)
                return;

            CanvasLayer backdrop = new CanvasLayer {
                Name = "BackgroundFill",
                Layer = BackgroundLayerIndex,
            };
            ColorRect rect = new ColorRect {
                Name = "Fill",
                Color = new Color(fill.R / 255f, fill.G / 255f, fill.B / 255f, fill.A / 255f),
            };
            rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            backdrop.AddChild(rect);
            AddChild(backdrop);
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
        // bounds clamp it at the edges so it never scrolls past them; a fast position smoothing keeps
        // the follow crisp without snapping. Parallax2D layers read this current camera to compute
        // their scroll.
        void AttachCamera(Player player, ResolvedLevel level) {
            Camera2D camera = new Camera2D {
                Name = "Camera",
                Zoom = new Vector2(CameraZoom, CameraZoom),
                LimitLeft = 0,
                LimitTop = 0,
                LimitRight = level.Width * level.TileSize,
                LimitBottom = level.Height * level.TileSize,
                PositionSmoothingEnabled = true,
                PositionSmoothingSpeed = CameraSmoothingSpeed,
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
