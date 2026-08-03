# Architectural Document: Level-Editor Layer Editing (create / delete / reorder + properties)

Source task: DiVoid #7501 · Project #7396 (Uberkarl) · Depends on: level model v0.2 #7420, editor MVP #7433, editor UI v2 #7466, package browser #7470 · Vision #7407.

Status: design. Implementation, unit tests, Godot-MCP verification, and the PR are the implementation chain's work (see §14). This document is the blueprint.

---

## 1. Problem Statement

The editor can paint tiles onto a *fixed* set of layers, but authors cannot yet shape the layer stack itself. To build real levels — a slow parallax backdrop, the world-locked collision layer, a foreground — authors need to **create, delete, and reorder layers** and **edit each layer's three properties** (`collision`, `scrollSpeed`, `repeat`). The whole flow must be **gamepad-first** (the editor's input paradigm is already gamepad-native), and must **guide** authors away from invalid property combinations rather than only rejecting them at load time.

Success criteria:
- An author can add a layer, delete a layer, and change draw order (back↔front), entirely on a gamepad.
- An author can toggle `collision`/`repeat` and step `scrollSpeed` through preset values (no free numeric/text entry).
- The UI makes the schema invariants unreachable rather than surfacing them as load errors.
- New layers + edited properties **persist**: save → reload reproduces them, and the runtime *behaves* accordingly (parallax scrolls, collision collides, order = draw order).

## 2. Scope & Non-Scope

**In scope**
- Layer CRUD: create, delete, reorder (adjacent move = draw-order change).
- Property editing: `collision` (toggle), `scrollSpeed` (preset stepper), `repeat` (toggle).
- Auto-naming new layers ("Layer N").
- A single gamepad-operable **Layer Manager** surface, summoned from the existing Layers radial.
- Invariant *guidance* in the UI (a collision layer stays `scrollSpeed == 1.0` and non-`repeat`).
- Engine-agnostic, unit-tested layer-mutation + invariant-guard + naming + preset-ladder logic in `src/Uberkarl.Editor`.

