# Design: Editor menu surfaces — mouse + keyboard as first-class, and the surface rule that replaces the radial where it cannot scale

**Repo (canonical):** `docs/architecture/editor-menu-surfaces.md` on `C:\dev\claude\uberkarl` (written in the working tree, NOT committed — Toni owns git). The DiVoid node mirrors it.

*Sarah (architect), 2026-08-19. Design-only — no implementation, no git, no PR.*

Source task **#8521** · editor input architecture **#7440** · pop-in menus **#7445** · editor UI v2 **#7466** · package browser **#7470** · tile scalability **#7450** · behavior authoring **#8049** (M4/M4b/M5 depend on this) · vision **#7407** · editor harness gap **#8505** · inert-surface ruling **#8237** · project **#7396**.

Load-bearing standards: **Design Contracts #1136** (§5 Pre-Design Checklist walked as §13 below) and **Code Contracts #114 §0**. The architect-template addendum of 2026-08-17 (**#1220**, from Toni's #8237 ruling) is treated as binding: *a design may only specify members the phase it belongs to will implement and reach; everything beyond is prose, never a member.*

> **AMENDED 2026-08-19 — see the addendum at the end.** QA #8545 of U1 produced two rulings: §6's gamepad release-on-neutral delta is **withdrawn** (the positional-aim fix stays and completes in U1), and §9's fifth property is **re-homed to a new milestone U2b**. Read §6 and §9 together with the addendum.

---

## 1. The ask, verbatim

> *"we need to focus on making the ui a bit more usable first - the following points:*
>
> *- currently we focused on gamepad input, but for scripting, and also to not have the need to have a gamepad around all the time we need a working mouse+keyboard - currently its not possible to use mouse and keyboard. the menus open on keyhold, but then the mouse doesn't interact with the menu (focus of mouse stays in level)*
>
> *- radial menus will not work out in long term if used like that - currently more and more items are added to a single menu. as soon as you have more than 8 items this turns awkward in handling and visibility. for regular action menus this might be a matter of cleaning menu structures, for menus like tiles its just completely impossible since a tileset can potentially have a next to infinite amount. So tile selection should always be some kind of list/browser, but just a first thought"*

Two complaints, one design, because they share a seam: **which surface renders a menu** is the same question as **which devices can operate it**.

---

## 2. Diagnosis — what is actually broken, verified in source on `main`

Three findings. The first reframes the fix from "a focus architecture problem" to something much smaller; the third is the one that decides the phase.

### F1 — the radial contains no mouse code at all

`PopInMenu` (`game/Editor/PopInMenu.cs`) sets `MouseFilter = Stop` in `_Ready` and never reads a mouse event. Its only handler is `_GuiInput`, which branches on `editor_paint`/`ui_accept` and `editor_erase`/`ui_cancel` — keyboard and gamepad actions. There is no `InputEventMouseMotion` branch, no `InputEventMouseButton` branch, and there are no per-wedge `Control`s: the wheel is drawn entirely in `_Draw`. It is added to the tree after `EditorCanvas`, so `MouseFilter = Stop` genuinely does swallow the click — the canvas does not paint underneath.

So Toni's *"focus of mouse stays in level"* describes the symptom accurately, but the cause is not a Godot focus bug. **The menu eats mouse input and does nothing with it.** That half of the fix is small.

