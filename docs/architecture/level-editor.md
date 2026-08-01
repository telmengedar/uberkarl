# Architectural Document: In-Game Level Editor (Phase 2a MVP)

> Toni's ask (verbatim): *"level editor it is - here a good ui is king. clear, load, save, select tiles and so on - go ahead."*

This document describes the architecture of Uberkarl's in-game level editor and the scope of its
**first increment (MVP)**, shipped together in one PR. The editor is the headline authoring surface of
the meta-engine (vision #7407, Phase 2): levels are portable data, authored in Uberkarl's own tight
tools, never hand-built in the Godot editor. This is a **multi-PR arc** — the architecture is designed to
grow into the deferred increments and, eventually, the Pooscript behavior loop (Phase 2b), but only the
MVP painting editor is built now.

---

## 1. Problem Statement

Author a level — the tile grids of a `.pkg` level (schema v0.2, #7420) — from inside the running game,
with a clear, direct UI: open a package, see the level, pick a tile, paint and erase on a chosen layer,
and save back to a package. The output must remain a valid `.pkg` that the existing runtime
(`LevelLoader` → `TileMapLevelBuilder` → play scene) loads and plays unchanged. Success = a person can
open the sample level, change it visually with the mouse, save it, reopen it, and see their changes.

## 2. Scope & Non-Scope

**In scope (MVP, this increment):**
- Editor scene + `Control`-based UI shell: toolbar, tile palette, canvas, layer selector.
- Load a `.pkg` (via `FileDialog`) and render its level.
- Select a tile from the palette (the active tile).
- Paint / erase tiles on the selected layer by mouse click **and** click-drag.
- Save the edited level back to a `.pkg` (save and save-as via `FileDialog`).
- New blank level at fixed starter dimensions with a built-in starter palette.
- Undo / redo via a command stack (implemented, bounded depth).
- The engine-agnostic edit model + the edit→save→load round-trip, unit-tested in `src/`.

**Out of scope (named deferred increments — see §12):**
- Tileset / graphic add-remove-edit · layer create/delete + property editing · spawn-placement UI ·
  background-color picker · level resize · the Pooscript event→action behavior loop (Phase 2b) ·
  playtest-from-editor · cross-package resource editing.

## 3. Assumptions & Constraints

| # | Assumption / Constraint | Confidence |
|---|---|---|
| A1 | Desktop-only, Godot 4.7 .NET, C#. File-based packages, no backend (locked, #7407). | High |
| A2 | Level schema v0.2 (#7420) and the package format (#7413) are stable and are what the editor reads/writes. | High |
| A3 | Edited levels are **self-contained** packages (every tile graphic is a self-reference in the same package, as the sample is). Cross-package graphics are rejected with a clear error and deferred. | High |
| A4 | The MVP edits **cell contents only**; geometry, palette, spawns, and metadata are fixed at load/create time. Editing those is later increments. | Med — confirm priority order with Toni (§13) |
| A5 | Engine-agnostic edit + save/load logic lives in `src/` and is unit-tested; UI/canvas/input live in `game/` (in the Godot compile set), matching the established layering. | High |
| A6 | The editor is an acceptable run target for the project (#7432 blesses it); a proper mode/menu shell comes later. | Med |

## 4. Architectural Overview

Three layers, matching the repo's existing split (engine-agnostic model in `src/`, Godot glue in `game/`):

```
                     ┌──────────────────────────── game/  (Godot compile set) ─────────────────────────────┐
                     │                                                                                      │
   mouse / clicks ──▶│  LevelEditor (Control) ── controller / composition root                             │
                     │     │  builds UI, owns file IO, translates UI intent → session calls                 │
                     │     ├── EditorTheme          (one Theme: dark shell + amber accent)                  │
                     │     ├── Toolbar / Palette / LayerList  (Control nodes)                               │
                     │     ├── EditorCanvas (Control)  ── renders the level, maps pointer → grid cell       │
                     │     │        └── TileMapLevelBuilder.BuildEditable(resolved)  (reused renderer)      │
                     │     └── FileDialog × 2  (open / save)                                                │
                     │                    │  intent (paint/erase/undo/redo/save)   ▲ CellChange to render   │
                     └────────────────────┼──────────────────────────────────────┼──────────────────────┘
                                          ▼                                        │
   ┌──────────────────────── src/Uberkarl.Editor  (engine-agnostic, unit-tested) ─┴──────────────────────┐
   │  LevelEditSession  ── the façade: PaintCell / EraseCell / Undo / Redo / Save, returns CellChange     │
   │     ├── EditableLevel (model: dims, palette, layer grids, spawns, package metadata + paths)          │
   │     ├── EditHistory  ── undo/redo stacks (bounded)                                                   │
   │     │     └── IEditCommand ▸ SetCellCommand  (paint = set tile; erase = set empty)                   │
   │     ├── EditableLevelReader   (Package → EditableLevel, lossless)                                    │
   │     ├── EditableLevelWriter   (EditableLevel → .pkg bytes, via PackageBuilder)                       │
   │     └── EditableLevelSnapshot (EditableLevel → ResolvedLevel, for the canvas renderer)               │
   └──────────────────────────────────────────────────────────────────────────────────────────────────┘
                                          │ reads / writes
   ┌──────────────────────────────────────┴───────────────────────────────────────────────────────────┐
   │  Uberkarl.Content (level v0.2 model, LevelLoader)   ·   Uberkarl.Packages (PackageReader/Writer/…)  │
   └────────────────────────────────────────────────────────────────────────────────────────────────────┘
```

The **one-way data path** is the spine of the design: input → controller → `session` (mutates model, records
command) → returns a `CellChange` → canvas reflects that one cell. The UI never mutates the model directly,
and the model never knows about Godot. This is what keeps the editable core unit-testable and the undo seam
honest.

## 5. Components & Responsibilities

### Engine-agnostic core (`src/Uberkarl.Editor`)

| Component | Owns | Does NOT own |
|---|---|---|
| **EditableLevel** | The mutable source of truth: dimensions, tile palette, per-layer grids, spawns, and the package identity/metadata/paths needed to re-save. Bounds and index helpers. | Rendering, file IO, undo bookkeeping, image decoding. |
| **EditableLayer** | One layer's mutable cell array + carried-through collision/scroll/repeat attributes. | Draw order, physics. |
| **EditableTile** | One palette tile: id, in-package graphic path, graphic bytes, collide flag. | Decoding/encoding images. |
| **IEditCommand / SetCellCommand** | A single reversible edit and its inverse; reports the one cell it changes. | Stack management, when it runs. |
| **EditHistory** | Undo/redo stacks; linear-history semantics; bounded depth. | What a command means; the model's validity. |
| **LevelEditSession** | The façade the UI drives: intent-level paint/erase/undo/redo/save; dirty tracking; no-op suppression; validation. | Godot types, file paths, rendering. |
| **EditableLevelReader** | Lossless load of an `EditableLevel` from a package (keeps graphic paths, collide flags, spawns, metadata). | Runtime resolution (that's `LevelLoader`). |
| **EditableLevelWriter** | Serialize an `EditableLevel` back to self-contained `.pkg` bytes via `PackageBuilder`. | Writing to disk. |
| **EditableLevelSnapshot** | Project the current model into a `ResolvedLevel` so the canvas renders through the same builder the play scene uses. | Mutation, incremental updates. |

### Godot glue (`game/Editor`, `game/Level`)

| Component | Owns | Does NOT own |
|---|---|---|
| **LevelEditor** (`Control`) | Composition root & controller: builds the UI tree, owns the `LevelEditSession`, wires toolbar/palette/layers/dialogs, performs file IO, applies returned `CellChange`s to the canvas, renders status. | Edit logic, undo mechanics, package (de)serialization — all delegated to the session/library. |
| **EditorCanvas** (`Control`) | The authoring surface: renders the built level, maps pointer→grid cell (fit-to-panel), raises cell-press events, draws grid + hover overlay, applies a single `CellChange` in place. | The edit model; it never mutates data. |
| **DefaultPalette** | Generates the starter tiles (solid-colour PNGs) a "New" level opens with — image encoding on the engine side. | The model (which only stores bytes). |
| **EditorTheme** | One `Theme` for the whole editor (dark slate shell, raised panels, single amber accent). | Layout. |
| **TileMapLevelBuilder.BuildEditable** *(extension of existing builder)* | Builds the shared `TileSet` + flat per-layer `TileMapLayer`s (no parallax), and exposes the layer nodes + tile-id→atlas-source map so the canvas can paint/erase one cell without a rebuild. | Parallax/camera (that stays in the existing `Build` used by the play scene). |

**Single renderer, two entry points.** The play scene calls `TileMapLevelBuilder.Build` (parallax + camera).
The editor calls the new `BuildEditable` (flat, 1:1 cell↔screen, exposes the nodes). Both share the tile-set
construction and cell-fill loop — authoring and play render tiles the same way.

## 6. Interactions & Data Flow

**Load:** `LevelEditor` reads `.pkg` bytes (sample at startup, or a `FileDialog` path) →
`EditableLevelReader.FromPackageBytes` → `EditableLevel` → wrapped in a new `LevelEditSession` →
`EditableLevelSnapshot.ToResolvedLevel` → `EditorCanvas.SetLevel` (builds via `BuildEditable`) → palette
and layer list populated from the model.

**Paint / erase (click or drag):** `EditorCanvas._GuiInput` maps the pointer to a cell `(x,y)` (suppressing
re-fires while the pointer stays in the same cell during a drag) → raises `CellPressed(x,y)` →
`LevelEditor.OnCellPressed` reads the active tool + active tile + active layer → calls
`session.PaintCell(layer,x,y,tile)` or `session.EraseCell(layer,x,y)` → session validates, suppresses
no-ops, executes a `SetCellCommand` through `EditHistory`, marks dirty, returns a `CellChange?` →
`LevelEditor` applies the change to `EditorCanvas` (one `SetCell`/`EraseCell`).

**Undo / redo:** button → `session.Undo()/Redo()` → history reverts/re-applies the command → returns the
`CellChange` to repaint → canvas updates that one cell.

**Save:** button → if no current path, open the save `FileDialog`; else write directly → `session.Save()`
serializes to bytes (`EditableLevelWriter`) and clears dirty → `LevelEditor` writes bytes to the chosen path
(re-marks dirty on failure). Save-as routes through the dialog and appends `.pkg` if omitted.

**Communication style:** all synchronous, in-process, single-threaded. No async, no events across the
Godot/engine boundary beyond plain C# delegates. This is an editor UI — simplicity wins.

## 7. Data Model (Conceptual)

- **EditableLevel** 1—* **EditableLayer** (each a row-major `int[]` grid of tile ids; `-1` = empty).
- **EditableLevel** 1—* **EditableTile** (the palette; a cell stores a tile's numeric id).
- **EditableLevel** carries **package identity + metadata + in-package resource paths** (level path, tileset
  path, per-tile graphic paths) so a save reproduces the package in place rather than minting a fork.
- **Spawns** (name→cell) and **default spawn** are carried through unchanged (round-trip fidelity; editing
  them is deferred).

The editable model is deliberately *richer than* `ResolvedLevel` (it keeps authoring provenance the runtime
view drops) and *distinct from* `LevelDefinition` (its grids are mutable). It is the authoring shape.

## 8. Contracts & Interfaces (Abstract)

- **IEditCommand** — `Apply(level) → CellChange` and `Revert(level) → CellChange`. Invariant: `Revert`
  after `Apply` restores the exact prior state; each command reports the single cell it touched. This is the
  extension seam: fill, rectangle, layer/spawn edits are new command kinds, no contract change.
- **LevelEditSession** — the intent surface: `PaintCell / EraseCell / Undo / Redo / Save`, each returning a
  `CellChange?` (`null` = nothing changed). Invariants: painting a cell that already holds the target tile is
  a no-op (keeps drags from stacking redundant history); an out-of-palette tile or invalid layer is rejected;
  `IsDirty` is true iff there are unsaved changes.
- **EditableLevelReader / Writer** — round-trip contract: for a self-contained package,
  `Reader(Writer(level))` reproduces the level's cells, palette, geometry, spawns, and metadata; and
  `Writer` output is also loadable by the runtime `LevelLoader`. (Both directions unit-tested.)
- **TileMapLevelBuilder.BuildEditable** — returns the parent node, the per-layer `TileMapLayer`s
  (index-aligned to the model's layers), and the tile-id→atlas-source map, so the canvas paints
  `Layers[i].SetCell(cell, SourceByTile[id], 0)` and erases `Layers[i].EraseCell(cell)`.

## 9. Cross-Cutting Concerns

- **Error handling:** load/save failures are caught at the controller boundary and logged (`GD.PrintErr`);
  a failed save re-marks the session dirty so the edit is not silently "saved". The engine-agnostic core
  throws typed exceptions (`LevelContentException`, argument exceptions) with clear messages; validation
  lives in the session and reader, not the UI.
- **Consistency / single source of truth:** the model is authoritative; the canvas is a projection. The UI
  applies exactly the `CellChange` the session reports — the render can never drift from the data, because it
  is only ever updated *from* committed changes.
- **Idempotency:** no-op paints are suppressed at the session, so a click-drag that re-touches a cell neither
  changes state nor grows history.
- **Undo bounds / memory:** history depth is capped (500); past the cap the oldest command is dropped. A
  `SetCellCommand` is tiny (a few ints), so this is generous and safe.
- **Observability:** concise `GD.Print` on load/save/new with counts and paths; a live status line (level
  name, dirty marker, file, active layer, active tool/tile).
- **Security / untrusted input:** editing goes through the same hardened `PackageReader` (ZIP-slip guard,
  format-version check, payload validation) the runtime uses; the editor adds no new trust surface. Saving
  never fabricates a new package identity — it edits in place.
- **Concurrency:** none — single-threaded UI. Deliberate.

## 10. Quality Attributes & Trade-offs

- **Maintainability / testability (primary):** the entire edit + save/load surface is engine-agnostic and
  unit-tested (apply-edit, undo/redo, edit→save→load round-trip, runtime-loadability). The Godot layer is
  thin glue. This is the biggest lever for a feature that will grow across many increments.
- **Simplicity (KISS):** one command type covers paint and erase; the canvas re-renders one cell per edit
  (no full rebuild, no flicker); undo is a plain linear stack; the model edits cells only. No speculative
  panels for deferred features (per anti-complexity guidance / #1184).
- **Extensibility:** the command seam, the tool/mode indirection, and the reader/writer boundary are the
  three places the deferred increments plug in without reshaping the core.

**Trade-offs & rejected alternatives:**
- *Reuse `TileMapLevelBuilder` for the canvas vs. a bespoke editor renderer* — **reused** (extended with a
  flat build path). One rendering path for authoring and play; less code; the play scene keeps parallax.
- *Full re-render per edit vs. incremental single-cell update* — **incremental.** O(1) per paint, no flicker;
  the builder exposes exactly what's needed (layer nodes + source map).
- *Undo: ship only the seam vs. a working stack* — **working bounded stack.** The command is trivial
  (`SetCell` with old/new), so a real, tested undo/redo costs almost nothing and is a better first
  impression; depth/branching stays a later concern behind the same seam.
- *Editable model = reuse `ResolvedLevel` vs. a dedicated authoring model* — **dedicated.** `ResolvedLevel`
  drops authoring provenance (graphic paths, collide flags, metadata) needed for lossless re-save; a purpose
  model keeps the round-trip exact.
- *SubViewport vs. a `Control` canvas with a child world node* — **Control canvas.** Direct `_GuiInput`
  pointer→cell mapping and `_Draw` overlays, no viewport input-routing complexity; the tile layers sit
  behind the control's overlay via `ShowBehindParent`.
- *Theme as a `.tres` vs. built in code* — **built in code** for now (palette lives beside the layout);
  trivially promotable to a resource later.

## 11. Risks & Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Native `FileDialog` UX inconsistency across desktop OSes | Med | Standard Godot `FileDialog`, `*.pkg` filter, `.pkg` auto-appended on save; a custom in-engine browser is a later polish. |
| Cross-package tile graphics not editable | Low (MVP) | Detected and rejected with a clear message; deferred with the resource-editing increment. |
| Grid/hover overlay ordering relative to tiles (`ShowBehindParent`) | Low | Verified visually in-engine; overlay draws on top, tiles read through the translucent grid. |
| Editor as run target changes the project's play entry point | Low | Blessed by #7432; a mode/menu shell is a named future increment. |
| Large levels / very deep paint sessions grow history | Low | Bounded history (500); tiny per-command footprint. |

## 12. Migration / Rollout — the multi-PR arc

This PR ships the MVP. The architecture is built so each following increment is additive:

1. **This PR — MVP painting editor** (load/paint/erase/select/save + new + undo/redo).
2. **Layer editing** — create/delete layers, edit name/collision/scroll/repeat. *Seam:* new commands
   (`AddLayerCommand`, `SetLayerPropertyCommand`); layer list gains add/remove; model grids already per-layer.
3. **Tileset / graphic editing** — add/remove/replace palette tiles and their graphics. *Seam:* the palette
   is model-driven; add tile-mutation commands + a graphics import path; cross-package refs become editable.
4. **Spawn placement + background-colour picker + level resize** — spawn tool, colour control, dimension
   change (grid remap). *Seam:* new tools/commands; model already carries spawns/background; resize is a
   model op behind a command.
5. **Fill / rectangle / selection tools** — more `IEditCommand` kinds behind a richer tool/mode enum; canvas
   gains a selection overlay. No core reshaping.
6. **Playtest-from-editor** — hand the in-memory (or just-saved) package to the existing play scene.
7. **Phase 2b — Pooscript behavior loop** (separate arc): attach event→action behavior to entities/tiles;
   the editor gains a behavior surface over the same session/model spine.

## 13. Open Questions (for Toni)

1. **UI / layout** — react to the mockup (§14). Is a left panel (layers over tiles) + top toolbar the right
   shape, or do you want the palette on the right / a bottom strip? Icon-grid palette vs. a labelled list?
2. **Which follow-up increment first?** Candidates: **layer editing**, **tileset/graphic editing**, or
   **spawn + background + resize**. Recommendation: **layer editing** next (it unblocks authoring
   multi-layer levels), then tileset editing. Confirm the order.
3. **New-level defaults** — starter dimensions (currently 24×16) and the built-in starter palette (grass /
   dirt / stone / brick / water). Good defaults, or should "New" prompt for dimensions?
4. **Editor as the run target** — keep the editor as the main scene for now, or add a small mode/menu shell
   (Editor / Play) sooner?
5. **File browsing** — native `FileDialog` acceptable, or do you want an in-engine package browser (part of
   the later distribution UX) earlier?
6. **Undo depth** — 500 steps is the current cap; fine, or expose it / make it unbounded?

## 14. UI Layout (mockup — please react)

Dark slate shell, one amber accent for the active tool and selection. Top toolbar for actions/tools, a fixed
left panel for **Layers** (top) and **Tiles** (bottom), and the canvas filling the rest. A status line on the
right of the toolbar shows level name, unsaved marker, file, active layer, and active tool/tile.

```
┌───────────────────────────────────────────────────────────────────────────────────────────┐
│  New   Open   Save   Save As  │  [Paint]  Erase  │  Undo  Redo    Demo *  · file.pkg · layer:│
│                                                                    terrain · tool: Paint (#5)│
├────────────────────┬──────────────────────────────────────────────────────────────────────┤
│ Layers             │                                                                        │
│ ┌────────────────┐ │        ▓▓░░░░░░░▓▓░░░░░░░▓▓ ← clouds (backdrop)                         │
│ │ backdrop       │ │                                                                        │
│ │ terrain [solid]│ │            ▟▓▓▙       ▟▓▓▙       ← hills (backdrop)                     │
│ └────────────────┘ │        ▄▟▓▓▓▓▓▙▄   ▄▟▓▓▓▓▓▙▄                                            │
│                    │      ██  ▬▬▬▬     ██   ▬▬▬▬  ██ ← platforms (terrain)                   │
│ Tiles              │      ██                     ██                                          │
│ ┌────┬────┬────┐   │      ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓ ← grass                                 │
│ │ 🟩 │ 🟫 │ ⬜ │   │      ▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒ ← dirt                                  │
│ ├────┼────┼────┤   │                                                                        │
│ │ 🟥 │ 🟦 │ ⬛ │   │      (grid overlay drawn on top; hovered cell highlighted amber)        │
│ └────┴────┴────┘   │                                                                        │
│  (active = amber   │                                                                        │
│   outline)         │                                                                        │
└────────────────────┴──────────────────────────────────────────────────────────────────────┘
```

Behaviour: select a layer to target it; select a tile (auto-switches to Paint) or press **Erase**; click or
drag on the canvas to paint/erase; the hovered cell is outlined; **Undo/Redo** disable when their stack is
empty; the title shows `*` while there are unsaved edits.

## 15. Implementation Guidance / Build Phases (as built)

1. **Engine-agnostic core (`src/Uberkarl.Editor`)** — model (`EditableLevel/Layer/Tile`), command seam
   (`IEditCommand`, `SetCellCommand`, `EditHistory`), façade (`LevelEditSession`), load/save
   (`EditableLevelReader/Writer`), render projection (`EditableLevelSnapshot`).
2. **Unit tests (`tests/Uberkarl.Editor.Tests`)** — apply-edit, no-op suppression, undo/redo + bounds,
   edit→save→load round-trip, runtime-loadability of saved bytes, reader guards, snapshot projection.
3. **Renderer extension** — `TileMapLevelBuilder.BuildEditable` (flat layers + source map), sharing the
   tile-set/cell-fill code with the existing parallax `Build`.
4. **Game UI** — `EditorTheme`, `EditorCanvas`, `DefaultPalette`, `LevelEditor` controller, and the
   `level_editor.tscn` scene; editor set as run target.
5. **In-engine verification (Godot MCP)** — load sample, select tile, paint (click + drag), erase, save to a
   new `.pkg`, reload it, confirm edits persisted; `get_editor_errors` clean; screenshots.
