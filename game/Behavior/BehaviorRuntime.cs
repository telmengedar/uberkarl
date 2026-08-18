using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Uberkarl.Behavior;
using Uberkarl.Content;

namespace Uberkarl {

    /// <summary>
    /// Godot glue wiring the Godot-free <c>Uberkarl.Behavior</c> core onto real level subjects: scripted tiles, area triggers, the level script, and free-moving objects.
    /// </summary>
    public partial class BehaviorRuntime : Node {

        const float ContactMargin = 1f;

        const string LevelScriptSubjectId = "level-script";

        static readonly bool DiagnosticsEnabled = false;

        BehaviorLoader loader;
        BehaviorScheduler scheduler;
        IntentBuffer intents;
        BehaviorLevel levelFacade;
        BehaviorPlayer playerFacade;
        Player player;
        int tileSize;
        bool hasLevelScript;

        Vector2 respawnPosition;

        readonly Dictionary<string, BehaviorSubject> subjectsById = new Dictionary<string, BehaviorSubject>();
        readonly Dictionary<string, Node2D> objectBodiesById = new Dictionary<string, Node2D>();
        readonly List<ScriptedTile> scriptedTiles = new List<ScriptedTile>();
        readonly List<ScriptedTrigger> scriptedTriggers = new List<ScriptedTrigger>();
        readonly List<ScriptedObject> scriptedObjects = new List<ScriptedObject>();
        readonly HashSet<string> contactedTileIds = new HashSet<string>();
        readonly HashSet<string> insideTriggerIds = new HashSet<string>();
        readonly HashSet<string> contactedObjectIds = new HashSet<string>();
        readonly HashSet<string> quarantinedSubjectIds = new HashSet<string>();

        /// <summary>Subject ids quarantined so far.</summary>
        public IReadOnlyCollection<string> QuarantinedSubjectIds => quarantinedSubjectIds;

        /// <summary>Object subject ids the player is currently in contact with, for host-level tests the core suite cannot reach (DiVoid #8237).</summary>
        public IReadOnlyCollection<string> ContactedObjectIds => contactedObjectIds;

        /// <summary>Names a script can resolve through <c>level.object(...)</c>, for host-level tests the core suite cannot reach (DiVoid #8051).</summary>
        public IReadOnlyCollection<string> ScriptVisibleObjectNames => levelFacade.Objects.Keys;

        bool loggedFirstTick;
        GridCell lastLoggedCell = new GridCell(int.MinValue, int.MinValue);

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

        readonly struct ScriptedObject {
            public ScriptedObject(string subjectId, Node2D body, Area2D sensor, bool hasBehavior) {
                SubjectId = subjectId;
                Body = body;
                Sensor = sensor;
                HasBehavior = hasBehavior;
            }
            public string SubjectId { get; }
            public Node2D Body { get; }
            public Area2D Sensor { get; }
            public bool HasBehavior { get; }
        }

        public override void _Ready() {
            if (DiagnosticsEnabled)
                GD.Print("[behavior] BehaviorRuntime._Ready attached");
        }

        /// <summary>
        /// Builds the behavior world for <paramref name="level"/> and <paramref name="spawnedPlayer"/>: registers
        /// scripted tiles, triggers, and the level script, then dispatches <c>onLevelStart</c>.
        /// </summary>
        public void Configure(ResolvedLevel level, Player spawnedPlayer) {
            if (level is null)
                throw new ArgumentNullException(nameof(level));
            player = spawnedPlayer ?? throw new ArgumentNullException(nameof(spawnedPlayer));
            tileSize = level.TileSize;
            respawnPosition = PlayRuntimeBuilder.SpawnWorldPosition(level);
            player.Died += OnPlayerDied;

            loader = new BehaviorLoader(BehaviorScriptBudgets.DefaultBehavior(), BehaviorScriptBudgets.DefaultInit());
            scheduler = new BehaviorScheduler();
            intents = new IntentBuffer();
            levelFacade = new BehaviorLevel(intents);
            playerFacade = new BehaviorPlayer(intents);
            scheduler.Quarantined += OnQuarantined;

            RegisterScriptedTiles(level);
            RegisterTriggers(level);
            RegisterLevelScript(level);
            RegisterObjects(level);

            if (DiagnosticsEnabled)
                GD.Print($"[behavior] Configure: {scriptedTiles.Count} tile cells, {scriptedTriggers.Count} triggers, " +
                    $"{scriptedObjects.Count} objects, levelScript={hasLevelScript}");

            if (hasLevelScript)
                scheduler.DispatchLevelStart(LevelScriptSubjectId);

            ApplyIntents();
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
            scheduler.Register(new BehaviorInstance(LevelScriptSubjectId, loader.CompileBinding(binding, Globals(subject), BehaviorScriptRole.Init)));
            hasLevelScript = true;
        }

