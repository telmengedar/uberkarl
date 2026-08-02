# Architectural Document: Pop-in / Hold-to-Reveal Editor Menus (Phase 2a.2)

> Paradigm **ratified by Toni** (2026-08-02, DiVoid #7440): pop-in / hold-to-reveal, gamepad-first, mouse
> and keyboard mapped to the same idiom. This document specifies the increment that builds it — additive
> over the merged input foundation (PR #9, DiVoid #7439/#7440), reusing the same `EditorAction` set with no
> rework of the edit/undo/save spine.

Companion docs: input architecture (`editor-input.md`, #7440 — especially §13, the ratified mapping) ·
level editor MVP (`level-editor.md`, #7433). Source task: DiVoid #7441.

---

## 1. Problem Statement

The foundation made the editor operable on gamepad, keyboard, and mouse with parity, but kept the
**mouse-first persistent toolbar + left panel** — panels that permanently consume edit area and are not the
"whole area is canvas" feel Toni wants. This increment replaces the *selection surfaces* (tiles, layers,
file/history actions) with a **pop-in radial menu** that appears only while a trigger is held and dismisses
on release/confirm, and makes the toolbar/panel **auto-hide and edge-reveal**, so the whole area is the edit
canvas by default. Success = a person can summon a menu, pick a tool/tile/layer/file-op from it, and have it
apply — on **gamepad, keyboard, or mouse** — with the canvas maximised the whole time, and with no
regression of the parity the foundation shipped.

## 2. Scope & Non-Scope

**In scope (built):**
- A **radial pop-in menu** surface (overlay control) driven by a device-neutral menu model.
- Three menus — **Tiles**, **Layers**, **Actions** (file ops + undo/redo + tool toggle) — plus a **mouse
  context** radial.
- The three input mappings (gamepad hold-shoulder/Start, keyboard hold-key, mouse right-hold), the
  press-vs-hold discriminator, and **routing every pick through the existing operations**.
- **Auto-hide toolbar + auto-hide layer/tile panel** with edge-reveal (mouse) and focus-reveal (gamepad/kb).
- Settling the four follow-ups left from #7440 (§9).

**Out of scope (unchanged / deferred):** the edit model, command/undo history, save round-trip, and
`LevelEditSession` (untouched — the pop-in is a front-end). Layer *editing* (next increment), tileset
editing, the scripting editor, deep theming. No menu entries for features not yet built (#1184) — the menus
list only operations that already exist.

## 3. Assumptions & Constraints

| # | Assumption / Constraint | Confidence |
|---|---|---|
| A1 | The foundation's `EditorAction`/`EditorActionMap`/`GridCursor`/`CyclicSelection` and the `LevelEditSession` façade are the substrate; this increment only adds a front-end. | High |
| A2 | Engine-agnostic logic (radial geometry, the menu model, the wedge→intent routing) stays Godot-free and unit-tested in `src/`; only rendering, device polling, and layout live in `game/` glue. | High |
| A3 | A trigger can carry a fast action on **tap** and open a radial on **hold**, distinguished by a hold threshold — the mechanism that lets mouse right-click both erase and open a menu. | High |
| A4 | One radial is open at a time; while open it owns directional input and holds focus, so the grid cursor stands still. | High |
| A5 | Reusing the existing (now auto-hidden) toolbar/panel controls as the mouse's discoverable fallback is cheaper and lower-risk than rebuilding their content inside the pop-in. | High |

## 4. Architectural Overview

Three cooperating layers, mirroring the foundation's shape: an **engine-agnostic menu core** (pure,
tested), a **Godot overlay** that renders it, and a **controller** that owns the triggers and routes picks
onto the editor's existing operations.

```
   ┌───────── DEVICES (held trigger + aim) ─────────┐   Trigger → named action = InputMap (project.godot):
   │ gamepad: hold LB / RB / Start ; stick|D-pad aim │     editor_menu_tiles  = LB  + key 1
   │ keyboard: hold 1 / 2 / 3      ; arrows aim      │     editor_menu_layers = RB  + key 2
   │ mouse:    right-HOLD          ; pointer aim     │     editor_menu_actions= Start+ key 3
   │           right-TAP = erase                     │     editor_menu_context= right mouse button
   └───────────────┬─────────────────────────────────┘
                   │ HoldWatch: tap vs hold (press-vs-hold threshold)
                   ▼
   ┌──────────────────────── LevelEditor (controller / glue) ────────────────────────┐
   │  on HOLD → build a RadialMenuModel from current state, open the overlay at the    │
   │  grid-cursor (gamepad/kb) or the pointer (mouse); feed it the live aim each frame; │
   │  on RELEASE/confirm → Dispatch(MenuOutcome) onto EXISTING ops.                     │
   └───────┬───────────────────────────────────────────────────────────┬──────────────┘
           │ renders / resolves                                         │ routes to (no new edit logic)
           ▼                                                            ▼
   ┌───────────── PopInMenu (Godot overlay) ─────────────┐   ┌──────── existing operations ────────┐
   │ draws wedges around a centre; SetAim → highlight;    │   │ OnPaletteSelected / OnLayerSelected  │
   │ Commit → Chosen(MenuOutcome). No edit logic.         │   │ Undo / Redo / ToggleTool / Save /    │
   └──────────────────────┬──────────────────────────────┘   │ New / Open / SaveAs (FileDialogs)    │
                          │ delegates geometry + routing to   └──────────────────────────────────────┘
                          ▼
   ┌─────────── engine-agnostic core (src/…/Input, unit-tested) ───────────┐
   │ RadialGeometry  — direction → wedge index; centre dead-zone.          │
   │ RadialMenuModel — ordered wedges; Resolve(dir) → MenuOutcome.         │
   │ MenuOutcome     — device-neutral intent (SelectTile/Layer/Action/File)│
   └───────────────────────────────────────────────────────────────────────┘
```

Key idea: **the InputMap still abstracts the device; the menu model abstracts the choice.** A held trigger
opens a menu; the same directional actions that move the grid cursor now steer the wheel (because the
overlay holds focus and the controller reads the aim vector); releasing the trigger commits the highlighted
wedge as a `MenuOutcome`, which the controller dispatches onto the *same* handlers the toolbar and hotkeys
already use. Nothing new touches the edit model.

## 5. Components & Responsibilities

### Engine-agnostic core (`src/Uberkarl.Editor/Input`, unit-tested)

| Component | Owns | Does NOT own |
|---|---|---|
| **RadialGeometry** (static) | Direction→wedge-index bucketing (clockwise from top); the centre dead-zone (aim too small = no wedge); a wedge's centre angle/direction for layout. | Rendering; what a wedge means; devices. |
| **RadialMenuModel** + **RadialMenuItem** | An ordered set of wedges (label + outcome) and `Resolve(dir)`→`MenuOutcome?` via the geometry; the title. | Icons/textures; input; how a pick is executed. |
| **MenuOutcome** (+ `MenuOutcomeKind`, `EditorFileCommand`) | The device-neutral **intent** of a pick: select a tile/layer index, invoke an `EditorAction`, or a file command. Carries no callback and no Godot type. | Executing itself (the controller does that). |
| **EditorAction** (extended) | Four new intents — `OpenTileMenu`, `OpenLayerMenu`, `OpenActionMenu`, `OpenContextMenu` — named in `EditorActionMap` so the glue never hard-codes a trigger string. | Device bindings (InputMap config). |

### Godot glue (`game/Editor`)

| Component | Owns (new) | Does NOT own |
|---|---|---|
| **PopInMenu** (`Control` overlay) | Drawing the radial (wedge chips, icons for tiles, the highlighted wedge, the hub title); `Open/SetAim/Commit/Cancel`; raising `Chosen(MenuOutcome)`/`Cancelled`; holding focus while open. | The menu's contents; edit logic; devices. |
| **HoldWatch** (plain class) | The press-vs-hold edge detector: fed a pressed-state per frame, it reports tap vs hold-crossing vs release. Engine-free. | Which action; what tap/hold do. |
| **LevelEditor** (controller, changed) | The trigger state machine (one `HoldWatch` per trigger), building the three menu models from live state, feeding the aim vector, **dispatching** a `MenuOutcome` onto existing ops, and the **auto-hide/edge-reveal** of the toolbar and panel + a **focus-zone** cycle so gamepad/kb can still reach them. | Cursor mechanics; the edit model; menu geometry. |
| **EditorCanvas** (small additions) | `CursorGlobalCenter()` (where a cursor-anchored radial opens) and `EraseAtGlobal()` (the mouse right-tap erase). | Menus. |
| **project.godot `[input]`** | The 4 new `editor_menu_*` actions; the moved gamepad bindings (§8). | Any editor logic. |

**Unchanged:** the entire `src/Uberkarl.Editor` edit/undo/save spine and `LevelEditSession`. The pop-in is a
new front-end on the same façade; mouse left-click paint/drag and the grid cursor are untouched.

## 6. Interactions & Data Flow

**Open (hold):** each frame the controller feeds every trigger's pressed-state to its `HoldWatch`. When one
crosses the hold threshold and no menu is open, the controller builds that trigger's `RadialMenuModel` from
current state (palette, layers, or the fixed action set), and opens the `PopInMenu` at the **grid-cursor**
centre (gamepad/keyboard) or the **pointer** (mouse context). The overlay grabs focus.

**Aim (while open):** the controller feeds the overlay a direction each frame — `Input.GetVector` over the
cursor-move actions (gamepad stick + D-pad + keyboard arrows) for the cursor-anchored menus, or
pointer-minus-centre for the mouse context menu. The overlay asks `RadialMenuModel.IndexAt` and highlights
the aimed wedge (dead-zone in the middle = "release to cancel").

**Commit (release / confirm):** releasing the trigger (or gamepad A / Enter) commits the highlighted wedge;
the overlay resolves it to a `MenuOutcome` and raises `Chosen`. The controller **dispatches**:
`SelectTile`→`OnPaletteSelected`, `SelectLayer`→`OnLayerSelected`, `InvokeAction`→`Undo/Redo/ToggleTool`,
`FileCommand`→`New/Open/Save/SaveAs`. Every target already exists — the mouse and hotkeys use them too.
Releasing on the dead-zone (or Esc / gamepad X) cancels. Focus returns to the canvas.

**Reveal:** the canvas fills the whole area. The toolbar (top) and layer/tile panel (left) are hidden by
default and revealed when the pointer enters their edge band **or** they hold focus. A `FocusNext` press
cycles focus canvas→toolbar→panel, revealing the surface it lands on — so a gamepad/keyboard user reaches
every button/list without a mouse, while the mouse reveals by edge-hover.

**Communication style:** synchronous, single-threaded, in-process — action polling, C# events, and `_Draw`.
No async, no new cross-boundary machinery (consistent with the foundation).

## 7. Data Model (Conceptual)

No persistent-model change. New **transient interaction state** only: which trigger's menu is open, the menu
centre, the highlighted wedge, and each trigger's hold timer. The **menu model** (`RadialMenuModel` +
`MenuOutcome`) is part of the editor's *interaction contract*, engine-agnostic, rebuilt per open from live
editor state — it is never persisted.

## 8. Contracts & Interfaces (Abstract)

**Trigger / binding table** (the four new actions; foundation bindings otherwise unchanged):

| Menu | Gamepad (hold) | Keyboard (hold) | Mouse | Opens at |
|---|---|---|---|---|
| **Tiles** | LB | `1` | right-HOLD (context) | grid cursor / pointer |
| **Layers** | RB | `2` | — | grid cursor |
| **Actions** (New/Open/Save/Save As/Undo/Redo/Tool) | Start | `3` | — | grid cursor |
| **Erase** | (X, unchanged) | (Delete, unchanged) | right-**TAP** | pointer cell |

Aim: gamepad **left stick / D-pad**, keyboard **arrows**, mouse **pointer**. Commit: **release** the
trigger, or gamepad **A** / **Enter**. Cancel: release on the dead-zone, **Esc**, or gamepad **X**.

**Moved foundation bindings** (to free the hold-buttons; reachability preserved — see §9/§10):
- Gamepad **LB/RB** removed from `editor_cycle_tile_prev/next` (tile cycling now via the Tiles radial; keyboard **Q/E** retained).
- Gamepad **Start** removed from `editor_save` (save now via the Actions radial; keyboard **Ctrl+S** retained).

**Invariants:**
- **RadialGeometry** — `IndexAt` returns a wedge in `[0,count)` for a direction past the dead-zone, `-1`
  otherwise (empty menu or neutral centre); wedge 0 is the top, advancing clockwise; a direction built from
  a wedge's centre angle resolves back to that wedge. (Unit-tested.)
- **RadialMenuModel** — `Resolve(dir)` yields the aimed wedge's outcome or `null` on the neutral centre;
  `OutcomeAt` is bounds-checked; a null item list is an empty menu that keeps its title. (Unit-tested.)
- **MenuOutcome** — a pure value; each kind carries exactly the payload it needs (tile/layer index, action,
  or file command). (Unit-tested — the wedge→intent routing.)
- **HoldWatch** — a press shorter than the threshold reports as a tap on release; a longer one reports the
  hold-crossing edge exactly once and never a tap.
- **Controller** — one menu open at a time; a pick is only ever executed by dispatching onto an existing
  operation; the edit model is only mutated through the unchanged session.

## 9. Cross-Cutting Concerns & the Four Follow-ups (settled)

- **Follow-up 1 — gamepad undo/redo/save.** Kept the foundation's **L3 = undo, R3 = redo** (frequent enough
  for a dedicated button); **save** moves off Start (now the Actions-menu trigger) and lives on the
  **Actions radial** + keyboard Ctrl+S. Rationale: save is low-frequency; a menu entry is the right home and
  it frees Start to open the actions wheel.
- **Follow-up 2 — mouse erase vs the right-click pop-in.** Resolved by **press-vs-hold on the right button**:
  a **tap** erases the cell under the pointer; a **hold** opens the context (Tiles) radial. This is the
  `HoldWatch` mechanism, verified live in-engine.
- **Follow-up 3 — analog cursor cadence.** Kept **step + repeat** (the foundation's initial-delay-then-stream
  cadence); rejected speed-by-tilt — a discrete grid does not benefit from analog velocity, and step+repeat
  is deterministic and already shipped. Inside a radial the stick chooses a wedge by **direction**, not by
  stepping.
- **Follow-up 4 — toolbar under the pop-in.** Chose **auto-hide + edge-reveal** (max canvas) over a thin
  persistent bar. The existing toolbar and panel are reused as-is but hidden by default; the mouse reveals
  them by edge-hover and gamepad/keyboard by the focus-zone cycle — so discoverability is preserved without
  permanently spending canvas.
- **Discoverability / affordance:** the grid cursor is always drawn; the hub shows the menu title and the
  current pick (or "release to cancel"); tile wedges show icons. On-screen glyph hints remain a future seam
  (not built, #1184).
- **Consistency / idempotency / error handling:** unchanged — every pick routes through the validated
  session; no-op suppression and dirty/save behaviour are the foundation's.
- **Determinism / testability:** all menu *logic* (geometry, resolution, routing, hold timing) is pure and
  unit-tested; only drawing, device polling, and layout are glue.
- **Concurrency:** none — single-threaded UI.

## 10. Quality Attributes & Trade-offs

- **Max canvas (primary):** the canvas fills the area; selection surfaces are summoned, not resident.
- **Parity preserved:** every operation stays reachable on each device — the moved gamepad bindings are
  *re-homed onto the radials*, not removed (tile pick → Tiles wheel, save → Actions wheel), so reachability
  is intact even though the raw button map changed.
- **Low burn-in / maintainability:** picks are device-neutral `MenuOutcome`s dispatched onto existing
  operations; the wheel is a front-end over the same action set, exactly as the foundation predicted.
- **Simplicity (KISS):** reuse the InputMap (devices), the focus system (reveal), and the existing
  toolbar/panel (fallback); the only new code is ~3 small pure classes + a thin overlay + a controller state
  machine.

**Trade-offs & rejected alternatives:**
- *Hold-only triggers (tap = no-op) vs. tap-and-hold on the same button.* Chose **hold-only** for the three
  menu triggers so a single named action can be bound to a gamepad button **and** a keyboard key without a
  device-specific tap meaning (the action layer hides which device fired). The mouse right button is the one
  tap-and-hold, and it is unambiguous because only the mouse produces it. This traded away a gamepad
  tap-cycle shortcut for a clean, ambiguity-free model — the radial supersedes blind cycling anyway. *(Open
  question O1 offers to restore tap-cycle later.)*
- *Rebuild toolbar/panel content inside the pop-in vs. reuse and auto-hide.* **Reused and auto-hid** — lower
  risk, keeps the familiar list-based mouse workflow as a discoverable fallback.
- *A bespoke radial-picker library vs. a pure geometry class + a thin overlay.* **The latter** — the only
  hard part (angle bucketing, dead-zone) is pure and testable; the drawing is a small `_Draw`.

## 11. Risks & Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| A held trigger's directional aim leaks to the grid cursor | High (double action) | While a menu is open the overlay holds focus (cursor `_Process` is focus-gated) and `_UnhandledInput` is suppressed; the controller reads the aim vector directly. Verified. |
| Radial clipped when opened near a level edge | Low (cosmetic) | Opens centred on the cursor/pointer; wedges off-screen are still selectable by direction. A future clamp-to-viewport is a named polish, not built. |
| Many tiles crowd the wheel | Low | The sample's 7 tiles read cleanly; a large palette is a future paging/scroll concern, not this increment. |
| Moved gamepad bindings surprise a foundation user | Low | Documented (§8); reachability preserved via the radials; O1 offers to restore tap-cycle. |
| Trigger-name drift between glue and config | Low | The four new actions are named in `EditorActionMap`; the completeness/uniqueness unit test covers them automatically. |

## 12. Verification (Godot MCP) — see the DiVoid node for screenshots

Ran the editor scene and drove the pop-in on **all three inputs**: **gamepad** — held LB, the Tiles radial
popped in around the grid cursor, aimed up-right to highlight tile #2, released to commit, then painted the
picked tile at the cursor (orange cell applied); **keyboard** — held `2`, the Layers radial popped in, aimed
down to highlight "terrain", released to commit the layer; **mouse** — right-click-hold popped in the
context Tiles radial at the pointer. The edit canvas stayed maximised (toolbar/panel auto-hidden)
throughout; a screenshot was captured for each input path plus baseline/commit/applied. `get_editor_errors`
carried **no project errors** — only the initial pre-mkdir PNG-save line and the MCP harness's own
`mcp_input_service.gd` keycode-lookup noise. Engine-agnostic menu logic is unit-tested: **Editor 48/48**
(34 → +14 pop-in), pop-in core (RadialGeometry / RadialMenuModel / RadialMenuItem / MenuOutcome) **100%
line / 100% branch**; Content 33/33, Packages 31/31; `dotnet build` 0 warnings / 0 errors.

*Harness note:* `simulate_mouse_move` injects motion events but does not update
`GetViewport().GetMousePosition()`, so the mouse-context **aim/commit** could not be driven in-harness (a
real mouse updates it); the mouse **summon** and the press-vs-hold split are verified, and the applied-pick
path is proven on the gamepad and keyboard menus which `simulate_action` drives correctly.

## 13. Open Questions (for Toni)

1. **O1 — restore gamepad tap-cycle?** The shoulders/Start are now hold-to-open (hold-only). Want a **tap**
   on LB/RB to still nudge the tile prev/next (and Start to quick-save) alongside the hold, or is the radial
   enough? (Restorable with per-device tap wiring if wanted.)
2. **O2 — mouse context radial contents.** It currently opens the **Tiles** wheel. Prefer a small **root**
   radial (Tiles / Layers / Actions sub-wheels) so the mouse reaches every menu without the edge-reveal
   panel, or keep Tiles + the edge-reveal for the rest?
3. **O3 — keyboard menu keys.** `1/2/3` are unbound-elsewhere and MCP-testable, but not mnemonic. Prefer
   mnemonic hold-keys (e.g. Tab/L/F) at the cost of overloading Tab's focus tap?
4. **O4 — Actions wheel contents.** Currently New/Open/Save/Save As/Undo/Redo/Tool (7). Add anything (e.g.
   New-with-dimensions, playtest) once those features exist, or keep it to shipped ops (per #1184)?
5. **O5 — radial clamp-to-viewport** when the cursor is at a level edge (cosmetic polish) — worth a small
   follow-up, or fine as-is?

## 14. As-Fixed Addendum — Gamepad Pop-in Bug Fixes (DiVoid #7449)

A real-gamepad playtest (Toni, 2026-08-02) found two defects that the in-harness verification missed because
`simulate_action` injects a single named action, whereas a real stick/D-pad fires the editor's directional
action **and** Godot's built-in `ui_*` focus-navigation from one physical input. Both were focus-fragility
bugs; both fixed with minimal, targeted C# (no `project.godot` change, edit/undo/save spine untouched).

**Bug A — grid cursor moved while a radial was open.** *Root cause:* the canvas cursor poll was gated only on
`HasFocus()`, and §11 relied on the open radial "holding focus". But aiming with the stick fires `ui_*`, and
the radial's focus neighbours were unset, so the aim bounced focus off the wheel onto the full-rect canvas
underneath — which then passed its own focus gate and stepped the cursor. *Fix:* (1) a new engine-agnostic
`CursorInputGate.AllowsCursorMovement(hasFocus, directionalCaptured)` policy; `EditorCanvas` exposes
`DirectionalInputCaptured`, and the controller sets it every frame to `radialOpen || focusZone != Canvas`, so
the cursor freezes independent of which control momentarily holds focus. (2) `PopInMenu` pins all focus
neighbours to itself so the aim can no longer bounce focus off the wheel (also keeps its own A/Esc confirm
working). The §11 risk row's premise ("focus-gated") is thus replaced by an **explicit** capture flag.

**Bug C — the B-revealed classic toolbar/panel was unusable on gamepad (any input insta-closed it).** *Root
cause:* `UpdateReveals` keyed panel visibility on the *live focus owner* (`FocusInside`); the first directional
press fired `ui_*`, which escaped the zone (the buttons'/lists' default focus neighbours point outside), so
`FocusInside` went false → the panel hid, and focus landed on the canvas which then moved the cursor. *Fix:*
(1) **sticky reveal** — visibility is now also held by the explicit `focusZone` (Toolbar/Panel), so momentary
focus loss no longer hides it (the mouse's `FocusInside`/edge-band reveal is retained). (2) **focus
containment** — the toolbar buttons pin their vertical (and row-end horizontal) neighbours to self, and the
panel's two lists pin their horizontal + outer-vertical neighbours to self, so directional input navigates
*within* the revealed surface instead of escaping. (3) the cursor stays frozen while any focus-zone is active
(same `DirectionalInputCaptured` flag). B still cycles Canvas→Toolbar→Panel→Canvas, so B "switches between the
system and tile menus" and cycling back to Canvas is the close.

**Contract added:** `CursorInputGate` (pure) — the grid cursor acts on a move request **iff** its surface has
focus **and** no radial/panel is capturing directional input. Invariant regression-tested engine-free in
`EditorInputTests` (gate truth-table + a `GridCursor` that stays put across repeated move requests while a menu
is "open", then moves once it closes).

**Verification (Godot MCP):** baseline — grid cursor moved/clamped freely. Radial held open + up/left aim → the
**wheel highlight changed** (aim steers the wheel) while the **cursor stayed at its corner cell**; on close the
same up/left moved it again (directions were live, not dead) — cursor was **frozen only while the radial was
open**. B → the classic toolbar revealed with focus; raw `ui_*` navigation moved focus across the toolbar and
into the layer list and **changed the active layer (backdrop→terrain)** with **nothing dismissing** and the
grid cursor **not** hijacked; B cycled back to canvas. `get_editor_errors`: only the MCP harness's own
`mcp_input_service.gd` keycode-lookup noise — no project errors. Tests: **Editor 50/50** (48 → +2 gate/freeze),
Content 33/33, Packages 31/31; `dotnet build` 0 warnings / 0 errors. §6.10 comment-grep (TODO/FIXME/HACK/XXX +
commented-out code) on the changed files **0**.

### 14.1 Round 2 — directional input still escaped the revealed panel (DiVoid #7449)

Round 1's sticky reveal made the panel **stay open**, but a second real-gamepad playtest found that **any
D-pad/stick movement snapped focus back to the edit canvas** (menu stayed open but unnavigable). Round 1's
in-harness check was a false positive for the same reason as before: `simulate_action` fires only the editor
action, never the `ui_*` companion, so the focus escape never reproduced in the sim.

*Root cause — the exact focus-return path.* On a real stick, one physical press fires **both** the
`editor_cursor_*` action **and** Godot's built-in `ui_left/right/up/down` (both are bound to the same D-pad
buttons + left stick; `ui_*` keeps its engine defaults). The `editor_cursor_*` half is already inert off-canvas
(the `DirectionalInputCaptured` gate). The `ui_*` half is the culprit: round 1 pinned only the *outer* focus
neighbours and left the **middle toolbar buttons' left/right** and the **two panel lists' inner vertical edges**
unset. An unset neighbour falls back to Godot's **geometric** focus search — and because `EditorCanvas` is a
**focusable full-rect Control underlying the whole viewport**, that search selects the canvas from essentially
any side. Focus lands on the canvas (whose own neighbours are self-pinned, so it's a trap); the panel stays
revealed (sticky reveal keys on `focusZone`, not focus owner) but is now unreachable.

*Fix (containment completed — no `project.godot` change, spine untouched).* Pin **every** side of **every**
focusable toolbar/panel control to a control **inside** its own surface, leaving **no** geometric side that can
reach the canvas: toolbar buttons wire left/right into an explicit sibling chain (ends → self, top/bottom →
self); the panel's two lists wire their inner vertical edges to each other (layer-list bottom → tile list, tile
list top → layer list) with the outer edges + horizontals → self. `FocusNext`/`FocusPrevious` are pinned to self
on those controls **and** on the canvas, so a keyboard Tab's `ui_focus_next` (the same double-fire class) cannot
escape either — only the explicit editor focus-next action changes zones. The "who owns direction" rule was
lifted into the pure `CursorInputGate.DirectionCaptured(radialOpen, nonCanvasZoneActive)` and the controller now
routes through it (engine-free regression test added).

*Verification (Godot MCP — double-fire reproduced in-harness this time).* Injected raw `ui_*` **alongside** the
editor action by parsing real `InputEventJoypadButton` (D-pad 11–14) and `InputEventJoypadMotion` (left stick)
events — the true one-press-fires-both path. Proof the harness now reproduces `ui_*` nav: a D-pad **right**
moved focus New→Open **within** the toolbar. Then **32 directional double-fires** (14 across the toolbar
including past both ends, 18 across the panel lists, plus analog-stick flicks) — after every one,
`GuiGetFocusOwner` stayed **inside the toolbar/panel and was never the canvas**, and both panels stayed
revealed. The zone cycle still closes: focus-next Panel→Canvas returned focus to the canvas. `get_editor_errors`:
only MCP-harness `gdscript://` / `mcp_game_inspector_service.gd` noise — no project errors. Tests **Editor 52/52**
(50 → +2: `DirectionCaptured` truth-table + a panel-zone double-fire that keeps the cursor frozen even when the
`ui_*` bounce lands focus on the canvas), Content 33/33, Packages 31/31; `dotnet build` 0/0; §6.10 comment-grep
on the changed files **0**.

## 15. Editor UI v2, Part 1 — Radial-Centric Consolidation (DiVoid #7464)

Toni ratified (2026-08-02) a **radial-centric** direction with one rule: *"whatever is possible with radial we
don't need as a duplicate."* The custom radials are gamepad-native and just work; the classic Godot `Control`
panels kept peeling gamepad bug-layers (stay-open → navigate → **can't activate**). Part 1 does three things —
drop the radial-duplicated side panel, make the remaining classic Controls gamepad-**activatable**, and report
on whether the Actions radial fully covers the system-menu toolbar. It is additive over the merged pop-in +
round-2 foundation; the edit/undo/save spine and `LevelEditSession` are untouched.

### 15.1 Drop the persistent side panel

The left panel (a `Layers` `ItemList` + a `Tiles` `ItemList`) duplicated the **Tiles (LB)** and **Layers (RB)**
radials, which own tile/layer selection. It is removed entirely — not merely auto-hidden — maximising the canvas
and eliminating an entire classic-Control focus surface (and its round-1/round-2 focus-containment burden). What
is kept is the **selection state**, not the widgets:

- `paletteTileIds` + `paletteTextures` remain the tile-selection state the **Tiles radial** reads (wedge order
  and wedge icons). `PopulatePalette` now builds only that state; there is no list to fill.
- Layer names are read live from the session by the **Layers radial** (`BuildLayersMenu`), so `PopulateLayers`
  only resets the active layer index.
- `Dispatch`, `CycleTile`, `CycleLayer` route through the same `OnPaletteSelected` / `OnLayerSelected` state
  handlers as before — the removed lines were only the now-dead `ItemList.Select` mirror calls.
- The **focus-zone cycle collapses to Canvas ⇄ Toolbar** (the `Panel` zone is gone). `AdvanceFocus` toggles the
  two; `DirectionCaptured(radialOpen, focusZone != Canvas)` still freezes the grid cursor while the toolbar owns
  input.

### 15.2 Gamepad activation of classic Controls — root cause and fix

**Symptom (Toni, real pad):** the toolbar can be navigated, but pressing confirm (A) on a focused item like
"Open" does nothing.

**Root cause (isolated in-harness):** with a toolbar button focused, injecting a pure `ui_accept` action
activates it, and a raw keyboard **Enter** activates it — but a raw **gamepad A** does not, and the canvas does
not paint either. `InputEventJoypadButton(button 0).is_action("ui_accept")` returns **false**:

> `ui_accept = {Enter, KP-Enter, Space}` — **no gamepad button.** `ui_cancel = {Escape}` — none either.
> Meanwhile `ui_left/right/up/down` **do** carry D-pad + left-stick bindings (why round-2 navigation works).
> `editor_paint` owns gamepad A, but nothing routes the pad to `ui_accept`.

So a focused classic Control could be *navigated* on the pad but never *confirmed* — the pad had no path to the
Godot activation action. This is the literal "`ui_accept` doesn't reach the focused Control", and it is why the
classic panels kept failing where the radials (which never rely on `ui_accept`) succeed.

**Fix — two parts, minimal:**

1. **InputMap (`project.godot`):** define `ui_accept` = {Enter, KP-Enter, Space, **joypad A / button 0**} and
   `ui_cancel` = {Escape, **joypad B / button 1**}. This is the root-cause fix: focused Buttons, `ItemList`s,
   **and the Open/Save `FileDialog` windows** now activate/cancel natively on the pad. Choosing the InputMap
   layer over a bespoke "activate the focus owner" router is deliberate — the router could not reach the
   `FileDialog` (a separate `Window`), yet those are exactly the "regular browser windows [that] need to react
   to gamepad buttons" this task is foundational for (the coming package browser + tile list-window).
2. **`EditorCanvas` (paint gate):** paint/erase in `_GuiInput` are now gated on
   `CursorInputGate.AllowsPrimaryAction(HasFocus(), DirectionalInputCaptured)` — the same capture flag that
   already freezes the grid cursor. While a radial is open or a non-canvas zone is active, `editor_paint` is
   **inert** on the canvas (it does not `AcceptEvent`), so the shared A press can never paint a cell nobody is
   aiming at while it is confirming a Control. Belt-and-suspenders alongside part 1, and the engine-free half of
   the invariant (unit-tested).

Shared-binding safety: A = `editor_paint` + `ui_accept`; B = `editor_focus_next` + `ui_cancel`. On the canvas, A
paints and the focused-Control `ui_accept` is a no-op; in the toolbar/dialogs, A confirms and `editor_paint` is
inert (canvas unfocused + gated). B still cycles the zone when no dialog is up, and doubles as "back/cancel"
inside a `FileDialog`. No new physical button is introduced.

### 15.3 Contract touched

`CursorInputGate` gains `AllowsPrimaryAction(surfaceHasFocus, directionalCaptured)` — the canvas primary action
(paint/erase = the gamepad/keyboard confirm at the cursor) may act **iff** the surface owns focus and nothing
else is capturing input. Same shape as `AllowsCursorMovement`; distinct name for the distinct concern. Pure and
engine-agnostic, regression-tested engine-free.

### 15.4 Verification (Godot MCP) — see the DiVoid node for screenshots

- **Side panel gone:** running-tree `ItemList` count **0**, one `PanelContainer` (the toolbar) remains; toolbar
  buttons intact (New/Open/Save/Save As/Paint/Erase/Undo/Redo). Canvas fills the viewport (screenshot).
- **Radials still select:** Tiles radial (LB hold → aim → release) changed the active tile **#1 → #4**; Layers
  radial (RB) changed the active layer **backdrop → terrain** — both through the reroute, with no side panel.
- **Confirm activates a focused Control:** with focus on "New", an injected raw **`InputEventJoypadButton`
  (button 0)** now fires the button (`new blank` printed). Before the fix the identical injection did nothing
  (and a pure `ui_accept` / keyboard Enter did fire — the isolation that pinned the root cause).
- **Activation-while-a-zone-is-active routing (regression):** in the toolbar zone, D-pad navigation stayed in
  the toolbar and a raw A confirmed the focused **Erase** toggle (tool `Paint → Erase`) while
  `DirectionalInputCaptured` stayed **true** (cursor frozen) and the level was **not** dirtied by any stray
  paint — `editor_paint` proven inert off-canvas.
- **Actions radial:** Start (button 6) hold opened it rendering New/Open/Save/Save As/Undo/Redo/Tool; committing
  a wedge on the pad fired the action (Redo dispatched; then the **Tool** wedge toggled `Erase → Paint`).
- `get_editor_errors`: only the MCP harness's own `gdscript://` `_mcp_error` unused-variable noise from the
  injected diagnostic scripts — **no project errors**. Tests **Editor 54/54** (52 → +2: `AllowsPrimaryAction`
  truth-table + a toolbar-zone confirm-inert regression), Content 33/33, Packages 31/31; `dotnet build` 0/0;
  §6.10 comment-grep on changed files **0**; `project.godot` diff = exactly the `ui_accept`/`ui_cancel`
  definitions (+14 lines, 0 removed).
- **Real-pad confirmation is Toni's** — the harness injects `ui_accept` / raw `InputEventJoypadButton`, which
  reproduced the exact symptom and now shows the fix; final sign-off on physical hardware is his.

### 15.5 Actions-radial report (for Toni — informs whether the toolbar can be dropped next)

**Does the Actions radial cover the system-menu toolbar? Yes, for every actionable command.**

| Toolbar control             | Actions-radial coverage                             |
|-----------------------------|-----------------------------------------------------|
| New / Open / Save / Save As | Yes — direct wedges (`FileCommand`)                 |
| Undo / Redo                 | Yes — direct wedges (`Invoke`)                      |
| Paint / Erase toggles       | Yes — via the **Tool** wedge (toggles paint <-> erase) |
| Status label (right)        | No — display-only, not an action; not duplicated    |

Every *command* on the toolbar is reachable from the gamepad-native Actions radial (Start-hold), so by Toni's
rule the toolbar is radial-duplicated **for its actions** and is a **droppable candidate**. **One caveat before
dropping it:** the toolbar also hosts the **status readout** (level name · file · active layer · tool) and the
explicit Paint/Erase pressed-state. Dropping the toolbar means **relocating the status readout** (e.g. a thin
always-on status line or an on-canvas HUD) and deciding whether the paint/erase state needs a persistent
indicator. Recommendation: drop the toolbar in a follow-up **once a lightweight status surface exists** — not in
Part 1. Per the task, the toolbar is **not** dropped here; this is the report so Toni decides.
