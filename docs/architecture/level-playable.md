# Architectural Document: Playable Level — Tile Collision + Player Controller

Phase 1c/1d. Turns the *displayed* level (Phase 1b, `level-display.md`) into a *playable* one:
a `CharacterBody2D` player with gravity, movement, and jump that collides against solid
tiles. Builds directly on the schema decisions ratified by Toni on 2026-08-01 (DiVoid #7416).

**This document reflects the level model v0.2** (Phase 1d, DiVoid #7419) — refined from Toni's
play-test feedback on the v0.1 increment (PR #4). The current model is stated below; §12 is the
v0.1 → v0.2 changelog so the deltas are explicit. Terms that were removed in v0.2 (the layer
`role` enum; the single `playerStart`) appear only in §12 as history, never as the current model.

This is a lean design update shipped together with a working increment. Scope is deliberately
small — see Non-Scope.

## 1. Problem Statement

The engine can render a level from a package but the level is inert. We need a player that
walks and jumps around it, standing on solid ground and platforms, without falling through.
Collision must be a property of the content (the tile/level), not hard-coded in the engine,
and must be honored only where the ratified schema says it should be.

## 2. Scope & Non-Scope

**In scope:** the `collides` tile flag; the per-layer `collision` flag; native Godot tile
collision via one shared TileSet; named `spawns` with a `defaultSpawn`; editor-adjustable player
physics; a minimal platformer controller; an extended sample level; a playable scene verified
in-engine.

**Out of scope (YAGNI):** enemies, hazards, death/respawn, scripted behavior, a level editor,
animation, camera work beyond a static framing camera, active-collision-layer switching, level
exits/transitions, parallax scroll speeds, and per-level/script-driven physics overrides — all
seams only (see §10).

## 3. Ratified Model Direction (v0.2, from #7418 / #7419)

- Collision is **a flag on the tile definition** (`collides`) AND **a flag on the layer**
  (`collision`). A tile collides only when a `collides` tile sits on a `collision:true` layer.
- **Draw order is the layer array order** (back → front) and is **independent of collision** —
  no role concept; a collision layer can sit anywhere in the draw stack.
- Player spawns are **named**: a `spawns` map (`name → {x,y}`) plus a `defaultSpawn` name.
  Runtime spawns at the default; named lookup is the seam for future transitions.
- Player physics (gravity/jump/speed) are **editor-adjustable** with the current values as
  defaults; per-level and script-driven overrides are a future seam.
- Keep resource kinds separate; level grid uniform square; flat `int[]` cells with a seam for
  alternates. Active-collision-layer *switching* among collision layers is **future** (seam only).

## 4. Schema v0.2 (`src/Uberkarl.Content`, engine-agnostic)

| Element | Field | Semantics | Default (back-compat) |
|---|---|---|---|
| Tile definition | `collides` (bool) | Tile is solid. Enforced only on a `collision:true` layer. | `false` — absent tiles are non-solid |
| Level layer | `collision` (bool) | Whether this layer is an active collision layer. A non-collision layer **never** collides, even for a `collides` tile. | `false` — a layer is display-only unless it opts in |
| Level | `spawns` (map `name → {x,y}` grid cell, tile units) | Named spawn points. | empty — a display-only level may declare none |
| Level | `defaultSpawn` (string, name) | The spawn used when no specific spawn is requested. | null — required whenever `spawns` is non-empty |

- `collision` serializes as a plain boolean; `collides`, `spawns`, and `defaultSpawn` are
  omitted from JSON when default/empty/null (the sample only writes what it needs).
- **Collision and draw order are orthogonal.** Draw order is purely the layer array order (first
  layer at the bottom, last on top), unchanged from Phase 1b and independent of the `collision`
  flag. Authors order layers for the visual result they want and tag whichever layer(s) should
  collide — the two concerns never interfere. This is the smallest model that satisfies the
  ratified rule without a role taxonomy.
- The loader now (a) collects the set of colliding tile ids from the tile set, (b) carries each
  layer's `collision` flag through to `ResolvedLevel`, (c) validates every spawn is within the
  grid and that `defaultSpawn` names a declared spawn (and is present iff spawns exist).
  `ResolvedLevel` gains `CollidingTileIds`, `Spawns`, `DefaultSpawn` (plus a `DefaultSpawnPosition`
  convenience and a `TryGetSpawn(name)` seam); `ResolvedLayer` gains `Collision`. All
  parsing/validation stays Godot-free and unit-tested.

## 5. Package → Godot Collision Mapping (don't fight the engine)

`TileMapLevelBuilder.Build(ResolvedLevel)` builds **one shared** `TileSet` from the graphics: it
has a single physics layer, and each **colliding** tile id gets a full-tile square collision
polygon on its atlas source tile. A non-colliding tile has no polygon.

Every `TileMapLayer` references that one shared TileSet, and each layer sets Godot's native
`CollisionEnabled` property **from its layer's `collision` flag**. Godot generates static
collision only for layers with `CollisionEnabled == true`; a `collision:false` layer produces no
collision bodies even when it places a `collides=true` tile (verified in §8 — the player walks
straight through the non-collision backdrop stone pillar). This is the engine-native
implementation of the ratified rule.

A tile is a *reference*, not a copy — the same graphic appearing in one shared TileSet used by
several layers is not real texture duplication. Consolidating to one TileSet (from the v0.1
two-TileSet split) removes the duplicated atlas sources and makes the collision decision a
per-layer switch, which is also the natural seam for future active-layer switching (§10).

## 6. Player & Playable Scene (Godot side, game compile set)

- **`Player` (`CharacterBody2D`)** — a readable platformer controller: gravity while airborne,
  `move_left`/`move_right` horizontal velocity, `jump` (edge-triggered, only when on floor).
  It builds its own `CollisionShape2D` (a rectangle) and a bright `Polygon2D` marker in code,
  so no hand-authored sub-scene is needed. Collision layer/mask default to 1, matching the
  tilemap's physics layer.
- **`LevelPlay` (`Node2D`)** — the playable scene root (`scenes/level_playable.tscn`, the run
  target). Loads the sample package (same path/flow as `LevelDisplay`), builds the tile layers,
  spawns the `Player` at the level's default spawn (`DefaultSpawnPosition`, fallback cell if the
  level declares none), and adds a static framing `Camera2D`. Errors are caught at the scene
  boundary and logged; the scene never crashes.
- **Input actions** (`move_left` = A/←, `move_right` = D/→, `jump` = Space/W/↑) are defined in
  `project.godot` for all devices.

Player physics are **editor-adjustable** `[Export]` fields — `MoveSpeed` (90 px/s), `JumpSpeed`
(330 px/s), `Gravity` (900 px/s²) — with the ratified feel values as defaults (not tuned in
v0.2; the ground-to-platform gap of ~3 tiles is comfortably clearable). Exporting them leaves a
clean seam for per-level and script-driven overrides later without changing the controller.

## 7. Contracts & Interfaces (unchanged seams)

- `ResolvedLevel` remains the **Godot-free boundary object** (primitives + `byte[]` only), so
  all schema parsing/validation stays unit-testable without the engine — the collision data is
  just an id set on it. The builder is a pure translation of that object into Godot nodes.
- The package/reference/resolver seam (#7413) is untouched.

## 8. Verification (Godot MCP)

- `dotnet build Uberkarl.csproj` → 0 errors; `Uberkarl.Content.Tests` 17/17 (was 13),
  `Uberkarl.Packages.Tests` 31/31; Content-lib coverage line 92.8% / branch 83.3% (all changed
  schema classes 100%). Authored-source comment-grep (TODO/FIXME/HACK/XXX + commented-out
  code): 0.
- Ran `scenes/level_playable.tscn`: player **spawns at the default spawn** (`start`, cell (2,7))
  and gravity settles it **onto the grass surface** (velocity 0, does not fall through). Engine
  inspection: `backdrop` layer `collision_enabled=false`, `terrain` layer `collision_enabled=true`
  (one shared TileSet, per-layer switch). Holding `move_right` → player traverses the full level
  and is **stopped hard against the right stone wall** (x≈298 at the col-19 wall, velocity 0),
  having **passed straight through the non-collision backdrop pillar** at cols 9-10 en route
  (reaching the wall is only possible if the pillar does not block). `jump` → player **lifts off
  the grass** and settles back. Log: `playable 20x12 level, 4 solid tile ids across 3 layers`.
  `get_editor_errors`: 0.

## 9. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| Input events bound to a specific device would ignore a real keyboard | Bound to all devices (`device:-1`) so both real keys and simulated actions work. |
| A solid tile reused on a non-collision layer silently blocking the player | The per-layer `CollisionEnabled` switch on the shared TileSet makes non-collision layers produce no collision bodies at all — structurally impossible to block. |
| A level with malformed spawns (out of bounds, default names a missing spawn) | Loader validates every spawn in-bounds and that `defaultSpawn` names a declared spawn (present iff spawns exist); a bad level throws `LevelContentException` at the Godot-free boundary, caught at the scene boundary. |

## 10. Future Seams (NOT built)

- **Active-collision-layer switching.** v0.2 collides on whichever layer(s) carry `collision:true`.
  Generalizing to a runtime-switchable active collision layer means flipping `CollisionEnabled`
  on the shared-TileSet layers at switch time — no schema or `ResolvedLevel` change is forced.
- **Level exits / transitions.** Named `spawns` + `TryGetSpawn(name)` are the entry seam: a
  transition system later enters a level at a chosen named spawn instead of the default. The
  transition/exit system itself is not built.
- **Parallax scroll speeds.** A per-layer `scrollSpeed` needs a scrolling camera and
  larger-than-screen levels (its own chunk). No dead field is added now.
- **Per-level & script-driven physics overrides.** The player's `[Export]` physics fields are the
  seam; nothing consumes an override source yet.

## 11. Open Questions for Toni

1. **Spawn shape** — a `spawns` map + `defaultSpawn` name is in. Is a bare `{x,y}` per spawn
   enough, or will spawns eventually need a facing/direction or a spawn *entity* (e.g. for
   transition-linked entry)?
2. **Collision-layer count** — v0.2 permits *any* number of `collision:true` layers (all collide
   simultaneously). The sample uses exactly one. Is "many simultaneous collision layers" a real
   need, or should the model assume a single active collision layer until switching lands?
3. **Spawn-less levels** — a level may declare no spawns (display-only), and the playable scene
   falls back to a hard-coded cell. Keep that fallback, or should a playable level be *required*
   to declare a default spawn (loader error if absent)?

## 12. Changelog: v0.1 → v0.2 (Phase 1d, DiVoid #7419)

Refinements from Toni's play-test of the v0.1 increment. Identifiers listed as *removed* here do
not appear anywhere else as the current model.

| v0.1 | v0.2 | Why |
|---|---|---|
| Layer `role` enum (`background`/`main`/`foreground`); only `main` collided | Removed. Per-layer `collision` (bool); any `collision:true` layer collides | Toni: roles conflated collision with draw stacking; a plain orthogonal collision flag is clearer |
| Two TileSets (physics + plain) chosen by role | One shared TileSet; per-layer native `CollisionEnabled` | Toni: a tile is a reference, not a copy — two TileSets was not real dedup; one shared set + a per-layer switch is the clearer engine-native path |
| Single `playerStart` (`{x,y}`) | Named `spawns` map + `defaultSpawn`; runtime uses the default | Sets up future level transitions (enter at a named spawn) without building them |
| Physics as `const` fields | Physics as `[Export]` fields, same default values | Editor-adjustable feel; a clean seam for per-level/script overrides. Not tuned (Toni: feels fine) |

Draw order was already the layer array order in v0.1 and stays so — v0.2 makes its independence
from collision explicit (no role can drive it).
