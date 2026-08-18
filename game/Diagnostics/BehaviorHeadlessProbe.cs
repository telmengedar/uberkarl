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
        const string BehaviorRuntimeNodeName = "BehaviorRuntime";
        const int PlatformLandingFrames = 30;
        const int PlatformRideFrames = 90;
        const int PlatformExtendedRunFrames = 1790;
        const int PlatformLateWindowFrames = 90;
        const float PlatformMovedThreshold = 1f;
        const float PlatformRideToleranceX = 4f;
        const float PlatformContactRestTolerance = 4f;
        const int JumpBlockGroundRow = 12;
        const int JumpBlockCycleFrames = 60;
        const int JumpBlockCycleCount = 67;
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
            bool platformReceivesContact = await RunPlatformContactCheck(bytes);
            bool jumpBlockReacts = await RunJumpBlockCheck(bytes);
            bool objectsResolveByName = RunObjectLookupCheck(bytes);

            GD.Print($"[probe] SUMMARY PathA-Forced={(pathAForced ? "OK" : "NO DAMAGE")} PathB-Forced={(pathBForced ? "OK" : "NO DAMAGE")} " +
                $"PathA-RealFall={(pathAReal ? "OK" : "NO DAMAGE")} PathB-RealFall={(pathBReal ? "OK" : "NO DAMAGE")} " +
                $"Platform={(platformRides ? "OK" : "NO MOVEMENT/RIDE")} PlatformContact={(platformReceivesContact ? "OK" : "NO CONTACT")} " +
                $"JumpBlock={(jumpBlockReacts ? "OK" : "NO REACTION")} ObjectLookup={(objectsResolveByName ? "OK" : "NOT BY NAME")}");

            bool allOk = pathAReal && platformRides && platformReceivesContact && jumpBlockReacts && objectsResolveByName;
            GetTree().Quit(allOk ? 0 : 1);
        }

        /// <summary>Drives the moving platform through landing, riding, and an extended run, then asserts it stays unquarantined and still moves within a trailing window.</summary>
        async Task<bool> RunPlatformCheck(byte[] bytes) {
            const string Label = "ObjectCheck-Platform(moves + player rides, 10s+, no quarantine)";
            GD.Print($"[probe] ==== {Label} ====");
            var root = new Node2D { Name = "World_" + Label };
            AddChild(root);

            ResolvedLevel level = BuildPathB(bytes);
            Player player = PlayRuntimeBuilder.Populate(root, level);
            BehaviorRuntime runtime = root.FindChild(BehaviorRuntimeNodeName, recursive: true, owned: false) as BehaviorRuntime;
            Node2D platform = root.FindChild(PlatformBodyName, recursive: true, owned: false) as Node2D;
            if (platform is null || runtime is null) {
                GD.PrintErr($"[probe] {Label}: FAILED to find platform body node '{PlatformBodyName}' or '{BehaviorRuntimeNodeName}'.");
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
            GD.Print($"[probe] {Label}: platform delta {platformDelta:0.00}px, player delta {playerDelta:0.00}px over the first {PlatformLandingFrames + PlatformRideFrames} frames");

            for (int frame = 0; frame < PlatformExtendedRunFrames; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

            float lateWindowMinX = float.MaxValue;
            float lateWindowMaxX = float.MinValue;
            for (int frame = 0; frame < PlatformLateWindowFrames; frame++) {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                lateWindowMinX = Mathf.Min(lateWindowMinX, platform.Position.X);
                lateWindowMaxX = Mathf.Max(lateWindowMaxX, platform.Position.X);
            }

            bool platformStillMovingLate = lateWindowMaxX - lateWindowMinX > PlatformMovedThreshold;
            bool noQuarantine = runtime.QuarantinedSubjectIds.Count == 0;
            int totalFrames = PlatformLandingFrames + PlatformRideFrames + PlatformExtendedRunFrames + PlatformLateWindowFrames;

            GD.Print($"[probe] {Label}: late-window range {lateWindowMaxX - lateWindowMinX:0.00}px over the trailing {PlatformLateWindowFrames} of {totalFrames} total physics frames");
            GD.Print($"[probe] VERDICT {Label}: platformMoved={platformMoved} playerRode={playerRode} " +
                $"platformStillMovingLate={platformStillMovingLate} noQuarantine={noQuarantine} (quarantined=[{string.Join(",", runtime.QuarantinedSubjectIds)}])");

            root.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            return platformMoved && playerRode && platformStillMovingLate && noQuarantine;
        }

        /// <summary>Asserts a script could reach the sample level's objects by their authored names, which is what <c>level.object(name)</c> promises (DiVoid #8051). Needs no frames: the registry is built during Configure.</summary>
        bool RunObjectLookupCheck(byte[] bytes) {
            const string Label = "ObjectCheck-Lookup(level.object resolves authored names)";
            GD.Print($"[probe] ==== {Label} ====");
            var root = new Node2D { Name = "World_" + Label };
            AddChild(root);

            ResolvedLevel level = BuildPathB(bytes);
            PlayRuntimeBuilder.Populate(root, level);
            BehaviorRuntime runtime = root.FindChild(BehaviorRuntimeNodeName, recursive: true, owned: false) as BehaviorRuntime;
            if (runtime is null) {
                GD.PrintErr($"[probe] {Label}: FAILED to find '{BehaviorRuntimeNodeName}'.");
                root.QueueFree();
                return false;
            }

            var visible = runtime.ScriptVisibleObjectNames;
            bool platformResolves = visible.Contains(PlatformBodyName);
            bool jumpBlockResolves = visible.Contains(JumpBlockBodyName);

            GD.Print($"[probe] VERDICT {Label}: platform={platformResolves} jumpBlock={jumpBlockResolves} " +
                $"(script-visible names=[{string.Join(",", visible)}])");

            root.QueueFree();
            return platformResolves && jumpBlockResolves;
        }

        /// <summary>Lands the player on the solid moving platform and asserts the contact sweep sees it — the passthrough jump-block already proves the sensor path, this proves a <see cref="ObjectCollisionRole.Solid"/> body reaches it too (DiVoid #8237).</summary>
        async Task<bool> RunPlatformContactCheck(byte[] bytes) {
            const string Label = "ObjectCheck-PlatformContact(a SOLID object receives contact)";
            GD.Print($"[probe] ==== {Label} ====");
            var root = new Node2D { Name = "World_" + Label };
            AddChild(root);

            ResolvedLevel level = BuildPathB(bytes);
            Player player = PlayRuntimeBuilder.Populate(root, level);
            BehaviorRuntime runtime = root.FindChild(BehaviorRuntimeNodeName, recursive: true, owned: false) as BehaviorRuntime;
            Node2D platform = root.FindChild(PlatformBodyName, recursive: true, owned: false) as Node2D;
            if (platform is null || runtime is null) {
                GD.PrintErr($"[probe] {Label}: FAILED to find platform body node '{PlatformBodyName}' or '{BehaviorRuntimeNodeName}'.");
                root.QueueFree();
                return false;
            }

            string platformSubjectId = null;
            for (int i = 0; i < level.Objects.Count; i++) {
                if (level.Objects[i].Name != PlatformBodyName)
                    continue;
                platformSubjectId = $"object:{i}";
                break;
            }
            if (platformSubjectId is null) {
                GD.PrintErr($"[probe] {Label}: FAILED to resolve a subject id for placement '{PlatformBodyName}'.");
                root.QueueFree();
                return false;
            }

            player.Position = new Vector2(platform.Position.X, platform.Position.Y - level.TileSize * 2);
            player.Velocity = Vector2.Zero;
            GD.Print($"[probe] {Label}: platform '{PlatformBodyName}' is subject '{platformSubjectId}', player dropped from {player.Position}");

            bool sawContact = false;
            for (int frame = 0; frame < PlatformLandingFrames + PlatformRideFrames; frame++) {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                player.Position = new Vector2(platform.Position.X, player.Position.Y);
                player.Velocity = new Vector2(0, player.Velocity.Y);
                if (runtime.ContactedObjectIds.Contains(platformSubjectId))
                    sawContact = true;
            }

            bool grounded = Mathf.Abs(player.Position.Y - (platform.Position.Y - Player.CollisionHalfExtents.Y - level.TileSize / 2f)) < PlatformContactRestTolerance;
            bool noQuarantine = runtime.QuarantinedSubjectIds.Count == 0;
            GD.Print($"[probe] VERDICT {Label}: sawContact={sawContact} playerRestingOnPlatform={grounded} noQuarantine={noQuarantine} " +
                $"(contacted=[{string.Join(",", runtime.ContactedObjectIds)}] player={player.Position} platform={platform.Position})");

            root.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            return sawContact && noQuarantine;
        }

        /// <summary>Drives the jump-block through repeated hit-bump-settle cycles and asserts it stays unquarantined and keeps reacting on every cycle.</summary>
        async Task<bool> RunJumpBlockCheck(byte[] bytes) {
            const string Label = "ObjectCheck-JumpBlock(reacts across many hits, 10s+, no quarantine)";
            GD.Print($"[probe] ==== {Label} ====");
            var root = new Node2D { Name = "World_" + Label };
            AddChild(root);

            ResolvedLevel level = BuildPathB(bytes);
            Player player = PlayRuntimeBuilder.Populate(root, level);
            BehaviorRuntime runtime = root.FindChild(BehaviorRuntimeNodeName, recursive: true, owned: false) as BehaviorRuntime;
            Node2D jumpBlock = root.FindChild(JumpBlockBodyName, recursive: true, owned: false) as Node2D;
            if (jumpBlock is null || runtime is null) {
                GD.PrintErr($"[probe] {Label}: FAILED to find jump-block body node '{JumpBlockBodyName}' or '{BehaviorRuntimeNodeName}'.");
                root.QueueFree();
                return false;
            }

            float restY = jumpBlock.Position.Y;
            bool allCyclesBumped = true;
            bool allCyclesSettled = true;

            for (int cycle = 0; cycle < JumpBlockCycleCount; cycle++) {
                player.Position = new Vector2(jumpBlock.Position.X, JumpBlockGroundRow * level.TileSize - Player.CollisionHalfExtents.Y);
                player.Velocity = new Vector2(0, -player.JumpSpeed);

                float cycleStartY = jumpBlock.Position.Y;
                float minY = cycleStartY;
                for (int frame = 0; frame < JumpBlockCycleFrames; frame++) {
                    await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                    minY = Mathf.Min(minY, jumpBlock.Position.Y);
                }

                float cycleEndY = jumpBlock.Position.Y;
                bool cycleBumped = cycleStartY - minY > JumpBlockBumpThreshold;
                bool cycleSettled = Mathf.Abs(cycleEndY - restY) < JumpBlockSettledTolerance;
                allCyclesBumped &= cycleBumped;
                allCyclesSettled &= cycleSettled;
                GD.Print($"[probe] {Label}: cycle {cycle} startY={cycleStartY:0.00} minY={minY:0.00} endY={cycleEndY:0.00} bumped={cycleBumped} settled={cycleSettled}");
            }

            bool noQuarantine = runtime.QuarantinedSubjectIds.Count == 0;
            int totalFrames = JumpBlockCycleFrames * JumpBlockCycleCount;
            GD.Print($"[probe] VERDICT {Label}: allCyclesBumped={allCyclesBumped} allCyclesSettled={allCyclesSettled} " +
                $"noQuarantine={noQuarantine} over {totalFrames} total physics frames (quarantined=[{string.Join(",", runtime.QuarantinedSubjectIds)}])");

            root.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            return allCyclesBumped && allCyclesSettled && noQuarantine;
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
