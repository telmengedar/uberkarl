# Architectural Document: Adjust Level Dimensions (resize the grid)

Source task: DiVoid #7550 · Project #7396 (Uberkarl) · Depends on: level model v0.2 #7420, editor MVP #7433, editor UI v2 #7466, layer editing #7502/#7501, playtest-from-editor #7519 · Vision #7407.

Status: design + implementation shipped together on branch `feat/level-dimensions` against `main`. This document is both the blueprint and the as-built record.

---

## 1. Problem Statement

There was no way to change a level's size after creation — `EditableLevel.Width`/`Height` were fixed at construction (`CreateBlank` or load-time). An author who under- or over-sized a level at "New" had no path forward except starting over. This closes that gap: a **resize** action that sets width/height in the editor, gamepad-friendly, preserving painted content on grow and warning before data loss on shrink.

Success criteria (mirrors DiVoid #7550's verify section):
- Resize larger and smaller, entirely on gamepad or keyboard.
- Growing never loses data; shrinking crops and asks for confirmation only when it would actually drop a painted cell.
- `tileSize` is untouched — this is a grid-dimension change, never a re-scale.
- New dimensions persist: save → reload reproduces them.
- Play/playtest camera bounds match the resized level.

## 2. Scope & Non-Scope

**In scope**
- A width/height resize control on the level (not per-layer — levels share one W×H across their whole layer stack, per #7420).
- Gamepad-friendly +/- steppers, no text entry.
- Grow = preserve existing cells at their original coordinates, fill new cells empty. Shrink = crop, with a confirm gate when painted cells would be dropped.
- Applied identically across every layer.
- Camera-bounds correctness in play/playtest (verified, not additionally implemented — see §4).
- Engine-agnostic, unit-tested resize/crop + confirm-query logic in `src/Uberkarl.Editor`.

**Explicitly out of scope**
- Any keyboard-text numeric entry.
- Tileset editing, spawn-placement UI, or any other authoring surface.
- Undoable resize (structural, like layer delete/move — see §5).
- Re-scaling tile content or `tileSize` changes.
- Reconciling spawns that a shrink crops out of bounds (the loader already validates spawn bounds at load time for the runtime path; the editor does not currently place spawns via UI at all, so this could not be exercised — noted as a follow-up, not built).

## 3. Where Dimensions Live

`EditableLevel.Width`/`Height` (`src/Uberkarl.Editor/EditableLevel.cs`) are the single source of truth in the authoring model. Everything downstream already reads them fresh on every use — no schema or cross-project change was needed:

- `EditableLevelSnapshot.ToResolvedLevel` copies `Width`/`Height` straight into the `ResolvedLevel` projected for the canvas and for playtest.
- `LevelMergeWriter.BuildContributions` writes them straight into the persisted `LevelDefinition` — `Uberkarl.Content`'s schema already carries `Width`/`Height` as plain ints with no v0.2-style change needed.
- `PlayRuntimeBuilder.AttachCamera` (`game/Play/PlayRuntimeBuilder.cs`) sets `Camera2D.LimitRight = level.Width * level.TileSize` / `LimitBottom = level.Height * level.TileSize` **from the projected level on every playtest/play start** — this is why camera bounds needed no code change at all; they were already derived live, never cached. Verified directly (§6).
- `EditorCanvas.SetLevel` already fully rebuilds the rendered tilemap + re-binds `GridCursor.Resize(width, height)` on every call — the exact same call the layer panel's `LayerModelChanged` refresh already uses, so no canvas-side change was needed either.

This meant the whole feature reduced to: (a) make `Width`/`Height` mutable, (b) add the resize/crop mutation + confirm-query, (c) add a UI surface that calls it and hooks into the same refresh path layer editing already established.

## 4. Model Changes (`src/Uberkarl.Editor`)

- **`EditableLevel.Width`/`Height`**: changed from `{ get; }` to `{ get; private set; }`.
- **`EditableLevel.Resize(int width, int height)`**: the structural mutation. Validates positive dimensions (throws, matching the constructor's existing guard); no-op (`false`) when the size is unchanged. For each layer, allocates a new `width*height` cells array filled empty, then copies the overlapping region (`min(oldWidth,width) × min(oldHeight,height)`) from the old array — this single copy loop handles grow, shrink, and mixed grow/shrink in one pass. Each `EditableLayer` instance is replaced (unlike `SetLayerProperties`, there is no existing array of the right length to reuse — a resize always reallocates). `TileSize` is never touched.
- **`EditableLevel.WouldDropPaintedCells(int width, int height)`**: a pure query, no mutation. `false` whenever neither dimension shrinks (growing never crops anything — fast path). Otherwise scans every layer's cells that would fall outside the proposed bounds for a non-empty tile. This is the confirm-gate signal — mirrors the layer manager's "Confirm Delete?" two-press pattern (§5), applied here only when there is actually something to lose.
- **`GridDimensionRules`** (new, pure): `Step(current, direction)` clamps to `[MinDimension=1, MaxDimension=500]`, stepping by exactly 1. Shaped like `ScrollSpeedLadder` (an engine-agnostic rule the Godot stepper Control drives) but a plain clamped range rather than a curated preset list — a grid dimension is a continuous cell count, not a handful of meaningful speeds. `MaxDimension` is a **UI-side soft cap only**, not a model invariant (`Resize` itself only rejects non-positive sizes) — a hand-authored or previously-saved level larger than the cap still loads and displays fine; the cap only stops a held gamepad stepper from growing the grid without bound.
- **`LevelEditSession.Resize(int width, int height) -> bool`**: the session-level intent. Calls `Level.Resize`; on a real (non-no-op) resize it **clears cell-edit history** (`history.Clear()`) and marks the session dirty. This mirrors `DeleteLayer`/`MoveLayer`'s history policy, but for a different reason: `SetCellCommand.Apply`/`Revert` re-resolve an absolute `(x,y)` to a cell index via **the level's current `Width`** at apply/revert time (`level.CellIndex(x,y)`), not a cached index. A resize changes `Width`, so a command recorded before it would compute the wrong cell index (or an out-of-range one, for a coordinate a shrink cropped away) if replayed after. Resize itself is not on the undo stack this increment — the same "structural ops aren't undoable yet" policy layer editing already established (§5 of DiVoid #7502/`layer-editing.md`), kept consistent rather than inventing a different policy for a different structural op.
- **`MenuOutcomeKind.OpenResizePanel`** + `MenuOutcome.OpenResizePanel()`: routes the Actions radial's new "Resize…" wedge, exactly like `OpenLayerManager` routes the Layers radial's "Manage…" wedge. No new `EditorAction` / device binding — it rides the existing Actions radial trigger.

## 5. UI (`game/Editor`)

`LevelResizePanel` (new) is a summoned, gamepad-first `Control`, reusing the `PackageBrowser`/`LayerManagerPanel` scaffolding verbatim: full-rect dim backdrop, centered panel, deferred grab-focus-on-summon, `ui_cancel` closes with nothing applied (belt-and-suspenders `_UnhandledInput` override for the same reason `LayerManagerPanel` needs one — a row Button, not the panel, almost always holds focus, and Godot does not bubble an unhandled action GUI event up through ancestor Controls).

Layout: a static "Current size: W x H" label, then two `DimensionStepper` rows (Width, Height), then an Apply row — wired into a `FocusGrid` 2D grid exactly like the layer manager's rows, with the same focus-position-tracking-across-Rebuild fix (`lastFocusedRow`) that the layer-editing PR (#16) had to add after finding rebuild-resets-focus was a real bug there.

**Why no enter-edit-mode gesture, unlike the layer panel's Scroll stepper (DiVoid #7512):** the Scroll stepper needed `SteppedValueEditor`'s enter/adjust/commit/cancel indirection because it shares a row with four other controls (header, Collision toggle, Repeat toggle, Move/Delete) — left/right had a real spatial-navigation job to do when the stepper was merely focused, not being edited. Here, each `DimensionStepper` is the **only** column in its row, so left/right has no sibling to navigate to; consuming `ui_left`/`ui_right` directly for adjustment loses no navigation capability. This is a deliberate simplification, not an oversight — reusing the heavier machinery where it isn't needed would have been the actual complexity violation.

**Why both dimensions apply in one atomic call:** each stepper only adjusts a **local** pending value (`DimensionStepper.Value`); the session is untouched until Apply reads both steppers' current values and calls `session.Resize(width, height)` once. A resize is inherently a single width+height operation — applying width and height as two separate `Resize` calls would (a) double the history-clear/dirty churn for one logical edit, and (b) make the confirm gate meaningless (the first of two calls could already crop something before the author ever set the second dimension).

**Confirm-on-data-loss**: `OnApplyPressed` checks `session.Level.WouldDropPaintedCells(newWidth, newHeight)`. If true and not already pending, it sets `pendingConfirm` and relabels the Apply button ("Confirm Resize? (crops painted tiles)") without resizing — a second Apply press proceeds. This is the exact two-press shape `LayerManagerPanel.OnDeletePressed` already uses for "Confirm Delete?", reused rather than reinvented per the task's explicit ask. Adjusting either stepper after a pending confirm resets the gate (`OnStepperAdjusted`) — the confirm was computed for a specific `(width, height)` combo, and changing either value invalidates it; carrying a stale confirm forward across a different combo would be a genuine bug (confirming a crop that no longer matches what's about to be applied).

`LevelEditor.cs` wiring mirrors the layer manager exactly: a `resizePanel` field, added in `BuildUi`, threaded into the same three modal guards `layerManager.IsOpen` already appears in (`CursorInputGate.DirectionCaptured` in `_Process`, `UpdateReveals`'s `menuOpen`, and `_UnhandledInput`'s early-out), a "Resize…" wedge appended to `BuildActionsMenu`, and `LevelModelChanged` wired straight to the existing `OnLayerModelChanged` handler — its body (`canvas.SetLevel(...); UpdateState();`) is exactly "refresh canvas + status from current model truth," which is what a resize needs too; no new refresh path was written.

## 6. Verification (Godot MCP, honest gate per #7407)

Driven against the running `level_editor.tscn` (main scene) via injected Godot `Input` actions — `simulate_action`/`simulate_sequence` on the project's own action names, which is the reliable path per the harness notes accumulated in prior sessions (#7466, #7505, #7515): logical-keycode `simulate_key` does not fire physical-keycode-bound actions, so actions were driven at the `Input` layer directly.

**Radial aim**: the Actions radial gained a 9th wedge ("Resize…" at index 8). `RadialGeometry.WedgeDirection`/`IndexAt` (unit-tested, `src/Uberkarl.Editor/Input/RadialGeometry.cs`) gave the exact aim vectors needed: holding `editor_cursor_up`+`editor_cursor_left` together (~315°) resolves to wedge 8 (Resize…); holding `editor_cursor_left` alone (270°) resolves to wedge 7 (Play) — both computed from the shared geometry rather than guessed, then confirmed by screenshot.

**Grow**: loaded the shipped sample level (60×16, 7 tiles, 2 layers). Opened Resize via the Actions radial (screenshot: panel summoned, "Current size: 60 x 16"). Stepped Width to 70 (10× `ui_right`, screenshot confirms stepper reads "70") and Height to 20 (`ui_down` to the Height row, 4× `ui_right`, screenshot confirms "20"), then Apply — applied **immediately**, no confirm (pure grow never crops): `LevelResizePanel: resized level to 70x20.` printed, "Current size: 70 x 20", canvas screenshot shows every existing mountain/floor/platform tile preserved at its original coordinate with new empty margin added on the right and bottom (top-left anchored, exactly as designed). `get_editor_errors` **0**.

**Playtest camera bounds**: hit Play (Actions radial, wedge 7) on the still-unsaved 70×20 buffer — `LevelEditor: playtesting 70x20 level 'demo'.` The player spawned and could move; a painted wall tile the sample level already has at its *old* right edge (column 59) physically blocks further walking past it — that is level *content*, not a resize artifact, so instead of relying on walking to the true new edge, the `Camera2D` node was inspected directly via `get_game_node_properties`: **`limit_right = 1120` (= 70 × 16), `limit_bottom = 320` (= 20 × 16)** — exactly the resized grid, confirming `PlayRuntimeBuilder.AttachCamera` picked up the new dimensions with zero code changes (§3). Returned via `ui_cancel`.

**Save → reload**: `editor_save` (writes to `content/sample.pkg`, backed up beforehand and restored after — repo hygiene, same pattern #7505 established) — `LevelEditor: saved 3023 bytes to .../content/sample.pkg.` Stopped and re-played the scene (forces a fresh disk load, not an in-memory reuse): `LevelEditor: loaded 70x20 level 'demo' ... from .../content/sample.pkg.` — new dimensions round-tripped through a real save/reload. `get_editor_errors` **0**.

**Shrink + confirm-on-data-loss**: on the reloaded 70×20 level, opened Resize again and stepped Width down to 31 (well inside the original painted 60-wide content — screenshot confirms "31"). First Apply press: **did not resize** — button relabeled "Confirm Resize? (crops painted tiles)" (screenshot), no `resized` log line, size still "70 x 20". Second Apply press: `LevelResizePanel: resized level to 31x20.` — screenshot confirms the level visibly cropped to 31 columns (mountains/floor beyond column 31 gone, everything within bounds intact). `get_editor_errors`: only the MCP harness's own transient-script diagnostics from an unrelated `execute_game_script` probe earlier in the session (established harness-only noise per #7440's audit) — zero project/`game`/`src` errors.

**Gamepad + keyboard parity**: `DimensionStepper` reads `ui_left`/`ui_right`/`ui_accept`/`ui_cancel` — the same built-in Godot actions `LayerManagerPanel`'s `ScrollStepper` already uses, which #7466 established (and fixed) carry both keyboard and gamepad (D-pad/stick, A/B) bindings in this project's `project.godot`. The Actions-radial trigger (`editor_menu_actions`) and cursor-aim actions (`editor_cursor_*`) explicitly carry both `InputEventKey` and `InputEventJoypadButton`/`InputEventJoypadMotion` entries (confirmed by reading `project.godot` directly). No new input bindings were added by this change — resize rides entirely on already-dual-bound actions. **Real-pad confirmation on physical hardware is Toni's** — the harness injects `Input` actions programmatically, not literal joystick hardware.

**Repo hygiene**: `content/sample.pkg` was written to once (the grow save) during verification; restored via `git checkout -- content/sample.pkg` afterward, so the PR diff for that file is empty.

## 7. §6 Audit

- Comment-grep (`TODO`/`FIXME`/`HACK`/`XXX` + commented-out code) on every changed/new file: **0**.
- `dotnet build Uberkarl.csproj`: **0 warnings / 0 errors** (includes the `game/` Godot-glue compile set).
- `Uberkarl.Editor.Tests`: **208/208** (was 185, **+23** — `GridDimensionRules` step/clamp, `EditableLevel.WouldDropPaintedCells`/`Resize` grow/shrink/multi-layer/no-op/invalid-input, `LevelEditSession.Resize` history-clear/no-op/save-reload-round-trip/runtime-loader-round-trip, `MenuOutcome.OpenResizePanel` routing). `Uberkarl.Content.Tests`/`Uberkarl.Packages.Tests` unaffected — no changes in `Uberkarl.Content`/`Uberkarl.Packages`, not run (scope discipline; the schema needed no changes — see §3).
- Changed files: `src/Uberkarl.Editor/EditableLevel.cs`, `src/Uberkarl.Editor/LevelEditSession.cs`, `src/Uberkarl.Editor/Input/MenuOutcome.cs`, `game/Editor/LevelEditor.cs`. New files: `src/Uberkarl.Editor/GridDimensionRules.cs`, `game/Editor/LevelResizePanel.cs` (+ its Godot-generated `.uid`), `tests/Uberkarl.Editor.Tests/LevelResizeTests.cs`, this document.

## 8. Open Questions for Toni

1. `GridDimensionRules.MaxDimension` (500) is an arbitrary soft cap on the stepper — worth exposing/tuning, or fine as an invisible ceiling?
2. Spawns are not reconciled on shrink (a spawn cropped out of bounds would fail the runtime loader's bounds check on next real play load, though not on save or in-editor playtest, which don't validate spawn bounds) — the editor has no spawn-placement UI yet at all, so this could not be exercised end-to-end. Worth a guard now, or defer until spawn placement itself lands?
3. Resize is not undoable this increment (matches layer-editing's precedent) — worth generalizing the undo contract to cover structural ops at some point, or is confirm-before-destructive-action enough?
