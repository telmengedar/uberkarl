using System;
using System.Collections.Generic;
using Godot;
using Uberkarl.Behavior;
using Uberkarl.Content;

namespace Uberkarl {

    /// <summary>
    /// Godot glue wiring the Godot-free <c>Uberkarl.Behavior</c> core onto real level subjects (DiVoid #7738,
    /// design #7704 §5.8/§9.1-9.3): scripted tiles, area triggers, and the level script. Added as a child by
    /// <see cref="PlayRuntimeBuilder.Populate"/>, so standalone play (<see cref="LevelPlay"/>) and editor
    /// playtest (<see cref="PlaytestOverlay"/>) get behavior identically (design C-4) -- this class owns no
    /// decision logic beyond "which subjects to create and when to dispatch"; the actual reacting is entirely
    /// the compiled Pooscript's.
    ///
    /// <para>
    /// <b>One subject/facade model, parameterized by kind</b> (DiVoid #7738 KISS guardrail): every scripted
    /// tile cell, every trigger, and the level itself is a plain <see cref="BehaviorSubject"/> with a
    /// different <c>Kind</c> string ("tile"/"trigger"/"level") -- there is no
    /// TileBehaviorSubject/TriggerBehaviorSubject/LevelBehaviorSubject split. This mirrors how the P0 core
    /// itself is built (<see cref="BehaviorSubject"/> already carries <c>Kind</c> as data); this glue never
    /// introduces a parallel subject type.
    /// </para>
    ///
    /// <para>
    /// <b>Contact detection</b> (design #7704 §9.3) is a geometric AABB overlap between the player's
    /// collision box (<see cref="Player.CollisionHalfExtents"/>) and each scripted cell's/trigger's world
    /// rect, re-evaluated every physics frame -- deliberately NOT read off Godot's tilemap physics-slide
    /// collision info, because a tile's SCRIPTED behavior is independent of whether its layer actually
    /// blocks movement (a non-colliding "hurt" layer must still fire <c>onContact</c>). Edge-triggered per
    /// design #7704 §7/§11: a cell/trigger only re-fires when the overlap STATE changes, tracked in
    /// <see cref="contactedTileIds"/>/<see cref="insideTriggerIds"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Intents applied this phase</b> (task #7738 scope -- "the ones meaningful without free objects yet"):
    /// <see cref="HurtIntent"/>/<see cref="HealIntent"/> (against <see cref="Player.Hurt"/>/<see cref="Player.Heal"/>)
    /// and <see cref="SetStateIntent"/> (routed to the level's shared state, the player's state, or the
    /// issuing subject's own private state). Every other intent kind is drained every frame (so the buffer
    /// never grows unbounded) but deliberately left unapplied -- <c>moveTo</c>/move-object and friends arrive
    /// with P2's free-moving objects.
    /// </para>
    ///
    /// <para>
    /// <b>Pooscript-compatibility</b> (DiVoid #7718/#7732/#7738): this glue dispatches exclusively through
    /// <see cref="BehaviorScheduler"/>'s <c>Dispatch*</c> methods and <see cref="BehaviorLoader.CompileBinding"/>
    /// -- the P0 entry points that already run every script invocation through <see cref="BehaviorWatchdog"/>.
    /// It introduces no separate try/catch around a handler invoke of its own, so the pending watchdog swap
    /// (task #7737, replacing thread-abandonment with native <c>ScriptLimits</c>) is entirely a change inside
    /// <c>Uberkarl.Behavior</c> -- a config change from this glue's point of view, exactly as required.
    /// </para>
    /// </summary>
    public partial class BehaviorRuntime : Node {

        // Per-invocation wall-clock watchdog budget (design #7704 §8.3). Generous relative to a 60fps frame
        // (~16.6ms) because it only matters on BREACH -- BehaviorWatchdog never blocks the calling thread past
        // this budget, so a healthy dispatch (the overwhelming common case) returns near-instantly regardless.
        static readonly TimeSpan WatchdogBudget = TimeSpan.FromMilliseconds(50);

        // The level script's own "self" subject id. Deliberately NOT BehaviorSubjectIds.Level ("level") --
        // that id is reserved for the SHARED level-wide state bag every subject reaches via the `level`
        // global (BehaviorLevel.State). Keeping this id distinct preserves the same "self is private,
        // `level`/`player` are shared" split every other subject kind already has; it does not collapse the
        // level script's own self.state into level.state.
        const string LevelScriptSubjectId = "level-script";

        BehaviorWatchdog watchdog;
        BehaviorLoader loader;
        BehaviorScheduler scheduler;
        IntentBuffer intents;
        BehaviorLevel levelFacade;
        BehaviorPlayer playerFacade;
        Player player;
        int tileSize;
        bool hasLevelScript;

        // The world position a died player is sent back to (DiVoid #7743 death -> respawn). Resolved once,
        // in Configure, via the SAME lookup the initial spawn used (PlayRuntimeBuilder.SpawnWorldPosition) --
        // "respawn" is deliberately just "go back to where you started", not a separate spawn-cell concept.
        Vector2 respawnPosition;