        void RegisterObjects(ResolvedLevel level) {
            for (int i = 0; i < level.Objects.Count; i++) {
                ResolvedObjectPlacement placement = level.Objects[i];
                string subjectId = $"object:{i}";

                Node2D body = ObjectBodyBuilder.Build(placement, tileSize);
                AddChild(body);
                objectBodiesById[subjectId] = body;

                var subject = new BehaviorSubject(subjectId, "object", placement.Name, intents) {
                    Cell = new GridCell(placement.Cell.X, placement.Cell.Y),
                    Position = new BehaviorVector2(body.Position.X, body.Position.Y),
                };
                foreach (KeyValuePair<string, object?> state in placement.State)
                    subject.SeedState(state.Key, state.Value);
                subjectsById[subjectId] = subject;

                // level.object(name) is a lookup by the AUTHORED name, not by the positional runtime id
                // (DiVoid #8051). Two contract decisions, both deliberate:
                //   - a placement with no name occupies no slot: it cannot be addressed by name anyway, and
                //     letting it take the empty-string key would shadow every other nameless object;
                //   - names are explicitly not unique (that is why ObjectsNamed returns a list), so the
                //     single lookup answers with the FIRST placement of that name rather than the last.
                if (!string.IsNullOrEmpty(placement.Name) && !levelFacade.Objects.ContainsKey(placement.Name))
                    levelFacade.Objects[placement.Name] = subject;

                bool hasBehavior = false;
                if (placement.Binding is { } binding) {
                    hasBehavior = true;
                    scheduler.Register(new BehaviorInstance(subjectId, loader.CompileBinding(binding, Globals(subject))));
                    scheduler.DispatchSpawn(subjectId);
                }

                scriptedObjects.Add(new ScriptedObject(subjectId, body, ObjectBodyBuilder.ContactSensor(body), hasBehavior));
            }
        }

