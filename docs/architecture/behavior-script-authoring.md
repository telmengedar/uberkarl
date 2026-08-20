# Design: Script Resource Authoring (Behavior Authoring M5)

**Repo (canonical):** `docs/architecture/behavior-script-authoring.md` on `C:\dev\claude\uberkarl` (written in the working tree, NOT committed — Toni owns git). The DiVoid documentation node mirrors it.

*Sarah (architect), 2026-08-20. Design-only — no implementation, no git, no PR.*

Phase design **#8049** (M5 is its §15 unit) · menu-surface rulings **#8525 §12** · M4 task **#8752** · M4 QA **#8760** · behavior system **#7704** · editor input **#7440** · package browser **#7470** · project **#7396** · code map root **#8056**.
Load-bearing standards: **Design Contracts #1136** (§5 checklist walked as §16), the **architect-template addendum #1220** (*a design may only specify members the phase it belongs to will implement and reach*), **Code Contracts #114 §0–§4**.

Base: `main` @ `f74371a` plus `feat/behavior-assignment-ux` (M4, commit `1283d5c`, PR #62, in review). Every source claim below was read on that tree; file:line citations are against it.

---

## 1. Problem statement

> *"Its still not possible to actually edit a script in the level editor :D — The goal is not the best tested non functioning level editor."*

M5 is the milestone that answers the original ask of the whole phase. Everything before it was prerequisite: M1 made behavior survive a save, M2 made objects placeable, M3 gave contact a direction, U1–U5 made the editor mouse- and keyboard-operable, M4 built the assignment seam. None of them let an author write a line of Pooscript.

**Success criterion, verbatim from #8049 §15:**

> One script, two placed objects bound to it, both run, package contains exactly one script resource.

Note what that forces and what it does not. It forces **sharing** — script resources are shared, not per-subject copies. It does **not** ask for a script *editor* in the IDE sense; it asks for a path from "I want this object to do something the four predefineds cannot" to "it does it", ending in a well-formed package.

---

## 2. What M4 built, and what M5 stands on

Read on the branch, not inferred. M5 adds almost no new machinery — it fills seams that already exist.

| Already built and reached | Where | M5's use |
|---|---|---|
| `editor_assign_behavior` action (key `4`, pad button 4) → `OnAssignBehaviorPressed` → `FindBehaviorSubjectAt(cursor)` → `BehaviorAssignmentPanel.Summon(kind)` | `LevelEditor.cs:442-457`, `project.godot` | The one entry point. **M5 adds no binding.** |
| `BehaviorAssignmentPicker` — engine-free stage machine, returns a `BehaviorBinding` to its caller rather than mutating a subject | `src/Uberkarl.Editor/Input/BehaviorAssignmentPicker.cs` | Extended with script choices. |
| `ChoiceList` — the shared summoned list, `IsOpen` polled by `AnyModalOpen()`, per-summon callbacks | `game/Editor/ChoiceList.cs` | The picker surface, unchanged. |
| Four assign commands + `LevelEditSession.Assign{Object,Trigger,TileBehaviorOverride,LevelScript}`, all undoable | `src/Uberkarl.Editor/` | Unchanged. A script binding travels the identical path a predefined binding does. |
| `MenuOutcomeKind.AssignLevelScriptBehavior` + its Actions-overflow row | `LevelEditor.cs:427`, `MenuCatalog` | The level-script entry, already wired. |
| `OnScreenKeyboard.RequestText(prompt, initial, onCommit)` | `game/Editor/OnScreenKeyboard.cs` | The naming path — exactly what #8049 §7 reserved it for. |

**The script data path is already complete and was completed by M1.** This is the single most important fact for scoping M5:

- `EditableLevel.Scripts` is an in-memory `ResourcePath → source` table (`EditableLevel.cs:46,180`), populated at read time by `EditableBehaviorBindings.Capture` for every script-kind binding the level declares (`EditableLevelReader.cs:103` and siblings).
- `LevelMergeWriter.BuildContributions` emits **one `PendingResource` per table entry**, `ResourceKind.Script`, media type `text/x-pooscript` (`LevelMergeWriter.cs:73-75`).
- `EditableLevelSnapshot` resolves every level-owned binding through that same table (`:78, :93, :101, :145`).

⇒ **Sharing is already structural.** Two bindings naming one `ResourcePath` are one table entry, one `PendingResource`, one resource in the `.pkg`. M5 does not build sharing; it builds the two things that are missing — **a way to put a new entry into that table, and a way to change an entry's text** — and the UI that reaches them.

Two members M4 shipped are, per #8760 §E, **unreachable from the UI today**: `SetLevelScriptCommand` and `LevelEditSession.AssignLevelScript`, because the level-script picker is always empty (no predefined applies to `LevelScript`). **M5 is what makes them reachable** — a level script is a script, and after M5 the level-script list has exactly one non-empty category. That is the milestone dependency #8760 identified and it holds.

---

## 3. Verified findings

Each of these is stated with how it was checked, per the standing rule that a design must not record an unverified runtime property as a finding.

### F1 — the InputMap collision, verbatim from `project.godot`

Read directly out of the `[input]` section (keycodes decoded against Godot's `Key` enum):

| Action | Bound to |
|---|---|
| `ui_accept` | Enter (4194309), KP-Enter (4194310), **Space (32)**, pad button 0 |
| `editor_paint` | Enter (4194309), **Space (32)**, pad button 0 |
| `ui_cancel` | Escape (4194305), pad button 1 |
| `editor_erase` | Delete (4194312), pad button 2 |

#8525 §12's premise is confirmed: **a text surface needs Enter and Space as text, and both are bound to two actions each.**

### F2 — but the collision is with `ui_accept`, not with `editor_paint`. #8525 §12's reason is wrong; its prescription is right

This matters because the wrong reason sends an implementer at the wrong target — rebinding `editor_paint`, or special-casing it — and §12 itself names that as the alternative that "breaks a shipped binding for every other surface".

Traced on the branch:

- `editor_paint` and `editor_erase` have **exactly one consumer**: `EditorCanvas._GuiInput` (`EditorCanvas.cs:270,276`). Both branches are gated on `canvasOwnsInput` **and** `!MutationLocked()`.
- `MutationLocked` is bound once, in `LevelEditor.BuildUi`, to `AnyModalOpen` — a live predicate, not a snapshot (`LevelEditor.cs:591`, and `EditorCanvas.cs:83`'s own contract note).
- `LevelEditor._UnhandledInput` **early-returns** when `AnyModalOpen()` (`LevelEditor.cs:553-555`), so every `editor_*` global action is suppressed while any modal is up.
- `AnyModalOpen()` is a disjunction of eight polled `IsOpen` properties (`LevelEditor.cs:308-312`).

⇒ **Once M5's source surface registers in `AnyModalOpen()`, `editor_paint` is doubly inert** — the canvas does not own input, and mutation is locked. No rebinding, no special case, nothing to budget for.

The hazard that *is* real is the one `OnScreenKeyboard`'s own source records, and it lives **inside** the modal, not outside it: Godot's GUI dispatch delivers a key event to the **focused Control** before `_UnhandledInput` runs, and a focused `Button` treats Enter and Space as `ui_accept` and activates itself. That is why `OnScreenKeyboard` intercepts in `_Input` — and it is why every summoned surface in this editor whose panel contains `Button`s has to think about Enter and Space.

**Ruling: #8525 §12's prescription stands — the surface must own Enter and Space before any focused control sees them — but the reason is restated. The target is the modal's own focused controls, not the canvas.** §12's "budget for it" advice is downgraded accordingly: this is a known, one-file pattern with a shipped precedent, not a milestone-sized risk.

### F3 — the one property I did **not** verify, marked as an assumption

**Assumption A1.** *A focused Godot `TextEdit` consumes Enter as a newline and Space as a space character in its own `_GuiInput`, accepting the event, so neither reaches `_UnhandledInput` and no sibling `Button` sees them.*

This is standard `TextEdit` behaviour and I believe it holds, but I did not run the engine to confirm it in this InputMap, and this phase has already paid six milestones for one design that asserted an unverified Godot runtime property as a finding (#8525 §2, `MouseFilter = Stop`). So:

- **The design does not depend on the answer.** §5.9 states the required behaviour as an *acceptance property of the surface*, not as a mechanism. If A1 holds, nothing is written. If it does not, `OnScreenKeyboard._Input` is the named pattern and the fix is one handler.
- **The check is cheap and is required by M5b's acceptance.** #8760 §A established that `simulate_sequence` gives frame-accurate remote input (and that #8757's "no cell-exact remote cursor control" premise is false — M5 depends on that correction standing). Pressing Enter into the open surface and asserting the level document is unchanged is a live walk of a few calls.

### F4 — the event dispatch matrix, and why a one-size starter template would plant the exact trap this phase named

`BehaviorEventKind`'s own doc comments say which kinds are "meaningful" per subject. The runtime is the authority; read at `game/Behavior/BehaviorRuntime.cs`:

| Subject | Events actually dispatched | Evidence |
|---|---|---|
| Tile (type default or instance override) | `onContact`, `onContactLeave` | `:257, :260` |
| Trigger | `onEnter`, `onLeave` | `:275, :278` |
| Object | `onSpawn`, `onContact`, `onContactLeave`, `onUpdate` | `:203, :299, :303, :318` |
| Level script | `onLevelStart`, `onUpdate` | `:125, :239` |

`onUpdate` — the obvious "universal" choice for a starter template — is **not** dispatched for tiles or triggers. A single `onUpdate` template would hand a tile author a script that compiles cleanly and never fires: precisely the failure #8049 made applicability load-bearing to prevent (*"`hurtOnContact` on a level script compiles and silently never fires"*). §5.5 rules accordingly.

### F5 — compiling is not a pure function of the text

`BehaviorLoader.Compile` (`src/Uberkarl.Behavior/BehaviorLoader.cs:46-60`) does three things in order: parse (`ScriptParserException` → quarantine `"parse error: …"`), **run the script's top-level init execute with the facade globals bound**, then validate that the init result is a map of handler lambdas. The runtime binds four globals — `self`, `level`, `player`, `event` (`BehaviorRuntime.cs:130-135`) — and that same composition exists a second time in the test rig (`tests/Uberkarl.Behavior.Tests/BehaviorTestContext.cs:50-56`).

Two consequences for §5.6: an editor-side check must supply globals or it cannot run at all, and it executes author code. Both are addressed there.

---

## 4. Scope

### In scope

1. Creating a new script resource in the level under edit: name it, seed it, bind it to the subject the author was standing on — in one flow.
2. Binding a subject to a script that already exists in the level's script table (the sharing half of the acceptance).
3. Editing a script's source text in a summoned, keyboard-driven text surface.
4. Validation feedback on the text, shown in that surface.
5. Making a level script authorable, which is what makes M4's `AssignLevelScript` path reachable.

### Out of scope — listed with the failure mode, not merely absent

| Excluded | Why |
|---|---|
| Script **rename**, **delete**, **reverse index**, **orphan collection** | Decided in #8049 §5.2, each with its failure mode already named. Cited, not re-argued. |
| Syntax highlighting, autocomplete, line numbers, find/replace, bracket matching, multi-caret, tabs | This is the IDE line. §5.1 draws it and says what is on the other side. |
| Editing the **tileset's** tile-type behavior scripts (`EditableTileSet` / `EditableLevel.TileScripts`) | A different document, a different session (`TileSetEditSession`), a different surface (`TileSetEditor`). M5's four subject kinds are the level-owned ones M4 already assigns. **Gap G7**, §15. |
| Listing scripts that exist in the open package but are **not** in this level's table | `EditableBehaviorBindings.Resolve` throws `LevelContentException("… has no known source")` for a path absent from the table, so offering such a pick would produce a binding that breaks the very next snapshot. Capturing them first is a real feature, not a row. **Gap G8**, §15. |
| Cross-package script references | #7594, already filed. |
| Quarantine visibility during playtest | M6. §5.6 draws the seam between M5's advisory check and M6's authoritative one. |
| Gamepad text entry for script **bodies** | Ratified by Toni in #7440 and re-ratified in #8049 §7. §5.2 says what it costs a pad-only author. |
| An "unassign / no behavior" choice | Does not exist in the editor today (`SetObjectBehaviorCommand` and its siblings reject a null binding). M5 does not add it. §5.7 answers the last-binding question without it, because *re-assignment* already reaches that state. |
| Editor-history undo of a script **text** edit | §5.8, with the cost named. The binding stays undoable — M4's commands are untouched. |
| Any change to `project.godot` | M5 adds no action and rebinds nothing. F2 is why none is needed. |

---

## 5. Decisions

### 5.1 How much editor — a plain multi-line text area, and nothing that makes it look like an IDE

**Decision: a summoned surface whose body is a single multi-line text field, using the engine's own multi-line text control. Above it, the script's name. Below it, one validation line. Two affordances in the header: close, and nothing else.**

A script in this system is short by construction. Every script in `content/sample.pkg` and in the whole test corpus is a handful of lines: a map of one to three handler lambdas. The budget the runtime enforces (`BehaviorScriptBudgets.DefaultBehavior()`: 4 000 steps, depth 8, 24 variables) is not a budget that admits long programs.

**Why the engine's text control rather than one built on `TextEntryEditor`.** `TextEntryEditor` is explicitly append-only with no interior cursor — its own doc says so, and says why: *"a rename/filename field never needs mid-string insertion, and the 'keep it simple' mandate rules out building one."* A source buffer needs a caret, newlines, selection and scrolling. Building those is a substantial pure-logic construction whose only consumer is this one surface, competing against a control the engine already ships that does it better. `TextEntryEditor` stays exactly what it is — the naming primitive — and §5.4 uses it, through `OnScreenKeyboard`, for exactly that.

**What that costs, stated:** the text buffer lives in a Godot control, so its editing mechanics are not unit-testable. That is the correct trade here and it is bounded — §6 names precisely what remains engine-free, and the buffer's *content* crosses into engine-free code the moment the surface hands it to the session.

**What is not built, each with the reason:**

| Not built | Reason |
|---|---|
| Syntax highlighting | Needs a Pooscript lexer the editor does not have. A colouring that is subtly wrong is worse than none. |
| Autocomplete | Needs a symbol model of the facades. The facade surface is small and documented; this is the definition of an IDE feature. |
| Line numbers / gutter | A ten-line file. Revisit when a real script is long enough that an error line number is hard to find. |
| Find / replace | Same. |
| Multi-file tabs | One script is open at a time by construction — the surface is summoned from a list that picks one. |

### 5.2 Gamepad — free-text bodies are keyboard-only, by ratification; everything else on the path is not

**Decision: authoring a script's *body text* is keyboard-only. This is not a new ruling — it is Toni's own, in #7440 (*"scripting is only possible with keyboard (except assignment of predefined scripts)"*), re-affirmed by #8049 §7. It is not reopened here. The on-screen keyboard does not cover it, and should not be asked to.**

Why the on-screen keyboard is the wrong instrument, beyond the ratification: it is a grid of character buttons over an append-only buffer with no caret. To make it a source editor you would first have to build the caret/line model §5.1 declines to build, and then ask an author to place that caret with a D-pad. That is not "gamepad support"; it is a worse keyboard.

**What a pad-only author can and cannot do after M5 — stated plainly, because this is the honest cost:**

| Act | Pad-only? |
|---|---|
| Assign a predefined and tune its parameters | **Yes** — M4, verified live in #8760 §A |
| Bind a subject to a script that already exists | **Yes** — a row in a list; the list is device-uniform (#8525 §5) |
| Re-bind, undo, redo, save, playtest | **Yes** — unchanged |
| Name a new script | **Yes** — `OnScreenKeyboard` is fully pad-operable |
| Create a new script (name + seed + bind) | **Yes** — the created script is a compiling, non-quarantined behavior before any typing happens (§5.5) |
| Type or change a script's **body** | **No** |

**The design consequence, which is what makes this acceptable rather than merely admitted:** the source editor is the only pad-inoperable surface, so it is placed **last in every flow and is always skippable**. Closing it with `ui_cancel` — pad B — leaves a named, bound, valid script behind. Concretely, the second half of the milestone's own acceptance case (*bind a second object to the same script*) never opens the source editor at all and is fully pad-operable.

### 5.3 Where the picker lives — one list, rows appended. #8049 §7 and #8525 §12's "`PackageBrowser` step" is **stale**; ruled

Both documents say the in-package script picker is a step on `PackageBrowser` — #8049 §7 (*"pickers extend `PackageBrowser` with a one-step 'list this package's resources of kind K' mode"*) and #8525 §12 restating it (*"a `PackageBrowser` step over the shared list … a resource-kind filter on an existing flow"*).

**That is stale in its driver attribution, and following it now would rebuild the exact shape #8525 §7 spent a milestone removing.** Three reasons:

1. **The set to pick from is not a package listing.** It is `EditableLevel.Scripts` — the level's own in-memory table. No package is opened, no resource is enumerated, no IO occurs. Routing an in-memory dictionary through a class whose job is package source selection, save targets, name collisions and confirm-overwrite is not reuse.
2. **`PackageBrowser` would again own two unrelated lifecycles.** §8525 §7 rejected exactly this — *"its step enum would carry package steps beside menu steps, and its `ResourceChosen`/`SaveRequested` events do not describe a tile pick"* — and extracted `ChoiceList` so that a driver other than `PackageBrowser` could summon a list. Since M4, `BehaviorAssignmentPanel` **is** that second driver. The extraction's whole purpose is to make §12's own prescription unnecessary.
3. **§12's intent is honoured exactly by not doing it.** What §12 was protecting against was *a new picker*. Adding rows to the picker M4 already shipped is the strongest possible form of "not a new picker" — the same widget, the same driver, the same chosen-index callback, one index space.

**Ruling: the script choices are rows in the assignment list `BehaviorAssignmentPanel` already opens. `PackageBrowser` is not touched by M5.** The *widget* prediction in §12 was right; the *driver* attribution predates M4 and is superseded.

That leaves one hazard, and it is the one #8525 §8 named by name: the list's index space now concatenates two collections, and *"the off-by-one that bounds check exists to survive is exactly the class of bug an engine-free test pins."* §6 puts the concatenation in engine-free code for that reason.

### 5.4 Create / name / edit / bind — **one flow with a branch**, plus one separate one-step flow for editing

The author's sequence, in order. Nothing here is a new paradigm; every step is a surface that shipped in an earlier milestone.

```
  cursor on a subject                              Actions overflow
  press assign (key 4 / pad Back)                  ✎ Edit Script…
            │                                              │
            ▼                                              ▼
  ┌───────────────────────────────┐              ┌──────────────────┐
  │  Assign Behavior — <Subject>  │              │  Edit Script     │
  │  ─────────────────────────    │              │  ──────────      │
  │  Hurt on Contact              │  predefineds │  patrol-fast     │
  │  Patrol                       │  (M4)        │  door-opener     │
  │  Bump on Hit From Below       │              │  (empty state if │
  │  ─────────────────────────    │              │   none exist)    │
  │  ▸ patrol-fast                │  scripts     └────────┬─────────┘
  │  ▸ door-opener                │  (M5)                 │
  │  ─────────────────────────    │                       │
  │  ＋ New script…               │  (M5)                 │
  └───┬──────────┬────────────┬───┘                       │
      │          │            │                           │
 predefined  existing      ＋ New                          │
      │       script          │                           │
      ▼          ▼            ▼                           │
  parameter    BOUND     name (OnScreenKeyboard)           │
  stepper      · done          │                           │
  (M4)                         ▼                           │
      │              create in table (template)            │
      ▼                   + BOUND                          │
    BOUND                      │                           │
    · done                     ▼                           │
                        ┌─────────────────┐ ◀──────────────┘
                        │  Script Editor  │
                        │  <name>         │
                        │  [ text area ]  │
                        │  ok / <reason>  │
                        └────────┬────────┘
                                 │ close (Done, ✕, or ui_cancel)
                                 ▼
                       text committed to the table
                       session marked dirty
                       focus returns to the canvas
```

**Answers to the question as posed — one flow or four:**

- **Create, name and bind are one flow, and are never separate acts.** An author never "creates a resource" and then goes looking for where to attach it. There is no file browser, no resource manager, no second modal to find.
- **Bind-to-existing is that same flow, two steps shorter** (assign → pick). It is the sharing case, and it is the pad-operable one.
- **Edit is reachable twice**, and that is deliberate rather than duplication: as the tail of the create flow (where the author expects to land), and as a one-step flow of its own from the Actions overflow list (where an author who is not standing on a subject can get to a script's text). One dispatch, one step, two triggers — the same shape #8525 §4 endorses for Tiles, where two physical triggers resolve to one list-only surface.

**Why the create branch binds *before* it opens the editor.** Two reasons, both structural:

1. It makes the flow safe to abandon at the only step that is not pad-operable. Close the editor with B and a named, bound, compiling script exists.
2. The source editor then has exactly one job — text — with no "did the binding happen?" branch and no way to leave a subject half-assigned.

The cost: an author who backs out of the text surface has bound a script that does nothing yet. That is **not** the inert-content shape #8237's ruling deleted, and the difference is the one #8049's own addendum drew: the author *asked* for this by picking `＋ New script…` and typing a name. The `healOnEnter` default it rejected was silent; this is chosen.

**Why `✎ Edit Script…` is a row in the Actions overflow list and not in the assignment list.** The assignment list's rows all mean one thing — *this becomes the subject's binding*. A row that means *open some text* would be a second verb in a one-verb list. The Actions overflow list is the editor's unbounded home for non-cell-addressed commands and already carries M4's `Assign Level Script`; one more row there costs one literal in `MenuCatalog` and goes red first against `MenuCatalogTests`' pinned row set.

### 5.5 A new script's initial source text — a working, per-subject-kind handler stub. This closes #8049's addendum open question 1

The addendum left this open with the instruction *"Watch it at M5; do not pre-decide it here."* Deciding it now, against the three candidates it named.

**Decision: the template is a minimal, compiling handler map containing exactly one handler — the one the runtime actually dispatches for the subject kind being assigned.**

Why not the other two:

- **Empty.** An empty file's init evaluates to nothing, so `BehaviorLoader.FromInitResult` quarantines it: *"script must end with a map of handler lambdas, but ended with nothing"* (`BehaviorLoader.cs:81`). The very first thing the author would ever see is an error about our file format, produced by an act they had not yet performed. Loud, yes — but loud about the wrong thing.
- **Commented template.** Comments do not change what the file evaluates to, so it quarantines identically — and *looks* as though it should not. Strictly worse than empty.
- **Working stub.** The script compiles clean from the first keystroke, so the first validation line the author ever sees is green and every red line afterwards is unambiguously about their own edit. It also teaches by example the single non-obvious thing about the format — that a behavior file is a map of event-name → lambda — which is otherwise documented nowhere the author will look.

**Why per-kind rather than one template.** F4: `onUpdate` is not dispatched for tiles or triggers. A one-size template would hand a tile author a script that compiles and never fires — the exact silent-failure shape #8049 made subject-kind applicability load-bearing to prevent. The mapping is one handler per kind, drawn from F4's matrix: tile → a contact handler, trigger → an enter handler, object → an update handler, level script → a level-start handler.

**This creates a data dependency that must be pinned, not remembered.** The template's handler name for each kind must be a name that kind receives. Per #8642, the test that pins it must assert **string literals** for all four kinds — a test comparing the template against whatever table produced it cannot fail. The red-first check is real: seeding an empty template makes the "not quarantined" guard fail for the documented reason.

### 5.6 Validation — where it appears, when it runs, and what save does

**Decision (a): the check is `BehaviorLoader.Compile` — the same call the runtime makes — run against a detached, inert set of the same four facade globals. Its result is rendered in the source editor's own footer line.**

Using the identical entry point is not convenience; it is the same principle `EditableLevelSnapshot` exists to serve and whose violation cost this codebase the P2 preview divergence: **author-sees must equal player-gets.** Any editor-side re-implementation of "is this script OK" would be a second opinion that drifts.

Per F5, that call needs globals, and the four-global composition currently exists twice — once in `BehaviorRuntime` (Godot glue, untestable) and once in the test rig. A third copy in the validator would mean that adding a fifth global would silently make the editor validate against a different world than the one the script will run in.

**Decision (b): the globals composition moves into one engine-free place in `Uberkarl.Behavior`, and `BehaviorRuntime` reads it.** A move, not a duplication — the same argument #8525 §8 used for the menu builders, and the same category of fix: pure logic currently inline in Godot glue where no test can reach it. The test rig may adopt it too; that is a fixture sharing a composition, not an assertion consuming a production value, so #8642 is not engaged.

The validator also selects the budget role the way the runtime does — the init role for a level script, the behavior role otherwise (`BehaviorRuntime.cs:175` versus `:146`) — so the editor's verdict and playtest's verdict are computed under the same limits.

**Decision (c): it runs when the surface opens, and again after the buffer has changed and typing has paused. Not per keystroke.** Per-keystroke would execute partially-typed author code on every character and make the footer flicker; validating only at close would deliver the news after the author has left the surface where they could act on it. The cost of each run is bounded by the script budget the runtime already enforces.

**Decision (d): the check is advisory. It never refuses anything — not the close, and not the save.**

> **A script that does not compile is stored verbatim.**

Three reasons, in order of weight:

1. **Refusing to save destroys work.** That is the harm category M1 (#8050) exists to have fixed. An editor that discards an author's text because it does not parse is worse than one that saves broken text.
2. **A broken script is content, not corruption.** `LevelLoader` does not compile; compilation happens per subject at `BehaviorRuntime.Configure` (`:146, :162, :175, :202`). A package containing a script with a syntax error loads fine, and the one subject bound to it quarantines with a named reason.
3. **The runtime already handles it correctly and per-playtest-session** — #8049 §6.4 established that `StopPlaytest` frees the play-world subtree and a restart clears quarantine. Nothing is stuck.

**Decision (e): the honest limitation, named rather than discovered.** Because `Compile` executes the script's top-level init, and the editor binds inert stand-in facades, a script whose init reads live level or player state can report a reason here that it would not produce at runtime — and can pass here and quarantine at play. **The footer is advisory; playtest is authoritative; M6 is the milestone that makes playtest's verdict visible.** That is the seam between the two milestones and it is why #8049 ordered them M5 → M6.

### 5.7 Sharing, and what happens when the last binding goes away

**Binding a second subject to an existing script** is one flow, two steps, no new machinery: cursor on object B → assign → pick the script row → `BehaviorBinding.FromScript(ResourceReference.ToSelf(path))` → `AssignObjectBehavior`, the same M4 command as any predefined. The table is not touched; no entry is added; `LevelMergeWriter` still emits one `PendingResource` for that path. **This is the acceptance criterion, and it is satisfied by construction rather than by new code.**

**When the last binding is removed** — reachable today by *re-assigning* the only subject bound to a script to something else, even though no explicit unbind exists:

**Decision: the table entry stays, and the script is still written to the package.** This is #8049 §5.2's ruling ("no GC"), cited and applied, not re-argued. The consequences, stated:

- The package carries an unreferenced script of a few hundred bytes.
- It remains listed in the assignment list and in the edit list, so re-binding it is one pick — which is what §5.2 predicted is usually about to happen.
- Nothing dangles: a stored script with no referrer is valid content; a *binding* with no script is what would be broken, and that cannot arise.
- Deleting it would need the reverse index §5.2 already rejected as a structure with no consumer.

### 5.8 Closing the source editor commits. There is no discard branch

**Decision: the surface has one exit. Done, ✕, and `ui_cancel` are the same act — the buffer is written into the script table and the session is marked dirty. Nothing is discarded.**

The house pattern for a *field* editor is enter/commit/cancel — `SteppedValueEditor`, `TextEntryEditor`, the layer panel's stepper, whose comment states the principle: *"a cancelled edit never reaches the model and needs no revert."* That is right for a field, where cancelling costs one value.

A source buffer is a **document**, and this editor already has a document-level dirty/save model. Applying field semantics to it makes Escape — a key people press reflexively — silently destroy an editing session's work. Adding a confirm dialog to guard it would introduce a step machine no other surface in this editor has.

Two callers whose correct response is identical need no discriminator — the same reasoning #8049 §8 used to let non-cell commands return `null`. One exit, no branch, no lossy key.

**The cost, stated:** an accidental edit to a shared script is not undoable at the document level. The mitigations are that nothing reaches disk until the editor's own Save, and that reopening and fixing is the same two steps as making the mistake.

### 5.9 The input-ownership property the surface must satisfy

Stated as required behaviour, deliberately **not** as a mechanism, because F3 marks the mechanism as depending on an unverified engine property.

While the source editor is open and focused:

| Key | Required result | Required non-result |
|---|---|---|
| Enter / KP-Enter | a newline in the buffer | no cell painted; no focused button activated; no level mutation |
| Space | a space in the buffer | as above |
| Delete / Backspace | ordinary text deletion | no cell erased |
| Any printable key | that character | no `editor_*` action fires |
| Escape / pad B | the surface closes, committing (§5.8) | — |

Plus the summoned-surface contract every modal here honours, and which #8525 §12 requires of M5 by name: an `IsOpen` polled by `AnyModalOpen()` so the grid cursor freezes and level mutation is locked; contained focus; dismissal on `ui_cancel`.

Per F2, the second column is already guaranteed for `editor_*` actions by `AnyModalOpen()` alone, provided the surface registers there. The first column, and the "no focused button activated" clause, are what the implementer must actually verify. `OnScreenKeyboard._Input` is the named precedent if verification shows it is needed.

---

## 6. Components and responsibilities

The standing lesson — *pure logic left inline in Godot glue is logic no test can reach; four defects shipped that way in the editor arc* — is discharged by naming the split explicitly.

### Engine-free (`src/Uberkarl.Editor`, `src/Uberkarl.Behavior`) — testable without Godot

| Unit | Owns | Does **not** own |
|---|---|---|
| **Script path convention** | Deriving `scripts/<slug>.poo` from an author-supplied name, via the existing `LevelResourcePaths.Slugify` / `UniqueSlug`; the "is this slug taken" predicate spans the level's table **and**, when the level is attached, the open package's resource paths. | Opening the package. |
| **`EditableLevel` script-table mutation** | Upserting one `ResourcePath → source` entry. | Deciding when, or what the text is. |
| **`LevelEditSession`** | The one intent-level call that upserts a script's source and marks the session dirty. | Undo semantics for text (§5.8). |
| **Starter template** | `BehaviorSubjectKind → source text` (§5.5). | Which kind is being assigned. |
| **`BehaviorAssignmentPicker`, extended** | The **single ordered choice list** — predefineds, then existing scripts, then "new script" — and the index → choice-kind dispatch; the name→path allocation for the new-script branch; the resulting `BehaviorBinding`; and the fact of whether this pick minted a new script path. | Rendering. Naming input. Opening the editor. |
| **Behavior globals composition** (`Uberkarl.Behavior`) | The four facade globals a compile binds (§5.6b). | Where they come from. |
| **Source validator** (`Uberkarl.Behavior`) | Compiling a source text for the right budget role against detached facades and reporting the quarantine reason, or none. | Deciding what to do with the answer. |

The index-space concatenation living here is the specific reason #8525 §8 gave for moving menu content out of glue, and this is the same hazard in the same widget.

### Godot glue (`game/Editor`) — thin, and only these three things

| Unit | Job |
|---|---|
| **`BehaviorAssignmentPanel`** | Renders the picker's choices as `ChoiceList` rows; on a new-script choice, chains `OnScreenKeyboard` for the name; hands the finished binding out on its existing `Assigned` event; asks `LevelEditor` to open the source editor when the pick minted a path. |
| **Script source editor** (new `Control`) | Multi-line text field, name header, validation footer; `IsOpen`; the §5.9 input property; on close, hands the buffer out. |
| **`LevelEditor`** | Owns the new surface, adds one `IsOpen` to `AnyModalOpen()`, adds one Actions-overflow outcome for `✎ Edit Script…`, and routes the committed buffer to the session. |

---

## 7. Interactions and data flow

**Create-and-bind** (the sequence, conceptually — one surface open at a time throughout):

1. Assign action fires at the cursor; `FindBehaviorSubjectAt` yields a subject and its kind.
2. The picker is constructed for that kind and produces its ordered choices: the applicable predefineds (M4), then the level's script table keys, then the new-script choice.
3. `ChoiceList` opens with those rows; the author picks the new-script row; the list hides.
4. `OnScreenKeyboard` opens for the name; on commit, the picker allocates a unique slug-derived path and produces a script binding, recording that this pick minted a path.
5. The panel raises its binding; `LevelEditor` routes it through the existing M4 command for the subject kind — undoable, dirty-marking, unchanged.
6. Because a path was minted, `LevelEditor` upserts the per-kind template into the script table through the session, then opens the source editor seeded with it.
7. The author types. The validator runs on open and on typing pause; the footer shows its verdict.
8. On close, the buffer is upserted into the table and the session marked dirty; focus returns to the canvas.

**Bind-to-existing:** steps 1–3, then the author picks a script row; the picker produces a script binding naming that existing path, minting nothing; step 5; done. No table write, no editor.

**Edit-only:** Actions overflow → `✎ Edit Script…` → `ChoiceList` over the table keys → step 6's editor, seeded from the table, with no binding touched.

**Save:** unchanged from M1. `LevelMergeWriter.BuildContributions` emits the level plus one `PendingResource` per table entry; `PackageMergeWriter.Compose` merges onto the existing archive.

**Playtest:** unchanged. `EditableLevelSnapshot` resolves each binding's source out of the table; `BehaviorRuntime.Configure` compiles it per subject.

---

## 8. Data model (conceptual)

No new persisted concept. No change to any content type, converter or schema.

| Entity | Identity | Owner | Lifecycle in M5 |
|---|---|---|---|
| Script resource | its `ResourcePath` (`scripts/<slug>.poo`) — **the identity every binding stores**, which is why #8049 §5.2 rules out rename | the level's script table while under edit; the package once saved | created (§5.4), edited (§5.8). Never renamed, never deleted. |
| Script binding | `BehaviorBinding.FromScript(ResourceReference)` | the subject that carries it | assigned and re-assigned through M4's four commands, unchanged |
| Script table | `ResourcePath → source`, per `EditableLevel` | `EditableLevel` | gains entries on create; entries change text on edit; entries are never removed |

**The N-share-one property is a property of this table, not of new code**: N bindings naming one path see one entry and produce one resource. The milestone's acceptance is a statement about the table's key set.

A new script's reference is a **self** reference (`ResourceReference.ToSelf`), because it lives in the package being saved. That matches what `EditableBehaviorBindings.Capture` already accepts (`IsSelf` or the package's own id) and keeps cross-package out (#7594).

---

## 9. Contracts (abstract)

| Boundary | In | Out | Invariants |
|---|---|---|---|
| Picker → its driver | a subject kind; the level's existing script paths; later, a chosen index; later still, a name plus a slug-taken predicate | an ordered choice list (label + kind per position); a `BehaviorBinding`; the minted path, when one was minted | Choice order is stable and total: every index maps to exactly one choice, and the three groups never overlap. A pick outside range is a no-op, not an exception. Cancel is terminal. |
| Path allocation | a display name; a predicate answering whether a slug is taken | a `ResourcePath` under `scripts/` | The result collides with neither an existing table entry nor, for an attached level, an existing package resource. Deterministic for a given name and predicate. |
| Session → model (script text) | a path and a source text | — | Upsert: creates or replaces. Marks the session dirty. Never removes. |
| Validator | a source text; a subject kind | either "no reason" or one human-readable reason string | Never throws for author input — every failure is a reason. Uses the runtime's own compile path and the kind's own budget role. Advisory: no caller may gate a mutation or a save on it. |
| Source editor → `LevelEditor` | — | the final buffer, once, on close | Exactly one commit per open. `IsOpen` is true from summon to close and is polled by `AnyModalOpen()`. Satisfies §5.9. |

---

## 10. Cross-cutting concerns

**Safety.** Free text is inside the boundary by #8049 §6.4's ruling: the author is the principal, the package is theirs, the sandbox is allow-listed (`TypeInstanceProvidersEnabled=false`, `TypeCastsEnabled=false`, `ImportsEnabled=false`) and the 1.1.0 guards fire per dispatch. M5 changes nothing about that. The one thing M5 adds is that author code now runs **at authoring time**, in the validator — bounded by the same `ScriptLimits` the runtime uses and wrapped by `ScriptExecutionGuard`, which converts a budget breach or any exception into a reason string. The gate stays on **provenance**, which M5 does not move: a self-reference to a script in the author's own package is the only shape reachable.

**Modal contract.** One new `IsOpen` in `AnyModalOpen()`. That single addition is what freezes the grid cursor, locks level mutation at `EditorCanvas`'s chokepoint, and suppresses every global `editor_*` action — the invariant #8525's 2026-08-19 addendum established as a chokepoint rather than a per-caller check.

**Focus.** The create flow chains three summoned surfaces (list → keyboard → editor). Required property: **at most one is open at any instant, and when the last one closes, focus is on the canvas.** This is a real hazard, not a formality: `OnScreenKeyboard` restores focus to whichever control held it at summon time, which in this chain is a `ChoiceList` row that is hidden by then — and #8760 §C7 records that `ChoiceList` rows are not freed on `Hide()`. Named as a property, with the mechanism left to the implementer.

**Cancel-path invariant (found in QA #8786, M5a).** *A naming step that can be abandoned must leave the list it was summoned from usable.* `OpenNamingStep()` summons `OnScreenKeyboard` out of the assignment list's `NamingNewScript` stage; that stage is also the one `SelectChoice` branch that does not hide the choice list, so the list is still on screen underneath the keyboard. `RequestText` therefore takes an `onCancel` alongside `onCommit`, and the panel's cancel handler resets the picker's stage before the list can be interacted with again — otherwise `SelectChoice`'s guard (`Stage != SelectingPredefined`) rejects every further row and the whole assignment aborts silently, indistinguishable from a real cancel. The same failure is reachable a second way, committing a **blank** name: `CreateNewScript` correctly holds the stage and expects a retry, so the glue must reopen the naming step on that path rather than treating it as done. Any future step that can both (a) leave a caller's surface open underneath it and (b) be abandoned needs this same pairing — a completion callback alone is not enough.

**Deferred focus grab, and its sixth instance (QA #8786, structural debt tracked as #8788).** `OnBehaviorAssigned` and `OnBehaviorAssignmentCancelled` must both restore focus to the canvas via `CallDeferred`, not synchronously, because either can run in the same frame as a stale `OnScreenKeyboard.Close()` restore targeting an already-hidden `ChoiceList` row. The deferred grab wins that race only **by Godot's FIFO queue ordering**, not by anything the code expresses — this is the sixth summon in the editor arc whose correctness depends on that ordering, and nothing tests or documents the requirement at the call site. #8788 proposes a single per-frame focus arbiter (last-writer-wins registered intent, resolved once after the deferred flush) so a future caller cannot silently win the race by queuing later. Until that lands, any new summoned surface that restores focus on close must queue its grab the same way, and should not assume it is the last one queued.

**Error handling.** Author-input errors are reasons, never exceptions (§9). Structural errors keep the existing typed shape: a binding naming a path with no source is a `LevelContentException` from `Resolve`, which is why §4 excludes offering package-only scripts as picks.

**Observability.** M5 adds one visible channel — the validation footer — and defers the runtime channel to M6 by design (§5.6e).

**Consistency.** One authoritative copy of a script's text at any moment: in the table, or in the open buffer. The surface holds it only while open, and closing always writes it back. No mirror pair.

---

## 11. Quality attributes and trade-offs

| Attribute | How it is addressed |
|---|---|
| **Testability** | Every decision that can be wrong — choice ordering and index dispatch, slug allocation and collision, the per-kind template's handler name, the validator's verdict, table upsert and round-trip — is engine-free. Only rendering, key routing and focus are glue, and §5.9 makes the one property that matters there a live-verifiable acceptance clause. |
| **Maintainability** | No new widget, no new modal paradigm, no new dispatch path, no new persisted concept, no new action binding. One new `Control`, one extended engine-free stage machine, one moved composition. |
| **Reachability** | Every member named here has an author-reachable call site in M5 (#1220). M5 additionally *retires* two members M4 shipped unreachable (§2). |
| **Performance** | The validator executes a script's init on a typing pause, bounded by the runtime's own limits. Nothing else in the flow is hot. |

**Trade-offs made, each stated where it is taken:** the text buffer is not unit-testable (§5.1) · pad-only authors cannot type bodies (§5.2) · a script text edit is not undoable at the document level (§5.8) · the validator can give a false verdict for an init that reads live state (§5.6e) · an abandoned create leaves a bound do-nothing script (§5.4) · an unreferenced script stays in the package (§5.7).

**Alternatives rejected:**

| Alternative | Why not |
|---|---|
| Route the script pick through `PackageBrowser`, per #8049 §7 / #8525 §12 | §5.3 — recreates the two-lifecycle shape #8525 §7 removed, to browse an in-memory dictionary. |
| Build a caret/line model on `TextEntryEditor` | §5.1 — a large pure-logic build with one consumer, competing with an engine control. |
| Make the on-screen keyboard the source editor | §5.2 — contradicts a standing ratification, and would first require the model above. |
| Rebind `editor_paint` off Enter/Space | F2 — unnecessary; the action is already inert under `AnyModalOpen()`. §8525 §12 already named the breakage cost. |
| Refuse to save a non-compiling script | §5.6d — the data-loss category M1 exists to have fixed. |
| Parse-only validation (no init execute) | Would miss the handler-map shape error and every unknown-event-name error, and would be a second opinion diverging from the runtime's. §5.6a. |
| One `onUpdate` template for all kinds | F4 — silently never fires for tiles and triggers. |
| Empty or comment-only new script | §5.5 — quarantines on creation, before the author has done anything. |
| A separate "script manager" panel | A parallel surface for a set the assignment list already renders. #1136 §2 form 2. |
| Cancel-discards semantics on the editor | §5.8 — makes a reflexive keypress destroy work, in a surface that edits a document rather than a field. |

---

## 12. Risks and failure modes

| # | Risk | Mitigation |
|---|---|---|
| R1 | A1 (F3) is false and the text surface does not naturally own Enter/Space, so typing paints cells or activates buttons | M5b's acceptance includes the live walk; `OnScreenKeyboard._Input` is the named one-file fix. The design states the property, not the mechanism. |
| R2 | The three-surface chain strands focus, or two surfaces are open at once | Stated as a property in §10; `AnyModalOpen()` makes a double-open observable because a second driver's open-attempt guards on it (#8525 §8's added-U3 invariant). |
| R3 | The concatenated index space is off by one, so picking "New script" binds a predefined | The concatenation is engine-free (§6) with per-position literal assertions (#8642). This is #8525 §8's named hazard, in its named widget. |
| R4 | A minted slug collides with an existing package resource, and the merge silently overwrites it | The taken-predicate spans the package's resource paths when the level is attached (§6). This is the #7571 failure family; a collision test on an attached level is required. |
| R5 | The template's handler name drifts from what the runtime dispatches, so new scripts silently never fire | Four literal-asserted tests, one per kind (§5.5). F4's matrix is recorded here with file:line so the reviewer has ground truth. |
| R6 | The validator's inert facades produce a verdict the runtime would not | Named and accepted (§5.6e). Advisory only — no mutation or save is gated on it. |
| R7 | Adding a fifth facade global makes the editor validate against a different world than the runtime | The composition becomes one shared engine-free thing (§5.6b). |
| R8 | An author expects the on-screen keyboard to work for script bodies on a pad | §5.2 makes the boundary explicit and places the only pad-inoperable surface last and skippable. |

---

## 13. Acceptance

**Every clause below is stated as something a user can do — the standing correction that acceptance must ask reachability, not existence. A correct code path was unreachable for six milestones because acceptance asked whether it existed.** And per the P2 carry-over (#7863): every test must be seen **red** against pre-change code, for the reason it exists.

### The milestone bar (#8049 §15), made precise

> One script, two placed objects bound to it, both run, package contains exactly one script resource.

Stated so it can be walked and so "exactly one" is unambiguous on a package that already contains scripts:

1. Open `content/sample.pkg`. Note its script-resource count.
2. Put the cursor on a placed object. Press assign. Choose `＋ New script…`. Type a name. Confirm.
3. In the source editor, write a handler with a visible effect. Close.
4. Put the cursor on a **second** placed object. Press assign. Pick the **same script** from the list. It binds; no editor opens.
5. Playtest. **Both objects exhibit the effect.**
6. Save. The package's script-resource count has grown by **exactly one**, and that resource's path is the one minted in step 2.
7. Reopen the saved package. Both objects still show the binding; the script's text is intact.

### Reachability clauses

- Step 4 is performed **entirely on a gamepad**, including the pick. (Step 3 is keyboard, by §5.2.)
- With the source editor open, pressing Enter inserts a newline and the level document is unchanged; the same for Space. Walked live per #8760 §A's `simulate_sequence` technique, not asserted in a unit test.
- A newly created script, before any typing, compiles **without a quarantine reason**, for each of the four subject kinds. Red-first by seeding an empty template.
- Deliberately break the script's syntax and close. The footer names a parse error. **Save succeeds.** Reopen the package: the broken text is present verbatim. (The red-first form: a save path that refuses.)
- `Assign Level Script` from the Actions overflow now offers the script rows and can bind one — the path #8760 §E recorded as dead until M5.

---

## 14. Build order — two units, each independently shippable

One feature, one PR. Both units are user-visible; neither is a refactor-only PR.

### M5a — the script table becomes authorable, and sharing works

Scope: script path convention · script-table upsert on `EditableLevel` and the session intent that reaches it · the per-kind starter template · `BehaviorAssignmentPicker` extended with the script and new-script choices · the naming chain through `OnScreenKeyboard` · routing the resulting binding through M4's commands. **No source editor** — the created script keeps its template text.

**Acceptance (user-doable, and it is the *sharing* half of the milestone bar):**

> Put the cursor on a placed object, press assign, choose `＋ New script…`, name it, confirm. Do the same on a second object, this time picking the script that now appears in the list. Save. The package has gained **exactly one** `scripts/<slug>.poo`, both objects' bindings name it, and playtest reports **no quarantine** for either.

Red-first: the "exactly one resource" clause fails if the second bind mints a second path; the "no quarantine" clause fails against an empty template.

Ships alone and is worth shipping alone: it makes level scripts assignable and makes M4's dead `AssignLevelScript` path live.

### M5b — the source editor

Depends on M5a. Scope: the summoned text surface · its `AnyModalOpen()` registration and the §5.9 input property · the `✎ Edit Script…` row in the Actions overflow list and its `MenuOutcome` · the validation footer · the globals-composition move and the validator (§5.6b — this is the feature reaching its surfaces, not a separate unit).

**Acceptance (user-doable, and it is the *both run* half):**

> Open a script from `✎ Edit Script…`, write a handler with a visible effect, close, playtest — the effect happens on every subject bound to that script. Then remove a brace, close, reopen: the footer names the parse error. Save anyway; reopen the package; the broken text is intact.
>
> Plus, walked live: with the editor open, Enter inserts a newline and Space inserts a space; no cell is painted, no button activates, the level document is unchanged.

Red-first: the parse-error clause fails against a surface with no validator; the input clause fails against a surface that does not register in `AnyModalOpen()`.

**M6 follows unchanged** — quarantine visibility is what turns §5.6's advisory footer into an authoritative report, and #8049 already orders it after M5.

---

## 15. Open questions and gaps

### For Toni — none blocking

1. **The starter template's handler choice for an object** — `onUpdate` or `onSpawn`. Both are dispatched (F4); `onUpdate` is proposed because it is the one that visibly does something every frame and therefore teaches fastest. Changing it changes one string and one literal assertion.
2. **Whether `✎ Edit Script…` also deserves a row at the bottom of the assignment list.** §5.4 rules against it to keep that list one-verb. If it turns out an author who just created a script wants to get back to its text without going to the Actions menu, it is one row.

### Gaps to file, not built

- **G7 — the tileset's own tile-type behavior scripts are not authorable.** `EditableTileSet` has its own script table and its own session and editor; M5 covers only the four level-owned subject kinds. An author can override a tile *instance* with a script but cannot write the tile *type's* default.
- **G8 — a script that exists in the open package but is not referenced by this level cannot be picked.** Includes scripts authored on a sibling level in the same package. The failure mode if it were offered naively is a `LevelContentException` from `Resolve` on the next snapshot; making it work means capturing the source into the table at pick time, which is a real feature.

### Deliberately closed — named so they are not reopened by accident

Script rename / delete / reverse index / orphan GC (#8049 §5.2) · gamepad text entry for bodies (#7440, #8049 §7) · any `project.godot` change (F2) · syntax highlighting and every other IDE affordance (§5.1) · a save that refuses (§5.6d) · cancel-discards on the editor (§5.8) · an unassign choice (§4) · `PackageBrowser` involvement (§5.3).

---

## 16. Pre-Design Checklist (#1136 §5)

**KISS / DRY / YAGNI**

- *No new type mirroring an existing one.* The script choices extend the picker M4 shipped rather than gaining a `ScriptPicker` sibling; the list is `ChoiceList`, unchanged; the naming path is `OnScreenKeyboard`, unchanged. The one genuinely new `Control` is the text surface, which has no existing counterpart.
- *No abstraction with one implementation and no second.* No interface is introduced. The globals composition (§5.6b) is a move that **removes** a copy and prevents a third, not an abstraction.
- *No element justified by "we might need X later".* §4 and §15 name every excluded element with its failure mode. Nothing is declared ahead of an author-reachable call site (#1220, #8237) — including the deliberate refusal to add an unassign path just because §5.7 discusses unbinding.
- *No deprecation window, feature flag, shim or transition period.* M5b replaces nothing; M5a adds to a live surface.
- *DRY math.* The `scripts/<slug>.poo` convention exists today as string literals at the sample generator and across four test files; a shared derivation is introduced and used from the one production site that mints paths. Test literals **stay literals** — a test asserting a path against the helper that produced it cannot fail (#8642).

**Existing systems first**

- *Audited.* `EditableLevel.Scripts` + `EditableBehaviorBindings` + `LevelMergeWriter` (the whole data path, already complete) · `BehaviorAssignmentPicker` / `Panel` · `ChoiceList` · `OnScreenKeyboard` and its `_Input` interception precedent · `LevelResourcePaths.Slugify`/`UniqueSlug` · `MenuCatalog`'s Actions overflow · `BehaviorLoader` · `ScriptExecutionGuard` · `BehaviorScriptBudgets` · `AnyModalOpen`. Each is named where it is reused; §2 is the audit.
- *New layer justified concretely.* The only new surface is the text editor, and §5.1 justifies it by what `TextEntryEditor` explicitly does not do, quoting its own contract.
- *No new persisted data point.* Nothing new is written into a package. The script table and its serialisation shipped in M1.
- *Consumer chain recursed.* Every member has a live caller in its own unit; M5 additionally gives two M4 members their first caller.

**Configurability** — no new knob. The validator's debounce is a design constant beside the surface, not a setting: no operator tunes it and it does not differ by environment.

**Less is better**

- *Delete / merge / inline.* Merged: script picking into the assignment list rather than a second picker (§5.3); the globals composition from two places into one (§5.6b). Deleted: the discard branch that a field-editor pattern would have added (§5.8). Not added: an unassign path, a script manager, a `PackageBrowser` step.
- *Trade-offs named where a change costs something.* §11 collects all six, each with a back-reference to where it is taken.
- *Radical-clean over compromise.* The one exit rule (§5.8) is the clean form; the compromise (cancel-with-confirm) is named and rejected.
- *Reader inventory covers string references.* `project.godot` is **not modified**; no action string is added, removed or re-homed, and glue reaches actions only through `EditorActionMap.NameOf` (#7440's standing rule).

**Data deliverables** — none. No schema, converter, migration or backfill.

**Document discipline** — #1136, #1220 and #114 §0–§4 cited at the head. Scope and non-scope both explicit with a reason per exclusion (§4). Two predecessor rulings are corrected in place with reasons rather than superseded wholesale (#8525 §12's diagnosis in F2, its `PackageBrowser` attribution in §5.3); #8049's addendum open question 1 is closed in §5.5. Every runtime claim carries its evidence, and the one unverified property is labelled an assumption (F3).

---

## 17. Status

Design complete. Every question the brief asked is answered in-document; the two items in §15 are ratifications, not forks. Two rulings correct earlier documents and are marked as such. Not committed, no PR.
