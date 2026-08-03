# Architectural Document: Gamepad On-Screen Keyboard + Layer Rename

Source task: DiVoid #7513 · Project #7396 (Uberkarl) · Depends on: layer editing #7501/#7502 (PR #16, `FocusGrid`/`LayerManagerPanel`), editor input architecture #7440 (PR #9) · Unblocks: Save-As naming #7552, tile naming #7551 · Vision #7407.

Status: initial increment merged via PR #19 (`feat/onscreen-keyboard` → `main`). This document is committed alongside the implementation (the "design-doc-as-deliverable" convention this repo already follows for layer editing / package browser / editor input).

## 0. PR #19 follow-up (2026-08-03) — this branch, `fix/keyboard-rename-ux`

Toni's playtest of PR #19: *"a good base aside from minor design... i expected to just select the name
and something happening (because selecting the layer in layer management does not really make sense to
me)."* Also: pressing physical Enter/Escape while the keyboard was open did not commit/cancel — it just
activated whatever grid key happened to have focus. Two small refinements, no redesign:

1. **Rename via the layer name, not a separate button.** §5.5's "Rename button" is gone. Activating a
   row's header/name cell (`ui_accept` or a click) now opens the keyboard directly, seeded with the
   current name. The header no longer sets the active layer — the Layers radial already owns that pick,
   so inside this management panel the header cell is free to mean one thing only. `ActiveLayerChosen`
   is now raised only by add/move/delete outcomes, never by a header press. The row's spatial-nav grid
   shrinks from 8 columns to 7 (header, Collision, Scroll, Repeat, Move↑, Move↓, Delete) — `FocusGrid` is
   unaffected, it's generic over row width.
2. **Physical Enter commits, physical Escape cancels — regardless of focus.** New
   `OnScreenKeyboardKeyRouter.Resolve(isEnter, isEscape)` (`src/Uberkarl.Editor/Input`, pure, unit-tested)
   returns `Commit`/`Cancel`/`None`. `OnScreenKeyboard` now overrides `_Input` (which runs before Godot's
   GUI dispatch) to intercept the raw `InputEventKey` for Enter/KpEnter/Escape and mark it handled before
   a focused Button's own `ui_accept` handling would otherwise consume Enter and merely activate itself.
   Only `InputEventKey` is inspected, so gamepad A (`InputEventJoypadButton`) and mouse clicks
   (`InputEventMouseButton`) on a grid key are structurally unaffected — they still correctly TYPE that
   key via the existing `OnKeyPressed` path. This retires the "accepted nuance" §5.3/§9 previously
   documented (Space/Enter both activating the focused key) for Enter specifically; Space keeps that
   behavior (out of scope — not raised by Toni).

Both changes verified live via Godot MCP (§8, updated); `Uberkarl.Editor.Tests` 143 → 147 (+4, the router's
Commit/Cancel/None/tie-break cases). No other files touched — no save/tileset/dimensions changes, per task
scope.

---

## 1. Problem Statement

Toni's playtest of the layer manager (DiVoid #7513, verbatim):

> *"renaming a layer per gamepad is also just not possible... We probably need some virtual keyboard or
> so anyways if godot provides this, else putting in names (level names in package aswell) would be
> impossible with gamepad."*

