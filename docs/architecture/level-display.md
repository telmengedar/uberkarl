# Architectural Document: Display a Level from a Package (Phase 1b spike)

Status: exploratory spike — schema is **v0**, expect iteration with Toni. Design + a working visual increment ship together in one PR.

Source task: DiVoid #7415. Consumes the Phase 1a package format (DiVoid #7413 / `docs/architecture/package-format.md`). Vision: DiVoid #7407.

Toni's verbatim ask:

> "lets try to display a level based on a package. This requires have some tile definitions and graphics in the package. Also we try to use the godot structures as much as we can to not fight the engine - i know that godot has a grid-based tilemap and it can get filled programmatically. Level definition should also be a resource - play around what we need and see where we get to."

## 1. Problem Statement

Prove the package format is a viable content substrate by making it drive something visible: load a single `.pkg` that carries tile definitions, tile graphics, and a level definition, and render that level in Godot by programmatically building a `TileSet` and filling `TileMapLayer`s. Display only — no interaction, no physics.

Success = tiles appear laid out per the authored grid, driven entirely by package data, using engine-native tilemap structures rather than fighting them.

## 2. Scope & Non-Scope

In scope: the three new resource kinds (level / tileset / tile-graphic), engine-agnostic parsing + resolution of them into a renderable model, a Godot renderer that turns that model into a live tilemap, a generated sample package, a scene that displays it, and unit tests on the schema/resolution.

Explicitly out of scope (DISPLAY only): player controller, collision/physics (the tile definition leaves *room* for it but builds none), level editor, scripting behavior, animation, camera/input beyond seeing the level, multi-package dependency use.

## 3. Assumptions & Constraints