> **CORRECTION (DiVoid #8654/#8656):** the sentence *"it is added to the tree after `EditorCanvas`, so `MouseFilter = Stop` genuinely does swallow the click"* records an unverified runtime property as a source finding, and it is provably false. `PopInMenu`'s runtime rect was degenerate (measured `size = (135.0, 0.0)`, later `(0, 0)`) because `SetAnchorsPreset(FullRect)` ran in `_Ready` *after* `AddChild`, so Godot's `keep_offset:false` anchor math preserved the control's then-current `0×0` rect as its permanent offset. `gui_get_hovered_control()` was `null` with the radial open — nothing was swallowing the click; nothing could ever reach `PopInMenu` at all. Fixed in `game/Editor/EditorLayout.cs`. Map node `#8210` repeated the same claim and is corrected alongside this one.

### F2 — hold-to-reveal structurally excludes the mouse, independent of F1

`LevelEditor._Process` polls four `HoldWatch` instances against `editor_menu_tiles/layers/actions/context`. While `activeTrigger != Trigger.None` the aim is re-read every frame and **release commits**. For the three non-mouse triggers the aim vector is `Input.GetVector(...)` over the four cursor actions — stick, D-pad and arrow keys only.

The consequence is not a missing feature, it is an impossibility: to keep the menu open you must keep the key held, and the hand holding `1` is the hand that would otherwise be on the mouse. Even with F1 fixed, a keyboard+mouse user cannot let go of the trigger to point at a wedge. **A menu that only exists while a key is held cannot be operated by a pointing device.**

This is why the fix is not "add mouse handling". Mouse handling without a way to keep the menu open buys nothing.

*(Related, minor, and fixed as a side effect: the mouse context radial feeds `RadialMenuModel.IndexAt` a **pixel** offset while the neutral dead-zone is `0.35` — expressed as a fraction of a normalised aim. A pixel offset clears 0.35 within one pixel of the centre, so the mouse context wheel has, in practice, no neutral centre and no distance bound at all: a wedge stays "aimed" from anywhere on screen.)*

### F3 — the radial's item count is already unbounded, and the Layers menu is the proof

`BuildTilesMenu` concatenates three collections into **one flat wheel with no cap**: every palette tile, then every terrain, then every object type. `BuildActionsMenu` is a fixed set of **11** wedges — already past Toni's threshold today. `BuildLayersMenu` produces one wedge per layer plus a trailing `Manage…`.

Toni's separation of "action menus = a structuring problem" from "tiles = structurally impossible" is right, but the boundary is not where the wording suggests. **Layers sits on the tiles side of it**: the layer count is a property of the level, not of the code, and nothing in the editor caps it. A nine-layer level produces a nine-wedge radial exactly as unavoidably as a nine-tile tileset does.

That observation is what makes the surface rule (§4) mechanical rather than a matter of taste.

---

## 3. Scope

### In scope

- The mouse and keyboard become first-class operators of every editor menu, with gamepad parity preserved.
- A rule deciding which surface a menu gets, made structural rather than remembered.
- Conversion of the Tiles and Layers menus to the list surface.
- Extraction of the list surface from `PackageBrowser` so there is one list widget with two callers.
- Trimming the Actions menu to fit the radial, without losing gamepad reachability of anything it currently reaches.
- Extraction of the menu-lifecycle sequencing into engine-free, unit-testable form (the #8505 obligation for the dispatch this design touches).
- The constraints this places on #8049's M4, M4b and M5.

### Out of scope — listed, not merely absent

| Out | Why |
|---|---|
| **Mouse-only operation** (no keyboard) | Toni's ask is *"a working mouse+keyboard"*, and #7407 locks the platform to desktop, so a physical keyboard is always present. Designing a pure-mouse route to every surface means an on-screen entry point for each menu — a second toolbar — bought for a user who does not exist. |
| **Free-text source editing** | #7440 already ratified that scripting is keyboard-only, and it is #8049 M5's deliverable. This phase makes the *menus* usable so M5 can be built; it does not build M5's text surface. §12 names what it forces on it. |
| **Tile tags / categories (#7450)** | A scrollable list is unbounded-correct on day one. Categories are a *findability* optimisation over a list that already works. #7450 stays open and is now cheap, because it becomes a filter over a shipped surface rather than a rescue for a broken one. |
| **A grid layout for tiles** | The vertical list is the shipped, focus-contained, mouse-clickable, gamepad-navigable shape. A grid needs 2D focus containment and is a second layout mode in the same widget — a knob (#1136 §3) bought before the list has been shown to be cramped. |
| **Per-device menu surfaces** | Rejected with reasoning in §10. |
| **Nested radials** | One level of overflow is needed (§6). It is served by the list, which already exists, rather than by a radial-opens-radial stack, which does not. |
| **A headless `LevelEditor` harness** | #8505's question stays #8505's, and stays timed at M4b. §9 says exactly how much of it this design discharges and how much it leaves. |
| **Dropping the toolbar** | Still gated on relocating the status readout (#7466 part 3). Unchanged by this design; the toolbar is in fact load-bearing here as the mouse's fast path to common file/edit commands. |
| **Merging `LayerManagerPanel` into the layer picker** | §7 explains why the conversion of the layer *picker* does not create a mirror with the layer *manager*. Merging them is layer-UX work, not scaling work. |

---

## 4. The surface rule

**A menu is rendered as a radial only when its item set is fixed in code and holds at most 8 items. Every other menu is a list.**

The list is the default; the radial is the optimisation. This is Toni's *"tile selection should always be some kind of list/browser"* generalised to a single rule, and it is one-directional — nothing is ever forced onto the radial.

### Why "fixed in code" and not a number

A count threshold alone is a rule someone has to remember, and #8237 is the standing evidence in this codebase that remembered rules fail silently. The property that actually matters is **who controls the count**:

- **Fixed in code** — the item list is written out in the builder. Its size is visible in a diff, and growth is a change a human reviews. The radial's value (direction-as-identity, muscle memory, one-gesture commit) depends on exactly this stability.
- **Derived from content** — tileset, package, level, objectset. The editor cannot cap it, the author changes it, and direction-as-identity is meaningless because wedge 3 is a different thing in every level.

### Made structural, at zero cost

The rule is observable in a signature, so it needs no flag, no enum and no routing table:

> **The radial renders only a menu produced by a builder that takes no content parameters. A builder that takes editor content as input produces a list menu.**

Today that is already literally true of the code: `BuildActionsMenu()` takes nothing; `BuildTilesMenu`/`BuildLayersMenu` read palette and level state. The rule names the existing shape rather than imposing a new one.

The ≤8 half gets one guard: **the radial's single entry point refuses a model carrying more than 8 items.** That is one branch guarding a scenario that is not hypothetical — it is the state of `main` today (11 wedges) — so it is not a defensive guard for an impossible case (#1136 §6). It is red on arrival, which is what forces the Actions trim into this phase rather than leaving it as an intention.

### What the rule decides, applied

| Menu | Item set | Surface | Change |
|---|---|---|---|
| Tiles (+ terrains + object types) | tileset / objectset content | **list** | converted (U3) |
| Layers | level content | **list** | converted (U4) |
| Actions | fixed in code, 11 today | **radial**, trimmed to 7 | trimmed (U5) |
| Actions overflow | fixed in code | **list** | new (U5) |

After U5 there is exactly **one radial** in the editor and **one list widget** with several callers. That smaller end state is the KISS argument for doing the Layers conversion nobody asked for: leaving Layers on the radial keeps two selection surfaces alive *and* leaves a level that adds a ninth layer hitting the cap guard at runtime.

### A list surface has no continuous aim to track (added U3, QA #8605)

The radial's per-frame loop (`StepOpenMenu`) exists to track a continuously aimed direction — a stick deflection or a mouse position relative to a centre — while the trigger is held. A list surface has no such concept: its rows are focus-chained buttons, resolved by the choice list's own focus/click callbacks, not by a polled direction. Two consequences follow directly and apply to every menu this rule converts (Tiles now, Layers/Actions-overflow later):

- **Its trigger always auto-latches, tap or hold alike**, rather than passing through the radial's Transient aim-tracking phase first — a list has nothing for that phase to do.
- **It is excluded from the per-frame aim-stepping loop.** Driving it from that loop would poll a direction the surface never reads.

A corollary specific to Tiles: two physical triggers (the dedicated Tiles trigger and the right-mouse hold, formerly its own Context radial) both resolve to this one list-only surface, so there is no second, radial, fallback to keep in sync with it.

---

## 5. Decision — one menu model, one dispatch seam, two surfaces, per-device affordances inside each

This is the fork Toni named ("*whether the three input modes share one menu model or diverge*"). The answer:

**One model. One dispatch seam. The surface is chosen by the menu's cardinality, never by the device. Devices differ only in how they drive a surface they share.**

- **`MenuOutcome` remains the seam** and does not change shape. It already carries no Godot type and no callback, and `LevelEditor.Dispatch` already funnels every pick onto the same handlers the toolbar and hotkeys use. The list produces `MenuOutcome`s exactly as the radial does, so **there is no second dispatch path and no second `UpdateState()` tail** — which is the structural answer to the CF-4 class of defect (#8505), not a rule anyone has to remember.
- **`RadialMenuModel` is renamed to `MenuModel`** (and `RadialMenuItem` to `MenuItem`). The alternative — a `ListMenuModel` beside it — is a parallel mirror pair, which #114 §5.4 and #1136 §1 both reject. The rename is mechanical across `PopInMenu`, `LevelEditor` and `RadialMenuTests`.
- **The model's radial-specific geometry helpers move off it.** `IndexAt`/`Resolve` on the model are thin delegations to `RadialGeometry` and belong with the radial surface now that the same model also feeds a list; the surface-neutral bounds-checked `OutcomeAt` stays.
- **Icons stay a positional provider supplied at summon time**, exactly as `PopInMenu.Open` takes one today. This is not inertia: `src/Uberkarl.Editor/Input` is engine-agnostic and cannot hold a `Texture2D`. The existing pattern is correct for the right reason.
- **No `Detail`/secondary-text field is added to `MenuItem`.** `PackageBrowser`'s rows carry file sizes; none of the menus this phase builds have a secondary value. The list's row input keeps its own two-part text shape and the menu driver passes an empty secondary. A field with no consumer in the phase that introduces it is the #8237 shape.

### Per-device affordances

| | Radial (Actions) | List (everything else) |
|---|---|---|
| **Gamepad** | hold Select → flick stick/D-pad → release commits. **Unchanged.** Latched: D-pad steps the highlight, A commits, B cancels. | already first-class: focus-chained `Button`s, `ui_accept` on pad A, `ui_cancel` on pad B. **Unchanged.** |
| **Keyboard** | tap `3` → latched → arrows step the highlight → Enter commits, Esc cancels. (Hold `3` → flick → release still works.) | focus chain + Enter + Esc. **Unchanged.** |
| **Mouse** | latched → hover highlights the wedge under the pointer → left-click commits → click outside the wheel cancels. **New.** | rows are real `Button`s: click activates; the header ✕/← button dismisses. **Unchanged.** |

Note what this table says about the second half of the problem: **the list surface is already device-uniform.** `PackageBrowser` solved mouse+keyboard correctly the first time by building out of real focusable `Button`s. The mouse+keyboard problem is specifically a *radial* problem, and moving the unbounded menus to the list resolves most of it by construction rather than by adding device handling.

---

## 6. The mechanism — latching, and what release means

> **AMENDED by the 2026-08-19 addendum — Ruling 1.** The release-on-neutral latch below is **withdrawn**; a **tap** latches instead, and hold-release keeps its shipped commit/cancel semantics exactly. The geometry split below is corrected from *phase* to *device*. Read this section with the addendum.

The single behavioural change that makes mouse+keyboard possible:

> **A menu opens when its trigger crosses the hold threshold. Releasing the trigger while a wedge is aimed commits that wedge — unchanged. Releasing the trigger with no wedge aimed *latches* the menu open instead of dismissing it. A latched menu no longer watches its trigger; it is dismissed by committing or by cancel.**

One rule, three devices, no per-device branch.

- **Gamepad hold-flick-release is bit-for-bit unchanged.** The shipped gesture, the one that works, passes through the same path it does today.
- **The keyboard gets a latched menu from a tap**, because a tap is a release with nothing aimed. Tap is currently a dead gesture on all three non-mouse triggers (`HoldWatch.ReleasedAsTap` is consumed only for the right-button erase), so nothing is displaced.
- **The mouse is freed**, because the menu no longer depends on a held key. This is the whole point: F2, not F1, is the blocker.

### The one gamepad delta, stated rather than hidden

Today, releasing a trigger on the neutral centre **cancels** (`PopInMenu.Commit` with `highlighted == -1` raises `Cancelled`, and the hub reads *"release to cancel"*). Under this rule it **latches**. That is a real change to shipped gamepad behaviour and it is the only one.

Accepted, because: the escape is still one button (B / `ui_cancel` / `editor_erase`, all already wired); a latched wheel is strictly more useful than a vanished one; and the alternative — latch only on tap, cancel on neutral release — makes the latch gesture unavailable to the mouse's right-hold trigger and forces a per-device branch into the one rule that is currently device-free. The hub caption changes accordingly. **If Toni dislikes the delta, the fallback is named in §14 and costs one condition.**

### Aiming a latched radial

Two input geometries, because they genuinely are two:

- **Held aiming is directional.** A stick's magnitude is bounded and carries no position; the existing direction-plus-dead-zone bucketing (`RadialGeometry.IndexAt`) is correct and unchanged. The mouse's right-hold-drag-release gesture is also directional and keeps using it.
- **Latched pointing is positional.** A mouse pointer has a real distance from the centre, so the radial needs a hit test bounded by an inner and an outer radius — a new pure sibling in `RadialGeometry`. A hit outside the wheel is no wedge, and a click there cancels; this also removes the pixels-versus-fractional-dead-zone defect noted in F2.
- **Latched stepping is discrete.** Directional input on a latched radial steps the highlight around the wheel rather than pointing at it, reusing `AnalogStepGate` (which already exists to turn held analog/digital deflection into single steps, and already has `Prime` to prevent a jump on open) and `CyclicSelection` (which already wraps an index). No new primitive. Mouse hover and keyboard stepping both write the same highlighted index; last writer wins, which is the correct and expected behaviour.

### Dismissal, unified

`ui_cancel` — Esc, pad B, the list header's ✕/← button, or a left-click outside the wheel — dismisses the open surface: one step back in a multi-step list flow, or entirely for a radial. While a menu is open its own trigger is inert, which falls out of `_Process`'s existing early return and needs no new state.

### A refused open must undo the arbitration's own step, not just its own (added U5, DiVoid #8635 W-1)

`MenuTriggerArbitration.TryOpen` steps `menuSession` to Transient/Latched *before* its caller opens the menu — the arbitration commits its own state change ahead of the open it is arbitrating. If the open then fails, the failure handler must undo both `activeTrigger` and `menuSession`, not just `activeTrigger`: `menuSession`'s step already happened one level up and nothing else will unwind it. Skipping the `menuSession` half degrades a refusal from "nothing opened this frame" to a wedge that blocks every future trigger.

### Validate before writing menu-open state, not after (added U5, DiVoid #8635 W-1)

A trigger's open path validates (e.g. `MenuCatalog.EnforceRadialCap`) *before* `activeTrigger` is set, even though the same validation also runs downstream inside `PopInMenu.Open`. A refusal that only lands downstream has nothing left to undo it, because `activeTrigger` is already committed by the time control gets there. The guard belongs at the point the menu model is built, ahead of the state write it is guarding — not wherever it happens to already run.

### Skip the discrete highlight step when a resolve or cancel already landed this frame (added U5)

`StepOpenMenu`'s discrete (Latched) branch steps the highlight only when nothing resolved this frame. Stepping first and reading the resolve afterward would move the highlight between the click and the read, committing whichever wedge the step landed on rather than the one the user actually aimed at.

---

## 7. The list surface — extract it, do not grow `PackageBrowser`

`PackageBrowser` contains two separable things: a **package flow** (source, summaries, save targets, name collisions, confirm-overwrite, keyboard naming) and a **generic summoned list** (backdrop, centred panel, header with title and back/close, scroll container, row construction, focus containment, empty state, cancel handling).

**Decision: extract the generic list into its own summoned `Control` — a choice list — with `PackageBrowser` as its first driver and `LevelEditor` as its second.**

### The DRY math (#1267, quoted as required)

The generic half spans `BuildLayout` (43 lines), `PopulateList` (38), `ContainListFocus` (12), `Close` (5), `_GuiInput` (9), `_UnhandledInput` (9) and the row text record (1) ≈ **117 lines**, needed at **2 sites**. `117 × 2 = 234` inlined versus `117` extracted plus roughly ten lines of driver per site. The threshold is ~15–20. Extraction is not a preference here; it is mandatory.

### The alternative, weighed and rejected

Adding a menu step to `PackageBrowser` avoids the extraction entirely and touches fewer files. It is rejected because it makes one class own two unrelated lifecycles: its step enum would carry package steps beside menu steps, and its `ResourceChosen`/`SaveRequested` events do not describe a tile pick. That is #1136 §2 form 2 (a parallel layer) arriving from the inside — the class becomes the parallel layer rather than gaining one. Extraction produces *one* list widget with two callers, which is the opposite shape.

### Ownership and wiring

One instance, owned by `LevelEditor`, handed to `PackageBrowser` — **the established pattern in this codebase**, identical to how the single `OnScreenKeyboard` instance is created by `LevelEditor` and attached to the browser, the layer manager and the tileset editor. No invention, no arbitration, and `AnyModalOpen()` gains one polled `IsOpen` and loses none.

### Contract (abstract)

| Concern | Owned by the choice list | Owned by the driver |
|---|---|---|
| Backdrop, panel, header, scroll, empty state | ✓ | |
| Row construction from a supplied count + per-index text (+ optional icon) | ✓ | |
| Focus containment and initial focus grab | ✓ | |
| `ui_cancel` handling and the mouse-clickable back/close affordance | ✓ raises a dismissal request | ✓ decides back-a-step vs. close |
| `IsOpen` for `AnyModalOpen()` | ✓ | |
| What the items *are*, and what a chosen index *means* | | ✓ |
| Step sequencing (packages → resources → confirm → name) | | ✓ |

The menu driver's mapping is trivial: a `MenuModel`'s items become rows, and a chosen index becomes the item's `MenuOutcome` handed to `LevelEditor.Dispatch`. Same seam, same tail.

### Why converting Layers does not create a mirror with `LayerManagerPanel`

It removes one. Today layer *selection* lives on the radial and layer *management* lives in `LayerManagerPanel`, reached through a `Manage…` wedge. After U4, selection is a list and `Manage…` is a row in it — the two surfaces stop being two shapes for one subject. `LayerManagerPanel` keeps its own job (rename, add, remove, reorder) and is not touched.

---

## 8. Menu content becomes data — the seam that makes menu size testable

The three menu builders live in `LevelEditor` (Godot glue) and are therefore untestable, which is why "the Actions menu has 11 wedges" was never caught by 655 tests.

**Decision: the builders move into the engine-free input core as pure functions over primitive editor state** — palette tile ids, terrain labels, object-type labels, layer names — returning `MenuModel`s. `LevelEditor` supplies the state and routes the result to a surface.

This is a move, not a duplication, and it buys three things that matter to this design specifically:

1. **The Actions menu's size becomes a unit-testable fact**, red today at 11, green at 7 after U5. The cap stops being an intention.
2. **The rule from §4 becomes visible in the signatures**: the parameterless builder is the radial-eligible one, and the parameterised ones are not.
3. **The tiles menu's index arithmetic becomes testable.** The tiles menu concatenates three collections into one index space; `Dispatch` currently papers over the seams with bounds checks. The off-by-one that bounds check exists to survive is exactly the class of bug an engine-free test pins.

No new vocabulary is introduced. The catalog holds the menus this phase builds and reaches, and nothing else.

### The shared choice list can gain more than one driver (added U3, QA #8605)

§7's "Ownership and wiring" already established one choice-list instance with multiple callers (`PackageBrowser`, then `LevelEditor`'s Tiles trigger). Once a second driver exists, two things follow and must hold for every future driver added the same way:

- **`AnyModalOpen()` polls the choice list's own `IsOpen` directly**, never through a proxy that aliases to it (e.g. `PackageBrowser.IsOpen` forwarding to `choiceList.IsOpen`) — an alias resolves to whichever single driver minted it and goes stale the moment a second driver can hold the list open that the alias's owner does not know about.
- **Every driver's own open-attempt guards on `AnyModalOpen()` before summoning the list**, so two drivers can never fight over the same widget's fields (one driver's `Open()` call stomping the row/callback state another driver mid-flow is relying on). For `PackageBrowser` specifically, the guard is not inline in `SummonBrowser()`/`SummonSaveBrowser()` — it sits one layer up, in the paths that can reach them: the global hotkeys, the toolbar's auto-hide gate, and the Actions radial dispatching only after `CloseMenu` has run. The invariant holds either way; a future driver added the same way should place its own guard at whichever layer actually gates its open-attempt, not assume it must be inline in the summon method itself (QA #8616 W9).

---

## 9. Verification — what this discharges of #8505, and what it leaves

> **AMENDED by the 2026-08-19 addendum — Ruling 2.** The fifth property below is **deferred to a new milestone U2b** and is not satisfied by U1. A separate, cheaper *entry-side* guard (openability as a step input) closes the class that actually shipped a defect. Read this section with the addendum.

#8505 is explicit that extracting a pure **predicate** (the `CursorInputGate` precedent) does not pin a **sequencing** property, and that adopting that pattern here would buy a test that passes while the bug ships. That warning applies directly: latching *is* a sequencing property.

**This design takes #8505's own first candidate — extract the whole handler into a pure function over an explicit state record, returning the transition rather than performing it.**

### The menu session

A small pure type in the engine-free input core holds the menu lifecycle: closed, transient (trigger still held), or latched. It is stepped once per frame with the trigger edges (each already produced by `HoldWatch`, which is engine-free today but lives in `game/` and is untested — it moves into the core with the session and gains tests), whether a wedge is currently aimed, and whether cancel or confirm was requested. It returns the new state **and the effect**: open this menu, latch, commit, or close.

The properties it pins, as *sequences* rather than truth tables, each verified red before the change:

- a hold opens exactly once, not once per frame past the threshold;
- release with an aimed wedge commits exactly once;
- release with no aimed wedge latches, and the trigger stops being watched thereafter;
- while a menu is open no other trigger opens one;
- every terminal path — commit and cancel alike — produces exactly one `Close` effect.

That last property is the CF-4 shape from #8505: **one transition, therefore one tail**, so the glue's `EndMenu()`/focus-restore has a single caller by construction rather than by discipline. It is the sequencing property a resolver could not have expressed.

### What remains glue-only, stated honestly

Focus containment, `MouseFilter` behaviour, `AcceptEvent` ordering between `_Input`/`_GuiInput`/`_UnhandledInput`, and mouse hit-testing against a drawn wheel cannot be reached engine-free. These are verified as every prior editor increment was — Godot MCP with simulated gamepad, keyboard and mouse, plus raw `InputEventJoypad*` injection for the double-fire class that `simulate_action` cannot reproduce (#7445 round 2) — and **real-pad and real-mouse confirmation remains Toni's**. `simulate_mouse_move` does not update `GetViewport().GetMousePosition()` (#7445), so latched mouse *hover* must be verified against injected `InputEventMouseMotion` reaching `_GuiInput`, not against the polled pointer position.

**#8505 is narrowed by this design, not closed.** The menu-lifecycle half of the editor's sequencing risk is lifted out; the paint-mode routing matrix — its original subject — is untouched and still lands at M4b, where the matrix grows from three modes to four.

### The guard-sizing carry-over stands

Every acceptance test below is verified **red** against pre-change code first (#7863, carried by #8049): *the question is not "does it pass" but "have I seen it fail for the reason it exists".*

---

## 10. Alternatives rejected, with reasons

| Alternative | Rejected because |
|---|---|
| **Add mouse handling to the radial and stop there** | F2. Mouse handling on a menu that only exists while a key is held buys nothing — the hand that holds the key is the hand that would use the mouse. This is the fix that looks sufficient and is not. |
| **Replace the radial everywhere with lists** | The gamepad hold-flick-release gesture works and is the shipped paradigm; #8521 is explicit that this is parity, not replacement. The radial earns its keep for a small stable set where direction *is* identity. |
| **Per-device surfaces** (mouse gets a dropdown, gamepad a radial, keyboard a palette) | Three surfaces to build, three to keep in sync, and menu *content* would drift between them because each surface would be populated separately. The device already differs only in how it drives a shared surface; that is where the difference belongs. |
| **A count threshold alone as the surface rule** | A rule someone must remember. §4 makes it a property of a builder's signature plus one guard that is red on arrival. |
| **Keep Layers on the radial** | The cap guard would then throw at runtime on a nine-layer level — the rule would have introduced a crash rather than prevented a UX regression. Content-derived means content-derived. |
| **Nested radials for the Actions overflow** | A radial-opens-radial stack is new machinery. The overflow is a short text-labelled list, which is precisely what the list surface renders, and the list already exists after U2. |
| **A `Detail` field on `MenuItem` for future secondary text** | No consumer in this phase. #8237/#1220: the seam is the deliverable, not the member. |
| **A root/context radial reaching all three menus** | Bought only for the mouse-only user, who is out of scope (§3). The right-hold gesture keeps its shipped intent by opening the tiles surface, which is now a list. |

---

## 11. Build order — five milestones, each independently shippable

> **AMENDED by the 2026-08-19 addendum — Ruling 2.** A new milestone **U2b** sits between U2 and U3. Corrected order: U1 → U2 → **U2b** → U3 → U4 → U5.

Numbered **U1–U5** to avoid collision with #8049's live M1–M6.

Dependencies: U1 is independent of everything. U2 precedes U3, U4 and U5. U3 and U4 are independent of each other. U5 needs U2. **U1 ships first** — it is the change Toni feels immediately and it touches no menu content.

---

### U1 — Latching + mouse operation of the radial, and the pure menu session

*Independent. No menu content changes, no new surface.*

- The menu session type + `HoldWatch` move into the engine-free input core with sequence tests (§9).
- Release-with-aim commits; release-without-aim latches. The radial's hub caption stops saying *"release to cancel"*.
- The radial gains mouse hover and click via its existing `_GuiInput`, using a new positional hit test in `RadialGeometry` bounded by inner and outer radius; a click outside the wheel cancels.
- Latched directional input steps the highlight via `AnalogStepGate` + `CyclicSelection`.

**Acceptance:** with the Tiles radial latched by a tap of `1`, moving the mouse highlights the wedge under the pointer, a left-click commits it and paints that tile, and a click outside the wheel dismisses it — while a gamepad hold-flick-release on the same menu still commits exactly as before. Verified red: the mouse cases fail against `main` because no mouse branch exists; the latch cases fail because release dismisses.

---

### U2 — Extract the choice list from `PackageBrowser`

*Pure refactor. No behaviour change.*

The generic list becomes its own summoned `Control`, owned by `LevelEditor` and attached to `PackageBrowser` the way the on-screen keyboard already is. `PackageBrowser` keeps its flow, its events and its steps.

**Acceptance:** every existing package load and save flow behaves identically — package list, level list, `+ New package…`, `+ New level…`, confirm-overwrite, keyboard naming, back-nav, ✕ close, mouse click, gamepad `ui_accept`/`ui_cancel`. The existing browser verification is re-run unchanged; a diff that changes observable behaviour is a defect in this milestone.

---

### U3 — Tiles move to the list

The menu catalog moves into the engine-free core (§8). Tiles, terrains and object types are summoned as one list, in the same order and with the same label prefixes the wheel uses today. The `editor_menu_tiles` trigger and the right-button hold both open it; `Trigger.Context` folds into the tiles case and is deleted. The Tiles radial is removed.

**Acceptance:** a package whose tileset carries roughly thirty tiles is fully selectable on gamepad, keyboard and mouse — scrolled, focused, clicked — and the picked tile paints. Verified red against `main`, where the same tileset produces a thirty-wedge wheel.

---

### U4 — Layers move to the list

One row per layer plus `Manage…`, which keeps opening `LayerManagerPanel`. The Layers radial is removed.

**Acceptance:** a level with more layers than the radial cap is fully selectable on all three devices and the active layer changes; `Manage…` still reaches the layer manager. Verified red against `main`.

---

### U5 — Enforce the radial cap: trim Actions, add the overflow list

The radial's entry point refuses a model above 8 items. The Actions menu is trimmed to **Open, Save, Undo, Redo, Tool, Play, More…** = 7 wedges, one spare. `More…` opens a list carrying **New, Save As, Resize…, Edit Tileset…, Bind Tileset…**.

**Reachability is preserved, which is the constraint that decides the split.** #7466 established that the Actions radial is the gamepad's route to every toolbar command — several of those commands have no gamepad binding at all. The overflow list is fully gamepad-navigable, so nothing becomes unreachable; only the number of gestures changes for the five least frequent commands.

**Acceptance:** every command reachable from today's 11-wedge Actions radial is still reachable on a gamepad with no keyboard and no mouse; the radial refuses a 9-item model; the catalog test asserting the Actions menu fits the radial goes from red to green.

---

### Do not build

Tile tags/categories · a grid tile layout · a second picker widget · nested radials · per-device surfaces · a mouse-only entry point for every menu · a headless `LevelEditor` harness · any menu vocabulary member that this phase does not also reach.

---

## 12. What this forces on #8049's M4, M4b and M5

Named now, so none of it is discovered during implementation the way M2's fork was.

### M4 — predefined descriptors + assignment/param UX

**The assignment picker is a list, not a radial.** Three independent reasons, any one sufficient:

1. The visible item set is **filtered by subject kind** (#8049 makes applicability load-bearing: `hurtOnContact` on a level script compiles and silently never fires). A set whose visible size varies at runtime is content-derived by §4's test even though the descriptors are written in code.
2. It grows with every predefined added, and will cross 8.
3. **M5 binds script resources through the same seam**, and those are unbounded. Building a radial at M4 and replacing it at M5 is exactly the rework #8521 exists to prevent.

M4 also keeps the requirement #8049's addendum already placed on it — the picker **returns** a binding to its caller rather than mutating a selected subject in place. The choice list's chosen-index callback supports that directly; no additional mechanism.

**Net effect on M4's scope: none. It gains a surface decision it would otherwise have had to make, and loses the risk of making it wrong.**

### M4b — trigger rect tool

- The tool selection that #8049 called `SelectTriggerTool` becomes **a row in the tiles list**, not a wedge. The list has unbounded room, so the concern about growing the tiles wheel disappears.
- M4b consumes M4's picker at placement time — a list, per above.
- **#8505 still lands at M4b, narrowed.** U1 pins the menu-lifecycle sequencing; the paint-mode routing matrix that grows from three modes to four at M4b is untouched by this design and remains the harness question's real subject. M4b should not assume the menu extraction covers it — that is the same mistake #8505 warns about with `CursorInputGate`.

### M5 — script resource authoring

- **The in-package script picker is a `PackageBrowser` step over the shared list**, which is what #8049 already assumed and what U2 makes cheap and correct. It is a resource-kind filter on an existing flow, not a new picker.
- **The forcing constraint, and the one worth budgeting for: a source editor cannot use ordinary focused-control key handling under the current InputMap.** `editor_paint` is bound to **Enter and Space**, and `ui_accept` is bound to Enter, KP-Enter and Space. A text surface needs both keys as *text*. So M5's editor must consume raw key events **before** action dispatch and mark them handled — precisely what `OnScreenKeyboard` already does in its `_Input` handler, and for exactly this reason. That precedent is the pattern to follow; the alternative (rebinding `editor_paint` off Enter/Space) breaks a shipped binding for every other surface.
- M5's editor must otherwise honour the same summoned-surface contract every modal here does: an `IsOpen` polled by `AnyModalOpen()` so the grid cursor freezes, contained focus, and `ui_cancel` dismissal.
- **What this design does not give M5:** a text surface. It removes the reason M5 was blocked — a UI where the keyboard is second-class — and nothing more.

---

## 13. Pre-Design Checklist (#1136 §5), answered in order

**KISS / DRY / YAGNI**

- *No new type mirroring an existing one.* `RadialMenuModel` is **renamed** to `MenuModel` rather than gaining a `ListMenuModel` sibling (§5). The extracted choice list **removes** a would-be duplicate rather than creating one (§7). Converting Layers **removes** the selection/management split (§7).
- *No abstraction with one implementation and no second.* The choice list has two callers on the day it lands (`PackageBrowser`, the menu driver) — that is the reason it is extracted. No interface is introduced for either.
- *No element justified by "we might need X later".* Tile tags, grid layout, per-device surfaces, a `Detail` field and a mouse-only entry point are all named in §3/§10 as *not built*, each with the concrete reason. The menu catalog holds only the menus this phase reaches (§8), per #1220's 2026-08-17 addendum.
- *No deprecation window, feature flag, shim or transition period.* U2 is a same-PR extraction; U3–U5 delete the old surface in the milestone that replaces it. No menu exists in two surfaces at once.
- *DRY math quoted.* §7: `117 × 2 = 234` inlined versus `117` extracted, against a ~15–20 threshold. Extraction mandatory, not preferred.

**Existing systems first**

- *Audited.* `PackageBrowser` (list rendering, focus containment, back-nav, mouse affordance), `RadialGeometry`, `AnalogStepGate`, `CyclicSelection`, `HoldWatch`, `MenuOutcome`, `CursorInputGate`, `OnScreenKeyboard`'s attach pattern, `LayerManagerPanel` — each named where it is reused. The only genuinely new pure logic is the menu session, the positional hit test, and the cap guard.
- *New layer justified concretely.* The choice list is not justified by separation-of-concerns feeling; it is justified by the #1267 math and by `PackageBrowser` otherwise owning two lifecycles (§7), with the do-nothing alternative stated and rejected rather than omitted.
- *No new persisted data point.* Nothing in this design is written into a package. Latched-ness, highlight index and menu session state are transient UI state, never serialised — the same ruling #8049 applied to the active objectset (#868).
- *Consumer chain recursed.* Every menu specified here has a live caller in the milestone that adds it: the catalog's builders are called by `LevelEditor`; the overflow list is reached from the `More…` wedge; the choice list is reached by both drivers. No member is declared ahead of its call site (#8237).

**Configurability**

- *No new config knob.* The 8-wedge cap is a design constant living in code beside the geometry, not a setting — no operator tunes it and it does not differ by environment (#1136 §3). Making it configurable would make it *"still a magic number, just with an extra layer of indirection"*.
- *No telemetry-then-tune compound.* None proposed.
- *Existing constants untouched.* The hold threshold, wedge radius, chip radius and dead-zone fraction stay where they are.

**Less is better**

- *Delete / merge / inline run on every element.* Deleted: the Tiles radial, the Layers radial, `Trigger.Context`, `RadialMenuModel`'s geometry helpers, `PackageBrowser`'s private list rendering. Merged: layer selection into one surface with `Manage…`; the mouse context gesture into the tiles trigger's case. Inlined: nothing new — the menu driver's model-to-rows mapping is a handful of lines at one site and gets no helper.
- *Trade-offs named where a change costs something.* The gamepad neutral-release delta (§6) with its fallback; U2's zero-visible-value refactor cost; the five Actions commands that gain a gesture (§11 U5); the mechanical rename churn (§5).
- *Radical-clean over compromise.* Where a menu changes surface, the old surface is removed in the same milestone. No menu is left renderable both ways "in case", which would be the compromise shape #1136 §4 rejects.
- *Reader inventory covers string references too.* The rename touches `RadialMenuTests.cs` by type name only; the `editor_*` action **strings** are reached exclusively through `EditorActionMap.NameOf` (the repo's standing rule that glue never hard-codes action strings, #7440), so no string-literal action reference exists to miss. `project.godot`'s `[input]` section is **not modified by any milestone** — no binding is added, removed or re-homed.

**Data deliverables** — none. No SQL, no migration, no backfill.

**Document discipline**

- Code Contracts (#114 §0) and Design Contracts (#1136) cited as load-bearing at the head.
- Scope and non-scope are both explicit, with a reason per exclusion (§3).
- No predecessor design is superseded end-to-end. #7440 (input architecture), #7445 (pop-in), #7466 (radial-centric v2) and #7470 (browser) all remain current; this design **amends** the pop-in paradigm at one point (release semantics) and the surface choice for two menus. Those docs need a pointer to this one, not a superseded banner — noted for the implementer as part of U1 and U3.
- No multi-paragraph rationale for things that obviously stay.

---

## 14. Open questions, and what is deliberately left closed

### For Toni — none blocking

1. **The gamepad neutral-release delta (§6).** *(CLOSED by the 2026-08-19 addendum, Ruling 1 — the delta is withdrawn; there is nothing left to ratify.)* Releasing a trigger without aiming now latches instead of cancelling. It is the only change to shipped gamepad behaviour and it is deliberate. If it feels wrong on a real pad, the fallback is *latch on tap only, cancel on neutral release* — one condition in the menu session, at the cost of the mouse's right-hold trigger losing its latch route. Worth a moment on the U1 PR, not worth blocking the design.
2. **The Actions trim split (§11 U5).** Which 6 commands stay on the wheel and which 5 move to `More…` is taste. The split proposed keeps the frequent edit/file verbs and demotes the panel-openers plus `New` and `Save As`. Re-sorting it changes one list and nothing structural.

### Deliberately left closed — named so they are not reopened by accident

- **Tile categories / tags (#7450).** The list is unbounded-correct without them. #7450 stays open and becomes a filter over a working surface. Do not fold it into U3.
- **A grid layout for tiles.** Revisit only if a real tileset makes the vertical list demonstrably cramped, with that tileset as the acceptance case.
- **Mouse-only operation.** Out of scope by §3; the ask is mouse *and* keyboard, and desktop guarantees the keyboard.
- **Dropping the toolbar.** Still gated on relocating the status readout (#7466). Unchanged.
- **A headless `LevelEditor` harness.** Stays #8505, stays timed at M4b, and §9 states exactly what U1 removes from its remit and what it does not.
- **Free-text source editing.** #8049 M5's deliverable. §12 names the one constraint this design discovers for it.

---

## 15. Status

Design complete. Every decision #8521 asked for is made in-document; the two items in §14 are ratifications, not forks. Not committed, no PR.

---

## ADDENDUM 2026-08-19 — two rulings from QA #8545 of U1. §6's gamepad delta is **withdrawn**; §9's fifth property moves to a new **U2b**

Raised by QA review **#8545** of U1's implementation — CF-4 (filed as **#8547**) and CF-3, with CF-1 as supporting evidence. Both findings are correct and both are mine: one is a justification that was never load-bearing, the other is two different guarantees conflated under one property.

**This addendum does not revise §1–§15. They stay readable — the phase is implementing against them. It corrects §6's release semantics, sharpens §6's geometry axis, and re-homes §9's fifth property.**

### Ruling 1 (CF-4 / #8547) — the gamepad delta is WITHDRAWN. The positional-aim fix stays, and completes, in U1.

QA measured that `CurrentAim()` still feeds a raw **pixel** offset for `Trigger.Context` into `IndexAt`'s **fractional** `0.35` dead-zone, so any right-hold release more than one pixel from the open point commits, so the mouse cannot latch unless held sub-pixel-still. Correct. As built, U1 pays the gamepad cost and does not collect the mouse benefit.

**The resolution is not to make §6's justification true. The justification was never sound.**

**§3 already ruled mouse-only operation out of scope**, on the grounds that #7407 guarantees a physical keyboard. §6 then changed shipped gamepad behaviour in order to give the mouse's *own* trigger a latch route — a requirement §3 had already dismissed. The two sections contradict each other, and §3 is the one that is right.

**And the requirement is transitional even on its own terms.** After U3 the right-hold gesture opens the Tiles *list*, which is latched by construction. After U5 the only radial is Actions, for which the mouse has no trigger at all — §5's own table already routes the mouse to it via a keyboard tap of `3`. So the mouse's need for a release-on-neutral latch spans U1–U2 and then evaporates. Permanently altering a shipped, working gamepad paradigm to serve a two-milestone transitional state is the wrong trade, and #8521's constraint — *"gamepad behaviour must not regress"* — should have settled it at design time.

**The corrected rule, replacing §6's opening statement:**

> A menu opens when its trigger crosses the hold threshold, or immediately on a **tap**. Releasing a held trigger with a wedge aimed **commits**; releasing it on the neutral centre **cancels** — both exactly as shipped. A **tap latches**. A latched menu no longer watches its trigger and is dismissed by committing or by cancel.

Gamepad delta: **none.** Keyboard: tap `1`/`2`/`3` latches — tap is a dead gesture on all three non-mouse triggers today (`ReleasedAsTap` is consumed only for the right-button erase), so nothing is displaced. Mouse: reaches a latched radial by keyboard tap, which is the mouse+keyboard workflow #8521 actually asked for.

Three consequences for the implementer:

- The hub caption *"release to cancel"* **stays correct** and needs no change. One fewer edit than §6 specified.
- The latch condition becomes `HoldWatch.ReleasedAsTap` — already computed, already engine-free. **`wedgeAimed` / `popIn.HasHighlight` drops out of the session's inputs entirely**, removing a glue→core coupling that was a smell in its own right. Commit-versus-cancel on release stays where it already is, inside `PopInMenu.Commit()`.
- Cost accepted and named: the gamepad gains a *second* way to open a menu (tap = latched, hold = transient) rather than one. That is additive over the shipped gesture, not a change to it.

**The positional-aim fix is NOT mooted by the withdrawal, and lands in U1.** It was mis-scoped in §6, not wrong.

§6 split the geometries by **phase** (held = directional, latched = positional) and then bolted the mouse's right-hold onto the directional side. **The axis is the device, not the phase:**

> A stick produces a *direction* whose magnitude is bounded and meaningless. A mouse produces a *position*. Stick / D-pad / arrows therefore resolve through directional bucketing in **both** phases; the mouse resolves through the positional hit test in **both** phases.

That removes a special case rather than adding one, and it is what F2's own parenthetical diagnosed three subsections before §6 contradicted it. It stays in U1 for three independent reasons, any one sufficient:

1. **It is a live shipped defect on its own terms.** With a pixel offset against a fractional dead-zone, the context wheel has no neutral centre — *you cannot cancel it by releasing near where you opened it*. The documented escape does not work. The fix **restores** shipped semantics rather than changing them.
2. Right-hold-drag-release starts behaving like a wheel (release over a chip commits; release near the centre or beyond the ring cancels) instead of a compass that honours any distance.
3. Latched mouse hover and click on the Actions radial — U5's end state — needs it regardless of everything above.

Size: it reuses the hit test U1 already built, reached from a second caller. Small, and U1's own acceptance clause is not met without it.

**§14's open question 1 is closed by this ruling** — there is no delta left to ratify on a pad. §14's open question 2 (the Actions trim split) stands unchanged.

### Ruling 2 (CF-3) — §9 conflated two guarantees. The entry-side one lands now; the exit-side one moves to a new **U2b**.

John's proportionality judgement is accepted. QA's judgement that the sizing should be bounced rather than silently absorbed is accepted. The design error underneath both is mine: **§9's fifth property describes an *exit*-side guarantee, and CF-1 is an *entry*-side failure. They are not the same mechanism, and I wrote them as one.**

- **Exit side** — *every terminal path produces exactly one `Close`, so `EndMenu()` has one caller by construction.* Requires `PopInMenu` to stop being the closure authority and become a passive renderer that records intent. That is the change John sized as out of scope for U1, and his sizing is right.
- **Entry side** — CF-1's soft-lock: `activeTrigger` set, then a null-session guard bails, leaving state that says a menu is open when none is. **A `Close` effect would not have caught this**, because the failure is not a missing exit; it is a *premature head*. Nothing terminal ever happened, so no terminal transition could have been missing.

The entry-side class closes cheaply and immediately, and it is a different fix:

> **Openability is an input to the step, not a guard after it.** Every precondition on opening — a live session, a non-empty menu — is evaluated *before* the session is stepped, so `Open` is never emitted for an open that cannot succeed, and the glue has nothing left to guard afterwards.

That is engine-free, costs one input, is pinnable **red against CF-1's exact shape**, and lands in the extraction pass already running: `TryOpenFromTriggers`'s arbitration is precisely the code CF-1 lived on, so the precondition arrives where the arbitration is already going.

**The exit-side property moves to U2b, a new milestone sequenced between U2 and U3.** Scope: *the menu session becomes the sole owner of menu-open state; `PopInMenu` and the choice list both become passive renderers that record intent rather than performing closure.*

Placement, reasoned the same way #8049's M4b addendum was — move the boundary, not the model:

- **After U2**, because the choice list exists by then, so the passivity contract is written once for both surfaces instead of written for the radial and retrofitted onto the list.
- **Before U3**, because U3 is the first milestone that summons a *second* surface for menus. Settling ownership after that means settling it with two owners already in place.
- **Not folded into U1**, which is already reviewed and whose subject is "latching + mouse". Making `PopInMenu` passive is a distinct structural change; bundling it is the tangled review one-feature-one-PR exists to prevent.
- **Not folded into U3**, for the same reason from the other side.
- U3/U4 later delete two of the four radial menus. That does **not** make U2b premature rework: if the session owns *which menu is open* abstractly, deleting two menus deletes two entries in a mapping, not a mechanism.

**What carries the guarantee meanwhile — stated plainly rather than implied.** Across U1 and U2, closure remains enforced by discipline in the glue: `EndMenu()` has two call sites, both hanging off `PopInMenu`'s two events. That is exactly the arrangement #8505 warns about, and it is accepted for **two milestones with a named end**, not indefinitely. Three things make the interim affordable:

1. The entry-side precondition closes the one class that has actually shipped a defect.
2. **U2 adds no radial open or close paths** — it is a `PackageBrowser` extraction with no behaviour change — so the unguarded surface does not grow during the wait.
3. The two `EndMenu()` call sites are enumerable, so the interim guarantee is at least *reviewable* — the same basis on which #8503 accepted CF-4's own unguarded fix.

**§9's properties are amended accordingly:** four are pinned in U1 as written. The fifth is **explicitly deferred to U2b** and is not to be reported as satisfied by U1. A milestone that pins four of five is fine; one that pins four of five while the document claims five is the thing to avoid.

**Corrected build order:** U1 → U2 → **U2b** → U3 → U4 → U5. U2b is a separate milestone and a separate PR.

### What this addendum does not reopen

The surface rule (§4), the choice-list extraction (§7), the menu catalog (§8), the M4/M4b/M5 constraints (§12) and every scope exclusion in §3 are unchanged. Neither ruling touches them.

---

## ADDENDUM 2026-08-19 — three fixes from QA #8669 of `fix/editor-modal-input-rect`: one invariant, stated once

QA **#8669** found three critical fails against the branch fixing the modal-input-rect symptoms (#8656): CF-1 (a stick/D-pad aim regressed the shipped gamepad cancel gesture), CF-2 (the new `EditorCanvas` guard swallowed a mouse-button release and left paint-drag stuck on), and CF-3 (`EraseAtGlobal`, reached from `_Process` rather than `_GuiInput`, was never covered by the guard at all). All three are fixed on that branch. This section records the two invariants the fixes rely on, so they live where the next person touching `EditorCanvas` or `PopInMenu` will meet them, rather than as narration inside the source.

### The mutation guard is a chokepoint, not a per-caller check (CF-2, CF-3) — and the chokepoint is a live predicate, not a per-frame mirror (QA #8683 CF-A/CF-B)

**`EditorCanvas` has four entry points that mutate the level document, or raise an event that leads straight to one: the gamepad/keyboard `editor_paint`/`editor_erase` branches in `_GuiInput` (which invoke `CellPressed`/`CellErased` directly), and `EmitCellAt`/`EraseAtGlobal` (the mouse and controller-driven paths). The invariant — while `AnyModalOpen()`, no input may change the level — is enforced once per site, at the instant of mutation, by calling `AnyModalOpen()` through the `MutationLocked` delegate the controller sets exactly once (in `BuildUi`). It is not enforced by guarding each call site with an independently-derived condition, and it is not enforced by a `bool` snapshotted once per frame.**

QA #8683 round 2 corrected two things about the round-1 version of this invariant, both now folded into the statement above:

1. **The entry-point count was wrong.** Round 1 stated "exactly two entry points" (`EmitCellAt`/`EraseAtGlobal`) and repeated it as a bolded claim in `EditorCanvas.cs`. There are four: the `_GuiInput` gamepad/keyboard branches raise `CellPressed`/`CellErased` directly, gated only by `CursorInputGate.AllowsPrimaryAction`, which carries no mutation-lock check of its own. No live breach was ever demonstrated through them (an open modal takes focus, closing the window in practice), but the doc claimed a completeness the code did not have — the fix is to gate all four through the one predicate, not to narrow the claim.
2. **A `bool` mirror cannot express a live predicate.** The controller used to write `canvas.LevelMutationLocked = AnyModalOpen()` once at the top of `LevelEditor._Process`, then rely on it staying valid for the rest of that frame and into the next. It cannot: in the *same* `_Process` call, `TryOpenFromTriggers()` (which can open a modal) runs *between* that snapshot and the `EraseAtGlobal` call further down, so a modal opened mid-frame erased a cell it had just been drawn on top of. `EditorCanvas` now holds the predicate itself as `Func<bool> MutationLocked`, set once in `BuildUi` to the `AnyModalOpen` method group — every site above calls `MutationLocked()` and gets the true, current answer, because there is no snapshot to go stale.

The reason a per-caller guard keeps recurring is that it has already missed a sibling **three times** before round 2 even started: the original symptom-2 fix (`EditorCanvas` soft-lock), CF-2 (the guard placed above the paint-drag release instead of inside the mutation, which swallowed the release and left `pointerDown` stuck true), and CF-3 (`EraseAtGlobal` itself, driven from `LevelEditor._Process` rather than `_GuiInput`, so a guard written only against the GUI input path never covered it at all). Gating the mutation itself, rather than every path that can reach it, means a future caller cannot reintroduce this class of defect by adding a new entry point and forgetting to guard it — there is nothing left to forget, and nothing to keep in sync with a mirror.

### The mouse is gated on the mutation lock, not on who owns the D-pad (QA #8683 CF-C)

`EditorCanvas`'s plain mouse handling (left-button press, wheel-zoom, hover/drag motion) used to bail out whenever `DirectionalInputCaptured` was true. That flag answers "does a radial, or a revealed toolbar/panel focus-zone, currently own directional (stick/D-pad/arrow) input" — it goes true even with **no modal open at all**, whenever the toolbar focus-zone is merely active (e.g. right after pressing the focus-cycle action). Godot's own click-focus does not reset that focus-zone, so once it went true the canvas's mouse stayed dead — no click, no drag, no wheel-zoom, no hover highlight — until the focus-cycle action was pressed again, on a branch whose entire purpose is making the mouse work. The fix: gate the mouse on `MutationLocked()` instead. A modal actually being open is the only condition under which the mouse should be kept off the canvas, and an open modal already takes the click by tree order regardless — the D-pad-ownership question a keyboard/gamepad guard needs to ask is simply the wrong question for a mouse event.

A related ordering point from CF-2's fix, worth stating explicitly: in `EditorCanvas._GuiInput`, the left mouse button's **release** half must always be able to clear `pointerDown`, unconditionally — a guard that swallows the release delivered while a modal owns input leaves `pointerDown` stuck true forever, and every later motion event then paints with no button held. Only the **press** half (which starts a new drag) is gated at all, and — per CF-C above — on `MutationLocked()`, not on `DirectionalInputCaptured`.

### The aim arbitration needs the highlight's source, not just its presence (CF-1)

`MenuAimArbitration.Resolve` decides, each Transient-phase frame, what a directional (stick/D-pad/arrows) reading should do to the radial's current highlight. The old code gated on aim *magnitude* alone: a neutral reading cleared the highlight regardless of what had set it. That is wrong, because releasing a held trigger with a wedge aimed must **commit**, and releasing it on the neutral centre must **cancel** — both exactly as shipped (§6, Ruling 1 above) — and once a directional reading has aimed a wedge, its own return to neutral **is** the cancel gesture, but a highlight the *pointer* set must survive a merely-neutral stick/key reading, because the mouse resolves through its own positional hit test independent of this polling.

`PopInMenu` therefore tracks which source (`None` / `Pointer` / `Directional`) set the current highlight, exposed as `HasPointerHighlight`. `Resolve` uses it to tell apart "a directional reading going neutral, clearing a highlight the directional source itself set" from "a directional reading going neutral, but the pointer holds the highlight, leave it alone" — magnitude alone cannot distinguish those two cases, which is exactly how CF-1 shipped.

### What this addendum does not reopen

Nothing above changes the addendum's earlier two rulings, or any section of the base design. It only records the rationale behind the CF-1/CF-2/CF-3 fixes (and, in the two sections revised for QA #8683 round 2, the CF-A/CF-B/CF-C fixes on top of them) so it does not live solely as prose in `EditorCanvas.cs`, `PopInMenu.cs`, and `MenuAimArbitration.cs`. The wiring that connects `MenuAimArbitration.Resolve` and `PopInMenu.SetPositionalAim`'s `AimSource` labelling to their call sites remains uncovered by any test in this suite (mutants G1/G2, QA #8683) — closing that gap needs a live Godot `Control`, which `Uberkarl.Editor.Tests`' plain NUnit project cannot provide; it is tracked as the in-engine smoke pass, DiVoid #8676.

## ADDENDUM 2026-08-19 — placement: the tile cursor follows the pointer, and the radial anchors to it independently (DiVoid #8663)

§3's device table and §14 both leave wheel *placement* unaddressed — this design never states where `menuCenterGlobal` comes from beyond `EditorCanvas.CursorGlobalCenter()`'s doc comment, and the mouse's placement behaviour before this addendum was a leftover of U3 deleting the per-trigger mouse anchor (`8f5f702`), not a decision this document made. DiVoid #8663 is Toni's ruling on the resulting complaint ("radial opens at the current tile location which is confusing when you use mouse"), and #8654 diagnosed the accompanying defect (the wheel is never clamped to the viewport). This section is the placement rule the next change needs to find, so it is not rediscovered from a diff again.

### The rule

The grid cursor now follows the pointer: `EditorCanvas` moves it to whatever cell the pointer hovers, in addition to the existing move-on-click. Painting, erasing, and the radial's default cursor-cell anchor all read the same cursor, so they follow as a consequence. The Actions radial additionally anchors to the pointer's own position rather than reaching it through the cursor cell, because the two diverge exactly at the cases that matter — the cursor cell is grid-snapped and clamped to the level bounds, the pointer is neither. Gamepad/keyboard behaviour is unchanged: the stick still moves the cursor, and the wheel still opens where the cursor sits, because neither device ever supplies a pointer position for the rule below to prefer.

### The device discriminator lives on the grid cursor, not on the trigger

`Trigger.Actions` opens from one shared keyboard/gamepad binding (`editor_menu_actions` — key `3` / pad button 6); the mouse has no trigger of its own for it (§14, unchanged). So "did the mouse open this menu" is not an available signal, and polling wherever the OS pointer happens to be would wrongly anchor a gamepad-opened menu to a pointer the player never touched. The signal used instead is `EditorCanvas.PointerDrivesCursor`: true when the pointer's own motion most recently placed the grid cursor, false when a directional (stick/D-pad/arrow) step did. `StepCursor` clears it before every move attempt, mouse hover and click set it before every `GridCursor.MoveTo`, so at menu-open time it answers "which device is currently driving the cursor" — the same question `CursorInputGate` already answers for who owns directional input, asked from the pointer's side instead.

### Hover-follow must not recenter the view (QA #8688 CF-1)

`EditorCanvas.MoveCursorToCell(cx, cy, recenterView)` takes `recenterView` as an explicit parameter rather than always recentring the way the pre-existing click path did, because the two callers need opposite answers. `UpdateHover` — driven by every `InputEventMouseMotion`, not just clicks — passes `false`: the pointer can only ever address a cell already on screen, so recentring there would scroll the view under a stationary pointer, which changes the cell now under it, which scrolls again — a positive-feedback loop with no settling point. The click/erase paths (`_GuiInput`'s button-down handler, `EraseAtGlobal`) still pass `true`, matching the pre-existing behaviour where a discrete action is allowed to pull the view to a cell that was off-screen. `EditorCanvas` is a Godot `Control` and so falls outside `Uberkarl.Editor.Tests`' plain-NUnit reach (the same gap tracked as DiVoid #8676); the finding was closed instead by an in-engine repro — a full-width pointer sweep and a stationary-pointer hold (111 cells, no drift) against the fixed `recenterView: false` path.

### The pointer position is read from `_GuiInput`, not `Viewport.GetMousePosition()`

`EditorCanvas.PointerGlobalPosition` is derived from the local position on the most recent `InputEventMouseMotion` the canvas itself received, not from polling the viewport. `Viewport.GetMousePosition()` reflects the OS-reported pointer and is not updated by a synthetically dispatched motion event (#7445) — true for the MCP harness, and equally true of any other source that injects input without moving the OS pointer. Reading the same event stream that already drives hover-follow keeps the anchor's value and the cursor's value consistent by construction, and keeps the whole feature exercisable by injecting `InputEventMouseMotion`, matching the one poll-based pointer read this file already accepted (`LevelEditor.EraseAtGlobal`'s caller, §1) rather than adding a second one.

### Resolution and clamping are pure, in `src/Uberkarl.Editor/Input/MenuAnchor.cs`

`MenuAnchor.Resolve(pointerDrivesCursor, pointerX, pointerY, cursorCenterX, cursorCenterY)` picks between the two candidate centres; `MenuAnchor.Clamp(x, y, viewportX, viewportY, viewportWidth, viewportHeight, margin)` keeps a disc of `margin` radius inside the given rect, centring on an axis too narrow to hold it. Both are plain arithmetic over `double`s, pinned in `MenuAnchorTests` without Godot — the same split this design already uses for `RadialGeometry`, `MenuAimArbitration`, and `EditorViewportClamp`. `LevelEditor.ResolveMenuCenter()` is the one glue call site that reads `EditorCanvas.PointerDrivesCursor`/`PointerGlobalPosition`/`CursorGlobalCenter()` and passes the canvas's own rect (`canvas.GlobalPosition`/`Size` — not the Godot viewport) through as the clamp target; it holds no decision of its own.

`PopInMenu.OuterMargin` (`WedgeRadius + ChipRadius + 8f`) is the single value both the backdrop disc's drawn radius and the clamp margin read, so the two cannot drift apart the way an inlined literal at each site could.

### What this addendum does not reopen

Placement is now decided for every device; §14's "mouse-only operation… out of scope" and the Actions radial's single shared trigger are unchanged. `MenuSession`, `MenuAimArbitration`, `MenuCloseArbitration`, and the `MutationLocked` chokepoint above are untouched — the anchor is resolved once, at `OpenMenu` time, and has no per-frame interaction with the latch/aim/close state machine those own.

## ADDENDUM 2026-08-20 — behavior assignment gets a menu entry, on the Actions radial (DiVoid #8802)

`editor_assign_behavior` (key `4` / pad button 4) reached `LevelEditor.OnAssignBehaviorPressed` with no menu entry anywhere — the only discoverable route to M3/M4/M5a was an unlabelled key. This addendum records where the entry landed and why, so the next surface change finds the reasoning instead of a diff.

### Surface: the Actions radial's eighth wedge, not the overflow list

Two surfaces were live candidates. The Actions overflow list (reached through the radial's "More…") already holds an analogous entry, "Level Script…" — the same operation on a fixed, non-cell-addressed subject. But that list is the file-ops neighbourhood (New/Save As/Resize…/Edit Tileset…/Bind Tileset…), and reaching it costs two gestures (open Actions, then More…) versus one for a radial wedge. Assignment is subject-*sensitive* — it acts on whatever the grid cursor is over at the moment of commit — which argues for the surface a user already has open while positioned on a cell, not the file-management list one hop further away. No dedicated per-cell "context" surface exists to prefer instead: §14's mouse-context trigger (`editor_menu_context`, RMB-hold) was repointed to the Tiles list in U3 (DiVoid #8654 §3), and reopening that assignment is out of scope here.

The Actions radial had exactly one open wedge (`MenuCatalog.RadialCap` is 8; the pre-existing menu used 7). `MenuCatalog.BuildActionsMenu` gained one `MenuOutcome.AssignBehaviorAtCursor` wedge, labelled `"Assign…"`, immediately before the trailing `"More…"` — Actions now sits exactly at the cap. `MenuCatalogTests` was updated entry-by-entry against this shape (not regenerated from the new builder), so the pinned `(Label, Outcome)` table and the radial-cap-fit test both went red first, for the reason each was written to catch, before being brought current.

### Empty cell: an honest state, not a silent no-op

Choosing "Assign…" over a cell with no object, trigger, or tile calls `LevelEditor.OpenNoSubjectNotice`, which opens the shared `ChoiceList` with the title "Assign Behavior" and a body naming the active layer, e.g. "Nothing on layer 'backdrop' under the cursor to assign a behavior to." (DiVoid #8805 CF-1 — the first cut said "Nothing under the cursor…" with no layer named, which read as false whenever the active layer was mostly empty, as `backdrop` is in the sample level) — the same honest-empty-state shape `ChoiceList`'s `emptyMessage` parameter already gives the picker itself when a subject has no applicable predefined behaviors. `LevelEditor.AssignBehaviorAtCursor` is the single lookup both the wedge and the pre-existing `4` key now route through, so the keybinding gained the same honest state as a side effect of not duplicating the found/not-found branch — it no longer no-ops silently either.

### Naming the subject: in the picker's title, not on the wedge

The wedge label stays static (`"Assign…"`), matching every sibling wedge's fixed, short caption; `PopInMenu`'s wedge-chip text has no wrapping or truncation, so a variable-length object/trigger name drawn into a 60px chip risked clipping or overlap on a surface no radial wedge has carried before. Instead, `BehaviorAssignmentPanel.Summon` takes an optional `subjectName`, and the picker's title becomes `"Assign Behavior — Object 'jump-block-1'"` (or `Trigger '…'`) when one is available — a wide label with room for it, shown at the point the user is about to commit rather than flashed on a wedge in passing. `LevelEditor.SubjectDisplayName` resolves it from the placement/trigger name already carried by `EditableLevel`; a tile subject has no per-instance name and the level script has exactly one instance, so both fall back to the bare kind, unchanged from before this addendum.

### Left for a separate task: signalling which cells resolve on the active layer

`EditorCanvas` already draws every object and trigger with its `Placement.Name`/`Name` label, so a cell holding one is visibly distinguishable from open sky before any menu opens. What is missing is narrower: nothing on the canvas shows whether the *hovered* cell would actually resolve through `FindBehaviorSubjectAt` on the *active* layer — objects and triggers resolve regardless of the active layer, but a tile only resolves when it sits on the layer currently selected in the layer list, and the grid cursor's appearance does not change to reflect that. Building that affordance (a cursor-state highlight, an icon, a status-bar hint) is a bigger change than this fix carries: it needs a per-frame "does the hovered cell resolve on this layer" query wired into `EditorCanvas`'s draw path, not just the on-commit lookup `AssignBehaviorAtCursor` already does. The empty-cell notice now names the active layer (`OpenNoSubjectNotice`, DiVoid #8805 CF-1) so a user who hits it is told why, but that is a one-shot message on commit, not a hover-time affordance. Left for Toni to decide whether to file.

### What this addendum does not reopen

The `editor_assign_behavior` binding, `FindBehaviorSubjectAt`, and the picker's own stage machine (`BehaviorAssignmentPicker`, `BehaviorAssignmentPanel`'s parameter-tuning step) are unchanged — this addendum is entry-point plumbing onto an existing, already-verified assignment path (DiVoid #8760 §A), not a rework of it.
