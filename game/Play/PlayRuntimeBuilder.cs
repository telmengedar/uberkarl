using Godot;
using Uberkarl.Content;

namespace Uberkarl {

    /// <summary>
    /// Builds the playable world — tile layers (parallax + collision via <see cref="TileMapLevelBuilder"/>),
    /// an optional background fill, a <see cref="Player"/> spawned at the level's default spawn (or a
    /// fallback cell when it declares none), and a camera that follows the player within the level bounds —
    /// from a <see cref="ResolvedLevel"/>, adding it all as children of a caller-supplied root. This is the
    /// ONE play runtime shared by <see cref="LevelPlay"/> (loads the sample package from disk) and the
    /// level editor's playtest overlay (projects the level currently being authored via
    /// <c>Uberkarl.Editor.EditableLevelSnapshot</c>) — only how the <see cref="ResolvedLevel"/> is obtained
    /// differs; how it is played is identical.
    /// </summary>
    public static class PlayRuntimeBuilder {

        const float CameraZoom = 3f;

        // Camera position-smoothing speed: a fast smooth — crisp follow that still eases the last few
        // pixels rather than snapping 1:1. This is the tuning seam for future camera scripting
        // (deadzone / look-ahead / zoom transitions); no scripting is built here.
        const float CameraSmoothingSpeed = 20f;

        // The back CanvasLayer index for the background fill: negative so it always draws behind the
        // level's layer-0 world content, and (being on a CanvasLayer) it does not scroll with the camera.
        const int BackgroundLayerIndex = -100;

        static readonly Vector2I FallbackStart = new Vector2I(1, 1);

        /// <summary>
        /// Adds the level's background fill, tile layers, player, following camera, behavior runtime
        /// (DiVoid #7738 -- scripted tiles/area triggers/level script, via <see cref="BehaviorRuntime"/>),
        /// and health HUD (DiVoid #7743, via <see cref="PlayerHud"/>) as children of <paramref name="root"/>.
        /// Returns the spawned <see cref="Player"/>.
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

        /// <summary>The world-pixel position a freshly-spawned or respawned player belongs at for
        /// <paramref name="level"/> -- <see cref="ResolvedLevel.DefaultSpawnPosition"/> when the level
        /// declares one, else <see cref="FallbackStart"/>. Shared by <see cref="SpawnPlayer"/> (initial
        /// spawn) and <see cref="BehaviorRuntime"/> (DiVoid #7743 death -&gt; respawn) so both resolve the
        /// same cell the same way -- respawn is deliberately "go back to where you started", not a distinct
        /// lookup.</summary>
        public static Vector2 SpawnWorldPosition(ResolvedLevel level) {
            Vector2I start = level.DefaultSpawnPosition is { } cell
                ? new Vector2I(cell.X, cell.Y)
                : FallbackStart;

            // Centre horizontally in the cell; sit near the top of the cell so gravity settles the body
            // onto whatever solid tile is below it (proving gravity + collision).
            return new Vector2(start.X * level.TileSize + level.TileSize / 2f, start.Y * level.TileSize);
        }

        // The behavior runtime is a plain child node added last, after the player exists (DiVoid #7738,
        // design #7704 §9.1 -- "PlayRuntimeBuilder.Populate gains a BehaviorRuntime step"). Being in THIS
        // shared builder is what makes standalone play (LevelPlay) and editor playtest (PlaytestOverlay)
        // get behavior identically (design C-4) -- neither caller needs its own wiring.
        static void AttachBehaviorRuntime(Node2D root, ResolvedLevel level, Player player) {
            BehaviorRuntime runtime = new BehaviorRuntime { Name = "BehaviorRuntime" };
            root.AddChild(runtime);
            runtime.Configure(level, player);
        }

        // Same "shared builder, both callers get it identically" reasoning as the behavior runtime above
        // (DiVoid #7743) -- the HUD is what makes hurt/heal intents actually observable during a playtest,
        // not just a spike silently decrementing an invisible number.
        static void AttachHud(Node2D root, Player player) {
            PlayerHud hud = new PlayerHud { Name = "PlayerHud" };
            root.AddChild(hud);
            hud.Configure(player);
        }

        // Renders the level's optional solid background fill behind every layer. A full-rect ColorRect
        // on a back CanvasLayer covers the whole viewport regardless of camera position and does not
        // scroll with the world, so a finite parallax layer's edge never hard-cuts to the clear colour.
        // When the level declares no backgroundColor, nothing is added and the viewport clear colour
        // shows through.
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

        // The camera is a child of the player, so it follows automatically. Limits from the level
        // bounds clamp it at the edges so it never scrolls past them; a fast position smoothing keeps
        // the follow crisp without snapping. Parallax2D layers read this current camera to compute
        // their scroll.
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
