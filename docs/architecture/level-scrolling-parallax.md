# Architectural Document: Scrolling Camera + Large Levels + Parallax Layers

Phase 1e. Turns the *playable* level (level model v0.2, `level-playable.md`, DiVoid #7419/#7420)
into a *scrolling* one: levels larger than the viewport, a camera that follows the player within
the level bounds, and per-layer parallax scroll speeds for a sense of depth. This lands the
parallax `scrollSpeed` seam that v0.2 deliberately deferred (level-playable.md §10).

This is a lean design update shipped together with a working increment. Scope is deliberately
small — see §2.

## 1. Problem Statement

Levels are currently framed whole by a single static camera, so a level can be no larger than
what fits on screen. We need three things, together: (a) levels that are **wider than the
viewport**, (b) a **camera that follows the player** but never scrolls past the level edges, and
(c) **background layers that scroll more slowly than the world** so distance reads as depth.
Success = the player can walk across a level bigger than the screen, the view tracks them and
stops cleanly at the edges, and a background layer visibly lags the foreground as they move.

## 2. Scope & Non-Scope

**In scope:** a per-layer `scrollSpeed` schema field with its collision invariant and loader
validation; a follow-camera with limits derived from level bounds and light position smoothing;
finite parallax layers via Godot's native `Parallax2D`; a wide sample level demonstrating the
depth differential; in-engine verification.

**Out of scope (YAGNI):** a level editor; level connectivity/exits; enemies/hazards;
infinite/repeating/auto-scrolling parallax; vertical-only or screen-locked camera modes;
screen-shake, zoom transitions, or deadzone tuning; speculative schema fields. All either belong
to later phases or are not needed for this feature.

## 3. Design Decisions

| # | Decision | Rationale | Alternatives rejected |
|---|---|---|---|
| D1 | Parallax is **one float per layer** (`scrollSpeed`), not a layer "distance" enum or a separate parallax layer type | A single scalar is the whole degree of freedom the feature needs; it composes with the existing per-layer `collision` flag with zero new taxonomy | A `depth`/`distance` enum (arbitrary buckets); a distinct `ParallaxLayerDefinition` type (splits the layer model for no gain) |
| D2 | **Collision layers must be world-locked** (`scrollSpeed == 1.0`); the loader enforces it | A parallax layer's on-screen position is not its world position, so collisions authored against it would be wrong. Collision only makes sense where screen = world | Silently allowing it (produces physics that does not match what the player sees); a runtime warning (a content bug should fail loudly at load, at the Godot-free boundary) |
| D3 | **Default `scrollSpeed = 1.0`** (world-locked), retained on omission | Back-compatible: every v0.2 layer loads unchanged as world-locked; parallax is strictly opt-in | Default 0 (would make every legacy layer a degenerate fixed backdrop) |
| D4 | Camera is a **child of the player** with Godot-native `limit_*` from the level bounds | The engine-native follow + clamp path — no per-frame follow script, no bounds math in `_process`. "Don't fight the engine," consistent with the v0.2 TileSet decision | A standalone camera + a follow script that lerps toward the player and manually clamps (reimplements what `Camera2D` already does) |
| D5 | Parallax layers use **`Parallax2D`** (Godot 4.3+) with `repeat_size = 0`; world-locked layers get **no wrapper** | `Parallax2D` is the modern single-node parallax primitive (simpler than `ParallaxBackground`/`ParallaxLayer`); `repeat_size = 0` keeps the layer finite (no tiling) as required. World-locked layers already move correctly with the camera, so wrapping them would be dead structure | `ParallaxBackground` + `ParallaxLayer` (heavier, viewport-oriented, built around tiling); wrapping every layer uniformly (adds a no-op node to collision layers) |
| D6 | Design lands as a **new doc** rather than growing `level-playable.md` | Scrolling/parallax is its own concern; the v0.2 doc's title and scope are about collision + spawns. Keeps each document single-purpose | Appending a §13 to `level-playable.md` (stretches that doc past its subject) |

## 4. Schema (`src/Uberkarl.Content`, engine-agnostic, unit-tested)

| Element | Field | Semantics | Default (back-compat) |
|---|---|---|---|
| Level layer | `scrollSpeed` (float) | Parallax scroll factor relative to the camera. `1.0` = world-locked (moves 1:1 with the world); `<1.0` = slower background (depth); `>1.0` = faster foreground (allowed, not required). | `1.0` — a layer is world-locked unless it opts into parallax; an **omitted** value loads as 1.0 |

- **Invariant (D2):** a `collision:true` layer MUST have `scrollSpeed == 1.0`. `LevelLoader`
  validates this per layer and throws `LevelContentException` with a clear message
  (`"…is a collision layer but has scrollSpeed X; a collision layer must be world-locked…"`) at
  the Godot-free boundary. Non-collision layers may take any scroll factor.
- `scrollSpeed` serializes as a plain number, exactly as the sibling `collision`/`collides`
  value-type flags do. Because the default is `1.0` (not the type default `0`), an omitted value
  is restored to `1.0` by the property initializer during deserialization — verified by a unit
  test — so v0.2 content round-trips as world-locked with no dead field introduced anywhere.
- `ResolvedLayer` gains `ScrollSpeed`; the loader carries each layer's value through onto it. All
  parsing/validation stays Godot-free and unit-tested (`SchemaV02Tests`, +5 tests).

## 5. Package → Godot Mapping (don't fight the engine)

### 5.1 Camera (the "large levels" enabler)

`LevelPlay` gives the **player a child `Camera2D`** so it follows automatically, and sets the
native limits from the level bounds so the view never scrolls past the edges:

| Camera limit | Value |
|---|---|
| `limit_left` / `limit_top` | `0` |
| `limit_right` | `Width * TileSize` |
| `limit_bottom` | `Height * TileSize` |

Light position smoothing (`PositionSmoothingEnabled`, speed 8) softens the follow. Zoom stays at
the v0.1/v0.2 value (3×). Because the level is wider than the visible area, the camera scrolls to
keep the player in view and clamps hard at both horizontal edges.

### 5.2 Parallax (`Parallax2D`, finite)

`TileMapLevelBuilder.Build` wraps each layer whose `ScrollSpeed != 1.0` in a `Parallax2D`:

| `Parallax2D` property | Value | Meaning |
|---|---|---|
| `scroll_scale` | `(scrollSpeed, scrollSpeed)` | layer scrolls at that factor of the camera's scroll |
| `repeat_size` | `(0, 0)` | finite level — no tiling/repeat |

A world-locked layer (`scrollSpeed == 1.0`) is added directly to the level root with **no
wrapper** — it moves with the camera naturally. Collision layers (all `1.0` by the D2 invariant)
are therefore never wrapped, so parallax never touches physics. Draw order remains the layer array
order (back → front), independent of both collision and scroll speed.

## 6. Contracts & Interfaces (unchanged seams)

- `ResolvedLevel` / `ResolvedLayer` remain the **Godot-free boundary object** (primitives only);
  `ScrollSpeed` is just another primitive on it. All schema parsing/validation stays
  engine-independent and unit-testable. The builder is a pure translation into Godot nodes.
- The package/reference/resolver seam and the collision/spawn model from v0.2 are untouched.

## 7. Verification (Godot MCP)

- `dotnet build Uberkarl.csproj` → 0 warnings / 0 errors. Tests: `Uberkarl.Content.Tests`
  **22/22** (was 17; +5 for `scrollSpeed` default/round-trip/carry-through and the
  collision-must-be-1.0 validation, both throw and allow paths), `Uberkarl.Packages.Tests`
  **31/31**. Content-lib coverage line **93.2%** / branch **84.5%** (all changed schema classes
  100%; `LevelLoader` 93%/86%). Authored-source comment-grep (TODO/FIXME/HACK/XXX +
  commented-out code): **0**. `get_editor_errors`: **0**.
- Ran `scenes/level_playable.tscn` against the committed pkg (a **60×16** level, far wider than the
  ~24-tile-wide visible area at 3× zoom). Engine confirmed the built tree: `backdrop` is a
  `Parallax2D` wrapping its `TileMapLayer`; `terrain` is a bare world-locked `TileMapLayer`; the
  `Camera2D` is a child of `Player`.
- **Player traversal + gravity/collision:** player spawns at the default spawn (`start`, cell
  (2,10)) and gravity settles it onto the grass (velocity 0, no fall-through). Holding `move_right`
  carries it the full width and it is stopped hard against the right stone wall (x≈938 against the
  col-59 wall).
- **Parallax differential (two-position proof):** between the left edge and the right edge the
  camera's clamped screen-center moved **192 → 768 (Δ576 px)** while the world-locked `terrain`
  node stayed pinned at `(0,0)` (so it moves the full 576 on screen) and the parallax `backdrop`
  layer's world position moved **−192 → +96 (Δ288 px)** — **exactly `0.5 × 576`**. On screen the
  background therefore moves at **half** the foreground's rate: `scroll_scale = 0.5` confirmed to
  the pixel. The two screenshots (player near the left wall vs. mid-level) show the background
  hills visibly lagging the grass/platforms.
- **Camera stops at edges:** at spawn the camera is clamped at the left limit (left wall flush
  against the screen's left edge, player left-of-centre); at the far right it is clamped at the
  right limit (right wall flush against the screen's right edge, player right-of-centre). It never
  scrolls past either edge.

## 8. Sample Content (`tools/SampleContent`)

A **60×16** level (world 960×256 px), regenerated into `content/sample.pkg`:

- **`terrain`** (`collision: true`, `scrollSpeed 1.0`): stone side walls (full height, cols 0 and
  59 — the edge stops), a grass surface (row 12), dirt fill below, and three floating brick
  platforms (rows 8–9) placed high enough to clear the walking player so the ground path stays
  traversable end to end while remaining jumpable-onto.
- **`backdrop`** (`collision: false`, `scrollSpeed 0.5`): distant hills and a cloud band spread
  across the full width (non-colliding tiles), so the reduced scroll speed is obvious as the
  camera pans. Two new non-colliding tiles (`hill`, `cloud`) were added to the palette.

Layer array order is `backdrop` then `terrain`, so the background draws behind the world.

## 9. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| A collision layer authored with a parallax speed would produce physics that doesn't match the screen | Loader validates `collision ⇒ scrollSpeed == 1.0` and throws at the Godot-free boundary; unit-tested |
| A v0.2 level (no `scrollSpeed`) loading as a degenerate fixed backdrop (0.0) | Default is `1.0` via property initializer; omitted value restored to world-locked; unit-tested |
| Finite parallax leaving a visible gap at the far edge (background shifted less than the world) | Accepted and by design (finite levels, `repeat_size = 0`); the sample spreads backdrop features across the width so there is always something on screen. Repeating parallax is explicitly out of scope |
| Floating platforms at head height silently blocking ground traversal | Caught in verification (player stalled mid-level); platforms raised to clear the walker. The wide level is now crossable end to end |

## 10. Future Seams (NOT built)

- **Repeating / auto-scrolling parallax.** `Parallax2D` supports `repeat_size` and `autoscroll`;
  both are left at their finite/off defaults. A future infinite-background mode is a property flip,
  no schema change forced.
- **Foreground parallax (`scrollSpeed > 1.0`).** Allowed by the schema and validation today; the
  sample simply does not exercise it. No code assumes a background-only range.
- **Camera feel (deadzone, look-ahead, zoom transitions).** The follow camera is intentionally
  plain (limits + light smoothing). Tuning knobs are a later feel pass.
- **Vertical/large-in-both-axes levels.** The limits already clamp on all four sides; a taller
  level needs no code change, only content.

## 11. Open Questions for Toni

1. **Foreground parallax range** — `scrollSpeed > 1.0` (faster-than-world foreground) is permitted
   and validated but unused. Keep it open, or constrain to `(0, 1]` (background-only) until a real
   foreground layer needs it?
2. **Finite-edge gap** — with finite parallax the background can run out before the level edge
   (transparent gap). Acceptable for now, or should a level declare a solid backdrop fill / clamp
   colour behind the parallax so edges never show empty space?
3. **Camera smoothing** — light position smoothing (speed 8) is on by default. Keep it, make it a
   per-level/authorable setting, or turn it off for a crisp 1:1 follow?
4. **Zoom** — the 3× zoom (≈24 tiles visible) is inherited from the framing camera. Is that the
   intended play field for a scrolling game, or should the visible span be tuned now?

## 12. Implementation Milestones (as built)

1. Schema: add `scrollSpeed` to `LayerDefinition` and `ResolvedLayer` (default 1.0); carry it
   through `LevelLoader`; add the `collision ⇒ 1.0` validation. Unit-test all four.
2. Camera: convert `LevelPlay`'s static framing camera into a player-child follow camera with
   bounds-derived limits and smoothing.
3. Parallax: `TileMapLevelBuilder` wraps `scrollSpeed != 1.0` layers in `Parallax2D`
   (`scroll_scale`, `repeat_size = 0`); world-locked layers unwrapped.
4. Content: widen the sample to 60×16 with a `scrollSpeed 0.5` backdrop; regenerate `sample.pkg`.
5. Verify in-engine (two-position parallax proof, edge clamps, gravity/collision, editor errors).