        readonly Dictionary<string, BehaviorSubject> subjectsById = new Dictionary<string, BehaviorSubject>();
        readonly List<ScriptedTile> scriptedTiles = new List<ScriptedTile>();
        readonly List<ScriptedTrigger> scriptedTriggers = new List<ScriptedTrigger>();
        readonly HashSet<string> contactedTileIds = new HashSet<string>();
        readonly HashSet<string> insideTriggerIds = new HashSet<string>();

        readonly struct ScriptedTile {
            public ScriptedTile(string subjectId, Rect2 worldRect, GridCell cell) {
                SubjectId = subjectId;
                WorldRect = worldRect;
                Cell = cell;
            }
            public string SubjectId { get; }
            public Rect2 WorldRect { get; }
            public GridCell Cell { get; }
        }

        readonly struct ScriptedTrigger {
            public ScriptedTrigger(string subjectId, Rect2 worldRect) {
                SubjectId = subjectId;
                WorldRect = worldRect;
            }
            public string SubjectId { get; }
            public Rect2 WorldRect { get; }
        }

        /// <summary>
        /// Builds the behavior world for <paramref name="level"/>/<paramref name="spawnedPlayer"/> -- compiles
        /// and registers every scripted tile cell (<see cref="ResolvedLevel.EffectiveTileBehaviors"/>), every
        /// trigger, and the level script, then dispatches the one-time <c>onLevelStart</c>. Call once, any
        /// time before or after this node joins the tree (dispatch here is synchronous -- it does not depend
        /// on <see cref="_PhysicsProcess"/> having run yet).
        /// </summary>
        public void Configure(ResolvedLevel level, Player spawnedPlayer) {
            if (level is null)
                throw new ArgumentNullException(nameof(level));
            player = spawnedPlayer ?? throw new ArgumentNullException(nameof(spawnedPlayer));
            tileSize = level.TileSize;
            respawnPosition = PlayRuntimeBuilder.SpawnWorldPosition(level);
            player.Died += OnPlayerDied;

            watchdog = new BehaviorWatchdog(WatchdogBudget);
            loader = new BehaviorLoader(watchdog);
            scheduler = new BehaviorScheduler(watchdog);
            intents = new IntentBuffer();
            levelFacade = new BehaviorLevel(intents);
            playerFacade = new BehaviorPlayer(intents);
            scheduler.Quarantined += OnQuarantined;

            RegisterScriptedTiles(level);
            RegisterTriggers(level);
            RegisterLevelScript(level);

            if (hasLevelScript)
                scheduler.DispatchLevelStart(LevelScriptSubjectId);
        }

        Dictionary<string, object> Globals(BehaviorSubject self) => new Dictionary<string, object> {
            ["self"] = self,
            ["level"] = levelFacade,
            ["player"] = playerFacade,
            ["event"] = scheduler.CurrentEvent,
        };

        void RegisterScriptedTiles(ResolvedLevel level) {
            foreach (var (layer, cell, binding) in level.EffectiveTileBehaviors()) {
                string subjectId = $"tile:{layer}:{cell.X}:{cell.Y}";
                var gridCell = new GridCell(cell.X, cell.Y);
                var subject = new BehaviorSubject(subjectId, "tile", string.Empty, intents) {
                    Cell = gridCell,
                    Position = new BehaviorVector2(cell.X * tileSize + tileSize / 2.0, cell.Y * tileSize + tileSize / 2.0),
                };
                subjectsById[subjectId] = subject;
                scheduler.Register(new BehaviorInstance(subjectId, loader.CompileBinding(binding, Globals(subject))));

                var worldRect = new Rect2(cell.X * tileSize, cell.Y * tileSize, tileSize, tileSize);
                scriptedTiles.Add(new ScriptedTile(subjectId, worldRect, gridCell));
            }
        }

        void RegisterTriggers(ResolvedLevel level) {
            for (int i = 0; i < level.Triggers.Count; i++) {
                ResolvedAreaTrigger trigger = level.Triggers[i];
                string subjectId = $"trigger:{i}";
                var subject = new BehaviorSubject(subjectId, "trigger", trigger.Name, intents) {
                    Cell = new GridCell(trigger.X, trigger.Y),
                    Position = new BehaviorVector2(trigger.X * tileSize, trigger.Y * tileSize),
                };
                subjectsById[subjectId] = subject;
                scheduler.Register(new BehaviorInstance(subjectId, loader.CompileBinding(trigger.Binding, Globals(subject))));

                var worldRect = new Rect2(trigger.X * tileSize, trigger.Y * tileSize, trigger.Width * tileSize, trigger.Height * tileSize);
                scriptedTriggers.Add(new ScriptedTrigger(subjectId, worldRect));
            }
        }

