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
   foreground layer needs it? *(Still open — Toni: keep `> 1.0` allowed. See §13.)*
2. **Finite-edge gap** — with finite parallax the background can run out before the level edge
   (transparent gap). Acceptable for now, or should a level declare a solid backdrop fill / clamp
   colour behind the parallax so edges never show empty space? **Resolved (§13):** both — a level
   `backgroundColor` fill behind everything *and* per-layer `repeat` tiling.
3. **Camera smoothing** — light position smoothing (speed 8) is on by default. Keep it, make it a
   per-level/authorable setting, or turn it off for a crisp 1:1 follow? **Resolved (§13):** keep
   smoothing, but bump to a fast smooth (speed 20) for a crisper follow. Per-level/authorable
   deferred (seam left).
4. **Zoom** — the 3× zoom (≈24 tiles visible) is inherited from the framing camera. Is that the
   intended play field for a scrolling game, or should the visible span be tuned now? *(Still open —
   Toni: leave zoom as-is, adjustable later.)*

## 12. Implementation Milestones (as built)

1. Schema: add `scrollSpeed` to `LayerDefinition` and `ResolvedLayer` (default 1.0); carry it
   through `LevelLoader`; add the `collision ⇒ 1.0` validation. Unit-test all four.
2. Camera: convert `LevelPlay`'s static framing camera into a player-child follow camera with
   bounds-derived limits and smoothing.
3. Parallax: `TileMapLevelBuilder` wraps `scrollSpeed != 1.0` layers in `Parallax2D`
   (`scroll_scale`, `repeat_size = 0`); world-locked layers unwrapped.
4. Content: widen the sample to 60×16 with a `scrollSpeed 0.5` backdrop; regenerate `sample.pkg`.
5. Verify in-engine (two-position parallax proof, edge clamps, gravity/collision, editor errors).

## 13. Scrolling Polish Pass (feel review of PR #6)

