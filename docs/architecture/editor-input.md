# Architectural Document: Editor Input Architecture (gamepad-first, before more editor UI)

> Toni's steer (verbatim, 2026-08-02): *"some like to use gamepad, so everything has to work with
> that aswell... others did that with pop-in menus on button - whole area available for edit, toolbar
> visible (or window) as soon as you keep something pressed - gamepad friendly, just not sure how we
> would do that with mouse and keyboard. Still, don't burn in desktop ui when users like to use gamepad,
> hard to change afterwards... scripting is only possible with keyboard (except assignment of predefined
> scripts)."*

This document defines how the Uberkarl level editor takes input, so the editor is **input-agnostic** —
gamepad, keyboard, and mouse all operate it with **parity** — and settles this *before* more editor UI
is built on the current desktop-only (mouse-first) assumption. It is deliberately **lean-but-real**: it
ships a paradigm-neutral foundation increment now, and it surfaces the one "hard to change afterwards"
decision (the menu paradigm) for Toni to ratify, with mockups and a recommendation, rather than
building it speculatively.

Companion docs: level editor MVP (`level-editor.md`, #7433) · level model v0.2 (#7420) · vision (#7407).

---

## 1. Problem Statement

The MVP editor (#7433) is operable **only with a mouse**: it maps a pointer to a grid cell for
paint/erase, and every tool/tile/layer choice is a mouse click on a toolbar or list. A gamepad has **no
pointer**, so today the editor is unusable on a gamepad, and each new panel added on the mouse-first
assumption deepens a burn-in that Toni has explicitly flagged as expensive to undo. The goal: make the
editor react to **named intents** rather than raw device input, give gamepad/keyboard a **cursor
stand-in** for the missing pointer, and make the existing panels reachable without a mouse — so that
authoring has **full parity** across all three input methods, and future UI is built on that neutral
base. Success = a person can open the sample level and paint, erase, pick a tile/layer/tool, undo/redo,
and save it using **only a gamepad**, **only a keyboard**, or a mouse, interchangeably.

## 2. Scope & Non-Scope

**In scope (design):**
- An **input-abstraction layer**: named editor actions mapped across gamepad + keyboard + mouse with parity.
- A **canvas grid cursor** for gamepad/keyboard that coexists with mouse hover/click.
- The **menu-paradigm decision** — (a) pop-in / hold-a-button vs (b) persistent focus-navigable — analysed,
  with mockups and a recommendation, **including how the pop-in paradigm maps to mouse + keyboard**.
- The **scripting seam**: scripting is keyboard-only (accepted); predefined-script *assignment* stays gamepad-doable.

**In scope (built now — the foundation increment, §12):**
- The input-abstraction + grid cursor + focus-navigable existing toolbar/palette/layers, proving parity on the current editor.

**Out of scope (explicitly):**
- Building the chosen pop-in paradigm — that is the **next** increment, after Toni ratifies (§13.1). This PR does not build it.
- Building *both* paradigms. Layer editing, tileset editing, the scripting editor itself, full theming polish (all deferred elsewhere).
- On-screen button-glyph hints / rebinding UI / touch input (named as future seams, not built — no speculative bindings, #1184).

## 3. Assumptions & Constraints

| # | Assumption / Constraint | Confidence |
|---|---|---|
| A1 | Desktop Godot 4.7 .NET, C#. Target devices are a keyboard+mouse **and** a standard 2-stick gamepad (Xbox-style button/axis layout). | High |
| A2 | The engine-agnostic core in `src/` must stay Godot-free and unit-tested; device→action binding is engine configuration (`project.godot` InputMap) — the correct Godot-native seam, not something to reinvent. | High |
| A3 | Godot's **Control focus system** (focusable controls + directional/next focus navigation) is the right substrate for making persistent panels gamepad-navigable — no bespoke focus manager. | High |
| A4 | Native `FileDialog` is the weakest surface for gamepad (OS-drawn); acceptable for the foundation, flagged for the future in-engine browser (already #7433 Q5). | Med |
| A5 | The gamepad button budget is tight; infrequent actions (undo/redo/save) get defensible-but-provisional bindings the pop-in menu will refine. Final button assignment is a ratify-time detail (§13). | Med |
| A6 | "Parity" means every authoring operation is *reachable and effective* on each device — not that the ergonomics are identical (a radial menu is not a toolbar). | High |

## 4. Architectural Overview

The design has **three cooperating layers**, two of which already exist in Godot and one small
engine-agnostic addition. The editor logic reacts to **named actions**, never to raw devices.

```
   ┌───────────── DEVICES ─────────────┐     Device → named-action binding lives in Godot's InputMap
   │  gamepad   keyboard   mouse        │     (project.godot [input]). One action, many device events:
   │  (D-pad/    (arrows/   (hover/      │       editor_cursor_right = D-pad-right + Right-stick-X+ + →key
   │   stick/     keys)      click)      │       editor_paint        = A button + Enter + Space
   │   buttons)                          │       editor_cycle_tile_next = RB + E …
   └───────┬───────────┬────────┬───────┘
           │ InputMap resolves events to named actions          ┌───────────────────────────────────┐
           ▼           ▼        ▼                                │  EditorActionMap (engine-agnostic) │
   ┌───────────────────────────────────────────────────┐        │  the ONE place action names live:  │
   │  WHICH SURFACE consumes the action = Godot focus.  │◀───────│  EditorAction ⇄ "editor_*" string. │
   │  Focused EditorCanvas → cursor + paint/erase.      │        │  glue asks it, never hard-codes.   │
   │  Focused ItemList/Button → list/nav + confirm.     │        └───────────────────────────────────┘
   │  Global actions (cycle/undo/redo/save) → controller │
   └───────────────┬───────────────────────────────────┘
                   │ intent (paint/erase/cursor-move/cycle/undo/save)
                   ▼
   ┌──────────────────────────── unchanged spine (#7433) ────────────────────────────┐
   │  LevelEditor controller → LevelEditSession (mutates model, records command)      │
   │      → returns CellChange → EditorCanvas repaints one cell.                      │
   │  Engine-agnostic edit core in src/ is untouched: input feeds the SAME façade.    │
   └─────────────────────────────────────────────────────────────────────────────────┘
```

The key idea: **the InputMap is the device-abstraction, and Control focus is the context router.** The
same D-pad moves the grid cursor when the canvas is focused, and navigates a list when a list is focused
— resolved automatically by *which control has focus*, with no mode flag to maintain. The only new
engine-agnostic code is a tiny action-name registry plus the cursor/selection *logic* (which is pure and
unit-tested); everything device-specific stays in Godot configuration and thin glue.

## 5. Components & Responsibilities

### Engine-agnostic core (`src/Uberkarl.Editor/Input`, unit-tested)

| Component | Owns | Does NOT own |
|---|---|---|
| **EditorAction** (enum) | The device-neutral vocabulary of intents the editor reacts to (move-cursor ×4, paint, erase, cycle tile/layer ±, toggle-tool, undo, redo, save, focus-next). | Which device triggers it; what a handler does. |
| **EditorActionMap** | The single authoritative `EditorAction ⇄ "editor_*"` name binding; completeness/uniqueness invariants. The glue asks it for names — no string literals scattered in Godot code. | Device bindings (those are InputMap config); handler behaviour. |
| **GridCursor** | A cell position `(x,y)` confined to a grid; clamped movement; re-clamp on level resize; reports whether a move actually changed cell (so a no-op at an edge doesn't redraw or "buzz"). | Rendering; how often it moves; device input. |
| **CyclicSelection** | Index arithmetic for prev/next cycling with end-wrap (tile palette, layer list); empty/single-item edges. | What is being selected; UI. |

### Godot glue (`game/Editor`)

| Component | Owns (new/changed) | Does NOT own |
|---|---|---|
| **EditorCanvas** (`Control`) | Focusable authoring surface. Holds a `GridCursor`; polls the held cursor-move actions **only while focused** (initial-delay + repeat clock) so the same D-pad/stick/arrows drive the cursor here but navigate panels elsewhere; handles `editor_paint`/`editor_erase` at the cursor; keeps mouse hover/click and **snaps the cursor to a mouse-clicked cell** so the two stay coherent; draws the cursor cell (bold when focused, dimmed when not). Pins its four directional focus-neighbours to itself so directional input never nudges focus off-canvas. | The edit model; tool state; global actions. |
| **LevelEditor** (controller) | Global actions via `_UnhandledInput` (fire regardless of focus): cycle tile/layer (via `CyclicSelection`, reusing the mouse selection path), toggle tool, undo/redo, save, focus-next; grabs initial focus to the canvas so a gamepad works with no click; wires the canvas's paint/erase events to the session. | Cursor mechanics; device bindings. |
| **project.godot `[input]`** | The 15 `editor_*` actions, each bound to gamepad + keyboard (+ mouse where relevant) events — the device-abstraction table (§8). | Any editor logic. |

**Nothing in `src/Uberkarl.Editor` besides the new `Input/` folder changed**, and the whole edit/undo/
save spine (`LevelEditSession`, `EditHistory`, reader/writer) is untouched — input is a new front-end on
the same façade.

## 6. Interactions & Data Flow

**Cursor move (gamepad/keyboard):** while the canvas is focused, `EditorCanvas._Process` reads the held
`editor_cursor_*` actions, applies a repeat clock (step, initial delay, then a faster stream), and calls
`GridCursor.TryMove` (clamped). A changed cell queues a redraw. The mouse path additionally calls
`GridCursor.MoveTo(clickedCell)` so a click leaves the cursor where the pointer acted.

**Paint / erase:** `editor_paint` (or a mouse click) → `CellPressed(cursorCell)` → controller applies the
**active tool** at that cell (parity with a mouse click). `editor_erase` → `CellErased(cursorCell)` →
controller force-erases (a device convenience with no single-button mouse analogue). Both flow into the
**unchanged** `LevelEditSession.PaintCell/EraseCell` → `CellChange` → one-cell canvas repaint.

**Tile / layer / tool selection:** `editor_cycle_tile_next/prev` and `_layer_next/prev` reach the
controller via `_UnhandledInput`, compute the new index with `CyclicSelection`, update the `ItemList`
selection, and route through the **same handler the mouse uses** (`OnPaletteSelected`/`OnLayerSelected`)
— so gamepad, keyboard, and mouse converge on one code path and one visible selection. `editor_toggle_tool`
flips paint/erase and syncs the toolbar toggles.

**Focus / reachability:** the canvas, palette, layer list, and toolbar buttons are all focusable.
`editor_focus_next` (Tab / gamepad B) walks focus across them; within a focused list or button, Godot's
native directional/confirm navigation selects. Undo/redo/save also have direct actions, and remain
reachable as focusable toolbar buttons. File open/save-as are reached by focusing the toolbar buttons.

**Communication style:** synchronous, single-threaded, in-process — plain C# delegates and action
polling/events. No async, no new cross-boundary machinery. It is an editor UI; simplicity wins.

## 7. Data Model (Conceptual)

No persistent-model change. The only new state is **interaction state**, all transient:
- **GridCursor** `(x, y, width, height)` — the device-neutral pointer stand-in; re-clamped when a level loads/resizes.
- **Active tool / active tile index / active layer index** — already in the controller; now mutated by both mouse and action paths through one handler each.

The action vocabulary (**EditorAction**) and its **name binding** are conceptually part of the *editor's
interaction contract*, not the level model — they live in the editor library, engine-agnostic, so they
can be reasoned about and tested without Godot.

## 8. Contracts & Interfaces (Abstract)

**The action ⇄ device binding table** (the device-abstraction; final bindings are a ratify-time detail, §13):

| EditorAction | Gamepad (Xbox layout) | Keyboard | Mouse | Frequency |
|---|---|---|---|---|
| MoveCursor Up/Down/Left/Right | D-pad + Left stick | Arrow keys | (pointer is the cursor) | very high |
| Paint (primary = active tool) | A | Enter / Space | Left click / drag | very high |
| Erase (force) | X | Delete | *(future: right-click / erase tool)* | high |
| CycleTile Prev / Next | LB / RB | Q / E | Click a palette tile | high |
| CycleLayer Prev / Next | LT / RT | PageUp / PageDown | Click a layer | med |
| ToggleTool | Y | T | Click Paint/Erase | med |
| Undo / Redo | L3 / R3 *(provisional)* | Ctrl+Z / Ctrl+Y | Click Undo/Redo | med |
| Save | Start *(provisional)* | Ctrl+S | Click Save | low |
| FocusNext (canvas ⇄ panels) | B | Tab (Shift+Tab reverse) | Click | low |

**Interface invariants:**
- **EditorActionMap** — every `EditorAction` has exactly one non-empty name; names are unique; a name
  resolves back to its action; an unknown name resolves to nothing. (Unit-tested — the "input-action mapping".)
- **GridCursor** — position is always inside `[0,Width)×[0,Height)`; `TryMove`/`MoveTo` clamp (never wrap,
  never throw) and return whether the cell changed; `Resize` re-clamps into the new grid. (Unit-tested.)
- **CyclicSelection** — `Next`/`Prev` wrap at the ends, return −1 for an empty list, stay put for a single
  item, and normalise an out-of-range current index. (Unit-tested.)
- **EditorCanvas** — directional input while focused moves the cursor and never transfers focus; mouse and
  grid cursor address the *same* cell; the model is only ever mutated through the session (unchanged).

## 9. Cross-Cutting Concerns

- **Context routing without modes:** the "which surface owns the D-pad" problem is solved by Godot focus
  ownership, not a hand-rolled mode flag — the single largest simplicity win. The one seam that needs care
  (directional input stealing focus off the canvas) is closed declaratively by pinning the canvas's four
  directional focus-neighbours to itself.
- **Discoverability / affordance:** a gamepad user cannot see a pointer, so the **grid cursor is always
  drawn** (bold when the canvas is focused, dimmed when a panel is), and the focused panel shows Godot's
  native focus ring. On-screen button-glyph hints are a named future seam, not built.
- **Repeat feel:** held cursor movement uses one initial delay then a faster repeat — the familiar
  text-cursor cadence — so a tap nudges one cell and a hold streams.
- **Error handling / consistency / idempotency:** unchanged from #7433 — all edits still pass through the
  validated session; no-op suppression still keeps a re-touched cell from stacking history; a failed save
  still re-marks dirty. Input adds no new trust surface.
- **Determinism / testability:** all input *logic* that can be engine-agnostic (cursor clamping, selection
  wrapping, the action-name registry) is pure and unit-tested; only device polling and drawing live in glue.
- **Concurrency:** none — single-threaded UI, deliberate.

## 10. Quality Attributes & Trade-offs

- **Parity (primary):** every authoring operation is reachable and effective on gamepad, keyboard, and mouse.
  Verified in-engine on all three (§11).
- **Low burn-in / maintainability:** editor logic depends on named actions, not devices; adding the pop-in
  paradigm later re-binds *how* actions fire without touching *what* they mean. The persistent-panel path and
  a future pop-in path are alternate front-ends over the same action set.
- **Simplicity (KISS):** reuse Godot's InputMap (device abstraction) and focus system (context routing)
  instead of a bespoke input manager; the only new code is ~4 small pure classes plus thin glue. No
  speculative bindings, no rebinding UI, no second paradigm (#1184).

**Trade-offs & rejected alternatives:**
- *A bespoke input-manager / command-router vs. Godot InputMap + focus* — **reused Godot.** A custom router
  would duplicate what the engine already does well and add a layer to maintain; the InputMap *is* the
  device-abstraction and focus *is* the context router.
- *A single "primary" action (mouse-parity) vs. dedicated paint + erase actions* — **both.** Primary
  (`editor_paint`) mirrors a mouse click through the active tool; a dedicated `editor_erase` gives gamepad a
  one-press erase that the single mouse button lacks. Small asymmetry, better ergonomics, still parity.
- *Cursor movement by input-event edges vs. polling in `_Process`* — **polled while focused**, so held D-pad
  and held stick get identical repeat handling and the focus-ownership gate is trivial; paint/erase/cycle stay
  edge-triggered where a single fire is wanted.
- *Grid cursor as engine logic vs. Godot-side only* — **engine-agnostic `GridCursor`.** Clamp/edge behaviour is
  exactly the kind of off-by-one logic worth unit-testing without the engine.
- *Wrap vs. clamp for tile/layer cycling* — **wrap** (a gamepad has no scrollbar to run off; every item stays
  reachable with repeated presses) for selection, but **clamp** for the cursor (a level has hard edges).

## 11. Risks & Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Directional input steals focus off the canvas | High (breaks cursor) | Canvas directional focus-neighbours pinned to self; verified — Up/Down/Left/Right move the cursor, only Tab/B leave. |
| Gamepad button budget too small for every action | Med | Frequent actions on buttons; infrequent (undo/redo/save) on provisional buttons the pop-in menu will absorb; final map is a ratify-time detail (§13). |
| Native `FileDialog` awkward on gamepad | Med | Reachable via focus + confirm for the foundation; in-engine browser is a named future increment (#7433 Q5). |
| Action-name drift between glue and config | Low | One `EditorActionMap` names every action; glue asks it; a unit test enforces completeness/uniqueness. |
| Cursor lost against busy tiles | Low | Bold amber outline + soft fill; dims (not hidden) when focus is on a panel so its position stays visible. |

## 12. The Foundation Increment (built in this PR — paradigm-neutral)

Ships the input-abstraction + grid cursor + focus-navigable existing panels on the **current** editor,
proving parity and de-risking the burn-in **without** committing to a menu paradigm:

1. **Engine-agnostic input core** (`src/Uberkarl.Editor/Input`): `EditorAction`, `EditorActionMap`,
   `GridCursor`, `CyclicSelection` — with unit tests (the action mapping + cursor + cycling).
2. **InputMap** (`project.godot`): the 15 `editor_*` actions bound across gamepad + keyboard + mouse (§8).
3. **EditorCanvas**: grid cursor (move/paint/erase), focus-gated cursor polling, mouse↔cursor coherence,
   cursor draw, directional-focus pinning.
4. **LevelEditor**: global actions (cycle tile/layer, toggle tool, undo/redo, save, focus-next), initial
   canvas focus, erase-at-cursor wiring.

The chosen pop-in paradigm (§13.1) is the **next** increment and is intentionally **not** built here.

## 13. The Menu-Paradigm Decision (for Toni to ratify) — analysis, mockups, recommendation

Toni named two shapes and one open question. Here they are analysed, with mockups, a recommendation, and
the answer to *how the pop-in paradigm maps to mouse + keyboard*.

### Option A — Pop-in / hold-a-button menus (gamepad-first)

The whole area stays canvas. A menu **appears while a button is held** and is dismissed on release,
maximising edit area. On a gamepad this is the console-native idiom (a radial/quick menu around the cursor).

```
   ── hold LB (gamepad) / hold Tab (kbd) / right-click (mouse) ──▶  a quick menu pops in at the cursor:

                    ╭───────  TILES  ───────╮
                    │        ▲ grass         │      Whole screen is canvas underneath.
                    │  brick ◀   ●   ▶ dirt  │      Flick stick / arrows / move mouse to a wedge;
                    │        ▼ water         │      release the hold (or click) to pick. Menu vanishes.
                    ╰───────────────────────╯      Different holds open different menus (tiles / layers / file).
```

**How the pop-in maps to mouse + keyboard (Toni's open question, solved):**

| | Open the menu | Move within it | Commit | Dismiss |
|---|---|---|---|---|
| **Gamepad** | hold LB (tiles) / RB (layers) / Start (file) | right stick / D-pad to a wedge | release the hold, or A | release / B |
| **Keyboard** | hold Tab (tiles), Shift+Tab (layers) — or a key per menu | arrow keys / number keys | release, or Enter | release / Esc |
| **Mouse** | **right-click** (radial context menu at the pointer), or press-and-hold left | move the pointer to a wedge | release / left-click | release outside / Esc |

Plus an **auto-hide toolbar** that **edge-reveals** on mouse (push the pointer to the top edge → the
toolbar slides in) and toggles on a button for gamepad/keyboard — so "whole area is canvas" holds for all
three devices, not just gamepad. This is the concrete answer: *hold-to-open + radial* on gamepad becomes
*right-click radial + edge-reveal toolbar* on mouse and *hold-key / key-per-menu* on keyboard.

- **Pros:** maximum canvas; genuinely gamepad-native; the direction Toni leaned; scales to many tools without a bigger toolbar.
- **Cons:** more custom UI to build (radial widget, edge-reveal); lower first-glance discoverability (menus are hidden until held) — mitigated by button-glyph hints and the always-present toolbar-reveal.

### Option B — Persistent focus-navigable toolbar/panels (current layout, made gamepad-navigable)

Keep the current top-toolbar + left-panel layout; make every control focusable and navigate it with the
D-pad/stick (Godot focus). This is exactly what the **foundation increment already ships**.

```
   ┌ New Open Save … │ Paint Erase │ Undo Redo ─────────────── status ┐   Focus ring moves across
   ├─────────┬───────────────────────────────────────────────────────┤   toolbar / layers / tiles /
   │ Layers  │                                                        │   canvas with Tab / B; the grid
   │ Tiles   │        canvas + grid cursor (bold when focused)        │   cursor lives on the canvas.
   └─────────┴────────────────────────────────────────────────────────┘   Panels always visible.
```

- **Pros:** already built and verified; nothing hidden (high discoverability); zero new custom widgets; familiar desktop shape.
- **Cons:** panels permanently consume edit area; not the maximal-canvas feel Toni described; can feel un-console-like on a gamepad for a big level.

### Recommendation

**Adopt Option A (pop-in / hold-to-open, with the mouse=right-click-radial + edge-reveal mapping above) as
the target paradigm — but ratify-then-build, not build-now.** Rationale: it is the gamepad-first direction
Toni leaned toward, it maximises the canvas that matters most for a level editor, and — critically — it is
the choice that is "hard to change afterwards", so it deserves an explicit ratify. We **do not** bet the
project on it: the foundation increment ships **Option B's mechanics** (persistent, focus-navigable), which
is a strict, always-available subset — even after the pop-in lands, the persistent panels can remain as the
discoverable fallback and the mouse's edge-reveal home. So the sequence is: **ship foundation (B-flavoured)
now → Toni ratifies A → build A next as an alternate front-end over the same action set.** If Toni instead
prefers to keep B as the permanent paradigm, the foundation *is* the finished product and no further input
work is needed. Either way, no rework of the action layer.

### Scripting seam (accepted, noted — not built)

Scripting text authoring is **keyboard-only** (accepted). The seam to preserve: **assigning a predefined
script** to a tile/entity is a *selection*, not text entry — so it routes through the same action/selection
surface (a pop-in picker or a focus-navigable list) and **stays fully gamepad-doable**. Editing script
*source* opens a text field that is keyboard-gated (and simply unavailable, with a clear message, on a
gamepad-only session). Keeping "assign predefined" on the selection surface and "edit source" behind a
keyboard gate is the boundary that honours Toni's note without blocking gamepad authors from wiring up
behaviour.

## 14. Open Questions (for Toni)

1. **Menu paradigm — the ratify (the load-bearing one).** Recommendation: adopt **pop-in / hold-to-open**
   (Option A) as the target, with the mouse = right-click-radial + edge-reveal mapping in §13; foundation
   ships the neutral persistent-panel base now. Confirm A as the next increment, or elect to keep B as the
   permanent paradigm.
2. **Provisional gamepad bindings** for infrequent actions — undo/redo on L3/R3, save on Start, focus on B.
   Fine as placeholders (the pop-in menu will absorb them), or do you want specific buttons now?
3. **Erase idiom on mouse** — keep left-click = active tool (paint or erase via the toggle), or add
   **right-click = erase** on the canvas as a desktop convenience (note: right-click is also the proposed
   pop-in trigger, so these would need to coexist via press-vs-hold)?
4. **Cursor-move on the analog stick** — current step+repeat cadence (initial delay then stream). Good, or do
   you want stick tilt to scale speed (further = faster)?
5. **Toolbar visibility in the future pop-in paradigm** — auto-hide with edge-reveal (max canvas), or a
   persistent thin toolbar with pop-in menus layered on top (discoverability)?

## 15. Verification (Godot MCP) — see §16 audit

Drove the editor's core actions with **simulated gamepad + keyboard** input (not just mouse): moved the grid
cursor (D-pad/stick actions, clamped at edges — no wrap), painted and erased at the cursor, cycled tile and
layer, toggled tool, undo/redo, and saved — via `simulate_action` (gamepad) and `simulate_key` (keyboard).
Screenshots captured the grid cursor and a gamepad/keyboard-driven paint and erase. Editor log carried **no
project errors** (only the MCP harness's own `mcp_input_service.gd` key-lookup lines, which are not project
code). Engine-agnostic input logic unit-tested: **Editor 34/34** (was 21 → +13 input tests). `dotnet build`
0/0.

---

## 17. Next increment — the ratified pop-in paradigm (Phase 2a.2)

Toni **ratified Option A** (pop-in / hold-to-reveal) on 2026-08-02, with the mouse = right-click-radial +
edge-reveal mapping from §13. That paradigm is built as the additive next increment (DiVoid #7441) over this
foundation — same `EditorAction` set, no rework of the edit/undo/save spine. Its full design (the radial
menu core, the three input mappings, the auto-hide/edge-reveal layout, and the resolution of the four
follow-ups in §14) lives in its own document: **`editor-popin-menus.md`**. This foundation's persistent
focus-navigable panels remain there as the auto-hidden, edge-revealed discoverable fallback.
