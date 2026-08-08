using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Uberkarl.Content;
using Uberkarl.Editor;
using Uberkarl.Packages;

namespace Uberkarl.Diagnostics {

    /// <summary>
    /// Headless, in-engine reproduction harness for the <see cref="PlayRuntimeBuilder"/>/<see cref="BehaviorRuntime"/> chain, run via <c>tools/run-behavior-probe.ps1</c>.
    /// </summary>
    public partial class BehaviorHeadlessProbe : Node2D {

        const string PackagePath = "res://content/sample.pkg";
        const int SpikeCellX = 20;
        const int SpikeCellY = 11;
        const int PhysicsFramesToRun = 20;
        const int FallStartRowsAboveSpike = 4;
        const int FallFramesToRun = 90;

        const string PlatformBodyName = "moving-platform-1";
        const string JumpBlockBodyName = "jump-block-1";
        const int PlatformLandingFrames = 30;
        const int PlatformRideFrames = 90;
        const float PlatformMovedThreshold = 1f;
        const float PlatformRideToleranceX = 4f;
        const int JumpBlockGroundRow = 12;
        const int JumpBlockFramesToRun = 60;
        const float JumpBlockBumpThreshold = 2f;
        const float JumpBlockSettledTolerance = 1f;

        public override async void _Ready() {
            GD.Print("[probe] BehaviorHeadlessProbe._Ready starting");

            byte[] bytes = Godot.FileAccess.GetFileAsBytes(PackagePath);
            if (bytes == null || bytes.Length == 0) {
                GD.PrintErr($"[probe] package '{PackagePath}' is missing or empty -- cannot run probe.");
                GetTree().Quit(2);
                return;
            }
            GD.Print($"[probe] loaded {PackagePath} ({bytes.Length} bytes)");

            bool pathAForced = await RunPath("PathA-Forced(editor-Play projection, teleported-into-tile)", () => BuildPathA(bytes), forceOverlap: true);
            bool pathBForced = await RunPath("PathB-Forced(stand-alone LevelLoader, teleported-into-tile)", () => BuildPathB(bytes), forceOverlap: true);

            bool pathAReal = await RunPath("PathA-RealFall(editor-Play projection, real physics)", () => BuildPathA(bytes), forceOverlap: false);
            bool pathBReal = await RunPath("PathB-RealFall(stand-alone LevelLoader, real physics)", () => BuildPathB(bytes), forceOverlap: false);

            bool platformRides = await RunPlatformCheck(bytes);
            bool jumpBlockReacts = await RunJumpBlockCheck(bytes);

            GD.Print($"[probe] SUMMARY PathA-Forced={(pathAForced ? "OK" : "NO DAMAGE")} PathB-Forced={(pathBForced ? "OK" : "NO DAMAGE")} " +
                $"PathA-RealFall={(pathAReal ? "OK" : "NO DAMAGE")} PathB-RealFall={(pathBReal ? "OK" : "NO DAMAGE")} " +
                $"Platform={(platformRides ? "OK" : "NO MOVEMENT/RIDE")} JumpBlock={(jumpBlockReacts ? "OK" : "NO REACTION")}");

            bool allOk = pathAReal && platformRides && jumpBlockReacts;
            GetTree().Quit(allOk ? 0 : 1);
        }