Every "author types a name" flow in this editor is gamepad-blocked today: layer rename was explicitly
deferred at the layer-editing increment for exactly this reason ("rename itself is deferred — it needs
gamepad text entry, exactly like Save-As naming" — `LayerNaming.cs`), and the same gap blocks Save-As
naming (#7552) and future tile naming (#7551). Godot's own `DisplayServer.virtual_keyboard_show` is
**mobile-only** — it does nothing on desktop, which is this project's target platform — so there is no
OS-provided fallback. The fix has to be a **custom in-engine on-screen keyboard**, built once as a
reusable primitive, with layer rename as the first real caller (the visible proof it works end to end).

**Success criteria.**
- A gamepad-navigable on-screen keyboard: a character grid navigated by D-pad/stick, `ui_accept` types
  the focused key, Backspace/Space/Shift/Done/Cancel all present.
- Physical keyboard **and** mouse also work — three input paths into the same buffer, not just gamepad.
- A reusable `RequestText(prompt, initial, onCommit)`-style API any future caller can summon without
  knowing anything about layers.
- Layer rename, wired through it, as the proof: open from the layer manager, seeded with the current
  name, Done applies it and it persists on save, Cancel discards.

## 2. Scope & Non-Scope

**In scope**
- The on-screen keyboard component (`game/Editor/OnScreenKeyboard.cs`) and its engine-agnostic core
  (`src/Uberkarl.Editor/Input`: `TextEntryEditor`, `KeyboardKey`, `OnScreenKeyboardLayout`).
- A **Rename** affordance on each layer row in `LayerManagerPanel`, wired end to end to
  `LevelEditSession.RenameLayer`/`EditableLevel.RenameLayer`.
- Gamepad, physical keyboard, and mouse all typing into the same buffer.

**Explicitly out of scope**
- Wiring the keyboard into Save-As (#7552) or tile naming (#7551) — the task is the reusable primitive
  plus **one** proof, not every consumer. Both are now unblocked and can adopt it directly.
- Any layout beyond a simple, reasonable character grid (no configurable layouts, no IME/composition, no
  clipboard, no interior-cursor/arrow-key text editing — a rename/filename buffer only ever needs
  insert-at-end + backspace, per the "keep it simple" mandate).
- Name-uniqueness validation on rename (layer names are plain labels; nothing else keys off them).

## 3. Assumptions & Constraints

| # | Assumption / Constraint | Confidence |
|---|---|---|
| A1 | `DisplayServer.virtual_keyboard_show` is mobile-only; desktop needs a custom in-engine keyboard. | Verified against Godot docs. |
| A2 | The proven summoned-window pattern (dim backdrop, centered panel, focus-contained grid, `ui_accept`/`ui_cancel`, `ZIndex` above the canvas) from `PackageBrowser`/`LayerManagerPanel` is the right vehicle here too. | High — same shape reused verbatim. |
| A3 | `FocusGrid` (built for the layer panel, DiVoid #7512) generalizes directly to the keyboard's character grid — it already handles ragged row widths, which the keyboard needs (digit/letter/symbol/action rows differ in length). | Verified in code (`FocusGrid.Contain`). |
| A4 | A physical key's `Unicode` field is already correctly cased/shifted by the OS — the on-screen Shift/Caps toggle is a *separate* input path (mouse/gamepad/keyboard activating a grid key), not something physical typing needs to consult. | Verified against Godot's `InputEventKey`. |
| C1 | No new `EditorAction`/input-map churn — the keyboard rides the existing `ui_accept`/`ui_cancel` bindings exactly as every other summoned panel does (input-map churn is a known bug magnet per #7449/#7466). | High. |

## 4. Architectural Overview

```
  LayerManagerPanel row header/name cell (per row — the rename affordance itself, no separate button)
        │
        ▼
  OnScreenKeyboard.RequestText(prompt, currentName, onCommit)   [game/Editor — Godot Control, summoned]
        │  grid keys (gamepad A / mouse click) → TextEntryEditor.Type/Insert/Backspace
        │  physical Enter → editor.Commit() → onCommit(text); physical Escape → editor.Cancel() (discarded)
        │  on-screen Done/Cancel keys → same Commit/Cancel path
        ▼
  onCommit callback (owned by the caller, here LayerManagerPanel.ApplyRename)
        │
        ▼
  LevelEditSession.RenameLayer(index, name)                      [src/Uberkarl.Editor — engine-agnostic]
        │
        ▼
  EditableLevel.RenameLayer(index, name)  — replaces the EditableLayer instance, reuses Cells array
        │
        ▼
  LayerModelChanged event → LevelEditor re-snapshots canvas + status  (existing refresh path, unchanged)
        │ on Save (existing path, unchanged)
        ▼
  EditableLevelWriter → .pkg → reload reproduces the new name
```

Below `OnScreenKeyboard`, everything is either pure data/logic (`TextEntryEditor`, `KeyboardKey`,
`OnScreenKeyboardLayout` in `src/Uberkarl.Editor/Input`, no Godot dependency) or an existing, unchanged
seam (`LevelEditSession`'s intent pattern, the `LayerModelChanged` refresh path, the save/load round-trip).
The keyboard itself carries **no domain knowledge** — it does not know it is renaming a layer; it only
knows a prompt, an initial string, and a callback, exactly per the `RequestText(prompt, initial,
onCommit)` shape requested.

## 5. Components & Responsibilities

### 5.1 `TextEntryEditor` — pure text buffer + caps state (NEW, `src/Uberkarl.Editor/Input`)
- **Owns:** a mutable buffer seeded from an initial string, plus a `CapsActive` flag.
- **Does:** `Insert(char)` (literal append — the physical-keyboard and Space path), `Type(normal,
  shifted)` (append one or the other per `CapsActive` — the on-screen character-key path, shared by
  letters and the digit row's symbol variants), `Backspace()` (no-op-safe), `ToggleCaps()`, `Commit()`
  (returns the buffer) / `Cancel()` (returns the original, untouched string) — mirrors
  `SteppedValueEditor<T>`'s enter/commit/cancel shape, the established convention for a summoned-panel
  edit-in-progress value in this codebase.
- **Does NOT own:** any Godot type, any layout/grid knowledge, any notion of what the text is *for*.

### 5.2 `KeyboardKey` / `OnScreenKeyboardLayout` — pure grid data (NEW, `src/Uberkarl.Editor/Input`)
- **Owns:** the fixed character grid — a digit row (with shifted symbol variants `!@#$%^&*()`), three
  QWERTY letter rows, a small punctuation row (`-.,'` / `_:;"`), and the five-key action row (Shift,
  Space, Backspace, Cancel, Done).
- **Does:** expose `Rows` as `IReadOnlyList<IReadOnlyList<KeyboardKey>>` — plain data, so the grid shape,
  the letter coverage, and the shift-display rule are unit-tested without Godot.
- **Does NOT own:** rendering, input routing, or focus wiring (the Godot glue's job).

### 5.3 `OnScreenKeyboard` — the surface (NEW, `game/Editor`, Godot `Control`)
- **Owns:** presentation and input routing only; holds no domain logic.
- **Reuses:** the `PackageBrowser`/`LayerManagerPanel` scaffolding verbatim (full-rect dim backdrop,
  centered panel, grab-focus-on-summon, `ui_cancel` closes, `ZIndex` above both existing panels since it
  can be summoned on top of either) and `FocusGrid.Contain` for the character grid's spatial navigation
  (up/down = same column in the row above/below, left/right = within the row, every edge pinned to
  itself), exactly as `LayerManagerPanel` already does.
- **`RequestText(prompt, initialText, onCommit)`:** the entire public surface. Captures whatever control
  currently holds focus (`GetViewport().GuiGetFocusOwner()`) and restores it on close — so any caller,
  present or future, gets focus handled for free without wiring a `Closed` event itself.
- **Three input paths into the same buffer:**
  1. **Gamepad/keyboard grid navigation:** `FocusGrid` moves focus; `ui_accept` fires the focused key's
     `Button.Pressed` — identical code path regardless of which device triggered it (mouse click,
     gamepad A, or a physical Enter/Space), exactly like every other summoned-panel key in this editor.
  2. **Mouse:** a `Button` click is a `Button` click; no special-casing needed.
  3. **Physical keyboard, typing directly:** `_UnhandledInput` reads raw `InputEventKey.Unicode` (and
     `Key.Backspace`) independent of which grid key currently has focus, calling `Insert` directly —
     bypassing `Type`/`CapsActive` entirely, since a real Shift key already produces the correctly-cased
     Unicode character at the OS level (A4).
- **Physical Enter/Escape (added in the PR #19 follow-up, §0):** routed ahead of the three paths above,
  in `_Input` (runs before Godot's GUI dispatch): physical Enter/Return always **commits**, physical
  Escape always **cancels**, regardless of which grid key currently has focus. The decision is
  `OnScreenKeyboardKeyRouter.Resolve(isEnter, isEscape)` — pure and unit-tested. This intentionally
  overrides the "activate whatever is focused" convention for these two physical keys specifically,
  because Toni's playtest read the old behavior as broken, not as an accepted nuance. Space is
  unaffected — it still activates the focused grid key (same as a gamepad A-button or mouse click) — since
  Toni's feedback was about Enter/Escape only and the "keep it simple" mandate rules out broadening scope
  unasked.

### 5.4 `EditableLevel.RenameLayer` / `LevelEditSession.RenameLayer` (MODIFIED, `src/Uberkarl.Editor`)
- **`EditableLevel.RenameLayer(index, name)`:** replaces the `EditableLayer` instance at `index`, reusing
  its `Cells` array and every other property — the exact same instance-replacement shape
  `SetLayerProperties` already uses, for the same reason (recorded cell-edit history re-resolves
  `Layers[i].Cells` fresh each apply/revert, so reuse keeps it valid). No-op (`false`) when the name is
  unchanged (ordinal compare); throws on an out-of-range index or an empty name (programmer-error guards,
  matching `AppendLayer`'s existing contract).
- **`LevelEditSession.RenameLayer(index, name)`:** the intent-level wrapper. Blank/whitespace-only input
  is treated as a **no-op**, not an exception — the keyboard's Cancel path never calls this at all, but a
  determined author backing a Done-committed buffer down to nothing should not crash the panel. The name
  is trimmed before being applied. Index-stable (like every other property-set intent here), so cell-edit
  history is preserved, and `IsDirty` is only set on an actual change.

### 5.5 `LayerManagerPanel` (MODIFIED, `game/Editor`)
- **Change (as of the PR #19 follow-up, §0):** the row's header/name cell itself is the rename affordance —
  there is no separate Rename button. `AttachKeyboard(OnScreenKeyboard)` — called once by `LevelEditor`
  alongside construction, mirroring how the panel already holds `session` — gives the panel a reference it
  calls directly (`keyboard.RequestText(...)`), exactly the same "the panel calls the collaborator
  directly, no extra event" pattern the panel already uses for `session`. Activating the header no longer
  sets the active layer either — that selection is the Layers radial's job; `ActiveLayerChosen` is now
  raised only by add/move/delete outcomes.
- **Rename flow:** activate the header (`ui_accept` or a click) → `keyboard.RequestText($"Rename
  '{currentName}'", currentName, name => ApplyRename(index, name))` → Done → `session.RenameLayer(index,
  name)` → on success, raise `LayerModelChanged` (the existing canvas-refresh signal) and `Rebuild()`
  (refreshes the row label + the focus-restore bookkeeping the panel already has for every other mutating
  button).
- **Modal nesting:** the keyboard can be summoned *on top of* the already-summoned layer manager. The
  panel's own `ui_cancel` handling (`_GuiInput`/`_UnhandledInput`) is guarded with `keyboard?.IsOpen !=
  true` so cancelling the keyboard never also closes the panel underneath it — the same "the modal on top
  owns input" discipline `LevelEditor` already applies to `popIn`/`packageBrowser`/`layerManager`.

### 5.6 `LevelEditor` (MODIFIED, `game/Editor`)
- **Change (minimal):** construct `OnScreenKeyboard`, attach it to `layerManager`, and OR
  `textKeyboard.IsOpen` into the three existing modal guards (`CursorInputGate.DirectionCaptured(...)` in
  `_Process`, the `menuOpen` check in `UpdateReveals`, the early-out in `_UnhandledInput`) — the exact same
  three sites `layerManager.IsOpen` was added to when the layer manager itself shipped.

## 6. Contracts & Interfaces (Abstract)

### 6.1 `TextEntryEditor` (pure)
| Member | Effect |
|---|---|
| `Insert(char)` | Appends the literal character (physical typing, Space). |
| `Type(normal, shifted)` | Appends `shifted` if `CapsActive` else `normal` (on-screen character keys). |
| `Backspace()` | Removes the last character; `false` no-op on empty. |
| `ToggleCaps()` | Flips `CapsActive`. |
| `Commit()` | Returns the current buffer. |
| `Cancel()` | Returns the original (pre-edit) text, unchanged. |

### 6.2 `OnScreenKeyboard` (Godot glue)
| Member | Effect |
|---|---|
| `RequestText(prompt, initialText, onCommit)` | Summons the keyboard; `onCommit(finalText)` fires only on Done. |
| `IsOpen` | True while summoned — the same modal-guard shape `PackageBrowser`/`LayerManagerPanel` expose. |

### 6.3 `LevelEditSession.RenameLayer`
| Input | Effect | History | Returns |
|---|---|---|---|
| `(index, name)` | Trims `name`; renames if changed | preserved (index-stable) | `LayerEditResult(happened, index)` |
| blank/whitespace `name` | no-op | preserved | `Happened:false` |
| out-of-range `index` | throws `ArgumentOutOfRangeException` | — | — |

## 7. Cross-Cutting Concerns

- **Focus containment.** The keyboard's grid uses the same `FocusGrid.Contain` + "every edge pins to
  self" technique as the layer panel, so a stick/D-pad aim can never bounce focus off the keyboard onto
  the layer panel or canvas underneath it.
- **Modal stacking.** Three layers can now be open at once in the worst case (radial → layer manager →
  keyboard). Each guards the one below it via the same `IsOpen` check pattern; `LevelEditor`'s three
  guard sites gate on the topmost (`textKeyboard.IsOpen` is OR-ed in alongside the other two), so the
  canvas cursor stays frozen and global hotkeys stay suppressed no matter how many modals are stacked.
- **Persistence.** No schema change — rename only ever changes `EditableLayer.Name`, already round-tripped
  by the existing writer/reader. Save → reload reproduces the new name exactly.
- **Observability.** Rename prints a `GD.Print` line on success, matching every other layer-management
  mutation's convention; a no-op rename (blank input, or Cancel) prints nothing.

## 8. Verification Strategy

**Authoring plane (editor scene, Godot MCP + gamepad/keyboard injection per #7407 method):**
1. Summon the Layer Manager; activate a row's header/name (no separate Rename button any more) → keyboard
   opens seeded with the current name (screenshot).
2. Type via mouse click on a grid key (still types), Backspace (click), then physical Enter (raw
   `InputEventKey`, not `ui_accept` grid activation) → commits regardless of which grid key had focus
   (verified with focus left on the "1" digit key — the old bug would have appended "1"; instead the row
   showed the typed name and printed the rename line).
3. Reopen, type again, physical Escape → cancels; name unchanged, no additional rename print line.
4. Gamepad-A-equivalent (`ui_accept` action) on a focused grid letter key still **types** that key (not
   commit/cancel) — confirmed the buffer grew by that letter with the keyboard staying open.
5. `get_editor_errors` clean (only the pre-existing MCP-harness `mcp_input_service.gd` key-lookup noise).

**Unit tests (`tests/Uberkarl.Editor.Tests`, no Godot):** `TextEntryEditor` (insert/backspace/caps
toggle/`Type` under both caps states/commit/cancel/a full typing sequence), `KeyboardKey` (display text
under both caps states, control-key kinds), `OnScreenKeyboardLayout` (row count, digit-row shiftability,
full unique 26-letter coverage, the action row's exact kind order), `EditableLevel.RenameLayer` /
`LevelEditSession.RenameLayer` (rename applies + preserves other properties/Cells array, same-name no-op,
out-of-range throws, blank/whitespace no-op, index-stable history preservation, dirty tracking), and (PR
#19 follow-up) `OnScreenKeyboardKeyRouter` (Enter→Commit, Escape→Cancel, neither→None, both-set tie-break).

**Honest gate (per #7407):** the harness injects gamepad input via Godot MCP simulated actions — **real
hardware-pad confirmation is Toni's**, stated explicitly, never claimed as "verified" by this agent.

## 9. Risks & Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Modal-on-modal input leakage (keyboard over layer manager) | A cancel/accept meant for the keyboard also affects the panel underneath | `keyboard?.IsOpen` guard on the panel's own `ui_cancel` handling (§5.5); `LevelEditor`'s three existing guards extended with `textKeyboard.IsOpen`. |
| Space ambiguity (grid-activate vs. literal space) | Minor UX surprise for a physical-keyboard-only user | Unchanged from the original increment — Toni's PR #19 feedback was about Enter/Escape only, so Space keeps activating the focused key; not broadened per "keep it simple." |
| `_Input`-level Enter/Escape interception missing a future summoned-panel case | A later modal stacked on top of the keyboard could see Enter/Escape swallowed before it gets a look | Scoped to `OnScreenKeyboard._Input`, gated on `Visible` (`IsOpen`) — inert whenever the keyboard itself is not summoned, so it cannot affect any other panel's own input handling. |
| Future callers (#7552/#7551) misuse `RequestText` assuming domain knowledge it doesn't have | Coupling creeps back into the primitive | `RequestText` takes only `(prompt, initialText, onCommit)` — no layer/tile/file awareness by construction; any domain logic lives in the caller's `onCommit`, exactly as `LayerManagerPanel.ApplyRename` demonstrates. |

## 10. Open Questions for Toni

1. **Layout shape.** QWERTY-ish with a digit row + small punctuation row was chosen for familiarity and to
   keep the grid small (per "keep it simple"). Happy to revisit if a different arrangement reads better
   on a real pad.
2. **Save-As / tile-naming adoption timing (#7552/#7551).** Both can now call `RequestText` directly —
   worth scheduling as the next two tasks, or fold in ad hoc as each is picked up?

## 11. Changelog
- v1.0 (2026-08-03) — initial implementation for #7513, PR #19: `TextEntryEditor`/`KeyboardKey`/`OnScreenKeyboardLayout`
  (engine-agnostic), `OnScreenKeyboard` (Godot glue), `EditableLevel.RenameLayer`/`LevelEditSession.RenameLayer`,
  `LayerManagerPanel` Rename button.
- v1.1 (2026-08-03) — PR #19 playtest follow-up (`fix/keyboard-rename-ux`): rename via the row's header/name
  cell instead of a separate Rename button (`ActiveLayerChosen` no longer raised by a header press);
  `OnScreenKeyboardKeyRouter` (new, engine-agnostic) + `OnScreenKeyboard._Input` route physical Enter to
  commit and physical Escape to cancel regardless of grid-key focus, while gamepad A / mouse click on a
  grid key still type it.
