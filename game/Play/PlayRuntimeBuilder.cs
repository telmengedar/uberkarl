using Godot;
using Uberkarl.Content;

namespace Uberkarl {

    /// <summary>
    /// Builds the playable world (tile layers, player, camera, behavior runtime, HUD) from a
    /// <see cref="ResolvedLevel"/>, shared by <see cref="LevelPlay"/> and the editor's playtest overlay.
    /// </summary>
    public static class PlayRuntimeBuilder {

        const float CameraZoom = 3f;

        const float CameraSmoothingSpeed = 20f;

        const int BackgroundLayerIndex = -100;

        static readonly Vector2I FallbackStart = new Vector2I(1, 1);

        /// <summary>
        /// Adds the level's background fill, tile layers, player, following camera, behavior runtime, and
        /// health HUD as children of <paramref name="root"/>. Returns the spawned <see cref="Player"/>.
        /// </summary>
        public static Player Populate(Node2D root, ResolvedLevel level) {
            AddBackgroundFill(root, level);
            root.AddChild(TileMapLevelBuilder.Build(level));
            Player player = SpawnPlayer(root, level);
            AttachCamera(player, level);
            AttachBehaviorRuntime(root, level, player);
            AttachHud(root, player);
            return player;
        }

        /// <summary>
        /// The world-pixel position a freshly-spawned or respawned player belongs at for <paramref name="level"/>.
        /// </summary>
        public static Vector2 SpawnWorldPosition(ResolvedLevel level) {
            Vector2I start = level.DefaultSpawnPosition is { } cell
                ? new Vector2I(cell.X, cell.Y)
                : FallbackStart;

            return new Vector2(start.X * level.TileSize + level.TileSize / 2f, start.Y * level.TileSize);
        }

        static void AttachBehaviorRuntime(Node2D root, ResolvedLevel level, Player player) {
            BehaviorRuntime runtime = new BehaviorRuntime { Name = "BehaviorRuntime" };
            root.AddChild(runtime);
            runtime.Configure(level, player);
        }

        static void AttachHud(Node2D root, Player player) {
            PlayerHud hud = new PlayerHud { Name = "PlayerHud" };
            root.AddChild(hud);
            hud.Configure(player);
        }

        static void AddBackgroundFill(Node2D root, ResolvedLevel level) {
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
            root.AddChild(backdrop);
        }

        static Player SpawnPlayer(Node2D root, ResolvedLevel level) {
            Player player = new Player {
                Name = "Player",
                Position = SpawnWorldPosition(level),
            };
            root.AddChild(player);
            return player;
        }

        static void AttachCamera(Player player, ResolvedLevel level) {
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
    }
}