        public override void _PhysicsProcess(double delta) {
            if (player is null)
                return;

            playerFacade.Position = new BehaviorVector2(player.Position.X, player.Position.Y);
            playerFacade.Velocity = new BehaviorVector2(player.Velocity.X, player.Velocity.Y);
            playerFacade.IsOnGround = player.IsOnFloor();

            Vector2 marginVector = new Vector2(ContactMargin, ContactMargin);
            Rect2 playerAabb = new Rect2(
                player.Position - Player.CollisionHalfExtents - marginVector,
                Player.CollisionHalfExtents * 2f + marginVector * 2f);
            GridCell playerCell = new GridCell(Mathf.FloorToInt(player.Position.X / tileSize), Mathf.FloorToInt(player.Position.Y / tileSize));

            if (!loggedFirstTick) {
                loggedFirstTick = true;
                if (DiagnosticsEnabled)
                    GD.Print($"[behavior] _PhysicsProcess tick alive (player at {player.Position}, cell {playerCell})");
            } else if (playerCell != lastLoggedCell) {
                if (DiagnosticsEnabled)
                    GD.Print($"[behavior] _PhysicsProcess tick (player at {player.Position}, cell {playerCell})");
            }
            lastLoggedCell = playerCell;

            DispatchTileContacts(playerAabb);
            DispatchTriggerOverlaps(playerAabb, playerCell);
            DispatchObjectContacts();

            if (hasLevelScript)
                scheduler.DispatchUpdate(LevelScriptSubjectId, delta);
            DispatchObjectUpdates(delta);

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
                    if (DiagnosticsEnabled)
                        GD.Print($"[behavior] CONTACT tile cell {tile.Cell} binding {tile.SubjectId}");
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

        void DispatchObjectContacts() {
            foreach (ScriptedObject obj in scriptedObjects) {
                if (obj.Sensor is null)
                    continue;

                bool touching = obj.Sensor.GetOverlappingBodies().Contains(player);
                bool wasTouching = contactedObjectIds.Contains(obj.SubjectId);
                if (touching == wasTouching)
                    continue;

                GridCell cell = new GridCell(Mathf.FloorToInt(obj.Body.Position.X / tileSize), Mathf.FloorToInt(obj.Body.Position.Y / tileSize));
                var other = new EventParty("player", string.Empty, cell);
                if (touching) {
                    contactedObjectIds.Add(obj.SubjectId);
                    scheduler.DispatchContact(obj.SubjectId, other);
                } else {
                    contactedObjectIds.Remove(obj.SubjectId);
                    scheduler.DispatchContactLeave(obj.SubjectId, other);
                }
            }
        }

        void DispatchObjectUpdates(double delta) {
            foreach (ScriptedObject obj in scriptedObjects) {
                if (!obj.HasBehavior)
                    continue;

                BehaviorSubject subject = subjectsById[obj.SubjectId];
                subject.Position = new BehaviorVector2(obj.Body.Position.X, obj.Body.Position.Y);
                subject.Cell = new GridCell(Mathf.FloorToInt(obj.Body.Position.X / tileSize), Mathf.FloorToInt(obj.Body.Position.Y / tileSize));
                scheduler.DispatchUpdate(obj.SubjectId, delta);
            }
        }

        void ApplyIntents() {
            List<BehaviorIntent> drained = intents.Drain().ToList();
            if (DiagnosticsEnabled && drained.Count > 0)
                GD.Print($"[behavior] dispatch -> intents: [{string.Join(", ", drained.Select(i => i.GetType().Name))}]");

            foreach (BehaviorIntent intent in drained) {
                switch (intent) {
                    case HurtIntent hurt: {
                        double before = player.Health;
                        player.Hurt(hurt.Amount);
                        if (DiagnosticsEnabled)
                            GD.Print($"[behavior] applied Hurt {hurt.Amount}, Player.Health {before}->{player.Health}");
                        break;
                    }
                    case HealIntent heal:
                        player.Heal(heal.Amount);
                        break;
                    case SetStateIntent setState:
                        ApplySetState(setState);
                        break;
                    case MoveToCellIntent moveToCell:
                        ApplyMoveToCell(moveToCell);
                        break;
                    case MoveToPositionIntent moveToPosition:
                        ApplyMoveToPosition(moveToPosition);
                        break;
                    case MoveByIntent moveBy:
                        ApplyMoveBy(moveBy);
                        break;
                    default:
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

        void ApplyMoveToCell(MoveToCellIntent intent) {
            if (objectBodiesById.TryGetValue(intent.SubjectId, out Node2D body))
                body.Position = new Vector2(intent.Cell.X * tileSize + tileSize / 2f, intent.Cell.Y * tileSize + tileSize / 2f);
        }

        void ApplyMoveToPosition(MoveToPositionIntent intent) {
            if (objectBodiesById.TryGetValue(intent.SubjectId, out Node2D body))
                body.Position = new Vector2((float)intent.Position.X, (float)intent.Position.Y);
        }

        void ApplyMoveBy(MoveByIntent intent) {
            if (objectBodiesById.TryGetValue(intent.SubjectId, out Node2D body))
                body.Position += new Vector2((float)intent.Dx, (float)intent.Dy);
        }

        void OnPlayerDied() {
            GD.Print("BehaviorRuntime: player died, respawning at level spawn.");
            player.Respawn(respawnPosition);
        }

        void OnQuarantined(BehaviorQuarantineEvent quarantine) {
            quarantinedSubjectIds.Add(quarantine.SubjectId);
            GD.PrintErr($"BehaviorRuntime: subject '{quarantine.SubjectId}' quarantined " +
                $"({(quarantine.TriggeringEvent is { } kind ? kind.ToString() : "init")}): {quarantine.Reason}");
        }
    }
}
