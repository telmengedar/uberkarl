# Architectural Document: Playable Level — Tile Collision + Player Controller

Phase 1c. Turns the *displayed* level (Phase 1b, `level-display.md`) into a *playable* one:
a `CharacterBody2D` player with gravity, movement, and jump that collides against solid
tiles. Builds directly on the schema decisions ratified by Toni on 2026-08-01 (DiVoid #7416).

This is a lean design update shipped together with a working increment. Scope is deliberately
small — see Non-Scope.

## 1. Problem Statement

The engine can render a level from a package but the level is inert. We need a player that
walks and jumps around it, standing on solid ground and platforms, without falling through.
Collision must be a property of the content (the tile/level), not hard-coded in the engine,
and must be honored only where the ratified schema says it should be.

## 2. Scope & Non-Scope

**In scope:** the `collides` tile flag; the layer `role`; native Godot tile collision on the
main layer; a minimal platformer controller; a player start; an extended sample level; a
playable scene verified in-engine.

**Out of scope (YAGNI):** enemies, hazards, death/respawn, scripted behavior, a level editor,
animation, camera work beyond a static framing camera, and multiple-active-layer switching
(seam only — see §10).

## 3. Ratified Schema Direction (from #7416, quoted)

- Collision is **a flag on the tile definition** (`collides`).
- Layers have a **role** (`background` / `main` / `foreground`); **only the `main` layer
  honors collision** — background and foreground always ignore it, *even if a tile has
  `collides`*.
- Keep resource kinds separate; level grid uniform square; flat `int[]` cells with a seam
  for alternates. Multiple switchable active layers is **future** (seam only).

## 4. Schema v0.1 Additions (`src/Uberkarl.Content`, engine-agnostic)

| Element | Addition | Semantics | Default (back-compat) |
|---|---|---|---|
| Tile definition | `collides` (bool) | Tile is solid. Only enforced on a `main` layer. | `false` — absent tiles are non-solid |
| Level layer | `role` (enum: `background`/`main`/`foreground`) | Only `main` honors collision. | `background` — pre-role levels stay display-only, never collide |
| Level | `playerStart` (optional grid cell `{x,y}`, tile units) | Where the player spawns. | absent → engine falls back to a spawn convention |

- `role` serializes as a **camelCase string** (`"main"`). `collides` and `playerStart` are
  omitted from JSON when default/null (the sample only writes what it needs).
- **Roles govern collision, not draw order.** Draw order remains child/array order (first
  layer at the bottom), unchanged from Phase 1b. Authors should still order layers
  background → main → foreground for the visual result they expect; the role is an orthogonal
  collision tag. Keeping these two concerns separate is the smallest change that satisfies the
  ratified rule without re-architecting the renderer.
- The loader now (a) collects the set of colliding tile ids from the tile set, (b) carries
  each layer's role through to `ResolvedLevel`, (c) validates `playerStart` is within the grid.
  `ResolvedLevel` gains `CollidingTileIds` and `PlayerStart`; `ResolvedLayer` gains `Role`.
  All parsing/validation stays Godot-free and unit-tested.

## 5. Package → Godot Collision Mapping (don't fight the engine)

`TileMapLevelBuilder.Build(ResolvedLevel)` builds **two** `TileSet`s from the same graphics:

- a **physics-enabled** TileSet — has one physics layer; each **colliding** tile id gets a
  full-tile square collision polygon on its atlas source tile;
- a **plain** TileSet — no physics layer.

Each `TileMapLayer` is assigned the physics TileSet **iff its role is `main`**, otherwise the
plain one. Godot's `TileMapLayer` then auto-generates static collision from the polygons — the
native path, no per-cell body juggling. Because background/foreground layers reference a TileSet
with no physics layer, they *cannot* collide even when they place a `collides=true` tile. This
is the unambiguous, engine-native implementation of the ratified rule (verified in §8 — the
player walks straight through the background stone pillar).

Cost: two TileSets duplicate a handful of small textures. For per-level tile counts this is
negligible and keeps the rule airtight; sharing one TileSet would leak collision onto
background layers whenever a solid tile id is reused there.

## 6. Player & Playable Scene (Godot side, game compile set)

- **`Player` (`CharacterBody2D`)** — a readable platformer controller: gravity while airborne,
  `move_left`/`move_right` horizontal velocity, `jump` (edge-triggered, only when on floor).
  It builds its own `CollisionShape2D` (a rectangle) and a bright `Polygon2D` marker in code,
  so no hand-authored sub-scene is needed. Collision layer/mask default to 1, matching the
  tilemap's physics layer.
- **`LevelPlay` (`Node2D`)** — the playable scene root (`scenes/level_playable.tscn`, the run
  target). Loads the sample package (same path/flow as `LevelDisplay`), builds the tile layers,
  spawns the `Player` at `playerStart` (fallback cell if absent), and adds a static framing
  `Camera2D`. Errors are caught at the scene boundary and logged; the scene never crashes.
- **Input actions** (`move_left` = A/←, `move_right` = D/→, `jump` = Space/W/↑) are defined in
  `project.godot` for all devices.

Physics constants (px, px/s): move speed 90, jump 330, gravity 900 — the ground-to-platform
gap (~3 tiles) is comfortably clearable.

## 7. Contracts & Interfaces (unchanged seams)

- `ResolvedLevel` remains the **Godot-free boundary object** (primitives + `byte[]` only), so
  all schema parsing/validation stays unit-testable without the engine — the collision data is
  just an id set on it. The builder is a pure translation of that object into Godot nodes.
- The package/reference/resolver seam (#7413) is untouched.

## 8. Verification (Godot MCP)

- `dotnet build Uberkarl.csproj` → 0 errors; `Uberkarl.Content.Tests` 13/13 (was 7),
  `Uberkarl.Packages.Tests` 31/31; Content-lib coverage line 93.2% / branch 81.8% (all new
  schema classes 100%). Authored-source comment-grep (TODO/FIXME/HACK/XXX + commented-out
  code): 0.
- Ran `scenes/level_playable.tscn`: player **rests on the grass surface** (gravity + collision,
  does not fall through). `simulate_action` move_right + jump → player traverses to the right
  wall and is **stopped by the wall** (collision), **passing through the background stone
  pillar** on the way (background never collides), and **lifts on jump**. Log:
  `playable 20x12 level, 4 solid tile ids across 3 layers`. `get_editor_errors`: 0.

## 9. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| Input events bound to a specific device would ignore a real keyboard | Bound to all devices (`device:-1`) so both real keys and simulated actions work. |
| A solid tile reused on a background layer silently blocking the player | Separate physics/plain TileSets make background collision structurally impossible. |
| Two TileSets duplicating textures at scale | Acceptable for per-level tile counts; revisit only if levels grow large (open question). |

## 10. Future Seam — multiple active layers (NOT built)

The rule is expressed as *"the layer whose role is `main` collides"*. Generalizing to *"the
currently-active layer collides"* later means selecting which layer(s) get the physics TileSet
at build/switch time — no schema or `ResolvedLevel` change is forced. Nothing here assumes a
single main layer beyond the sample content.

## 11. Open Questions for Toni

1. **Player start representation** — kept as an optional `{x,y}` grid cell on the level (you
   offered "a level field or a convention"). Good enough, or do you want multiple named
   spawns / a spawn *entity* later?
2. **Two-TileSet collision split** — clean and rule-tight, but duplicates textures. Fine, or
   would you prefer one shared TileSet with a documented "don't reuse solids on background"
   caveat?
3. **Role vs. draw order** — role is collision-only; draw order stays array order. Keep them
   orthogonal, or should role also drive draw order (background behind, foreground in front)?
4. **Physics tuning** — speed/jump/gravity are placeholder platformer values. Tune now or defer
   to a feel pass?