        /// <summary>DiVoid #7863: asserts the moving platform's body actually moves AND the player standing on it rides it (moves with it).</summary>
        async Task<bool> RunPlatformCheck(byte[] bytes) {
            const string Label = "ObjectCheck-Platform(moves + player rides)";
            GD.Print($"[probe] ==== {Label} ====");
            var root = new Node2D { Name = "World_" + Label };
            AddChild(root);

            ResolvedLevel level = BuildPathB(bytes);
            Player player = PlayRuntimeBuilder.Populate(root, level);
            Node2D platform = root.FindChild(PlatformBodyName, recursive: true, owned: false) as Node2D;
            if (platform is null) {
                GD.PrintErr($"[probe] {Label}: FAILED to find platform body node '{PlatformBodyName}'.");
                root.QueueFree();
                return false;
            }

            Vector2 platformStart = platform.Position;
            player.Position = new Vector2(platformStart.X, platformStart.Y - level.TileSize * 2);
            player.Velocity = Vector2.Zero;
            GD.Print($"[probe] {Label}: platform spawned at {platformStart}, player dropped from {player.Position}");

            for (int frame = 0; frame < PlatformLandingFrames; frame++) {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                player.Position = new Vector2(platform.Position.X, player.Position.Y);
                player.Velocity = new Vector2(0, player.Velocity.Y);
            }

            Vector2 playerBeforeRide = player.Position;
            Vector2 platformBeforeRide = platform.Position;
            GD.Print($"[probe] {Label}: after landing, player at {playerBeforeRide}, platform at {platformBeforeRide}");

            for (int frame = 0; frame < PlatformRideFrames; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

            Vector2 playerAfterRide = player.Position;
            Vector2 platformAfterRide = platform.Position;

            float platformDelta = platformAfterRide.X - platformBeforeRide.X;
            float playerDelta = playerAfterRide.X - playerBeforeRide.X;
            bool platformMoved = Mathf.Abs(platformDelta) > PlatformMovedThreshold;
            bool playerRode = Mathf.Abs(playerDelta - platformDelta) < PlatformRideToleranceX && Mathf.Abs(playerDelta) > PlatformMovedThreshold;

            GD.Print($"[probe] {Label}: platform delta {platformDelta:0.00}px, player delta {playerDelta:0.00}px over {PlatformRideFrames} frames");
            GD.Print($"[probe] VERDICT {Label}: platformMoved={platformMoved} playerRode={playerRode}");

            root.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            return platformMoved && playerRode;
        }

        /// <summary>DiVoid #7863: asserts the jump-block reacts (bumps up then settles back) when hit from below.</summary>
        async Task<bool> RunJumpBlockCheck(byte[] bytes) {
            const string Label = "ObjectCheck-JumpBlock(reacts when hit from below)";
            GD.Print($"[probe] ==== {Label} ====");
            var root = new Node2D { Name = "World_" + Label };
            AddChild(root);

            ResolvedLevel level = BuildPathB(bytes);
            Player player = PlayRuntimeBuilder.Populate(root, level);
            Node2D jumpBlock = root.FindChild(JumpBlockBodyName, recursive: true, owned: false) as Node2D;
            if (jumpBlock is null) {
                GD.PrintErr($"[probe] {Label}: FAILED to find jump-block body node '{JumpBlockBodyName}'.");
                root.QueueFree();
                return false;
            }

            float initialY = jumpBlock.Position.Y;
            player.Position = new Vector2(jumpBlock.Position.X, JumpBlockGroundRow * level.TileSize - Player.CollisionHalfExtents.Y);
            player.Velocity = new Vector2(0, -player.JumpSpeed);
            GD.Print($"[probe] {Label}: jump-block at {jumpBlock.Position}, player jumping from {player.Position}");

            float minY = initialY;
            for (int frame = 0; frame < JumpBlockFramesToRun; frame++) {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                minY = Mathf.Min(minY, jumpBlock.Position.Y);
            }

            float finalY = jumpBlock.Position.Y;
            bool bumped = initialY - minY > JumpBlockBumpThreshold;
            bool settled = Mathf.Abs(finalY - initialY) < JumpBlockSettledTolerance;

            GD.Print($"[probe] {Label}: initialY={initialY:0.00} minY={minY:0.00} finalY={finalY:0.00}");
            GD.Print($"[probe] VERDICT {Label}: bumped={bumped} settled={settled}");

            root.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            return bumped && settled;
        }

        static ResolvedLevel BuildPathA(byte[] bytes) {
            EditableLevel editable = EditableLevelReader.FromPackageBytes(bytes);
            return EditableLevelSnapshot.ToResolvedLevel(editable);
        }

        static ResolvedLevel BuildPathB(byte[] bytes) {
            var registry = new PackageRegistry(PackageReader.Open(new MemoryStream(bytes)));
            try {
                ResourceReference levelRef = FindLevelReference(registry.Origin);
                return LevelLoader.Load(registry, levelRef);
            } finally {
                registry.Dispose();
            }
        }

        static ResourceReference FindLevelReference(Package package) {
            foreach (ResourceEntry entry in package.Manifest.Resources) {
                if (entry.Kind == ResourceKind.Level)
                    return ResourceReference.ToSelf(entry.Path);
            }

            throw new LevelContentException("Package does not contain a level resource.");
        }

        async Task<bool> RunPath(string label, Func<ResolvedLevel> buildLevel, bool forceOverlap) {
            GD.Print($"[probe] ==== {label} ====");
            var root = new Node2D { Name = "World_" + label };
            AddChild(root);

            ResolvedLevel level;
            try {
                level = buildLevel();
            } catch (Exception ex) {
                GD.PrintErr($"[probe] {label}: FAILED to build ResolvedLevel: {ex.GetType().Name}: {ex.Message}");
                root.QueueFree();
                return false;
            }

            int scriptedTileCount = level.EffectiveTileBehaviors().Count();
            GD.Print($"[probe] {label}: level {level.Width}x{level.Height}, tileSize={level.TileSize}, " +
                $"scriptedTiles={scriptedTileCount}, triggers={level.Triggers.Count}, hasLevelScript={level.LevelScript != null}");

            Player player = PlayRuntimeBuilder.Populate(root, level);

            Vector2 spikeCenter = new Vector2(
                SpikeCellX * level.TileSize + level.TileSize / 2f,
                SpikeCellY * level.TileSize + level.TileSize / 2f);

            int framesToRun;
            if (forceOverlap) {
                player.Gravity = 0f;
                player.MoveSpeed = 0f;
                player.Position = spikeCenter;
                player.Velocity = Vector2.Zero;
                framesToRun = PhysicsFramesToRun;
                GD.Print($"[probe] {label}: player TELEPORTED into spike cell ({SpikeCellX},{SpikeCellY}) world {spikeCenter} (gravity/move frozen), Health before = {player.Health}");
            } else {
                Vector2 dropStart = spikeCenter - new Vector2(0, FallStartRowsAboveSpike * level.TileSize);
                player.Position = dropStart;
                player.Velocity = Vector2.Zero;
                framesToRun = FallFramesToRun;
                GD.Print($"[probe] {label}: player DROPPED from {dropStart} ({FallStartRowsAboveSpike} cells above spike cell ({SpikeCellX},{SpikeCellY})) under real gravity, Health before = {player.Health}");
            }

            double healthBefore = player.Health;

            for (int frame = 0; frame < framesToRun; frame++) {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                if (forceOverlap) {
                    player.Position = spikeCenter;
                    player.Velocity = Vector2.Zero;
                }
            }

            double healthAfter = player.Health;
            bool damaged = healthAfter < healthBefore;
            GD.Print($"[probe] {label}: player final position {player.Position} (spike world rect top-left " +
                $"{new Vector2(SpikeCellX * level.TileSize, SpikeCellY * level.TileSize)}, size {level.TileSize}x{level.TileSize}), " +
                $"Health after {framesToRun} physics frames = {healthAfter}");
            GD.Print($"[probe] VERDICT {label}: health {healthBefore} -> {healthAfter} ({(damaged ? "OK" : "NO DAMAGE")})");

            root.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            return damaged;
        }
    }
}
