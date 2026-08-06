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
    /// Headless, in-engine reproduction harness for DiVoid #7747 (REOPENED): runs the REAL
    /// <see cref="PlayRuntimeBuilder"/>/<see cref="BehaviorRuntime"/> chain against the REAL
    /// <c>content/sample.pkg</c> from inside an actual running Godot <c>SceneTree</c> (physics ticking,
    /// nodes truly entering the tree) — not a C# unit test on a seam. Every prior "fixed" write-up on
    /// #7747 was validated only by source review + Godot-free unit tests; this is the missing
    /// verification layer those could not provide (no Godot MCP / no engine access at the time).
    ///
    /// <para>
    /// Exercises BOTH ways a <see cref="ResolvedLevel"/> reaches <see cref="PlayRuntimeBuilder.Populate"/>:
    /// <list type="bullet">
    /// <item><b>Path A</b> — the editor-Play projection Toni actually plays:
    /// <see cref="EditableLevelReader.FromPackageBytes"/> -&gt; <see cref="EditableLevelSnapshot.ToResolvedLevel"/>
    /// (mirrors <c>LevelEditor._Ready</c> -&gt; <c>LevelEditor.StartPlaytest</c>).</item>
    /// <item><b>Path B</b> — the stand-alone play path, for comparison:
    /// <see cref="LevelLoader.Load"/> (mirrors <see cref="LevelPlay"/>).</item>
    /// </list>
    /// For each, a player is planted directly on top of the spike's authored cell (20,11) with gravity
    /// and move speed zeroed (so the probe — not incidental physics — controls whether contact geometry
    /// overlaps), several physics frames are stepped via the real engine loop, and
    /// <see cref="Player.Health"/> is compared before/after. Every existing <c>[behavior]</c>
    /// <c>GD.Print</c> stage marker in <see cref="BehaviorRuntime"/> (commit 71c3483) still fires
    /// normally and lands in the SAME captured stdout as this probe's own <c>[probe]</c> lines, so a
    /// single run pinpoints the dead stage without any extra wiring.
    /// </para>
    ///
    /// <para>
    /// Reusable: see <c>tools/run-behavior-probe.ps1</c>, which builds the C# solution then runs this
    /// scene headless and greps the verdict lines, so this behavior chain can be re-verified in-engine
    /// on demand instead of only via source review or Godot-free unit tests.
    /// </para>
    /// </summary>
    public partial class BehaviorHeadlessProbe : Node2D {

        const string PackagePath = "res://content/sample.pkg";
        const int SpikeCellX = 20;
        const int SpikeCellY = 11;
        const int PhysicsFramesToRun = 20;
        // How many cells above the spike the player is dropped from for the "real fall" scenario --
        // enough clearance that gravity + MoveAndSlide fully settle the body onto the (solid) spike tile
        // under genuine physics, exactly like a player walking off a ledge onto it, rather than the
        // forced-overlap scenario which proves nothing about real collision response.
        const int FallStartRowsAboveSpike = 4;
        const int FallFramesToRun = 90; // 1.5s @ 60fps -- ample settle time for a 4-tile (64px) drop.

        public override async void _Ready() {
            GD.Print("[probe] BehaviorHeadlessProbe._Ready starting");

            byte[] bytes = Godot.FileAccess.GetFileAsBytes(PackagePath);
            if (bytes == null || bytes.Length == 0) {
                GD.PrintErr($"[probe] package '{PackagePath}' is missing or empty -- cannot run probe.");
                GetTree().Quit(2);
                return;
            }
            GD.Print($"[probe] loaded {PackagePath} ({bytes.Length} bytes)");

            // Sanity scenario: force the player's AABB to deeply overlap the spike's rect (teleport +
            // re-pin every frame). This proves the dispatch/intent-application chain CAN work when the
            // geometry genuinely overlaps -- it does NOT prove real gameplay ever produces that overlap.
            bool pathAForced = await RunPath("PathA-Forced(editor-Play projection, teleported-into-tile)", () => BuildPathA(bytes), forceOverlap: true);
            bool pathBForced = await RunPath("PathB-Forced(stand-alone LevelLoader, teleported-into-tile)", () => BuildPathB(bytes), forceOverlap: true);

            // Reproduction scenario: drop the player from above under REAL gravity/collision onto the
            // (solid) spike tile -- exactly what "walk onto the spike" looks like in an actual playtest.
            // No repositioning after the initial drop point; Godot's own MoveAndSlide governs where the
            // body ends up.
            bool pathAReal = await RunPath("PathA-RealFall(editor-Play projection, real physics)", () => BuildPathA(bytes), forceOverlap: false);
            bool pathBReal = await RunPath("PathB-RealFall(stand-alone LevelLoader, real physics)", () => BuildPathB(bytes), forceOverlap: false);

            GD.Print($"[probe] SUMMARY PathA-Forced={(pathAForced ? "OK" : "NO DAMAGE")} PathB-Forced={(pathBForced ? "OK" : "NO DAMAGE")} " +
                $"PathA-RealFall={(pathAReal ? "OK" : "NO DAMAGE")} PathB-RealFall={(pathBReal ? "OK" : "NO DAMAGE")}");

            // Path A-RealFall is the closest headless reproduction of what Toni actually plays (editor
            // Play button, real physics) -- gate the exit code on THAT.
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
                // Freeze gravity/horizontal movement so the PROBE controls whether the player's AABB
                // overlaps the spike's world rect for the whole window -- isolating contact-detection/
                // dispatch/intent-application from incidental platformer physics.
                player.Gravity = 0f;
                player.MoveSpeed = 0f;
                player.Position = spikeCenter;
                player.Velocity = Vector2.Zero;
                framesToRun = PhysicsFramesToRun;
                GD.Print($"[probe] {label}: player TELEPORTED into spike cell ({SpikeCellX},{SpikeCellY}) world {spikeCenter} (gravity/move frozen), Health before = {player.Health}");
            } else {
                // Real physics: drop the player from directly above the spike so gravity + MoveAndSlide
                // (the SAME collision resolution real gameplay uses, since the spike tile is Solid=true --
                // tools/SampleContent/Program.cs) decide where the body actually ends up. No repositioning
                // after this point.
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
                    // Re-pin every frame in case Godot's own collision response nudges the body -- this
                    // scenario is testing SCRIPTED contact detection in isolation, not physics resolution,
                    // so the overlap must hold for the whole window regardless of what MoveAndSlide does.
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
