# Architectural Document: Expanded Tile/Tileset Model + Edit-Tileset Authoring

> Status: **DESIGN ONLY** — no code, no PR. Precise build order for the implementer(s), plus decisions framed for Toni to ratify. Source task DiVoid #7551. Refs: level model v0.2 #7420, level-display #7416, package format #7413, package-VFS model #7572, scalable tile selection #7450, on-screen keyboard #7513, vision #7407.

---

## 1. Problem Statement

A tile in Uberkarl today is *a single graphic + a `collides` boolean* (`TileDefinition`: `Id`, `Graphic` reference, `Collides`). Before we build the **edit-tileset** authoring feature, Toni has expanded what a tile must be able to *be*:

1. **Simple tile** — one graphic → one id (today's model).
2. **Animated tile** — several frames plus timing.
3. **Meta / auto-tile ("terrain type")** — the author paints a *logical type* (e.g. "earth"); the engine **auto-selects the real tile — sprite and possibly a different collision/bounding box — from the pattern of surrounding same-type tiles** (grass bordering earth picks the correct edge/corner sprite). Toni's words: *"I don't want to swap between such tiles every time even though I actually logically want to place the same thing, just that depending on what the surroundings are another real tile fits better."*

Alongside the model, three structural goals:

- **Tileset must become a shared package resource** that levels *reference*, not a per-level embedded copy (removes the redundancy Toni saw — the same fix #7572 already anticipated).
- **Graphic import** — a path for PNGs to enter a package, gamepad-operable where feasible.
- **In-engine, gamepad-first authoring UI** for all of the above, reusing the pop-in radial / summoned-panel / `FocusGrid` / on-screen-keyboard patterns already in the editor.

**Success criteria.** (a) A tileset is an independently authorable, shared resource many levels bind to. (b) The JSON model expresses simple, animated, and terrain tiles, is package-friendly, and stays engine-agnostic (`Uberkarl.Content`, no Godot). (c) The runtime mapping leans on Godot 4's native `TileSet` features (atlas animation, Terrain Sets + peering bitmasks, physics-layer collision polygons) rather than re-implementing them. (d) The whole authoring flow is operable on a gamepad. (e) It ships as a sequence of small, individually valuable PRs.

## 2. Scope & Non-Scope

**In scope (design):**
- The expanded engine-agnostic tile/tileset schema in `src/Uberkarl.Content` (simple, animated, terrain, per-variant collision).
- The JSON-model → runtime Godot `TileSet` mapping (animation, terrains, physics-layer collision).
- Tileset promoted to a standalone shared package resource; how a level binds one; migration from the current embedded/owned tileset.
- Graphic-import approach recommendation.
- The authoring UI shape (add/remove/name tiles, animation frames, terrain/peering authoring, per-tile collision) and the level-side terrain paint tool — described at the interaction level.
- A phased, PR-sized build plan.

**Out of scope (this design):**
- Writing any code, JSON schema DDL, or Godot scene wiring.
- Tile tagging/categories **selection UI** and the "100 tiles" scalable picker — that is task **#7450**, a sibling. This design only *reserves the schema seam* (a `tags`/`category` field on a tile) so #7450 can land without a schema break; it does not design the picker.
- The **sprite/animation editor** (vision #7407 pillar 5, "lowest value-per-effort, defer"). We import graphics; we do not draw them.
- Entity/behaviour authoring, Pooscript, music — unrelated pillars.
- Non-square or per-layer tile sizes (still one square `tileSize` per level, as in v0.2).
- Multiplayer/determinism concerns.

## 3. Assumptions & Constraints

| # | Assumption / Constraint | Confidence |
|---|---|---|
| A1 | Godot 4 `TileSet` is the runtime substrate: `TileSetAtlasSource` (with per-tile animation), Terrain Sets + Terrains with peering bitmasks, and physics layers with per-tile collision polygons. The current `TileMapLevelBuilder` already uses the first and the collision polygon. | High |
| A2 | `Uberkarl.Content` stays **Godot-free** (plain net8.0, unit-tested); all Godot types appear only in `game/` (`TileMapLevelBuilder` and the editor). This layering is load-bearing and must not be broken. | High |
| A3 | The package format (#7413) already has a `tileset` resource kind and a swappable `IResourceResolver`; a level already references a tileset via `ResourceReference` (`packageId:path`). The plumbing to *share* a tileset largely exists; the editor's ownership model is what must change (#7572). | High |
| A4 | The **package-VFS correction (#7572)** is the intended editor save model: package = archive of typed resources; a level is one resource; save merges. Under shared tilesets, *"a level references (not owns) the tileset, so it drops out of the level's contributions."* This design assumes #7572's `PackageContext` / `LevelMergeWriter` land (or land alongside). | Medium — #7572 is held on PR #21's branch; sequencing matters (see §12). |
| A5 | Desktop-only target (#7407). No OS on-screen keyboard on desktop; the custom `OnScreenKeyboard` (#7513) is the text primitive and already exists. | High |
| A6 | Native OS file-open dialogs are **not gamepad-operable**. Any import path that must work on a gamepad needs either a bundled built-in set or a custom in-engine file browser. | High |
| A7 | Godot's `TileMapLayer.set_cells_terrain_connect` writes concrete atlas cells into the layer and stores per-cell terrain association in Godot's own tilemap data. Our portable JSON must carry its own terrain-paint representation and re-drive the connect on load. | High |
| A8 | The editor's interaction paradigm is pop-in / hold-to-reveal radials + summoned panels; `FocusGrid` provides contained 2-D focus wiring; `LevelEditSession` is the intent-level façade over an `EditableLevel` on an undoable command path. New authoring must route through these, not around them. | High |

## 4. Architectural Overview

Three layers, unchanged in shape, extended in content:

```
                 ┌──────────────────────────────────────────────────────────┐
                 │  src/Uberkarl.Content   (engine-agnostic POCOs + loader)   │
                 │  TileSetDefinition ── tiles[] (simple | animated | variant)│
                 │       │              terrainSets[] (logical types + mode)  │
                 │       │              collisionShape per tile               │
                 │  LevelDefinition ── tileSet: ResourceReference (BIND)      │
                 │       │              layers[].cells   (concrete ids)       │
                 │       │              layers[].terrain  (logical paint)  ◄─┐ │
                 │  LevelLoader ─► ResolvedTileSet + ResolvedLevel           │ │
                 └───────────────────────────┬──────────────────────────────┘ │
                                             │ primitives + byte[] only        │
                 ┌───────────────────────────▼──────────────────────────────┐ │
                 │  game/Level  (Godot mapping — "don't fight the engine")   │ │
                 │  TileSetBuilder: atlas sources + animation frames +        │ │
                 │     Terrain Sets/Terrains + peering bits + physics layers  │ │
                 │  TileMapLevelBuilder: places concrete cells directly;      │ │
                 │     for terrain-painted cells → set_cells_terrain_connect ─┘ │
                 └───────────────────────────┬──────────────────────────────────┘
                 ┌───────────────────────────▼──────────────────────────────┐
                 │  src/Uberkarl.Editor + game/Editor  (authoring)           │
                 │  EditableTileSet (new authoring model, shared) +           │
                 │  TileSetEditSession (façade, undoable) ; EditableLevel     │
                 │  binds a tileset. Pop-in radials + summoned panels +       │
                 │  FocusGrid + OnScreenKeyboard drive it. Level paint gains  │
                 │  a "terrain brush".                                        │
                 └────────────────────────────────────────────────────────────┘
```

The **key new idea** is a clean split between *concrete* and *logical* cell content:

- A level layer keeps its existing flat `cells: int[]` of **concrete tile ids** (simple/animated/plain).
- A layer optionally gains a parallel **terrain channel** recording, per cell, the **logical terrain the author painted** (a `(terrainSet, terrain)` pair, sentinel = none). At build/load time the engine runs Godot's terrain-connect over exactly those cells so the *right* atlas variant (and its own collision polygon) is chosen from neighbours — preserving Toni's "paint the type, engine resolves" intent, portably.

## 5. Components & Responsibilities

| Component | Owns | Does NOT own |
|---|---|---|
| **`TileSetDefinition`** (`Content`, expanded) | The portable description of a tileset: its tiles (simple/animated/variant), its terrain sets/terrains, per-tile collision shape. JSON-serializable, Godot-free. Becomes the payload of a standalone `tileset` package resource. | Graphic bytes (holds `ResourceReference`s); any Godot type; placement. |
| **`TileDefinition`** (expanded) | One tile's identity + how it renders: graphic ref(s), optional animation (frames + speed), optional terrain membership (which terrain set/terrain + peering bits), collision shape, optional tags/category (reserved for #7450). | Which cells use it; the atlas layout in Godot. |
| **`TerrainSetDefinition` / `TerrainDefinition`** (new, `Content`) | The *logical types*: a terrain set (matching mode) and its terrains (name + author colour). Mirrors Godot's terrain-set/terrain concept, engine-free. | Which concrete tiles satisfy a pattern (that lives as peering bits on tiles) — it only *names* the type. |
| **`CollisionShape` descriptor** (new, `Content`) | A tile's collision footprint: none / full-tile / rectangle / polygon / named preset (e.g. slope). Portable points/enum, no Godot. | Physics simulation; the actual `TileData` polygon (built in `game/`). |
| **`LevelDefinition` / `LayerDefinition`** (extended) | Level binds a tileset by `ResourceReference`. A layer keeps `cells` (concrete) and gains an optional `terrain` channel (logical paint). | The tileset content (now referenced, not embedded). |
| **`LevelLoader` / new `TileSetLoader`** (`Content`) | Resolve a tileset reference → a fully-materialized `ResolvedTileSet` (tile ids → graphic bytes + animation metadata + terrain metadata + collision shapes); validate; produce `ResolvedLevel` carrying both concrete cells and terrain paint. All validation Godot-free and unit-testable. | Any Godot construction. |
| **`TileSetBuilder`** (new, `game/Level`, split out of `TileMapLevelBuilder`) | Translate a `ResolvedTileSet` → a runtime Godot `TileSet`: one atlas source per tile (multi-frame for animated), terrain sets + terrains + per-tile peering bits, physics layer + per-tile collision polygon from the shape descriptor. | Placement / the tilemap layers. |
| **`TileMapLevelBuilder`** (extended) | Build the layer tree from a `ResolvedLevel`: place concrete cells as today; for terrain-painted cells, drive `set_cells_terrain_connect` so Godot resolves the variant. Keep the shared-`TileSet`, per-layer `CollisionEnabled`, and `Parallax2D` behaviour intact. | Tileset construction (delegates to `TileSetBuilder`); authoring. |
| **`EditableTileSet` + `EditableTile` (extended) + `TileSetEditSession`** (new/extended, `Uberkarl.Editor`) | The authoring model of a tileset and the undoable intent-level façade over it (add/remove/rename tile, set frames, define terrain, assign peering bits, set collision). Mirrors the `EditableLevel`/`LevelEditSession` pattern. Holds graphic bytes in memory so a tileset round-trips without a live handle. | Godot UI; file IO; the level's cells. |
| **`LevelEditor` + new `TileSetEditor` UI** (`game/Editor`) | Composition roots translating input into session calls and reflecting results. `TileSetEditor` is the new "edit tileset" surface; `LevelEditor` gains a **terrain brush** and a tileset-binding affordance. Reuse `PopInMenu`, `FocusGrid`, `OnScreenKeyboard`, `PackageBrowser`. | Edit logic (lives in the sessions); model authority. |
| **Graphic import adapter** (new, `game/Editor`) | Bring PNG bytes into the editing session: a bundled starter set (gamepad) + a file pick (mouse/keyboard) + optional drag-drop. Produces `tilegraphic` contributions merged into the package (#7572 merge writer). | Editing the image; the sprite editor (deferred). |

**Single-responsibility note:** `TileMapLevelBuilder` today does both tileset construction and layer placement. As the tileset grows (animation, terrains, per-variant collision), **split tileset construction into `TileSetBuilder`**. Placement and tileset-build become independently testable, and the editor can reuse `TileSetBuilder` to preview a tileset without a level.

## 6. Interactions & Data Flow

### 6.1 Load / render (runtime)
1. `LevelDefinition.tileSet` (a `ResourceReference`) resolves through the package registry to a `tileset` resource → `TileSetDefinition`.
2. `TileSetLoader` materializes it into a `ResolvedTileSet`: for each tile, graphic bytes (or a frame list) + animation timing + terrain membership + collision shape; validates every graphic resolves, every animated tile has ≥1 frame, every terrain peering bit references a declared terrain, every terrain-painted level cell references a declared terrain.
3. `LevelLoader` produces a `ResolvedLevel` carrying concrete `cells` **and** the per-layer terrain channel.
4. `TileSetBuilder` builds the shared Godot `TileSet` once (atlas sources with animation frames, terrain sets + peering, physics layer + collision polygons).
5. `TileMapLevelBuilder` fills each `TileMapLayer`: concrete cells via `SetCell`; then, for the layer's terrain-painted cells, one `set_cells_terrain_connect` call per terrain so Godot auto-selects the matching variant tiles (and, because each variant is its own atlas tile, its own collision polygon).

### 6.2 Author a tileset (editor)
- Author opens/creates a tileset resource (via `PackageBrowser`, extended to list `tileset` resources).
- `TileSetEditSession` mediates every mutation; the UI applies the returned change to a live preview built with `TileSetBuilder`.
- Naming goes through `OnScreenKeyboard`; grid navigation through `FocusGrid`; menus through `PopInMenu`.

### 6.3 Bind a tileset to a level
- In `LevelEditor`, a "tileset" affordance lets the author pick a tileset resource (same package or a dependency package) → sets `LevelDefinition.tileSet`. On save, the level's contributions **exclude** the tileset (it is referenced), matching #7572.

### 6.4 Paint a terrain on a level
- The author selects a terrain (logical type) from the tileset's terrains in the Tiles radial.
- Painting writes the **logical** `(terrainSet, terrain)` into the layer's terrain channel (not a concrete id), and the editor immediately re-drives terrain-connect over the touched cell + its neighbours so the canvas shows the resolved variants live (matching runtime).

### 6.5 Communication style
All synchronous, in-process, single-threaded (editor + runtime are one Godot process). No brokers/queues. The only "contract boundary" is the `ResolvedLevel`/`ResolvedTileSet` seam between Godot-free content and Godot mapping — the same seam #7416 established.

## 7. Data Model (Conceptual)

Entities and ownership (no schema DDL — prose/relationships only):

- **Tileset** — a named, versioned, *shared* resource. Owns a set of **Tiles** and a set of **Terrain Sets**. Referenced by many **Levels**.
- **Tile** — belongs to exactly one Tileset. Has:
  - an **id** (stable within the tileset; what a level cell stores for concrete placement);
  - a **kind**, one of *simple* (single graphic), *animated* (ordered frames + speed), determined structurally by whether it carries frames;
  - a **graphic reference** (simple) or **ordered frame references / a strip reference + frame count** (animated);
  - optional **terrain membership**: which Terrain Set + Terrain it belongs to, and its **peering bits** (which of the surrounding directions must be the same terrain for this variant to be chosen);
  - a **collision shape** (none / full / rect / polygon / preset);
  - optional **tags / category** (reserved seam for #7450).
- **Terrain Set** — belongs to one Tileset. Has a **matching mode** (corners / sides / corners-and-sides) and an ordered set of **Terrains**.
- **Terrain** — a *logical type* (name + author-facing colour). The thing the author paints. Satisfied at runtime by whichever member Tile's peering bits match the neighbourhood.
- **Level** — **references** one Tileset. Owns **Layers**.
- **Layer** — owns a flat array of **concrete cell ids** (existing) and an optional parallel **terrain channel** of logical `(terrainSet, terrain)` marks (sentinel = none). A cell is either concrete or terrain-painted; the two channels are mutually exclusive per cell (an invariant the loader enforces).

**Relationships:** Tileset 1—* Tile; Tileset 1—* TerrainSet 1—* Terrain; Tile *—0..1 Terrain (a tile may be a plain tile or a terrain variant); Level *—1 Tileset; Level 1—* Layer.

**Why a parallel terrain channel rather than baking concrete ids at paint time?** Toni explicitly wants to *keep painting the same logical thing* and have the surroundings decide the sprite. If we baked the resolved id into `cells`, editing a neighbour would not re-flow the border, and the level would lose the author's intent. Storing the *logical* paint and re-resolving on load/edit is what makes auto-tiling behave like auto-tiling. (Trade-off in §10.)

## 8. Contracts & Interfaces (Abstract)

Described as inputs → outputs → semantics/invariants. No signatures.

| Interface | Input | Output | Invariants / Semantics |
|---|---|---|---|
| **Tileset serialization** | `TileSetDefinition` | camelCase JSON (via the existing `LevelContentSerializer` conventions) | Round-trips losslessly. Omit-when-default (animation absent on simple tiles, terrain absent on plain tiles, collision `none` omitted). References use the `packageId:path` string form. |
| **`TileSetLoader.Load`** | resolver + tileset reference | `ResolvedTileSet` (ids → bytes/frames + animation timing + terrain metadata + collision shapes) | Fails typed (`LevelContentException`) if any graphic missing, an animated tile has zero frames, a peering bit names an undeclared terrain, or a collision polygon is degenerate. Godot-free. |
| **`LevelLoader.Load`** (extended) | resolver + level reference | `ResolvedLevel` (concrete cells + per-layer terrain channel + resolved tileset) | Every concrete cell id is defined in the tileset; every terrain mark names a declared terrain; a cell is not both concrete and terrain-marked; existing bounds/spawn invariants unchanged. |
| **`TileSetBuilder.Build`** | `ResolvedTileSet` | Godot `TileSet` + tile-id→(source, atlas-coords) map + terrain index map | One shared set. Animated tiles → one atlas source with N frames + speed. Terrain variants → tiles assigned terrain + peering bits. Collision shapes → physics-layer polygons. Pure translation, no IO. |
| **`TileMapLevelBuilder.Build`** (extended) | `ResolvedLevel` | layer tree | Concrete cells placed directly; terrain cells resolved via `set_cells_terrain_connect`. Shared `TileSet`, per-layer `CollisionEnabled`, `Parallax2D` behaviour preserved. |
| **`TileSetEditSession`** | intent calls (add/remove/rename tile, add/reorder frame, set speed, define terrain set/terrain, assign peering bits, set collision shape, import graphic) | a change descriptor to reflect in the preview (or null no-op) | Every mutation is undoable and routes through the session; the UI never mutates the model directly (mirrors `LevelEditSession`). Ids stable across edits; removing a tile that a bound level uses is guarded (see §11 risk). |
| **Level ↔ tileset binding** | a chosen tileset reference | updated `LevelDefinition.tileSet` | On save the tileset is a *reference*, never a contribution (dedup). Rebinding to an incompatible tileset (missing ids) surfaces a validation warning, not a silent break. |
| **Graphic import** | a PNG source (bundled / file pick / drop) | a `tilegraphic` contribution merged into the package + a graphic reference for a tile | PNG validated (readable by the loader). Merged via #7572's add-or-replace-by-path at a namespaced path. |

## 9. Cross-Cutting Concerns

- **Engine-agnosticism (the prime invariant):** all model + validation in `Uberkarl.Content`, Godot-free, unit-tested; Godot only in `game/`. Terrain/animation/collision *metadata* is portable data; only its realization is Godot.
- **Error handling:** typed `LevelContentException` at the content boundary; the Godot side catches at the scene boundary and logs (never crashes), exactly as `LevelDisplay`/`TileMapLevelBuilder` already do. Author-facing validation (e.g. "tile 7 has no frames") surfaces in the editor, not as a hard load failure.
- **Undo/redo:** tileset authoring uses the same `EditHistory`/command pattern as level editing. Terrain and collision edits are commands too.
- **Idempotency / determinism of auto-tiling:** terrain resolution must be a pure function of (terrain paint + tileset peering bits). The same level + tileset must resolve to the same variants everywhere (aligns with #7407's "shared content behaves identically" concern). Godot's terrain-connect is deterministic given fixed peering bits; we must not depend on cell iteration order.
- **Consistency between editor preview and runtime:** the editor must resolve terrains with the *same* mechanism the runtime uses (`set_cells_terrain_connect`), so what the author sees is what plays. This argues for the editor building a real (headless) `TileSet` + layer via the shared `TileSetBuilder`, not a bespoke preview path.
- **Migration safety:** de-embedding tilesets touches existing content; migration must be lossless and reversible-in-review (see §12).
- **Observability:** keep the existing build-time log lines (`rendered NxM level…`); add a line for tileset build (tile/terrain/animated counts) to make live verification legible.
- **Performance:** tilesets are built once per load; animation is Godot-native (no per-frame C#); terrain-connect runs once per load per terrain. All well within budget for hand-authored levels.
- **Security:** untrusted community packages already flow through the format's ZIP-slip / version guards (#7413). Terrain/animation add no new execution surface (pure data). PNG decode already goes through Godot's validated loader.

## 10. Quality Attributes & Trade-offs

| Attribute | How the design addresses it |
|---|---|
| **Maintainability** | Splitting `TileSetBuilder` out of `TileMapLevelBuilder` gives each a single responsibility; the content/Godot seam keeps the growing complexity unit-testable. Mirroring the existing `EditableLevel`/`LevelEditSession` pattern for tilesets means one learnable shape. |
| **Extensibility** | The tile "kind" is structural (frames present ⇒ animated), so new kinds don't need an enum migration. `tags`/`category` reserved for #7450. Non-square/per-layer sizes remain a future seam, untouched. |
| **Consistency of shared content** | Logical terrain paint + deterministic peering resolution means a border looks the same for everyone. |
| **Authoring simplicity (Toni's product bar)** | "Paint the type, engine resolves" is delivered literally. Naming/collision/frames each reuse an existing gamepad primitive rather than inventing UX. |
| **Dedup / storage** | Shared tileset resource removes the per-level copy Toni flagged. |

**Trade-offs made (and alternatives rejected):**

1. **Logical terrain channel vs. baking concrete ids at paint time.** *Chosen:* store logical paint, re-resolve. *Rejected:* bake resolved ids into `cells`. Baking is simpler (no second channel, no re-resolve) but breaks re-flow on neighbour edits and discards intent — the exact thing Toni does *not* want. The cost is a second per-layer channel and a load-time connect pass; worth it.
2. **Lean on Godot Terrain Sets vs. a custom auto-tile resolver.** *Chosen:* Godot native (`set_cells_terrain_connect`, peering bitmasks). *Rejected:* our own neighbour-pattern → tile resolver in `Content`. A custom resolver would be fully portable and testable Godot-free, but re-implements a mature engine feature (matching modes, corner/side peering, connect) for no product gain. We keep the *metadata* portable and delegate *realization*. Risk: if we ever target a non-Godot runtime, the resolver must be reimplemented — acceptable given desktop-Godot is the locked platform (#7407).
3. **Split `TileSetBuilder` now vs. keep it inline.** *Chosen:* split during Phase 1. Doing it up front avoids a painful extraction once animation + terrains + collision have all piled into one method.
4. **Per-variant collision "for free" vs. authored shapes.** *Chosen:* deliver structural per-variant collision in the terrain phase (each variant is its own tile with its own polygon), and *authored non-full-tile shapes* as a later phase. Rejected bundling arbitrary-shape authoring into the terrain phase — it is a separable capability that also benefits simple tiles.
5. **One shared tileset per level (v0) retained.** A level binds exactly one tileset (as today). Multiple tilesets per level is a future seam, not built — keeps ids unambiguous.

## 11. Risks & Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| **Removing/renumbering a tile that bound levels reference** | Levels break silently (dangling ids). | Tile ids are stable and never reused; removal is a guarded operation that warns if the tileset is (or may be) referenced; the loader validates ids and surfaces dangling references as typed errors, not crashes. |
| **Editor preview diverges from runtime terrain resolution** | Author sees one border, player sees another. | Editor and runtime both resolve via the shared `TileSetBuilder` + `set_cells_terrain_connect`; no bespoke preview resolver. |
| **Terrain-authoring UX is genuinely hard on a gamepad** (assigning up to 16 peering bits per variant) | Feature is unusable where it matters most. | Recommend a **peering-bit grid** UX: a 3×3 cell diagram (center = the tile, 8 surrounds) where the author toggles which neighbours must match, mirrored on the real tile preview; presets ("full interior", "top edge", "outer corner") pre-fill common patterns so most tiles need zero manual bit-toggling. Detailed in §14. Toni ratifies. |
| **Migration de-embeds tilesets incorrectly** | Content loss / churn. | Migration is a mechanical, reviewed pass that lifts each level's embedded tileset into a sibling `tileset` resource and rewrites the level's reference; verified by round-trip (load migrated content, render, compare) before the PR. Sequenced after/with #7572's merge writer. |
| **#7572 not yet merged** when Phase 1 starts | Save model still fabricates single-level packages, clobbering a shared tileset. | Phase 1 **depends on** #7572's `PackageContext`/`LevelMergeWriter` (the "level references, doesn't own, the tileset" line is literally in #7572). Either sequence Phase 1 after #7572 merges, or fold the minimal merge-writer capability into Phase 1a. Flagged as decision D5. |
| **Animated tiles + collision interaction** | An animated tile's collision must be stable across frames. | Collision shape is per-*tile*, not per-frame; frames are visual only. Documented invariant. |
| **Godot terrain "connect" touching neighbours we didn't paint** | Unexpected edits to adjacent concrete cells. | Terrain-connect is constrained to the terrain channel's cells; concrete cells are never overwritten by a terrain pass (the two-channel invariant enforces this). |

## 12. Migration / Rollout Strategy

**From:** every level effectively owns its tileset (the editor fabricates a single-level package; the tileset is a level contribution).
**To:** the tileset is a standalone shared `tileset` resource; the level carries only a `ResourceReference`.

1. **Land / confirm #7572's package-VFS model** (`PackageContext`, `LevelMergeWriter`, per-resource namespaced paths). This is the prerequisite that lets a package hold a level *and* a sibling tileset without clobbering. (Decision D5: sequence after #7572, or fold the minimal capability in.)
2. **Introduce the standalone `tileset` resource path** and make the level's save treat the tileset as a *reference* (drop it from the level's contributions). New levels bind an existing or freshly-created tileset.
3. **Mechanical migration of existing content**: for each existing package/level, lift the embedded tileset into a sibling `tileset` resource at a namespaced path, rewrite the level's `tileSet` reference to point at it, dedup identical tilesets within a package. Verify by load-render-compare.
4. **Schema additions are omit-when-default**, so old content (simple tiles, no terrains, no animation) loads unchanged; only the *ownership* (embedded → referenced) migrates in this step. Animation/terrain/collision-shape fields simply don't appear until authored.
5. **Roll out per phase** (§13); each phase is independently shippable and leaves the app working.

## 13. Phased Build Plan

Each phase is a **shippable increment**; larger phases are decomposed into **one-feature-per-PR** units in dependency order (per the global PR-scope rule). A later PR referencing an earlier one names it in its body.

### Phase 1 — Shared tileset resource + simple-tile authoring shell + graphic import
*Goal: a tileset is its own editable, shared resource; you can create/open one, add/remove/name simple tiles, import graphics, set full-tile collision; levels reference a tileset instead of embedding one. No new tile capabilities yet — this is the foundation.*

- **PR 1a — De-embed the tileset (ownership refactor).** Promote `tileset` to a standalone shared resource a level *references*; level save excludes the tileset (builds on #7572's merge writer); mechanical migration of existing content; loader validates the reference. Split `TileSetBuilder` out of `TileMapLevelBuilder` (pure refactor, behaviour identical). *Pure structural change; no user-visible new feature beyond "levels share a tileset."*
- **PR 1b — Tileset authoring surface + graphic import.** New `EditableTileSet`/`TileSetEditSession` + `TileSetEditor` UI (add/remove/rename tile, full-tile collision toggle) reusing pop-in/`FocusGrid`/`OnScreenKeyboard`/`PackageBrowser`. Graphic import: bundled starter set (gamepad) + file pick (mouse/keyboard). Level-side "bind tileset" affordance.

### Phase 2 — Animated tiles
*Goal: a tile can have multiple frames + speed, rendered natively.*

- **PR 2a — Animated-tile model + Godot mapping.** `TileDefinition` gains ordered frames + speed; `TileSetLoader` validation; `TileSetBuilder` maps to a `TileSetAtlasSource` with N frames + speed. Runtime + loader tests. (No authoring yet — verifiable via a hand-made sample.)
- **PR 2b — Animation authoring.** In `TileSetEditor`: mark a tile animated, add/reorder frames (import each as a graphic), set speed, live preview.

### Phase 3 — Terrain / meta auto-tiles (placement + authoring)
*Goal: author paints a logical terrain; engine auto-selects the real variant. Per-variant collision arrives structurally here (each variant is its own tile with its own full-tile-or-none polygon).*

- **PR 3a — Terrain model + Godot terrain mapping + level terrain channel.** `TerrainSetDefinition`/`TerrainDefinition`, per-tile terrain membership + peering bits, per-layer terrain channel, two-channel invariant. `TileSetBuilder` builds Godot Terrain Sets + peering bits; `TileMapLevelBuilder` drives `set_cells_terrain_connect` for terrain cells. Loader validation + tests. (Verifiable with a hand-made terrain sample.)
- **PR 3b — Level terrain paint tool.** `LevelEditor` gains a terrain brush: select a terrain in the Tiles radial, paint → writes logical marks + live re-connect on the canvas (neighbour reflow).
- **PR 3c — Terrain authoring UI (the hard part).** In `TileSetEditor`: define terrain sets/terrains, assign peering bits per variant via the 3×3 peering-bit grid + presets (§14). Naming via keyboard.

### Phase 4 — Authored per-variant collision (bounding boxes beyond full-tile)
*Goal: collision shapes richer than full-tile-square (rect / polygon / preset such as a slope), per tile — benefits both simple and terrain-variant tiles. This is the "possibly a different bounding box" part of Toni's ask, made author-controllable.*

- **PR 4a — Collision-shape model + Godot mapping.** Replace the `Collides` boolean with a `CollisionShape` descriptor (none / full / rect / polygon / preset) — `Collides:true` migrates to `full`, `false` to `none`. `TileSetBuilder` builds the polygon from the descriptor.
- **PR 4b — Collision-shape authoring.** In `TileSetEditor`: pick a preset or edit a simple polygon per tile with a gamepad-friendly handle UX; preview overlay.

**Phase-boundary rationale (for Toni):** Phase 1 gives the durable structural win (shared tilesets + an authoring shell) with *zero* new tile semantics, so it's low-risk and immediately useful. Animation (Phase 2) is small and self-contained. Terrains (Phase 3) are the largest conceptual jump and get their own phase, decomposed so the *model+mapping* lands and is verifiable before the *authoring UX* (the risky part) is built. Authored collision shapes (Phase 4) are deferred because structural per-variant collision already ships in Phase 3; arbitrary shapes are a separable polish capability.

## 14. Implementation Guidance for the Next Agent

Ordered, architectural-unit milestones. No code.

1. **(Prereq) Confirm #7572's state.** If `PackageContext`/`LevelMergeWriter`/namespaced paths have merged, Phase 1a builds on them. If not, decide D5 (sequence-after vs. fold-in) with Toni before starting.
2. **Phase 1a first — extract `TileSetBuilder`** from `TileMapLevelBuilder` with behaviour identical (regression-guarded by the existing render verification), *then* flip tileset ownership to referenced + migrate. Keep these reviewable as one focused refactor PR.
3. **Phase 1b — mirror the existing editing shape.** `EditableTileSet`/`TileSetEditSession` should look like `EditableLevel`/`LevelEditSession` (undoable commands, façade, in-memory bytes). The `TileSetEditor` UI reuses `PopInMenu` + `FocusGrid` + `OnScreenKeyboard` + `PackageBrowser` verbatim — new front-end, not new edit logic.
4. **Graphic import (Phase 1b):** ship the **bundled starter set** (gamepad-complete) + **file pick** (mouse/keyboard). Do **not** build a custom in-engine file browser yet unless Toni ratifies D2 for it; a gamepad file browser can reuse the `PackageBrowser` list shape later.
5. **Phase 2 — animation is mostly a `TileSetBuilder` mapping** (`TileSetAtlasSource` frames + speed) + a small model addition. Authoring reuses the tile editor + import.
6. **Phase 3 — build the model + Godot mapping (3a) and verify with hand-authored sample content before touching authoring UX.** The two-channel (concrete vs terrain) invariant is the crux — enforce it in the loader. The editor's live terrain preview must use the *same* `set_cells_terrain_connect` path as runtime.
7. **Phase 3c terrain-authoring UX — recommended shape:** a **3×3 peering-bit grid** per variant tile (center cell = this tile; the 8 surround cells toggle "must be same terrain"), rendered next to the tile preview so the author sees which neighbourhood selects this variant. Provide **presets** ("interior/fill", "top/bottom/left/right edge", "the four outer corners", "the four inner corners") that pre-assign the common bit patterns so a typical 47-tile blob terrain is mostly preset-driven, not hand-toggled. Matching mode (corners / sides / corners+sides) is a terrain-set-level choice set once. This is the single riskiest UX; prototype it early and put it in front of Toni.
8. **Phase 4 — collision shapes:** migrate the boolean to a descriptor (`full`/`none` preserve today's behaviour); authoring leans on presets first, freehand polygon last.
9. **Keep the content library Godot-free throughout** and unit-test every schema/loader addition (the project's established bar: high line/branch coverage, comment-grep clean).
10. **Verification per phase** via the Godot MCP: build a sample tileset+level exercising the new capability, render/play it, and confirm editor-errors clean — the same live-verification discipline the prior increments used.

## 15. Open Questions for Toni

1. **D1 — Graphic import approach (ratify).** Recommendation: **bundled starter set + mouse/keyboard file pick** in Phase 1; gamepad-navigable in-engine file browser and the sprite editor **deferred**. Acceptable that *importing custom PNGs* is a mouse/keyboard action while *authoring* is fully gamepad? Or is a gamepad file browser required in Phase 1?
2. **D2 — Terrain-authoring UX (ratify).** The **3×3 peering-bit grid + presets** (§14) — good enough to prototype, or do you want a different mental model (e.g. paint-a-swatch-and-let-the-tool-infer-bits)?
3. **D3 — Phase 1 contents / phase boundaries (ratify).** Phase 1 = **shared tileset resource + simple-tile authoring shell + import**, *no* animation or terrains. Animation is Phase 2, terrains Phase 3, authored collision shapes Phase 4. Agree, or pull animation into Phase 1?
4. **D4 — Per-variant collision.** Structural per-variant collision (each terrain variant is its own tile with its own full-tile-or-none polygon) ships in Phase 3. **Authored arbitrary shapes** (rect/polygon/slope presets) are Phase 4. Is full-tile-or-none per variant enough for the first terrain release, with shapes following?
5. **D5 — Sequencing vs. #7572.** Phase 1 depends on the package-VFS merge model. Sequence Phase 1 **after #7572 merges**, or fold the minimal merge-writer capability into Phase 1a?
6. **D6 — Tags/categories seam.** Confirm the tileset schema should *reserve* a `tags`/`category` field on a tile (so #7450's scalable picker lands without a schema break), even though this design does not build the picker.
7. **D7 — Multiple tilesets per level.** Retain "one tileset per level" (as today) for the foreseeable future, or is mixing tiles from several tilesets in one level a near-term need?
8. **D8 — Animation frame source.** Author animation frames as **N separate imported PNGs** (simplest, gamepad-friendly) vs. a **single horizontal strip PNG + frame count** (fewer files, needs a strip-aware importer). Recommendation: separate PNGs first; strip support later if authors ask.
```
