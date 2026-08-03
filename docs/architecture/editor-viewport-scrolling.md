# Architectural Document: Fixed-Zoom Scrolling Editor Viewport + Discrete Analog-Stick Steppers

Source task: DiVoid #7576 · Project #7396 (Uberkarl) · Depends on: level dimensions #7550/#7575 (resize panel), editor input architecture #7440 (`GridCursor`, `EditorAction`, the shared stepper input), level-scrolling-parallax #7429/#7421 (the play camera's clamped-follow pattern) · Vision #7407.

Status: design + implementation shipped together on branch `feat/editor-scrolling-viewport` against `main`. This document is both the blueprint and the as-built record.

---

## 1. Problem Statement

Two usability bugs surfaced in Toni's resize playtest (2026-08-03), both rooted in "the editor was never exercised at a realistic level size":

**A (priority) — the editor camera fit the whole level on screen.** `EditorCanvas.Recenter()` computed a `viewScale` that shrank to fit the entire level into the panel. At a 24×16 starter size this reads fine; at anything like a real level (Toni's example: ~100 wide) it zooms out to illegibility, and for a tiny level it zooms in absurdly. Toni: *"one screen fitting the entire level is not feasible in realistic level sizes... we need more of an editor scrolling feature and a fixed zoom - perhaps the zoom can be made adjustable."*

**B — analog-stick stepper input was level-triggered, not edge-triggered.** The Resize panel's width/height steppers (`LevelResizePanel.DimensionStepper`) and the layer manager's Scroll-speed stepper (`LayerManagerPanel.ScrollStepper`) both react to `ui_left`/`ui_right` via `@event.IsActionPressed(...)`. Godot delivers a fresh `InputEventJoypadMotion` — and therefore a fresh "pressed" reading — on essentially every frame an analog stick sits deflected, unlike a D-pad *button*, which Godot never echoes. Two symptoms fell out of the same root cause: holding the stick stepped the value every frame (a fast, uncontrolled jump) instead of the D-pad's one-press-one-step feel; and opening the Resize panel with the stick still deflected from aiming the radial (Tiles/Layers/Actions menus are aimed with the same stick) instantly consumed that deflection as a step, landing on an arbitrary width the instant the panel appeared.

## 2. Scope & Non-Scope

**In scope**
- Replace the editor viewport's fit-to-level transform with a fixed default zoom + a view that scrolls to keep the grid cursor's cell in frame, clamped to the level bounds.
- A zoom control (keyboard `=`/`-`, mouse wheel, gamepad — new `editor_zoom_in`/`editor_zoom_out` actions) that steps through a small fixed ladder of zoom levels. Default is fixed; never auto-fit.
- Edge-triggered, discrete analog-stick stepping for the shared stepper input (`DimensionStepper`, `ScrollStepper`), so a stick behaves like the D-pad: one step per deflection, requiring a return to neutral before the next.
- Engine-agnostic, unit-tested clamp/centre math (`EditorViewportClamp`) and edge-trigger logic (`AnalogStepGate`) in `src/Uberkarl.Editor`.

**Explicitly out of scope** (per the task's anti-complexity note)
- Save/tileset/dimensions logic, playtest, or the menu (pop-in radial) structure — untouched.
- A "real" Godot `Camera2D` for the editor canvas (see §3 for why, and what was reused instead).
- Continuous/variable-rate analog stepping (speed-by-tilt) — the fix is discrete edge-triggering, matching the D-pad exactly, not a new continuous-input paradigm. `editor-input.md` §14 already flags "analog-stick cursor cadence" as an open question for a future increment; this ships the minimal, D-pad-matching behaviour only.

## 3. Editor Viewport: Fixed Zoom + Clamped Cursor-Follow

### 3.1 Why not literally reuse `Camera2D`

The task's framing suggested reusing `PlayRuntimeBuilder.AttachCamera`'s pattern directly. That method parents a `Camera2D` to the `Player` and sets `LimitLeft/Top=0`, `LimitRight/Bottom=width/height*tileSize` — Godot's engine-native clamped follow, free of per-frame script.

`EditorCanvas` can't attach a `Camera2D` the same way: it's a `Control` that renders the level inline via a child `Node2D` (`worldRoot`), sharing the same `Viewport` as the toolbar, panels, and every other editor UI element. A `Camera2D` changes the *whole viewport's* 2D transform — attaching one to `worldRoot` would drag the toolbar and pop-in menus around with it, not just the level. (The play runtime has the luxury of a `Camera2D` because gameplay owns the whole viewport; the editor does not.)

So `EditorCanvas` keeps its existing approach — a hand-rolled `viewOffset`/`viewScale` applied to `worldRoot.Position`/`.Scale` — but the *clamp formula* is now the exact same one `Camera2D.Limit*` encodes, reimplemented by hand and factored out into a pure, unit-tested class so the "reuse the pattern" intent is honoured in substance even though the Godot API can't be reused literally.

### 3.2 `EditorViewportClamp` (`src/Uberkarl.Editor/EditorViewportClamp.cs`)

```csharp
public static float Offset(float targetWorldCenter, float panelExtent, float levelExtentWorld, float scale)
```

Given a target world-space centre (the grid cursor's cell), the panel size, the level's pixel size, and the zoom scale, this returns the screen-space offset that:
- centres the target when the (scaled) level is smaller than the panel — same behaviour `Camera2D` gives when its limits sit inside the viewport;
- otherwise clamps to `[panelExtent - levelExtentScaled, 0]` — the exact `LimitLeft/Top=0, LimitRight/Bottom=size` rule.

Applied independently per axis. Unit tests (`EditorInputTests.cs`, "editor viewport clamp" region) cover: level-smaller-than-panel (centres), target near each edge (clamps, does not show past the level bound), and a mid-level target (centres exactly, unclamped). 100% line/branch coverage on the new class.

### 3.3 `EditorCanvas` wiring

- `ZoomLevels = { 1f, 1.5f, 2f, 3f, 4f, 6f }`, default index 3 (**3×**) — matches `PlayRuntimeBuilder.CameraZoom` exactly, so authoring and playtesting read at a comparable scale, per Toni's ask.
- `Recenter()` (fit-to-level) is replaced by `UpdateView()` (fixed zoom + clamped cursor-follow), called from every place the old fit logic ran (`SetLevel`, `OnResized`) **plus** every place the grid cursor's cell changes (`StepCursor`, the mouse-click cursor snap, `EraseAtGlobal`) — the view re-centres/re-clamps around the cursor's new cell each time it moves.
- `ZoomIn()`/`ZoomOut()` step the `zoomIndex` and re-run `UpdateView()`. Reachable three ways: mouse wheel (handled directly in `EditorCanvas._GuiInput`, alongside the existing click/hover handling — a hover-local convenience, not gated by focus); the new `editor_zoom_in`/`editor_zoom_out` device-neutral actions (keyboard `=`/`-`, gamepad right-stick vertical axis), dispatched from `LevelEditor._UnhandledInput` alongside the other global actions (Undo/Redo/Save/...).

## 4. Discrete Analog-Stick Stepper Input

### 4.1 `AnalogStepGate` (`src/Uberkarl.Editor/Input/AnalogStepGate.cs`)

An edge-trigger gate, one instance per stepper control:

- `Poll(negativePressed, positivePressed) -> int` returns `-1`/`+1` only on a neutral→deflected transition for that direction, `0` otherwise (already deflected, or neutral) — a held stick steps once, then nothing more until it returns to neutral.
- `Prime(negativePressed, positivePressed)` seeds the gate's internal deflected/neutral state from the stick's *current* position **without** firing a step. Called the moment a stepper starts listening (gains focus, or enters an edit-mode gesture) — this is what fixes "opened the panel with the stick still deflected": the already-deflected state becomes the new baseline rather than a fresh edge, so nothing steps until the stick is released and re-pushed.

Unit tests cover: fresh deflection steps once then holds with no repeat; return-to-neutral then re-deflect steps again; the two directions are independent; `Prime` from a deflected state suppresses the next immediate poll (the exact panel-open bug) and priming from neutral does not interfere with a genuinely fresh deflection. 100% line/branch coverage.

### 4.2 Wiring — distinguishing the analog stick from the D-pad/keyboard

Both `DimensionStepper` (`LevelResizePanel.cs`) and `ScrollStepper` (`LayerManagerPanel.cs`) already branch on the raw `InputEvent` reaching their `_GuiInput`. The fix adds one more branch, checked *before* the existing plain `ui_left`/`ui_right` check:

```csharp
if (@event is InputEventJoypadMotion motion && motion.Axis == JoyAxis.LeftX) {
    int step = analogGate.Poll(Input.IsActionPressed("ui_left"), Input.IsActionPressed("ui_right"));
    AcceptEvent();
    if (step != 0) Adjust(step);
    return;
}
```

`InputEventJoypadMotion` is Godot's own distinct event type for analog axis motion — a D-pad press arrives as `InputEventJoypadButton`, a keyboard press as `InputEventKey`; neither matches this branch and both fall through to the original, unchanged `@event.IsActionPressed("ui_left"/"ui_right")` handling (immediate, one-shot — D-pad buttons are never echoed by Godot, so this was already correct and is untouched). Only the **left-stick horizontal axis** (`JoyAxis.LeftX`, axis 0 — the same axis `ui_left`/`ui_right` bind to by Godot default) is routed through the gate; the vertical axis is left unhandled so up/down spatial focus navigation between rows (e.g. Width ⇄ Height) still works.

`Prime` is called at the two "this control just started listening" moments:
- `DimensionStepper.OnFocusEntered` (subscribed to `FocusEntered`) — covers the initial deferred `GrabFocus()` right after `LevelResizePanel.Summon()`, i.e. exactly the "panel opened with the stick still deflected from aiming the radial" scenario, since each `DimensionStepper` is a fresh instance per `Rebuild()`.
- `ScrollStepper`'s `ui_accept` handler, at the `edit.Enter(...)` branch (entering edit mode) — the equivalent boundary for a control whose left/right is gated behind an explicit edit-mode gesture rather than bare focus.

### 4.3 Why this needed no change to `SteppedValueEditor`

`SteppedValueEditor<T>` (the enter/adjust/commit/cancel state machine backing `ScrollStepper`) is unchanged — it only ever sees the *already-gated* `Adjust(direction)` call, exactly as before. The fix lives entirely at the device-input layer, one level below where `SteppedValueEditor` operates, which is why both the resize steppers (no edit-mode gesture) and the scroll stepper (has one) share the same `AnalogStepGate` mechanism despite differing UI shapes.

## 5. Verification

### 5.1 What the harness proved

- `dotnet build Uberkarl.csproj`: 0 warnings / 0 errors.
- `Uberkarl.Editor.Tests`: 217/217 passing (was 208 before this change; +9 for `AnalogStepGate` and `EditorViewportClamp`). Both new classes: 100% line / 100% branch coverage.
- Comment-grep (TODO/FIXME/HACK/XXX) over the diff: 0.
- Godot MCP, live editor: resized the sample level to **100×16** via the Resize panel (driven by simulated D-pad/keyboard input), confirmed a **fixed 3× zoom** (unchanged from the small-level case — no auto-fit shrink), moved the grid cursor to each edge and observed the view **scroll and clamp** correctly (screenshots at the right-edge, bottom-right-corner, and left-edge/original-terrain positions — the left/right pair is at identical zoom, proving the fix), and exercised zoom in/out (2×→4×) via the new `editor_zoom_in`/`editor_zoom_out` actions. `get_editor_errors`: 0 project-code errors throughout (only the MCP harness's own `mcp_input_service.gd` key-lookup lines from `simulate_key` calls on physical-keycode-bound keys — a pre-existing harness quirk noted in `editor-input.md`, not project code).
- Discrete-stepping regression check: repeated `ui_right` presses on the Width stepper (via the MCP `simulate_action` tool) incremented the value by exactly 1 per call, every time — the D-pad/keyboard path is unaffected by the fix.

### 5.2 A harness gap, found and documented rather than glossed over

`simulate_action` (the MCP tool used above) synthesizes a Godot `InputEventAction` (confirmed by reading `addons/godot_mcp/mcp_input_service.gd:193-198`, `_create_action_event`), not a raw `InputEventJoypadMotion`. `simulate_sequence`'s event types are limited to `key`/`mouse_button`/`mouse_motion`/`action` — none construct a joypad axis-motion event. **This harness has no mechanism to inject a raw analog-stick axis event**, so the live, in-engine `AnalogStepGate` wiring (the `@event is InputEventJoypadMotion` branch specifically) could not be mechanically exercised end-to-end through Godot's real input pipeline in this session.

What *was* verified, precisely: the `AnalogStepGate` class's edge-trigger/prime logic in total isolation (unit tests, 100% coverage); that the wiring compiles and type-discriminates correctly (`InputEventJoypadMotion` vs `InputEventJoypadButton`/`InputEventKey`), following the same pattern already used elsewhere in this file (`EditorCanvas` already distinguishes `InputEventMouseButton` from `InputEventMouseMotion` the same way); and that the un-touched D-pad/keyboard path still steps correctly one-by-one.

**Real-pad confirmation — holding the stick and observing one discrete step per flick, and confirming no jump when the Resize panel opens with the stick still deflected — is Toni's**, per the task's ask, and now doubly load-bearing given the harness gap above.

## 6. Open Questions / Follow-ups (not built)

- Zoom ladder tuning (`{1, 1.5, 2, 3, 4, 6}`) is a first guess; Toni may want different steps once authoring at real sizes.
- `editor-input.md` §14's "analog-stick cursor cadence (step-repeat vs speed-by-tilt)" question is about the *grid cursor's* movement (already repeat-based, unaffected by this change), not the steppers — still open, unrelated to this fix.
- The godot-mcp-pro harness gap (§5.2: no raw `joypad_motion`/`joypad_button` event injection in `simulate_sequence`, only `action` → `InputEventAction`) is filed to DiVoid as a documentation note so future gamepad-specific verification work knows the limitation up front.
