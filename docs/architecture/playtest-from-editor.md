# Architectural Document: Playtest-from-Editor

> Toni (2026-08-03): *"i could not really test the edited level (you said play it, well, that is not
> possible :D)."*

This document describes the design of **playtest-from-editor** (DiVoid #7514): a Play action inside the
level editor that launches the author's *current, in-memory* edited level through the real play runtime
(collision, parallax, camera, player), and a return control that comes back to the editor with the
in-progress edit buffer untouched. It closes the author → play → edit loop that the editor (#7433) and
the playable runtime (#7418) shipped separately but never connected.

## 1. Problem Statement

Before this change there was no way to play the level currently open in the editor. The play scene
(`LevelPlay` / `scenes/level_playable.tscn`) always loaded the fixed `res://content/sample.pkg`, and the
editor's in-memory `EditableLevel` had no path into that runtime short of saving to a file and manually
swapping scenes. Success = hit Play, walk/jump the level exactly as it stands in the editor buffer right
now (including unsaved edits), then return to editing with nothing lost and nothing reloaded from disk.

## 2. Scope & Non-Scope

**In scope:**
- A "Play" wedge on the editor's Actions radial, a toolbar button, and a keyboard hotkey
  (`editor_playtest`, all invoking the same `LevelEditor.StartPlaytest()`), each launching a playtest of
  the level currently under edit.
- Projecting the live `EditableLevel` to a `ResolvedLevel` (reusing the existing
  `EditableLevelSnapshot.ToResolvedLevel` projection — already used for the canvas) and running it
  through the **same play runtime** `LevelPlay` uses: `TileMapLevelBuilder.Build` (parallax + collision),
  `Player`, and a following `Camera2D`.
- A return control (`ui_cancel` — Escape on keyboard, the B button on gamepad, already bound in the
  project) that tears the playtest down and restores the editor, with the in-progress edit buffer
  untouched.
- Handling the always-true "unsaved buffer" case and empty/edge levels (no declared spawn, nothing
  painted) gracefully, by construction (see §5).

**Out of scope (anti-complexity, explicitly excluded per the task):**
- Virtual keyboard (#7513), continuous drag-paint (#7517), browser/New/Save-As changes, the mouse+kb menu
  pass (#7500).
- Rebuilding or forking the play runtime — it is reused verbatim.

## 3. Assumptions & Constraints

| # | Assumption / Constraint | Confidence |
|---|---|---|
| A1 | `EditableLevelSnapshot.ToResolvedLevel` already produces a **complete** `ResolvedLevel` (spawns, default spawn, per-layer collision/scrollSpeed/repeat, background) — confirmed by reading `src/Uberkarl.Editor/EditableLevelSnapshot.cs`; no changes needed there. | High |
| A2 | The play-side building blocks (`TileMapLevelBuilder.Build`, `Player`, camera-follow) are engine-correct and only need to be **reusable from two call sites** instead of one. | High |
| A3 | `ui_cancel` is already bound (Escape + gamepad B, `project.godot`) and unused inside the editor's own input surface, so it is free to mean "leave playtest" without a new binding. | High |
| A4 | Building a fresh play world on every `Start()` and freeing the whole subtree on `Stop()` is sufficient to guarantee the editor's model is never touched by playtesting — no explicit "restore" step is needed because there is nothing to restore. | High |

## 4. Mechanism Choice: Overlay, Not Scene-Swap

Two mechanisms were on the table: (a) scene-swap to `level_playable.tscn` with the level passed via a
singleton/autoload, or (b) an overlay play-runtime toggled within the editor scene. **Overlay was
chosen** — it is the simpler of the two for this repo's shape:

- The editor's `session`/`EditableLevel` already live as fields on the `LevelEditor` node. A scene-swap
  would tear down and recreate that node, which means the buffer would have to be serialized out and
  back in (or threaded through a singleton) to survive the round trip — exactly the kind of "force a
  save" side-channel the task explicitly rules out.
- With an overlay, `LevelEditor` never leaves the tree. The buffer survives **by construction**, not by
  an explicit save/restore step: nothing about starting or stopping a playtest run touches `session` or
  `EditableLevel` at all.
- A `Camera2D` becoming current during playtest changes the viewport's 2D transform for every
  `CanvasItem` in the default canvas layer, including the editor's own `Control` tree. Freeing the whole
  play-world subtree (camera included) on `Stop()` hands the viewport back to its default (no active
  camera) transform, so the editor renders normally again with no explicit camera reset needed.

## 5. Architecture

```
                     ┌──────────────────────────── game/  (Godot compile set) ─────────────────────────────┐
                     │                                                                                      │
  Play (wedge /      │  LevelEditor (Control)                                                               │
  toolbar / hotkey)─▶│    StartPlaytest()                                                                   │
                     │      1. ResolvedLevel level = EditableLevelSnapshot.ToResolvedLevel(session.Level)   │
                     │      2. playtestOverlay.Start(level)                                                 │
                     │      3. hide canvas / topBar / shellBackground; _Process/_UnhandledInput early-return │
                     │                                                                                      │
                     │  PlaytestOverlay (Control) ── owns ONE playtest run                                  │
                     │    Start(ResolvedLevel)  → new Node2D "Playtest" → PlayRuntimeBuilder.Populate(...)  │
                     │    Stop()                → QueueFree the whole "Playtest" subtree (incl. camera)     │
                     │    _UnhandledInput        → ui_cancel raises ExitRequested                           │
                     │                                                                                      │
                     │  PlayRuntimeBuilder (static) ── the ONE shared play runtime                          │
                     │    Populate(Node2D root, ResolvedLevel level):                                       │
                     │      background fill → TileMapLevelBuilder.Build(level) → spawn Player → attach      │
                     │      following Camera2D (limits = level bounds)                                      │
                     │                                                                                      │
                     │  LevelPlay (Node2D, scenes/level_playable.tscn — unchanged run target)                │
                     │    _Ready(): loads res://content/sample.pkg → LevelLoader.Load → PlayRuntimeBuilder   │
                     │              .Populate(this, level)   ── same builder, different level source        │
                     └──────────────────────────────────────────────────────────────────────────────────────┘
```

`PlayRuntimeBuilder` is the extraction that makes reuse literal rather than aspirational: it is exactly
the body `LevelPlay._Ready()` used to have (background fill, `TileMapLevelBuilder.Build`, spawn, camera),
moved verbatim into a static method parameterized on the destination `Node2D` root and the
`ResolvedLevel`. `LevelPlay` now calls it after loading the sample package from disk; `PlaytestOverlay`
calls it after projecting the editor's live buffer. One code path builds "what play looks like"; only how
the `ResolvedLevel` is obtained differs.

**Return / buffer-preservation invariant.** `PlaytestOverlay.Stop()` and `LevelEditor.StopPlaytest()` never
call `session.Save()`, never reload from a package, and never touch `EditableLevel` in any way — they only
free Godot nodes and flip `Visible`/`_Process` guards. The buffer is preserved not because anything
restores it, but because playtesting was never given a way to mutate it: `StartPlaytest()` reads
`session.Level` through a pure projection and hands the *result* (a value type, `ResolvedLevel`) to the
overlay; the overlay never sees `session` or `EditableLevel` at all.

**Empty / edge levels.** No special-casing was needed: `EditableLevel` already enforces positive
width/height and at least one layer (existing invariants), `ResolvedLevel.DefaultSpawnPosition` is
already nullable and `PlayRuntimeBuilder.Populate` already falls back to a default cell when it is null
(the same fallback `LevelPlay` always had), and an all-empty grid places no tiles and adds no collision —
`TileMapLevelBuilder.Build` handles that by simply not looping over anything. A freshly-created blank
level (`EditableLevel.CreateBlank`, no declared spawn) playtests exactly as gracefully as the loaded
sample.

**Input isolation.** While a run is live, `LevelEditor.Playtesting` gates `_Process` and
`_UnhandledInput` to a no-op. This matters because several editor hotkeys share physical keys with
gameplay actions (e.g. Space is bound to both `editor_paint` and `jump`) and — independently —
`editor_focus_next` and `ui_cancel` share the gamepad B button; without the gate, a B-press to leave
playtest could also fire the editor's own focus-cycle handler underneath. `PlaytestOverlay` is the only
thing listening for `ui_cancel` during a run.

## 6. New Editor Action

`EditorAction.Playtest` was added to the device-neutral action enum, bound to `editor_playtest`
(keyboard `P`) in `EditorActionMap`/`project.godot`. Unlike the editor's other actions it deliberately has
no dedicated *gamepad* button binding — the Actions radial (Start button hold → aim) already reaches it
on gamepad, mirroring how `Undo`/`Redo`/`ToggleTool` are reachable both via the radial and a keyboard
shortcut without needing a second dedicated gamepad button each.

## 7. Verification (Godot MCP)

Ran against the shipped sample level (`res://content/sample.pkg`, 60×16, loaded as the editor's default):

1. **Edited the level in the editor** (real UI interaction — the Layers radial hold-and-aim, the
   `LayerManagerPanel`'s Add/Collision buttons, and click-to-paint on the canvas — not direct model
   pokes): added a new layer with collision **on**, painted a 3-tile platform onto it near the ground, and
   painted 3 tiles onto the level's existing parallax `backdrop` layer (already `scrollSpeed=0.5`,
   `repeat=true` in this sample — painting onto it *is* "adding a parallax backdrop"). Screenshot:
   editor showing both new elements.
2. **Hit Play** (toolbar button). `PlaytestOverlay` built a `ResolvedLevel` from the live buffer and the
   player spawned; `get_editor_errors` stayed clean (all log noise present was the MCP addon's own
   transient-script warnings, not `game/` or `src/` code).
3. **Collision on the new layer**: dropped the player from above the new platform under real engine
   gravity — it settled at `y≈132`, exactly the platform's top surface (row 9 × 16px − half-height),
   `velocity=(0,0)`, `is_on_floor=true`. A separate single-tick check (real synthesized `jump` input
   reaching `Player._PhysicsProcess` unmodified) showed the same tile blocking the player from below
   (`velocity.y` snapped to 0 immediately on contact) — the tile is solid from every direction, not just
   on top. Screenshot: player resting on the new platform.
4. **Parallax**: `backdrop`'s `Parallax2D.scroll_scale = (0.5, 0.5)` and `repeat_size = (960, 256)` (=
   level width×height×tileSize) confirmed the authored scroll speed and repeat flag carried through
   `EditableLevelSnapshot` → `TileMapLevelBuilder.Build` unchanged.
5. **Collision at the level boundary**: real keyboard-driven `move_right`/`move_left` runs repeatedly
   capped the player at `x≈938` (the right boundary wall column, `59×16 − halfWidth`), never exceeding
   it — the pre-existing wall collision still holds inside a playtest launched from the editor.
6. **Returned via `ui_cancel`** (Escape). `PlaytestOverlay.IsPlaying` → false, canvas visible again;
   queried the live `TileMapLayer` nodes directly and confirmed the new platform and backdrop tiles were
   still present, unchanged, with **no save and no reload** in between. Repeated across **two** separate
   Play→edit round trips (edited again mid-session, replayed, returned again) with the same result both
   times. Screenshot: editor after return, pixel-identical layout to the pre-Play screenshot.

**Harness note:** the MCP bridge's synthetic action events reliably drove continuous/level-state input
(`move_left`/`move_right`, `ui_cancel`) over the real automatic engine loop, confirmed by consistent,
repeatable results (the wall-stop at `x≈938` reproduced identically across multiple independent runs).
Edge-triggered `IsActionJustPressed` reads (the `jump` action) were not reliably observable through that
same automatic-frame path within this session — verified instead via a real, unmodified `Player` node
receiving a genuinely synthesized `jump` press with deterministic single-tick and real-gravity-drop
checks, both showing the correct blocked/landed collision result. **Real-gamepad confirmation on physical
hardware is Toni's** — this session used only the Godot MCP bridge's synthetic input and cannot stand in
for a physical controller.

### §6 audit
- Comment-grep (TODO/FIXME/HACK/XXX) on changed files: **0**.
- `dotnet build Uberkarl.csproj`: **0 errors / 0 warnings**.
- `Uberkarl.Editor.Tests`: **107/107** (was 102 — +5 new tests in `PlaytestProjectionTests.cs` covering
  the play-relevant projection fields — spawn, per-layer collision/scrollSpeed/repeat — and the
  buffer-preservation invariant: projecting a `ResolvedLevel` for playtest touches neither the session's
  dirty flag nor its undo history, and editing continues normally afterward).
- `get_editor_errors`: clean of `game/`/`src/` code (all log lines present are the MCP addon's own
  transient-script diagnostics).

## 8. Open Questions for Toni

1. `editor_playtest` currently has no dedicated gamepad button (radial-only on gamepad) — worth a
   dedicated button (e.g. Back/Select, currently free) for a one-press launch, or is radial access enough?
2. The new platform ended up on a layer named `"Layer 1"` (the `LayerManagerPanel`'s auto-name) — no
   rename UI exists yet; acceptable for now, or worth pulling layer-rename forward?