A lean follow-up increment off Toni's feel review of the scrolling feature (task #7430). Three
small, cohesive changes; no new scope beyond the ask (#1184). Resolves §11 Q2 and Q3.

### 13.1 Design decisions

| # | Decision | Rationale | Alternatives rejected |
|---|---|---|---|
| D7 | **Level `backgroundColor`** is an optional authored **hex string** (`#RRGGBB`/`#RRGGBBAA`), parsed to an engine-agnostic `RgbaColor` value type at the loader boundary | A solid fill behind everything is the simplest cure for the finite-edge hard-cut; a hex string is human-authorable and conventional; parsing at the Godot-free boundary keeps validation unit-testable and fails loudly on a malformed colour, consistent with D2 | A per-channel `{r,g,b,a}` object in JSON (verbose to author); validating in the engine (moves a content error past the Godot-free seam); a full theme/gradient (over-scoped) |
| D8 | The fill renders as a **full-rect `ColorRect` on a back `CanvasLayer`** (`layer = -100`), not a world-space node | A `CanvasLayer` is unaffected by the `Camera2D` transform, so the fill always covers the viewport regardless of camera position and never scrolls with the world — exactly the requirement. A negative layer index keeps it behind all layer-0 world content | A giant world-space `ColorRect` sized/moved to track the camera (reimplements what a `CanvasLayer` gives free); the viewport clear colour (not per-level authorable) |
| D9 | **Per-layer `repeat` (bool, default false)**; a repeating layer's `Parallax2D.repeat_size` = the layer's content size | Backgrounds are usually repeatable or larger than the level (Toni); a single bool is the whole degree of freedom, composing with `scrollSpeed` with no new taxonomy. Content size as the repeat period tiles the backdrop seamlessly across the scroll extent | A repeat-count or explicit tile-size field (more knobs than needed); infinite/procedural backgrounds (out of scope) |
| D10 | A collision layer **MUST NOT repeat** (loader-enforced), mirroring the D2 scroll invariant | Tiling the visuals would not tile the authored collision geometry, so screen and world would disagree — the same class of bug D2 guards against | Allowing it (silent physics/visual mismatch) |
| D11 | `repeat` wraps a layer in `Parallax2D` **even when world-locked** (`scrollSpeed == 1.0`) — wrap when `scrollSpeed != 1.0` **or** `repeat` | `repeat` is a rendering behaviour that needs the `Parallax2D` to express; honouring it regardless of scroll speed avoids a surprising silent no-op on a world-locked repeating layer | Only honouring `repeat` on parallax layers (a world-locked `repeat:true` would silently do nothing) |
| D12 | **Camera position-smoothing speed 8 → 20** (a fast smooth), extracted to a named constant | Toni wanted a crisper follow that still eases rather than snapping 1:1; 20 is crisp-but-not-instant. The named constant is the tuning seam for future camera scripting (deadzone/look-ahead), which is explicitly not built here | Instant 1:1 follow (loses the ease Toni wanted to keep); building camera scripting now (out of scope — seam only) |

### 13.2 Schema additions (`src/Uberkarl.Content`, unit-tested)

| Element | Field | Semantics | Default (back-compat) |
|---|---|---|---|
| Level | `backgroundColor` (string?) | Optional solid fill behind all layers, hex `#RRGGBB` or `#RRGGBBAA` (leading `#` optional, 6-digit = opaque). Parsed to `RgbaColor` on `ResolvedLevel`. | `null` — omitted from JSON; the viewport clear colour shows through, as before |
| Level layer | `repeat` (bool) | Whether the layer tiles across the scroll extent instead of ending at a finite edge. | `false` — finite unless it opts in |

- New engine-agnostic value type **`RgbaColor(byte R,G,B,A)`** with `TryParse` for the two hex
  forms. `ResolvedLevel.BackgroundColor` is `RgbaColor?`; `ResolvedLayer.Repeat` is `bool`.
- **Invariants (loader-enforced, both throw `LevelContentException` at the Godot-free boundary):** a
  malformed `backgroundColor` throws (`"…is not a valid hex colour…"`); a `collision:true` layer
  with `repeat:true` throws (D10). Non-collision layers may repeat freely.
- `backgroundColor` omits-when-null (like `defaultSpawn`); `repeat` serialises as a plain bool
  (like `collision`). All parsing/validation stays Godot-free and unit-tested.

### 13.3 Package → Godot mapping

| Concern | Node | Key properties |
|---|---|---|
| Background fill | `CanvasLayer` "BackgroundFill" (`layer = -100`) → `ColorRect` "Fill" (`FullRect` preset) | `Color` from `RgbaColor` (/255 per channel); covers the viewport, never scrolls |
| Repeating layer | existing `Parallax2D` wrapper | `repeat_size = (Width·TileSize, Height·TileSize)` when `repeat`, else `(0,0)`; wrapped when `scrollSpeed != 1.0 OR repeat` (D11) |
| Camera feel | existing player-child `Camera2D` | `position_smoothing_speed = 20` (was 8), via named constant |

`scrollSpeed > 1.0` stays allowed (unchanged); zoom stays 3× (unchanged).

### 13.4 Verification (Godot MCP)

- `dotnet build Uberkarl.csproj` → **0 warnings / 0 errors**. Tests: `Uberkarl.Content.Tests`
  **33/33** (was 22; +11 for `backgroundColor` serialise/omit/parse/malformed, `RgbaColor` hex
  formats, and `repeat` default/round-trip/carry-through/collision-invariant throw+allow),
  `Uberkarl.Packages.Tests` **31/31**. Content-lib coverage line **94.0%** / branch **88.4%** (all
  changed schema classes 100%; `LevelLoader` 94%/88%, `RgbaColor` 95%/95%). Authored-source
  comment-grep (TODO/FIXME/HACK/XXX + commented-out code): **0**. `get_editor_errors`: **0**.
- Ran the wide sample (`backgroundColor "#3A5A8C"`, `backdrop` `repeat:true`). Engine confirmed the
  built tree: `BackgroundFill` `CanvasLayer` (`layer -100`) → `Fill` `ColorRect` (`color #3a5a8cff`,
  size `1152×648`, `FullRect`); `backdrop` `Parallax2D` `repeat_size (960,256)` = content size,
  `scroll_scale 0.5`; `terrain` bare world-locked; `Camera2D` `position_smoothing_speed 20`,
  `limit_right 960`, zoom 3×.
- **Screenshots (two):** at the **right edge** (camera clamped at the right limit, right wall flush
  at screen right) the dusk-blue fill covers the whole viewport with no hard cut to the clear
  colour, and backdrop hills are present right up to the wall (no finite gap); at **mid-level**
  (player dead-centre, camera unclamped) the fast follow keeps the player centred, with the fill
  behind the tiled hills. The player follows visibly crisper than the prior speed-8 smoothing.

### 13.5 Future seams (still not built)

- **Per-level / authorable camera smoothing** — the speed is a named constant seam; future camera
  scripting (deadzone, look-ahead, zoom transitions) layers on at `AttachCamera`. Not built.
- **Background gradient / image fill** — `backgroundColor` is a single solid colour; a gradient or
  image backdrop is a later, separate concern.
- **Foreground parallax (`scrollSpeed > 1.0`)** and **zoom tuning** — left as Toni directed
  (allowed/unchanged); §11 Q1 and Q4 remain open.