        void RegisterLevelScript(ResolvedLevel level) {
            if (level.LevelScript is not { } binding)
                return;

            var subject = new BehaviorSubject(LevelScriptSubjectId, "level", string.Empty, intents);
            subjectsById[LevelScriptSubjectId] = subject;
            scheduler.Register(new BehaviorInstance(LevelScriptSubjectId, loader.CompileBinding(binding, Globals(subject))));
            hasLevelScript = true;
        }

        public override void _PhysicsProcess(double delta) {
            if (player is null)
                return;

            playerFacade.Position = new BehaviorVector2(player.Position.X, player.Position.Y);
            playerFacade.Velocity = new BehaviorVector2(player.Velocity.X, player.Velocity.Y);
            playerFacade.IsOnGround = player.IsOnFloor();

            Rect2 playerAabb = new Rect2(player.Position - Player.CollisionHalfExtents, Player.CollisionHalfExtents * 2f);
            GridCell playerCell = new GridCell(Mathf.FloorToInt(player.Position.X / tileSize), Mathf.FloorToInt(player.Position.Y / tileSize));

            DispatchTileContacts(playerAabb);
            DispatchTriggerOverlaps(playerAabb, playerCell);

            if (hasLevelScript)
                scheduler.DispatchUpdate(LevelScriptSubjectId, delta);

            ApplyIntents();
        }

        void DispatchTileContacts(Rect2 playerAabb) {
            foreach (ScriptedTile tile in scriptedTiles) {
                bool touching = playerAabb.Intersects(tile.WorldRect);
                bool wasTouching = contactedTileIds.Contains(tile.SubjectId);
                if (touching == wasTouching)
                    continue;

                var other = new EventParty("player", string.Empty, tile.Cell);
                if (touching) {
                    contactedTileIds.Add(tile.SubjectId);
                    scheduler.DispatchContact(tile.SubjectId, other);
                } else {
                    contactedTileIds.Remove(tile.SubjectId);
                    scheduler.DispatchContactLeave(tile.SubjectId, other);
                }
            }
        }

        void DispatchTriggerOverlaps(Rect2 playerAabb, GridCell playerCell) {
            foreach (ScriptedTrigger trigger in scriptedTriggers) {
                bool inside = playerAabb.Intersects(trigger.WorldRect);
                bool wasInside = insideTriggerIds.Contains(trigger.SubjectId);
                if (inside == wasInside)
                    continue;

                var who = new EventParty("player", string.Empty, playerCell);
                if (inside) {
                    insideTriggerIds.Add(trigger.SubjectId);
                    scheduler.DispatchEnter(trigger.SubjectId, who);
                } else {
                    insideTriggerIds.Remove(trigger.SubjectId);
                    scheduler.DispatchLeave(trigger.SubjectId, who);
                }
            }
        }

        void ApplyIntents() {
            foreach (BehaviorIntent intent in intents.Drain()) {
                switch (intent) {
                    case HurtIntent hurt:
                        player.Hurt(hurt.Amount);
                        break;
                    case HealIntent heal:
                        player.Heal(heal.Amount);
                        break;
                    case SetStateIntent setState:
                        ApplySetState(setState);
                        break;
                    default:
                        // Every other intent kind (MoveTo*/SetGraphic/Despawn/Spawn/SetTile/Message/Teleport/
                        // SetSpawn/SetPhysics/ScheduleTimer) is deliberately not applied in P1 (task #7738
                        // scope -- "moveTo/move-object come with P2 objects, not here"). Draining the buffer
                        // regardless keeps it from growing unbounded even though nothing acts on these yet.
                        break;
                }
            }
        }

        void ApplySetState(SetStateIntent intent) {
            if (intent.SubjectId == BehaviorSubjectIds.Level)
                levelFacade.State[intent.Key] = intent.Value;
            else if (intent.SubjectId == BehaviorSubjectIds.Player)
                playerFacade.State[intent.Key] = intent.Value;
            else if (subjectsById.TryGetValue(intent.SubjectId, out BehaviorSubject subject))
                subject.SeedState(intent.Key, intent.Value);
        }

        // Death -> respawn (DiVoid #7743, design #7704 §15 Q-4): built-in and minimal for now -- no
        // lives/game-over, just straight back to the level's spawn cell at full health. The future
        // data-driven path (noted, not built here) is a scriptable `onDeath` facade hook so a level script
        // could override this -- Player.Died is the seam that hook would sit behind.
        void OnPlayerDied() {
            GD.Print("BehaviorRuntime: player died, respawning at level spawn.");
            player.Respawn(respawnPosition);
        }

        // "Logged once" falls out of BehaviorScheduler's own state machine (design #7704 §8.3) -- this is
        // simply where that one log line surfaces on the Godot side, matching the repo's existing GD.Print
        // diagnostic style (e.g. LevelPlay).
        void OnQuarantined(BehaviorQuarantineEvent quarantine) {
            GD.PrintErr($"BehaviorRuntime: subject '{quarantine.SubjectId}' quarantined " +
                $"({(quarantine.TriggeringEvent is { } kind ? kind.ToString() : "init")}): {quarantine.Reason}");
        }
    }
}