- **csproj split (hard constraint).** `Uberkarl.csproj` (Godot.NET.Sdk) excludes `src/**` and `tests/**` from its compile. Engine-agnostic schema/parsing must live under `src/`; Godot-dependent rendering must live in the game compile set (repo root / a globbed game folder), never `src/`.
- Square tiles for v0 (one `tileSize` in px, not width×height).
- Tile graphics are real PNG bytes (Toni's ask names `Image.LoadPngFromBuffer`), not raw pixel buffers.
- The package is read through the existing `Uberkarl.Packages` API (`PackageReader`, `PackageRegistry` as `IResourceResolver`, `ResourceReference` = `PackageId` + `ResourcePath`). References inside one package use the `self:` package id.
- Godot 4.7.1 .NET; `TileMapLayer` (one grid layer per node) is the current tilemap primitive.

## 4. Architectural Overview

Two engine-agnostic seams and one engine-bound seam, in a strict one-way dependency chain:

```
  .pkg  ──PackageReader──▶  Package / PackageRegistry (Uberkarl.Packages)
                                     │  IResourceResolver.Resolve(ref) -> byte[]
                                     ▼
                        LevelLoader (Uberkarl.Content, engine-agnostic)
                          reads level JSON + tileset JSON + tile PNGs
                                     │  produces
                                     ▼
                        ResolvedLevel  (ints, strings, byte[] PNGs — no Godot types)
                                     │  consumed by
                                     ▼
        TileMapLevelBuilder (game compile set, Godot)  ──builds──▶  Node2D
          PNG bytes -> Image -> ImageTexture -> TileSetAtlasSource            │
          tile-id -> source_id ;  SetCell per grid cell                       ▼
                        LevelDisplay (Node2D scene script) adds it, fits to viewport
```

`Uberkarl.Content` never references Godot. `TileMapLevelBuilder`/`LevelDisplay` are the only Godot-aware pieces. The boundary object is `ResolvedLevel`: fully materialized, engine-neutral, trivially testable.

## 5. Components & Responsibilities

| Component | Layer | Owns | Does NOT own |
|---|---|---|---|
| `LevelDefinition`, `LayerDefinition`, `TileSetDefinition`, `TileDefinition` | `src/Uberkarl.Content` | The v0 on-disk schema (POCOs) | Serialization mechanics, resolution, rendering |
| `LevelContentSerializer` | `src/Uberkarl.Content` | JSON read/write of the schema (camelCase, refs as strings) | Package I/O, byte resolution |
| `LevelLoader` | `src/Uberkarl.Content` | Resolve a level ref → `ResolvedLevel`: pull level+tileset+graphics via the resolver, validate dims/ids/graphics | Anything Godot; ZIP/manifest details (delegated to Packages) |
| `ResolvedLevel` / `ResolvedLayer` | `src/Uberkarl.Content` | The engine-neutral render input | Textures, nodes |
| `TileMapLevelBuilder` | game (`game/Level`) | Build a `TileSet` (one `TileSetAtlasSource` per graphic) + `TileMapLayer` per level layer, `SetCell` per grid cell | Loading packages, parsing schema |
| `LevelDisplay` (`Node2D`) | game (`game/Level`) | Scene entry: read the `.pkg` via Godot `FileAccess`, open it, find the level resource, invoke loader+builder, fit to viewport | Schema, resolution details |
| `tools/SampleContent` | tools (excluded from game compile) | Author the sample: generate placeholder tile PNGs + tileset + level grid, write `content/sample.pkg` | Runtime concerns |

New resource kinds added to `Uberkarl.Packages.ResourceKind`: `tileset`, `tilegraphic` (`level` already existed).

## 6. Interactions & Data Flow (display path)

1. `LevelDisplay._Ready` reads `res://content/sample.pkg` via Godot `FileAccess.GetFileAsBytes` (works in-editor and in an exported `.pck`), opens it into a `Package`, wraps it in a `PackageRegistry` (the `IResourceResolver`).
2. It finds the first resource of kind `level` in the manifest and forms a `self:` reference to it.
3. `LevelLoader.Load(resolver, levelRef)`:
   - resolve level bytes → `LevelDefinition`; validate `tileSize`/`width`/`height` > 0;
   - resolve the level's `tileSet` reference → `TileSetDefinition`;
   - for each tile definition, resolve its `graphic` reference → PNG bytes, keyed by tile id (reject duplicate/reserved ids);
   - validate every layer: cell count equals `width*height`, and every non-empty cell id is defined;
   - return `ResolvedLevel`.
4. `TileMapLevelBuilder.Build(resolvedLevel)`: per graphic, `Image.LoadPngFromBuffer` → `ImageTexture.CreateFromImage` → a `TileSetAtlasSource` (region = tile size, single tile at atlas `(0,0)`), added to a shared `TileSet`; record tile-id → source-id. Then one `TileMapLayer` per level layer sharing that `TileSet`; for each non-empty cell call `SetCell((x,y), sourceId, (0,0))`. Returns the root `Node2D`; child order = draw order (first layer at the bottom).
5. `LevelDisplay` adds the node under a holder it scales/centres to the viewport.

All communication is synchronous, in-process, one call each. No events, no async — a display spike has no need for them.

## 7. Data Model (Conceptual, v0)

Three package resources, all resolved by `ResourceReference` (`self:` within one package):

- **Tile graphic** — a `tilegraphic` resource whose payload is PNG bytes. Opaque to the schema.
- **Tile set** — a `tileset` JSON resource: a list of **tile definitions**, each = `{ id: int, graphic: <ref to a tilegraphic> }`. The object shape (not a bare id→graphic map) is the deliberate "room for later props like collision" — new fields attach to the tile definition without breaking the grid. No such fields exist yet (YAGNI).
- **Level** — a `level` JSON resource: `{ tileSize: int(px), width: int, height: int, tileSet: <ref>, layers: [ { name, cells: int[] } ] }`. `cells` is row-major (`index = y*width + x`), length `width*height`; the sentinel `-1` (`LayerDefinition.EmptyCell`) means "no tile". Tile ids in cells index into the tile set.

Ownership: the package owns the bytes; the level owns the grid and layer ordering; the tile set owns the id→graphic mapping. A level points at exactly one tile set for v0.

Example (abridged) level JSON:

```
{
  "tileSize": 16, "width": 20, "height": 12,
  "tileSet": "self:tileset.json",
  "layers": [
    { "name": "ground",     "cells": [ -1, -1, 3, ... ] },
    { "name": "decoration", "cells": [ -1, -1, -1, ... ] }
  ]
}
```

## 8. Contracts & Interfaces (Abstract)

- **`LevelContentSerializer`** — total function both ways between the schema POCOs and JSON bytes; malformed JSON surfaces as `LevelContentException`. References serialize as their canonical `packageId:path` string.
- **`LevelLoader.Load(IResourceResolver, ResourceReference) -> ResolvedLevel`** — invariants enforced before returning: positive dimensions; each layer's cell count matches the grid; every referenced tile id is defined; every tile graphic resolves. Any failure (bad schema, unresolved ref, inconsistent grid) is a single `LevelContentException` with a human-readable reason. Output contains only primitives and `byte[]`.
- **`TileMapLevelBuilder.Build(ResolvedLevel) -> Node2D`** — pure translation: no I/O, no package/schema knowledge. Precondition: graphics are decodable PNGs (a decode failure throws `LevelContentException`). Postcondition: a `Node2D` with one `TileMapLayer` child per level layer, cells set, sharing one `TileSet`.
- **`IResourceResolver`** (existing, from Packages) is the only seam the loader needs — so the same loader works over a single package or a multi-package registry later.

## 9. Cross-Cutting Concerns

- **Error handling.** Untrusted content: every schema/resolution failure is a typed `LevelContentException`; `LevelDisplay` catches at the scene boundary and logs via `GD.PrintErr` rather than crashing the engine (aligns with the vision's "engine never freezes on bad shared content" posture — here for data, not scripts).
- **Security.** Reuses the format layer's ZIP-slip guard and payload validation; the loader adds no new file access of its own (all bytes come through the resolver).
- **Observability.** `LevelDisplay` prints a one-line summary (dimensions, tile count, layer count) on success.
- **Consistency / performance.** All resolution is eager and synchronous; the package can be disposed right after materialization because `ResolvedLevel` copies the bytes out. Fine for a spike's data sizes.

## 10. Quality Attributes & Trade-offs

- **Don't-fight-the-engine (primary).** The renderer uses `TileSet` + `TileSetAtlasSource` + `TileMapLayer` exactly as Godot intends; the level grid maps one-to-one onto `SetCell`. No custom drawing.
- **One atlas source per tile graphic (v0).** Simplest possible id→(source,coords) mapping — atlas coords are always `(0,0)`. Trade-off: a real tileset sheet would pack many tiles per atlas (fewer textures, autotiling/terrains available). Rejected for v0 because it adds a packing/coordinate scheme the spike doesn't need. The seam (`ResolvedLevel.TileGraphics` keyed by id) does not foreclose switching to sheet-backed atlases later.
- **Separate `Uberkarl.Content` lib rather than extending `Uberkarl.Packages`.** Keeps the generic container (bytes, manifest, identity) free of content taxonomy; content schema can evolve without touching the format. Small extra project; worth it for the clean seam and reusability (future editor/CLI).
- **Fully-materialized `ResolvedLevel` boundary.** The engine side receives primitives only, so all parsing/validation is unit-testable with zero Godot dependency. Trade-off: bytes are copied once; negligible here.
- **Integer tile ids + `-1` empty sentinel.** Compact, maps directly to atlas source ids; matches Godot's own "empty cell" convention.

## 11. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| Schema churn as Toni reacts | Everything is v0 behind the `ResolvedLevel` seam; the format's swappable-resolver guarantee (#7413) already absorbs identity changes. Renderer only depends on the resolved model. |
| Tile-graphic PNG unreadable at runtime | Loader validates ids/dims; builder converts decode failure into a typed exception caught at the scene boundary (logged, no crash). |
| Package unreadable inside exported `.pck` | `LevelDisplay` reads via Godot `FileAccess` (stream), not `System.IO` paths, so it works both in-editor and exported. |
| One-atlas-per-tile won't scale to big tilesets | Acceptable for v0; seam allows sheet-backed atlases without touching schema or renderer contract. |

## 12. Migration / Rollout

Additive only. New content lib + new game files + new sample + new scene; the sole edits to existing artifacts are the two new `ResourceKind` constants, the game csproj now `ProjectReference`-ing the two libraries (the Phase-1a doc deferred this to Phase 1b) and excluding `tools/**` from its compile, and `project.godot`'s run target. No behavior of the existing package format changes.

## 13. Open Questions (for Toni)

1. **Tile-graphic kind vs. sprite.** v0 adds a dedicated `tilegraphic` kind, distinct from the existing `sprite` kind, on the theory that static grid tiles and animated entity sprites diverge later. Keep them separate, or collapse tiles into `sprite`?
2. **One tile = one image, vs. a packed tileset sheet.** v0 is one PNG per tile (one atlas source each). Do you want authored sheet atlases (multiple tiles per image, atlas coords in the tile def) reasonably soon, or is per-tile fine until the editor exists?
3. **Where do tile definitions live?** v0 puts them in a separate `tileset` resource the level references. Alternative: embed them directly in the level. Separate keeps tilesets shareable across levels/packages — is that the intent, or is per-level simpler for now?
4. **Square tiles + single tile size.** OK for v0, or do you already foresee non-square / per-layer tile sizes?
5. **Layer semantics.** v0 layers are pure draw order (later child = drawn on top). Do layers eventually need roles (background / collision / foreground), or stay generic named paint layers?
6. **Empty-cell sentinel.** `-1` in a flat `int[]`. Fine, or would you prefer a sparse representation (only non-empty cells) for large, mostly-empty levels?

## 14. Implementation Guidance / Build Order (as shipped)

1. Schema POCOs + `LevelContentException` in `src/Uberkarl.Content`.
2. `LevelContentSerializer` (+ a `ResourceReference` JSON converter) — round-trip tests.
3. `LevelLoader` → `ResolvedLevel` with validation — resolution + failure-path tests over an in-memory package.
4. `tools/SampleContent`: dependency-free PNG encoder (stored via `ZLibStream` + CRC32) → placeholder tiles → tileset + hand-authored grid → `content/sample.pkg`.
5. Wire the game csproj: `ProjectReference` both libs; exclude `tools/**`.
6. `TileMapLevelBuilder` + `LevelDisplay`; scene `scenes/level_display.tscn`.
7. Verify: build game + both test suites; run the scene via Godot and screenshot; confirm editor errors clean.