**Explicitly out of scope**
- Layer **rename** (needs gamepad text entry — deferred exactly as Save-As naming is; auto-name only).
- Tile scalability / tile-set editing (#7450).
- The Pooscript behavior loop (Phase 2b).
- Browser polish (#7500).
- Any layer property the v0.2 schema does not have (#1184) — only `collision`/`scrollSpeed`/`repeat`.
- Undoable layer operations (structural changes are **not** on the undo stack this increment — see §9.3; noted as a future seam).
- Free numeric or text entry of any kind.

## 3. Assumptions & Constraints

| # | Assumption / Constraint | Confidence |
|---|---|---|
| A1 | Layer counts stay small ("few layers now"), so a linear vertical control list is acceptable for the panel. | High (stated in #7501/#7450). |
| A2 | Persistence needs **no schema change**: `EditableLevelWriter`/`Reader` + `LayerDefinition` already round-trip the full layer array and all three properties. | Verified in code. |
| A3 | The editor canvas is a **flat** authoring view (`TileMapLevelBuilder.BuildEditable` — no `Parallax2D`, collision off). Parallax/collision behaviour is a **play-scene** concern (`Build`). | Verified in code. |
| A4 | The proven summoned-window pattern (dim backdrop, centered panel, focus-contained vertical chain, `ui_accept`/`ui_cancel`, cursor-freeze via `CursorInputGate`) from `PackageBrowser` is the right vehicle for a variable-length, multi-control surface. | High. |
| A5 | The schema invariant is one-directional and collision-dominant: `collision ⇒ scrollSpeed == 1.0 ∧ ¬repeat`. A non-collision layer may take any preset speed and may repeat. | Verified in `LevelLoader.ValidateLayer`. |
| A6 | A level must always have **≥ 1 layer** (grids are per-layer; a zero-layer level is meaningless for painting). | Design decision; loader does not forbid empty layer lists but the editor should. |
| C1 | No new device bindings should be added if the feature can be reached through existing triggers — the input map churn is a known bug magnet (#7449/#7466). | High. |

## 4. Architectural Overview

The feature is **model-mutation + one UI surface**, layered exactly on the existing seams. No new persistence, no new rendering path, no new input action.

```
  Layers radial (RB hold)                         [ existing surface, gains ONE wedge ]
        │  "Manage…" wedge
        ▼
  MenuOutcome.OpenLayerManager  ──►  LevelEditor (controller)  ──►  summons
        │                                    │
        │                                    ▼
        │                        ┌──────────────────────────┐
        │                        │   LayerManagerPanel       │  game/Editor  (Godot Control)
        │                        │   (summoned-window)       │  reuses PackageBrowser pattern
        │                        └────────────┬─────────────┘
        │                                     │ intent calls
        ▼                                     ▼
  ┌───────────────────────────────────────────────────────────────┐
  │  LevelEditSession  (façade)                                    │  src/Uberkarl.Editor
  │   + AddLayer / DeleteLayer / MoveLayer / SetLayerProperties…   │  (engine-agnostic, unit-tested)
  │                     │                                          │
  │                     ▼                                          │
  │  EditableLevel   (owns mutable layer list)                    │
  │   + AppendLayer / RemoveLayerAt / MoveLayer / SetLayerProps    │
  │                     │ coerces through                          │
  │                     ▼                                          │
  │  LayerPropertyRules · ScrollSpeedLadder · LayerNaming (pure)   │
  └───────────────────────────────────────────────────────────────┘
                        │ after any layer op
                        ▼
  EditableLevelSnapshot ──► ResolvedLevel ──► EditorCanvas.SetLevel (full rebuild, flat)
                        │
                        ▼ on Save (unchanged path)
  EditableLevelWriter ──► .pkg  ──►  play scene Build ⇒ parallax + collision behaviour
```

Three engine-agnostic pure helpers (`LayerPropertyRules`, `ScrollSpeedLadder`, `LayerNaming`) carry the *rules*; `EditableLevel` gains the *structural mutations*; `LevelEditSession` exposes them as *intents*; one Godot `Control` (`LayerManagerPanel`) is the *surface*. Everything below the session is unit-testable without Godot.

## 5. Components & Responsibilities

### 5.1 `LayerPropertyRules` — pure invariant guard (NEW, `src/Uberkarl.Editor`)
- **Owns:** the single authoritative encoding of `collision ⇒ scrollSpeed == 1.0 ∧ ¬repeat`.
- **Does:** given a *proposed* `(collision, scrollSpeed, repeat)` triple, returns a **coerced valid** triple plus which fields were forced. Collision is dominant: enabling it snaps `scrollSpeed → 1.0` and `repeat → false`.
- **Also answers:** "are `scrollSpeed`/`repeat` editable for this layer?" → false when `collision` is on. This is the seam the UI reads to disable/grey controls.
- **Does NOT own:** any Godot type, any persistence, any decision about *when* a set happens.

### 5.2 `ScrollSpeedLadder` — pure preset stepper (NEW, `src/Uberkarl.Editor`)
- **Owns:** the ordered preset ladder `{0.25, 0.5, 0.75, 1.0, 1.5, 2.0}` (the exact set is a §13 open question).
- **Does:** step up / step down from a current value with **clamp at the ends** (not wrap — a magnitude ladder reads more predictably clamped); and **snap** an arbitrary loaded value to the nearest preset for display.
- **Does NOT own:** the world-locked `1.0` special case for collision layers (that is `LayerPropertyRules`' job; the ladder is only reachable on non-collision layers).

### 5.3 `LayerNaming` — pure auto-namer (NEW, `src/Uberkarl.Editor`)
- **Owns:** "Layer N" generation.
- **Does:** given the current layer names, return the next unused `"Layer N"` (smallest N ≥ 1 that is unique). Deterministic, so tests pin it.
- **Does NOT own:** rename (deferred).

### 5.4 `EditableLayer` — authoring layer (MODIFIED, `src/Uberkarl.Editor`)
- **Change:** `Collision`/`ScrollSpeed`/`Repeat` stay **immutable get-only**; property edits are done by **replacing the layer instance** at its index with a new one that **reuses the same `Cells` array** (§9.4). `Cells` remains mutated-in-place by `SetCellCommand` as today.
- **Rationale:** keeps `EditableLayer` immutable (its current contract) while letting the level swap in a coerced copy; because `SetCellCommand` re-reads `level.Layers[i].Cells` each apply/revert, reusing the array keeps recorded cell history valid across a property edit.

### 5.5 `EditableLevel` — authoring model (MODIFIED, `src/Uberkarl.Editor`)
- **Change:** back `Layers` with a mutable `List<EditableLayer>` (still exposed as `IReadOnlyList`), and add structural mutations:
  - **AppendLayer(name, collision, scrollSpeed, repeat):** allocates a full `Width*Height` empty-cell array, coerces the properties through `LayerPropertyRules`, appends. New layers default **`collision:false, scrollSpeed:1.0, repeat:false`** (a display layer — the common "add a backdrop/foreground" case; contrast `CreateBlank`, which seeds a collision `terrain`).
  - **RemoveLayerAt(index):** removes, refusing to drop the **last** layer (returns a rejected result / throws a guard exception the session translates to a no-op).
  - **MoveLayer(index, direction):** swaps with the adjacent layer (clamped at the ends) — this *is* the draw-order change.
  - **SetLayerProperties(index, collision, scrollSpeed, repeat):** coerces via `LayerPropertyRules`, replaces the layer instance (reusing its `Cells`).
- **Invariant maintained:** every layer always holds exactly `Width*Height` cells (constructor already asserts this; mutations must not break it).
- **Does NOT own:** history, dirty tracking, canvas refresh (all the session/controller's job).

### 5.6 `LevelEditSession` — façade (MODIFIED, `src/Uberkarl.Editor`)
- **Change:** add intent methods mirroring the model mutations: `AddLayer()`, `DeleteLayer(index)`, `MoveLayer(index, direction)`, `SetCollision(index, bool)`, `StepScrollSpeed(index, direction)`, `SetRepeat(index, bool)`.
- **Each method:** performs the mutation, sets `IsDirty`, and (for **index-shifting** ops — delete/move) **clears the cell-edit history** (§9.3) and returns enough for the controller to reconcile the active layer.
- **Return shape:** a small `LayerEditResult` value carrying: did-it-happen (some ops are no-ops, e.g. delete-last, move-at-end, already-that-value), and the **new index of the affected layer** (so the controller can keep the active layer selected across a move). No `CellChange` — layer ops do not map to a single cell.
- **Rationale:** keeps the model authoritative and every layer op on one intent path, exactly as paint/erase/undo/redo already are.

### 5.7 `MenuOutcome` / `MenuOutcomeKind` — routing seam (MODIFIED, `src/Uberkarl.Editor`)
- **Change:** add `MenuOutcomeKind.OpenLayerManager` and a payload-less `MenuOutcome.OpenLayerManager()` factory.
- **Rationale:** the "Manage…" wedge routes as a device-neutral outcome, dispatched by the controller to summon the panel — the same wedge→intent seam tiles/layers/actions already use. **No new `EditorAction`** is introduced, so the `EditorActionMap` completeness test is untouched and no new device binding is required (C1).

### 5.8 `LayerManagerPanel` — the surface (NEW, `game/Editor`, Godot `Control`)
- **Owns:** presentation and gamepad interaction only. Holds no edit logic; drives the session.
- **Does:** render the layer stack as a focus-contained vertical control chain, translate `ui_accept`/`ui_left`/`ui_right`/`ui_cancel` into session intent calls, and after every mutation ask the controller to refresh (re-snapshot the canvas + rebuild its own rows + reconcile active layer + update status).
- **Reuses:** the `PackageBrowser` scaffolding verbatim — full-rect dim `ColorRect` backdrop, centered `PanelContainer`, `ContainListFocus`-style neighbour pinning, grab-focus-on-summon, `ui_cancel` closes, `ZIndex` above the canvas.
- **Does NOT own:** the invariant (asks `LayerPropertyRules`), file IO, or the model.

### 5.9 `LevelEditor` — controller/composition root (MODIFIED, `game/Editor`)
- **Change (minimal):**
  - Add a `"Manage…"` wedge to `BuildLayersMenu()` (routes `MenuOutcome.OpenLayerManager()`).
  - Handle `OpenLayerManager` in `Dispatch` → `SummonLayerManager()`.
  - Construct/host `LayerManagerPanel`; on any panel mutation, call the existing refresh path (`canvas.SetLevel(EditableLevelSnapshot.ToResolvedLevel(session.Level))` + palette/layer/status refresh) and reconcile `activeLayerIndex`.
  - Extend the three existing "a modal owns input" guards to include `layerManager.IsOpen`: the `CursorInputGate.DirectionCaptured(...)` call in `_Process`, the `menuOpen` check in `UpdateReveals`, and the early-out in `_UnhandledInput`.
- **Rationale:** the controller stays glue; all three guard sites are the exact places `packageBrowser.IsOpen` is already OR-ed in, so the change is mechanical and symmetric.

## 6. Interactions & Data Flow

### 6.1 Summon
`RB` held → Layers radial opens (existing) → author aims at the new **Manage…** wedge → release commits `MenuOutcome.OpenLayerManager()` → `LevelEditor.Dispatch` → `SummonLayerManager()` → panel becomes visible, grabs focus, `CursorInputGate.DirectionCaptured` goes true so the grid cursor underneath freezes.

### 6.2 A property edit (e.g. make a parallax backdrop)
1. Author focuses the target layer's **Collision** toggle, presses `ui_accept` to turn it **off**.
2. Panel calls `session.SetCollision(index, false)`; the coercion leaves scroll/repeat untouched (they were already valid), but now the **Scroll** stepper and **Repeat** toggle become **enabled** (`LayerPropertyRules` says editable).
3. Author focuses the **Scroll** stepper, presses `ui_left`/`ui_right` to step to `0.5`; panel calls `session.StepScrollSpeed(index, −1…)`.
4. Author focuses **Repeat**, `ui_accept` to turn it **on**; `session.SetRepeat(index, true)`.
5. After each call: controller re-snapshots → `canvas.SetLevel(...)` (flat rebuild); panel rebuilds its rows (reflecting enabled/disabled state + new values); status line updates; `IsDirty` set.

### 6.3 Turning collision ON (the guided coercion)
Author toggles Collision **on** on a `0.5`-speed repeating layer → `session.SetCollision(index, true)` → `LayerPropertyRules` coerces `scrollSpeed → 1.0`, `repeat → false` → panel rebuild shows scroll snapped to `1.0`, repeat off, both controls now **disabled/greyed** with an inline hint ("collision layers are world-locked and non-repeating"). The author is *guided*, never shown a load error.

### 6.4 Create / delete / reorder
- **Add:** `session.AddLayer()` → new `"Layer N"` appended at the front-of-array end → canvas rebuild shows the empty layer; it becomes selectable immediately.
- **Delete:** `session.DeleteLayer(index)` → refused (no-op) if it is the last layer; otherwise removed, **cell-edit history cleared**, active layer reconciled (clamp), canvas rebuild.
- **Reorder:** `session.MoveLayer(index, up/down)` → adjacent swap → **draw order changes** → history cleared → active layer follows the moved layer (result carries its new index) → canvas rebuild (flat z-order reflects the new array order; the saved package reflects it in the play scene).

All interactions are **synchronous, single-threaded** (editor input loop). No events cross a process/thread boundary.

## 7. Data Model (Conceptual)

No schema change. The conceptual entities are unchanged from level model v0.2:

- **Level** owns an **ordered list of Layers** (array order = draw order, back→front) and a tile palette.
- **Layer** = `{ name, collision, scrollSpeed, repeat, cells[W*H] }`.
- **Invariant** (unchanged, enforced at load, now *guided* at author time): `collision ⇒ scrollSpeed == 1.0 ∧ ¬repeat`.

The only model *behaviour* that changes: the layer list becomes **mutable in the authoring model** (create/delete/reorder/property-set), where before it was fixed at load.

## 8. Contracts & Interfaces (Abstract)

### 8.1 `LayerPropertyRules` (pure)
| Query | Input | Output / Semantics |
|---|---|---|
| Coerce | proposed `(collision, scrollSpeed, repeat)` | valid triple; if `collision` true → `scrollSpeed = 1.0`, `repeat = false`; else unchanged. Also reports which fields were forced. |
| Editable | a layer's `collision` | `scrollSpeed`/`repeat` are editable iff `collision` is false. |

### 8.2 `ScrollSpeedLadder` (pure)
| Query | Input | Output / Semantics |
|---|---|---|
| Step | current value, direction | next preset up/down, **clamped** at ends. |
| Snap | arbitrary value | nearest preset (for displaying a loaded non-preset value). |

### 8.3 `LayerNaming` (pure)
| Query | Input | Output |
|---|---|---|
| Next | existing names | smallest unused `"Layer N"`, N ≥ 1. |

### 8.4 `LevelEditSession` layer intents
| Method | Effect | History | Returns |
|---|---|---|---|
| AddLayer() | append display layer `"Layer N"` (`false,1.0,false`) | preserved (append is index-stable) | new layer index |
| DeleteLayer(i) | remove; **no-op if last layer** | **cleared** (indices shift) | happened?; reconciled active index |
| MoveLayer(i, dir) | adjacent swap; no-op at end | **cleared** (indices shift) | new index of moved layer |
| SetCollision(i, b) | coerced property set | preserved (index-stable) | happened? |
| StepScrollSpeed(i, dir) | ladder step (ignored if collision) | preserved | happened? |
| SetRepeat(i, b) | coerced property set (ignored if collision) | preserved | happened? |

Invariants of the contract: every method is a **no-op-safe intent** (invalid/blocked calls return "did not happen" rather than throwing across the UI boundary, mirroring `PaintCell` returning `null`); every mutation sets `IsDirty`; the layer list is never left empty; every layer always has `Width*Height` cells.

### 8.5 `LayerManagerPanel` ⇄ controller
| Direction | Signal | Payload | Semantics |
|---|---|---|---|
| panel → controller | LayerModelChanged | (none) | "I mutated the model; refresh canvas + reconcile active layer + status." |
| panel → controller | ActiveLayerChosen | layer index | author picked a layer to paint on (parity with the Layers radial pick). |
| panel → controller | Closed | (none) | dismissed; return focus to canvas. |

The panel calls the session directly for mutations (it holds the session reference, as the browser holds the source); it raises `LayerModelChanged` so the controller owns the canvas rebuild — the panel never touches the canvas or the builder.

## 9. Cross-Cutting Concerns

### 9.1 Invariant guidance (the "guide, don't error" requirement)
`LayerPropertyRules` is the **one** place the invariant lives on the author side (the loader remains the enforcement backstop). The UI expresses it structurally: **collision is dominant**, and while it is on the `scrollSpeed` and `repeat` controls are **disabled/greyed with a hint**, so the invalid combination is *unreachable*, not *rejected*. To author a parallax layer, the author turns collision off first — a natural, discoverable order.

### 9.2 Persistence & consistency
Unchanged and already complete (A2). `Save()` serializes the current layer list + properties via the existing writer; reload via the existing reader reproduces them. There is **no** partial-write or migration concern — a layer op only mutates in-memory model state; bytes are written only on explicit Save.

### 9.3 Undo/redo interaction — the layer-index aliasing hazard (key decision)
Recorded `SetCellCommand`s store an absolute `layerIndex`. **Delete** and **reorder** shift indices, so replaying old cell commands after such an op would paint/revert on the *wrong* layer. Decision:
- **Delete and reorder CLEAR the cell-edit history** (the same `history.Clear()` already used on load/save-as). Safe and simple.
- **Add (append) and property-set do NOT shift indices** → history is preserved.
- **Layer operations are themselves not undoable this increment.** Making them undoable requires generalising the command return type beyond `CellChange` and interleaving structural + cell commands safely — deliberately deferred; noted as a future seam. This matches the anti-complexity mandate and the "few layers, deliberate ops" reality.

### 9.4 Property edit without disturbing paint history
A property set **replaces the `EditableLayer` instance but reuses its `Cells` array**. Because `SetCellCommand.Apply/Revert` resolve `level.Layers[i].Cells` fresh each time, recorded cell history keeps hitting the same array → paint undo still works across a property edit. (This is why property edits can safely preserve history while delete/reorder cannot.)

### 9.5 Active-layer reconciliation
The controller's `activeLayerIndex` must stay valid and, ideally, keep pointing at the *same* layer across a move. Rules: after **delete**, clamp to `[0, count−1]`; after **move**, follow the moved layer to its new index (from the result); after **add**, optionally auto-select the new layer (recommended — the author just made it to work on it).

### 9.6 Cursor freeze / input ownership
The panel must freeze the grid cursor exactly as the radial and browser do: OR `layerManager.IsOpen` into the `CursorInputGate.DirectionCaptured(...)` argument, the `UpdateReveals` `menuOpen` guard, and the `_UnhandledInput` early-out. This guarantees a stick/D-pad press inside the panel never leaks to the canvas.

### 9.7 Focus containment
Reuse the `PackageBrowser`/toolbar neighbour-pinning technique: every focusable control in the panel pins its horizontal neighbours (and `FocusNext`/`Previous`) to self or to a sibling in the vertical chain, so a stick/D-pad aim cannot bounce focus onto the full-rect canvas underneath. The `scrollSpeed` stepper is the one control that **wants** `ui_left`/`ui_right`: it consumes them (in its own `_GuiInput`) to step the value, which is safe precisely because horizontal focus movement is pinned away.

### 9.8 Observability / errors
Follow the existing editor convention: mutations print a concise `GD.Print` line (e.g. "added Layer 3", "moved terrain to front", "deleted decor"); blocked ops (delete-last) print nothing or a soft note and no-op. No exceptions cross into the UI. `get_editor_errors` must stay clean.

## 10. Quality Attributes & Trade-offs

| Attribute | How the design addresses it |
|---|---|
| **Testability** | All rules (coercion, ladder, naming) and all mutations (add/delete/move/set + history-clear behaviour + active-index reconciliation) live below the Godot boundary and are unit-tested in `src/`. |
| **Simplicity** | No new persistence, no new render path, no new device binding, no new command type. One new UI Control reusing a proven pattern; three tiny pure helpers; a handful of session/model methods. |
| **Consistency** | Layer ops route through the same intent-façade + device-neutral-outcome seams as every existing editor action. |
| **Gamepad-first** | Steppers/toggles only (no numeric/text entry); reuse of the focus-contained `ui_accept`/`ui_cancel` window that is already proven on a pad. |
| **Robustness** | Invalid combinations are structurally unreachable; index-aliasing is defused by clearing history on shifting ops; the layer list can never go empty. |

**Trade-offs made (and alternatives rejected):**
- **Summoned panel over extending the radial into a management tool.** A radial excels at quick directional selection among a few items; layer *management* needs a persistent multi-control, reorderable list. Rejected: cramming per-layer property controls + reorder into radial wedges (unreadable, no room for steppers). Chosen: keep the radial for *selection*, add one "Manage…" wedge that summons a list panel.
- **Layer ops not undoable.** Rejected: generalising the command/history to interleave structural and cell edits (a real ripple through the `CellChange`-typed command contract and its tests) — not worth it for infrequent, deliberate ops now. Chosen: clear cell history on shifting ops; revisit when it hurts.
- **Collision-dominant lock over free-form fields + validation error.** Rejected: letting the author set any combo and erroring on save. Chosen: disable dependent controls while collision is on — the invariant becomes a UI affordance, not a failure mode (directly satisfies "guide, don't just error").
- **Vertical control chain over master-detail inspector.** Chosen for A1 (few layers) and to reuse the exact proven focus-containment technique. Master-detail is the noted scalability refinement when layer counts grow.
- **New layers default to display (non-collision).** Chosen because "add a layer" almost always means adding a backdrop/foreground; the collision layer usually already exists. The author can toggle collision on if needed.

## 11. Risks & Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Recorded cell-undo aliases the wrong layer after delete/reorder | Corrupted edits | Clear cell-edit history on index-shifting ops (§9.3). |
| Active layer index dangles after delete/move | Paint on wrong/no layer | Reconcile from the op result (§9.5). |
| Level ends up with zero layers | Nothing to paint; broken save | Refuse to delete the last layer (guard in model + session). |
| Stick/D-pad leaks from panel to canvas | Stray paint / cursor drift | OR `layerManager.IsOpen` into the three existing modal guards + focus containment (§9.6–9.7). |
| A loaded layer has a non-preset `scrollSpeed` (hand-authored pkg) | Stepper can't represent it | `ScrollSpeedLadder.Snap` to nearest preset for display; stepping proceeds from the snapped value. Round-trip note in §13. |
| Editor canvas shows layers flat, so a reviewer "can't see parallax" in the editor | False negative in verification | Verify parallax/collision in the **play scene** with the saved pkg, not the editor canvas (A3, §12). |
| New input churn re-triggers gamepad-activation bugs (#7449) | Regression | No new device binding (C1) — the panel rides existing `ui_accept`/`ui_cancel`; the wedge rides the existing Layers trigger. |

## 12. Verification Strategy (for the implementer / QA)

Two-plane verification, because authoring and behaviour render on different planes (A3):

**Authoring plane (editor scene, Godot MCP + gamepad/keyboard injection per #7407 method):**
1. Summon the Layer Manager from the Layers radial's Manage… wedge (screenshot the panel).
2. **Create** a layer → auto-named "Layer N" (screenshot); paint on it.
3. Make it a **parallax backdrop**: collision off, `scrollSpeed 0.5`, repeat on — observe scroll/repeat controls enable when collision goes off; observe them lock/grey when collision goes back on (guided coercion).
4. Ensure a **collision** layer exists (collision on → scroll snaps 1.0, repeat off, both locked); paint on it.
5. **Reorder** → confirm the flat draw order (child/z-order) changes in the editor.
6. **Delete** a spare layer; confirm delete-last is refused.

**Behaviour plane (play scene with the saved package):**
7. **Save → reload** the package into the editor → layers + properties reproduce exactly (names, collision, scrollSpeed, repeat, order).
8. Run the saved package in the **play scene**: the `0.5` backdrop **scrolls slower** than the world (parallax), the collision layer **collides** (player stops), and the reordered stack **draws** in the new order.

**Every plane driven via injected `ui_*` / `InputEventJoypadButton` + keyboard.** `get_editor_errors` clean on a clean run. **Real-pad confirmation is Toni's** — the harness injects input; state that explicitly and never claim a real-pad "verified".

**Unit tests (`tests/Uberkarl.Editor.Tests`, no Godot):**
- `LayerPropertyRules`: collision-on forces `1.0`/no-repeat; collision-off leaves values; editability query truth table.
- `ScrollSpeedLadder`: step up/down, clamp at both ends, snap-to-nearest for off-ladder values.
- `LayerNaming`: next-name with gaps, duplicates, empty.
- `EditableLevel`/`LevelEditSession` layer ops: add appends full empty grid + correct default props; delete refuses last + shifts + clears history; move swaps + changes order + clears history + reports new index; property-set coerces + preserves cell history (paint → set property → undo still reverts the paint); active-index reconciliation after delete/move.
- `MenuOutcome.OpenLayerManager` routing (in the radial/menu tests).

**Audit gate (per #7407 §6):** comment-grep (TODO/FIXME/HACK/XXX + commented-out code) on changed files = 0; `dotnet build` 0/0; report Editor test count delta + line/branch coverage of the new classes.

## 13. Open Questions for Toni

1. **Preset ladder values.** Proposed `{0.25, 0.5, 0.75, 1.0, 1.5, 2.0}`. Right set/granularity, or do you want finer steps < 1.0 (depth) and coarser > 1.0?
2. **Off-ladder loaded speeds.** If a hand-authored pkg has e.g. `scrollSpeed 0.6`, the stepper snaps it to the nearest preset **for display** — but should Save then rewrite it to the snapped value (normalising to the ladder), or preserve `0.6` until the author actually steps it? (Recommend: preserve until touched.)
3. **New-layer defaults + insertion point.** New layers default to display (`false, 1.0, false`) and append at the front-of-array end. Prefer inserting **relative to the active layer** instead, and/or a different default?
4. **List orientation.** Show the list **top = back (index 0) → bottom = front** (1:1 with the array, "move up = toward back"), or Photoshop-style **top = front**? (Recommend array-order for a direct mental model; happy to flip.)
5. **Summon point.** Manage… wedge on the **Layers** radial (recommended — keeps it under "layers"), or a "Layers…" wedge on the **Actions** radial instead?
6. **Delete confirmation.** Delete acts immediately (undo not available for layer ops this increment). Want a confirm step for delete, given it is not undoable?

## 14. Implementation Guidance for the Next Agent

Ordered milestones. **No code in this document — these are architectural build units.** Ship as **one PR** against `main` on `feat/layer-editing` (per the one-feature-one-PR rule), committing this design doc at `docs/architecture/layer-editing.md`.

- **M1 — Pure rules (src/, unit-tested first).** `LayerPropertyRules`, `ScrollSpeedLadder`, `LayerNaming`. Land with their tests; they have no dependencies.
- **M2 — Model mutations (src/).** `EditableLevel` structural methods + `EditableLayer` instance-replacement property path, feeding through M1. Maintain the `Width*Height` cell invariant and the ≥1-layer floor.
- **M3 — Session intents + history policy (src/).** `LevelEditSession` layer methods returning `LayerEditResult`; clear cell history on delete/move, preserve on add/property-set; dirty tracking; active-index reporting. Unit-test the aliasing-safety and the paint→property→undo case.
- **M4 — Routing seam (src/).** `MenuOutcomeKind.OpenLayerManager` + factory; test it resolves.
- **M5 — Panel (game/).** `LayerManagerPanel` reusing the `PackageBrowser` scaffolding: vertical focus-contained chain, `ui_accept`/`ui_left`/`ui_right`/`ui_cancel`, disabled/greyed dependent controls + invariant hint, mutation → `LayerModelChanged`.
- **M6 — Controller wiring (game/).** Manage… wedge in `BuildLayersMenu`; `Dispatch` handles `OpenLayerManager`; host the panel; refresh path (`canvas.SetLevel` re-snapshot + active-index reconcile + status); OR `layerManager.IsOpen` into the three existing modal guards.
- **M7 — Godot-MCP verification (§12), both planes.** Screenshots (panel + created parallax layer), save→reload persistence, play-scene parallax/collision/order, errors clean. Honest gate: harness-injected input; real-pad is Toni's.

Suggested chain: **john-backend-dev** for M1–M4 (engine-agnostic `src/` core + tests), then M5–M6 (`game/` glue), then M7 (Godot-MCP verify) — or split M5–M6 to a Godot-facing implementer — then **jenny-qa-reviewer**.

## 15. Changelog
- v1.0 (2026-08-02) — initial design for #7501.
