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

            GD.Print($"[probe] SUMMARY PathA-Forced={(pathAForced ? "OK" : "NO DAMAGE")} PathB-Forced={(pathBForced ? "OK" : "NO DAMAGE")} " +
                $"PathA-RealFall={(pathAReal ? "OK" : "NO DAMAGE")} PathB-RealFall={(pathBReal ? "OK" : "NO DAMAGE")}");

            GetTree().Quit(pathAReal ? 0 : 1);
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
