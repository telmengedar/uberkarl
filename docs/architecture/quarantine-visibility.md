# Design: Quarantine Visibility in Playtest (Behavior Authoring M6)

**Repo (canonical):** `docs/architecture/quarantine-visibility.md` on `C:\dev\claude\uberkarl`, committed on `feat/quarantine-visibility`. The DiVoid documentation node mirrors it.

*Sarah (architect), 2026-08-28. Design-only — no implementation, no PR.*

Source task **#9952** · phase design **#8049** (M6 is its last build-order unit) · M5 design **#8769** (§5.6 decision (e) draws this seam) · behavior system **#7704** · compile-lifecycle concept **#8246** · playtest design **#7519** · project **#7396** · map root **#8056**.
Load-bearing standards: **Design Contracts #1136** (§5 checklist walked as §15), the architect-template addendum **#1220** (*a design may only specify members the phase it belongs to will implement and reach*; and its §5 addendum — *a universal claim must name what would falsify it*), **do not seed complexity #1184**, **Code Contracts #114 §0–§4**.

**Base: `main` @ `99d0248`.** Every file:symbol claim below was read on that tree. M6 does not depend on PR #69.

---

## 1. Problem statement

Toni's standing framing for this phase, and the bar M6 is measured against:

> *"The goal is not the best tested non functioning level editor."*

And the seam, verbatim from **#8769 §5.6 decision (e)**:

> *"The footer is advisory; playtest is authoritative; **M6 is the milestone that makes playtest's verdict visible.**"*

M5 gave the author a compile check in the editor, computed against detached, inert stand-in facades. It can be wrong in both directions. M6 is where the run itself answers back.

**The premise, evidenced rather than assumed (#9952 §2, re-read on `99d0248`):** when a script dies mid-playtest the author sees *nothing*. The only production output is a `GD.PrintErr` in `BehaviorRuntime.OnQuarantined` (`game/Behavior/BehaviorRuntime.cs:381`), which reaches stderr and the Godot Output panel — neither of which is part of the surface the author is looking at, because `run/main_scene` is `res://scenes/level_editor.tscn`. There is no console, log overlay or message surface anywhere in `game/`. The one on-screen text channel during a run is `PlayerHud`, which draws a health bar and nothing else.

So the observable difference between *"this script died on frame 3 with a step-limit breach"* and *"this object has no behavior"* is zero. That is #8049 decision (b)'s claim, still true on `99d0248`.

**Success criterion, from #8049's build order:**

> A `MaxSteps`-tripping script produces a named, visible report instead of silence.

---

## 2. What already exists — verified, not assumed

The reason-producing half of this milestone is already built and already tested. **Nothing in §2 is redesigned.**

### F1 — the quarantine record already carries everything a report needs

`BehaviorQuarantineEvent(string SubjectId, BehaviorEventKind? TriggeringEvent, string Reason)` (`src/Uberkarl.Behavior/BehaviorScheduler.cs:4`) is raised exactly once per subject, through two doors that both end at the same one-way latch:

| Door | Where | `TriggeringEvent` |
|---|---|---|
| Before registration (parse error, init failure, unknown predefined, bad predefined parameter, no usable handler) | `BehaviorLoader.Compile` / `CompileBinding` produce an already-quarantined `CompiledBehavior`; `BehaviorScheduler.Register` (`:25`) raises on registration | `null` |
| During a dispatch (budget breach, throw, cancellation) | `BehaviorScheduler.Dispatch` (`:42-48`) runs the handler inside `ScriptExecutionGuard.TryRun`, calls `CompiledBehavior.Quarantine`, raises | the kind that tripped it |

`CompiledBehavior.Quarantine` (`CompiledBehavior.cs:20`) is idempotent and records only the first reason; `Dispatch` returns early for an already-quarantined instance, so **exactly one event per subject** is the guarantee `ScriptLimitQuarantineTests` already asserts (*"quarantine is permanent and logged exactly once"*).

Reason strings come from `ScriptExecutionGuard.TryRun` and have three shapes: `cancelled: …`, `exceeded budget ({ExceptionTypeName}): …`, `threw: {exception}`. Budgets are `BehaviorScriptBudgets.DefaultBehavior()` (`MaxSteps = 4_000`, `Timeout = 250 ms`) and `DefaultInit()` (`MaxSteps = 40_000`, `Timeout = 1000 ms`).

### F2 — the reason does not survive, and cannot be recovered afterwards

`BehaviorRuntime.OnQuarantined` (`:381`) does exactly two things: adds the subject id to a `HashSet<string>` surfaced as `QuarantinedSubjectIds` (`:44`), and formats the reason into a `GD.PrintErr`. **The reason is written to a log and dropped.** It is not recoverable later either: `BehaviorScheduler.instances` is private and the scheduler exposes only `IsQuarantined(subjectId)` and `RegisteredSubjectIds` — no accessor hands back a `CompiledBehavior` or its `QuarantineReason`.

### F3 — the subject identity is in hand at exactly the moment the reason is

`BehaviorRuntime.subjectsById` holds one `BehaviorSubject` per subject, carrying `Kind` (`"tile"` / `"trigger"` / `"object"` / `"level"`), `Name`, and `Cell`. All four registration methods (`RegisterScriptedTiles:136`, `RegisterTriggers:152`, `RegisterLevelScript:168`, `RegisterObjects:183`) write `subjectsById[subjectId] = subject` **before** the `scheduler.Register` call on the following line, and `Configure` subscribes `scheduler.Quarantined += OnQuarantined` (`:113`) before any of them run. So at `OnQuarantined` time the subject is always resolvable. This is a whole-file, four-site invariant with no other writer — §8.5 decides to trust it rather than guard it.

### F4 — init-time quarantines have already happened before `PlaytestOverlay.Start` returns

`PlayRuntimeBuilder.Populate` calls `AttachBehaviorRuntime`, which calls `runtime.Configure(level, player)` **synchronously**. `Configure` registers every subject (raising for any that compiled quarantined) and dispatches `onLevelStart`. All of that is complete before `Populate` returns, and therefore before `PlaytestOverlay.Start` finishes.

**Consequence that drives §4.2:** anything that *subscribes* to `BehaviorScheduler.Quarantined` after `Populate` returns has already missed every parse error, init failure and `onLevelStart` breach in the level. A surface built on subscription would be silent for the entire class of failure the author is most likely to hit.

### F5 — the runtime is already located by node name, by a shipped consumer

`AttachBehaviorRuntime` names the node `"BehaviorRuntime"`. `BehaviorHeadlessProbe` already re-derives it with its own `const string BehaviorRuntimeNodeName = "BehaviorRuntime"` (`:27`) and `root.FindChild(...)` at four sites. The name is therefore already a de-facto contract with a duplicated spelling — §4.4 makes it a single one rather than adding a third copy.

### F6 — the runtime cannot name which script died

`ResolvedBehaviorBinding` (`src/Uberkarl.Behavior/ResolvedBehaviorBinding.cs`) carries `Script` as **source text** and `PredefinedId`. The `ResourceReference` / `ResourcePath` is spent at resolve time and never reaches the runtime. §4.5 decides what to do about that.

### F7 — what reads quarantine state today

Grep for `QuarantinedSubjectIds` outside `docs/`: `BehaviorHeadlessProbe` at **six** occurrences across six lines — four `Count == 0` verdicts and two diagnostic joins. Nothing else reads it. No test references it. `BehaviorSourceValidator.cs:17` reads `CompiledBehavior.IsQuarantined`, which is a different surface and is untouched by this milestone.

### F8 — the level script's `onUpdate` fires every physics frame, with no player input

`BehaviorRuntime._PhysicsProcess` dispatches `scheduler.DispatchUpdate(LevelScriptSubjectId, delta)` whenever `hasLevelScript` (`:233-234`). §7 uses this to make the acceptance fixture deterministic and input-free.

### F9 — the editor's own package plumbing, as it bears on reachability

- `LevelEditor` auto-loads `res://content/sample.pkg` at startup (`:144-145`), so a launched editor is already holding a level with **no** quarantining subject.
- `InitializePackageSource` (`:873`) points the browser at `user://packages`, and `SeedPackagesDirIfEmpty` (`:881`) copies **every** `.pkg` from `res://content` into it — **only when that folder contains no package at all.** On a machine that has run the editor before, a newly added `res://content` package is therefore **not** visible in Open. §12 carries this as an explicit precondition rather than leaving it to be discovered.

---

## 3. Scope

### In scope

1. Retaining the quarantine reason, its triggering event and the subject's identity for the life of a playtest run.
2. Rendering them, live, on the playtest screen, in author-facing wording.
3. A content fixture that reaches the report without typing, so the whole walk is machine-drivable.

### Out of scope — each with the failure mode named, not merely absent

| Out | Why, and what the reader must not assume |
|---|---|
| **A script whose *parse* does not terminate** (#9341; upstream fix in flight as #9350) | `ScriptParser.ParseDictionary` spins forever at end-of-input on an unterminated top-level `{` — the mandatory final construct of every behavior script. Parsing sits **outside** `ScriptExecutionGuard`, so playtest boot hangs the main thread inside `CompileBinding` and **no quarantine is ever produced**. M6 cannot surface a verdict that does not exist. This is a ceiling M6 inherits and cannot lift; do not read any clause of this design as covering it. |
| **A playtest that fails to *start*** (#9952 OQ7) | `LevelEditor.StartPlaytest`'s `catch` (`:1033-1035`) is `GD.PrintErr`-only, so a `LevelContentException` at boot is silent in exactly the way a quarantine is. It is excluded because there is **no run** to report into: the overlay never opens, so the verdict would need a second, editor-side surface — the precise multiplicity §4.1 rejects. Adjacent code, different failure class. Filed as a gap in §14. |
| **Quarantine *policy*** (#8042 — strike counts, recovery for transient wall-clock breaches) | #8049 decision (b) split visibility from policy deliberately: *"The P3-forced requirement is therefore visibility, not policy."* The report consequently cannot say whether a failure will recur; a deterministic step-limit breach and a one-unlucky-frame wall-clock overrun read identically and are both permanently fatal. |
| **Making M5's preview `self` faithful** (#9753 / #9076 — the validator's `self` carries an empty kind and name) | #9952 deliberately did not fold this in, and nothing in this design forces it: M6 reads identity from `BehaviorRuntime`'s real subjects, never from the validator's stand-in. Folding it in would be seeded complexity (#1184). |
| **A partially-rejected handler map is still silent** (#8333) | A different silence, wanting a warning channel rather than a quarantine report. |
| **Naming the script resource that died** | Decided in §4.5, with the cost of the alternative priced there. |
| **Restating engine reason strings in author vocabulary** | Decided in §4.6. |
| **Persisting the verdict past the run** | Decided in §4.1. |

---

## 4. Decisions

### 4.1 One surface: a live report inside the playtest run, which dies with the run

**Decision: the verdict is rendered on the playtest screen, while the run is live, by a node the playtest owns. It is not persisted into the editor, and there is no expiry policy, because there is nothing to expire.**

The milestone's own sentence settles the *where*: *the run tells the author which script died*. A post-hoc notice tells the author after they have left the situation that produced it; it cannot say *when* in the run the script died or what the author was doing at the time.

The four candidates #9952 named, and why three lose:

| Candidate | Verdict |
|---|---|
| **`PlaytestOverlay`** — the editor-only host of a run | **Chosen.** Editor-only by construction (standalone `LevelPlay` never instantiates it), owns the run's lifetime, and can show both init-time and mid-run quarantines. Costs one small HUD node. |
| **`PlayerHud`** | **Rejected.** It is attached by the **shared** `PlayRuntimeBuilder.Populate`, so anything landing here also ships to a player of a released level. Engine diagnostics — `exceeded budget (ScriptStepLimitExceededException)` — are not the player's problem and are not what this milestone is for. There is no flag that separates the two; only the attachment point does, which is why §4.4 keeps `Populate` free of the report. |
| **The cursor status line** (`LevelEditor.CursorSubjectLabelText:1179`) | **Rejected for M6.** Post-hoc only, and it forces the expiry problem: a *"quarantined last playtest"* mark on a subject whose script has since been edited is a worse lie than no mark, so something must clear it, and the obvious clearing events (an edit to the bound script; the next playtest) live in different files. That is a second surface plus a lifecycle, bought for a run the author just watched. |
| **A `ChoiceList` notice on return** | **Rejected.** Interrupts the return to editing on every run, and still says nothing *during* the run. |

**The cost of choosing "dies with the run", stated plainly:** an author who looks away for the whole run and then presses Escape has to run again to read the verdict. Mitigations: the report does not auto-dismiss — once shown it stays for the rest of the run; and re-running is one button. Against that, the rejected alternative buys a permanent mark that must be invalidated correctly or it lies. **Trade-off taken: the transient truth beats the persistent maybe-lie.**

This also satisfies #8049 §6.4's per-session property **by construction** rather than by new code: `PlaytestOverlay.Stop` frees the play-world subtree, `Start` rebuilds it with fresh `CompiledBehavior`s, and the report node lives inside that subtree.

### 4.2 The report reads the runtime's retained records; it does not subscribe to the scheduler

**Decision: `BehaviorRuntime` retains one record per quarantine and exposes them as a read-only list. The report node polls that list.**

Per **F4**, a subscriber created after `Populate` has already missed every init-time quarantine. Polling a list that the runtime filled *during* `Configure` sees them all, with no subscription-ordering hazard to reason about and no unsubscribe to get wrong when the subtree is freed. It is also the house pattern: `PlayerHud._Process` already polls its bound `Player` every frame.

The poll is a single `int` comparison against the list's count; the text is rebuilt only when it grows. Nothing else in the frame changes it — quarantine is one-way and one-shot per subject (**F1**).

### 4.3 The retained record is a new value type, and it replaces `QuarantinedSubjectIds`

**Decision: `BehaviorRuntime` exposes `Quarantines` — an ordered, read-only list of `QuarantinedSubject` — and `QuarantinedSubjectIds` is removed.**

`BehaviorQuarantineEvent` cannot be retained as-is: it is addressed by subject **id** (`object:2`, `tile:0:20:11`), because the scheduler is the only thing that raises it and the scheduler does not know what a subject *is*. Subject identity is knowable only at `BehaviorRuntime`, and only while `subjectsById` is in hand (**F3**). The two types therefore have genuinely different owners and different value spaces — the §2 bar for a new type — and this is enrichment, not a mirror (#114 §5.4): neither type can be expressed in the other's terms.

**Why the id set goes rather than staying beside the list** (#1136 §2 form 2 — parallel layer, and §4's radical-clean rule): both would accumulate the same fact at the same moment for the same reason. The list subsumes the set, carrying the id as one of its fields. Keeping both means two accumulators to keep consistent for no consumer's benefit.

The migration is bounded and enumerated, per **F7** — **one file, six lines**:

| `BehaviorHeadlessProbe` site | Becomes |
|---|---|
| `:129`, `:212`, `:261`, `:312` — `runtime.QuarantinedSubjectIds.Count == 0` | `runtime.Quarantines.Count == 0` |
| `:134`, `:264` — `string.Join(",", runtime.QuarantinedSubjectIds)` | `string.Join(",", runtime.Quarantines.Select(q => q.SubjectId))` |

No test and no other production reader exists (**F7**), so there is nothing else to migrate and no shim to write.

### 4.4 The overlay reaches the runtime by its node name, made a single spelling

**Decision: `PlayRuntimeBuilder` exposes the runtime child's node name as a public constant; `AttachBehaviorRuntime` uses it, `PlaytestOverlay` uses it to fetch the node, and `BehaviorHeadlessProbe`'s private copy is redirected to it. `Populate`'s signature does not change.**

Rejected alternative — **`Populate` returns the runtime alongside the `Player`.** It reads cleanly and would delete the probe's four `FindChild` lines, but it changes the signature of the surface shared by `LevelPlay`, the overlay and the probe (seven call sites) in order to serve one new caller, and it needs a carrier for two return values. The constant is four lines, changes no signature, and closes the duplicated spelling that already exists (**F5**). Cheaper on every axis that matters here.

**Missing-node case: trusted, not guarded** (#1136 §6 — assert the invariant or trust it, never both). `Populate` calls `AttachBehaviorRuntime` unconditionally, in the same method, immediately before the overlay's lookup. There is no code path that produces a populated play world without the runtime. No null branch, no fallback, no log line for a state that cannot occur.

### 4.5 The report does **not** name the script resource

**Decision: the report identifies the subject, not the script. `ResolvedBehaviorBinding` is not changed and no reverse map is built.**

This is the expensive branch #9952 priced, and the price is not worth paying:

- **The content-layer route** — threading the `ResourcePath` through `ResolvedBehaviorBinding` and everything that builds it — is exactly the class of behaviour-layer change **#8049's addendum refused to land inside an editor milestone**.
- **The editor-side reverse map** — turning `object:{i}` / `trigger:{i}` back into `EditableLevel.Objects[i]` / `Triggers[i]` — *works today*, because `EditableLevelSnapshot.ToResolvedLevel` projects both collections with an order-preserving `Select(...).ToArray()`. But that correspondence is an implicit ordering contract spanning three files with nothing pinning it — the same shape as the surviving finding in **#9753**. Buying a script name with an unpinned invariant is a bad trade.
- **The author loses nothing they cannot recover in one step.** The editor addresses bindings by subject, not by path: the author puts the cursor on the named subject and presses assign, and the existing cursor status line (`BehaviorBindingLabel.FormatFull`) already tells them which script is bound there. The report hands them the subject; the editor hands them the script.

**What would falsify this** (#1220 §5 addendum): if a single script were bound to many subjects and the author needed to know *which script* rather than *which subject*, the subject label would be the wrong handle. It is not: the subject is what the author must navigate to in order to change anything, and one script bound to many subjects that all break the same way produces one line per broken subject, each pointing at a place the author can act.

### 4.6 The reason is shown verbatim, first line only

**Decision: the report shows the quarantine reason exactly as the engine produced it, truncated at the first line break. No author-facing restatement, no character cap, no configuration.**

Verbatim-in-full is not viable: `ScriptExecutionGuard` formats a thrown failure as `threw: {exception}` — the full `Exception.ToString()`, stack trace included. Its **first line** is `threw: {TypeName}: {message}`, which is the part an author can act on; the rest is engine internals. `cancelled:` and `exceeded budget (...)` reasons are single-line already, so the rule is a no-op for them.

Rejected alternative — **a mapper from engine reasons to author wording.** #8246 names the distinction worth drawing (*"you misspelled a handler"* versus *"your script looped forever"*), and today both arrive as the same shape of string, so a mapper would have to pattern-match on reason text. That is a second vocabulary maintained beside the first, drifting the moment `ScriptExecutionGuard` or the loader changes a phrase — and #8769 §5.6 established this project's rule that a second opinion about a script's health is the thing to avoid. If the wording is later judged too raw, that is its own task with #8246 in hand.

**The full untruncated reason is not lost:** `OnQuarantined`'s existing `GD.PrintErr` is kept unchanged and remains the complete record.

### 4.7 What one report line says

The block, when non-empty:

```
Behavior stopped (N)
<line per quarantined subject, in the order they were quarantined>
```

Each line: `{subject label}{cell} — {event}: {reason first line}`

| Part | Source | Rule |
|---|---|---|
| subject label | `BehaviorSubjectLabel.Format(kind, name)` — the **same** formatter the cursor status line uses | `Tile` · `Trigger 'heal-zone'` · `Object 'moving-platform-1'` · `Level Script` |
| cell | the subject's `Cell` | ` (X, Y)` for tile / trigger / object; **omitted for the level script**, which has no cell |
| event | `BehaviorEventNames.ToVariableName(kind)` — the spelling the author actually typed (`$onUpdate`) | `init` when the triggering event is `null` (quarantined before registration) |
| reason | the retained reason | up to the first line break |

Worked examples:

```
Behavior stopped (1)
Level Script — onUpdate: exceeded budget (ScriptStepLimitExceededException): ...

Behavior stopped (2)
Object 'moving-platform-1' (50, 9) — onContact: threw: ScriptRuntimeException: boom
Tile (20, 11) — init: parse error: ...
```

**Reusing `BehaviorSubjectLabel` rather than writing a second formatter is deliberate** (#1136 §1 DRY): the author sees the same words for the same subject in the editor status line and in the playtest verdict, and there is one place where `Object` / `Level Script` are spelled.

**Why the header carries a count.** It is not decoration: it is the only thing that makes acceptance clause **A4** machine-checkable. `assert_screen_text` tests for presence, so a second run that *accumulated* the previous run's verdict would still satisfy a substring assertion on the line itself. `Behavior stopped (1)` after the second run is a positive assertion that nothing accumulated. Named consumer, named decision (#868's bar, applied to on-screen data).

**Accepted limit, stated rather than discovered:** an object or trigger with no author-given name renders as `Object (5, 9)` / `Trigger (2, 2)` — the cell is what distinguishes it. Two *unnamed* subjects at the same cell would render identically; that cannot occur for tiles or triggers, and for two objects placed on one cell it is a genuine, accepted ambiguity that the count in the header still discloses.

### 4.8 The kind vocabulary gets the same treatment events already have

`BehaviorSubject.Kind` is a **string** (`"tile"` / `"trigger"` / `"object"` / `"level"`) because it is a script-facing facade field (`self.kind`, contract stated on `ISelfFacade.Kind`). `BehaviorSubjectLabel` takes the **enum** `BehaviorSubjectKind`. Something has to cross between them.

**Decision: add `BehaviorSubjectKinds` to `Uberkarl.Behavior`, modelled exactly on the existing `BehaviorEventNames`, with two members — `NameOf(kind)` and `Parse(name)` — and route `BehaviorRuntime`'s four registration sites through `NameOf` so the four string literals have one definition.**

- It is not new vocabulary (#1220): the four kinds and their two spellings already exist. This gives the existing pair a single home, the way `BehaviorEventNames` already does for event kinds.
- Both members are **reached** by this milestone: `NameOf` by the four registration sites, `Parse` by the report formatter. Neither is speculative.
- Rejected alternative: a parallel `Dictionary<string, BehaviorSubjectKind>` kept alongside `subjectsById` in `BehaviorRuntime`. Same size, but it is a second per-subject map that can drift from the first, and it leaves the four literals scattered.
- `Parse` resolves through a dictionary lookup and throws on an unknown name, exactly as `BehaviorEventNames.ToVariableName` does. Its input is machine-produced by `NameOf`, so the value space is closed; **no fallback branch** (#1136 §6). The one empty-kind subject in the codebase — the M5 validator's stand-in (#9076) — never reaches `BehaviorRuntime` and therefore never reaches `Parse`.

### 4.9 The formatter is engine-free; the runtime records data, not presentation

**Decision: `BehaviorRuntime` composes a data record and nothing else. The author-facing string is composed by a pure function in `Uberkarl.Editor`, called by the report node.**

Two reasons, both load-bearing:

1. **Testability.** The standing lesson quoted in #8769 §6 — *pure logic left inline in Godot glue is logic no test can reach; four defects shipped that way in the editor arc.* The formatter is the only place in M6 where a decision is made about wording, ordering, truncation and the cell rule. It belongs where a unit test can reach it.
2. **Layering.** `BehaviorRuntime` runs in the shipped game (`LevelPlay`). Author-facing editor presentation must not be a dependency of the play runtime. Putting the formatter in `Uberkarl.Editor` — beside `BehaviorSubjectLabel`, which it composes — keeps the arrow pointing the right way: `game/Editor` → `Uberkarl.Editor` → `Uberkarl.Behavior`, and `game/Behavior` → `Uberkarl.Behavior` only.

---

## 5. Components and responsibilities

### Engine-free (`src/`) — testable without Godot

| Component | Owns | Does **not** own |
|---|---|---|
| `Uberkarl.Behavior` · **`QuarantinedSubject`** *(new)* | The shape of one retained quarantine: subject id, kind (string, as the subject spells it), name, cell, triggering event kind (nullable), reason (full text). A value record, no behaviour. | Formatting. Ordering. Deciding what is shown. |
| `Uberkarl.Behavior` · **`BehaviorSubjectKinds`** *(new)* | The single definition of the four subject-kind strings and their `BehaviorSubjectKind` counterparts. | Labels for humans (that is `BehaviorSubjectLabel`). |
| `Uberkarl.Editor` · **`QuarantineReportText`** *(new)* | Turning an ordered list of `QuarantinedSubject` into the author-facing block of §4.7 — header with count, one line per subject, cell rule, event spelling, first-line reason. Returns empty for an empty list. | Where the text is drawn. When it is recomputed. Visibility. |
| `Uberkarl.Editor` · `BehaviorSubjectLabel` *(existing, unchanged)* | Kind + optional name → `Object 'spike'`. | Cells; the report appends those. |
| `Uberkarl.Behavior` · `BehaviorEventNames` *(existing, unchanged)* | Event kind ↔ the author's handler-variable spelling. | — |

### Godot glue (`game/`) — thin, and only these four things

| Component | Change | Responsibility boundary |
|---|---|---|
| **`BehaviorRuntime`** (`game/Behavior`) | Registration sites source their kind string from `BehaviorSubjectKinds.NameOf`. The `HashSet<string>` accumulator becomes an ordered `List<QuarantinedSubject>`; `QuarantinedSubjectIds` becomes `Quarantines`. `OnQuarantined` resolves the subject from `subjectsById`, appends the record, and keeps its existing `GD.PrintErr` verbatim. | Records facts. Formats nothing. Knows nothing about any surface. |
| **`QuarantineReportHud`** (`game/Editor`, *new*) | A `CanvasLayer` bound to one `BehaviorRuntime`. Each `_Process`, compares the retained count to the last one it rendered; on growth, rebuilds its `Label` text through `QuarantineReportText` and shows itself. Hidden while the count is zero. | Drawing and visibility only. No wording decisions, no runtime knowledge beyond the list. |
| **`PlaytestOverlay`** (`game/Editor`) | After `Populate`, fetches the runtime child by the shared node-name constant, creates the report HUD, binds it, and adds it **to the play-world subtree** so `Stop`'s `QueueFree` takes it with everything else. | Wiring and lifetime. It never reads the quarantine data itself. |
| **`PlayRuntimeBuilder`** (`game/Play`) | Exposes the runtime child's node name as a public constant and uses it when attaching. **Nothing else changes** — no new node, no new return value. This is what keeps the report out of standalone `LevelPlay`. | — |
| **`EditorLayout`** (`game/Editor`) | Gains `PinBottom`, the bottom-edge sibling of the existing `PinTop`: bottom-wide anchors growing upward from content, so a two-line and a five-line report are both fully visible. | The sanctioned layout path stays the only layout path. |
| **`BehaviorHeadlessProbe`** (`game/Diagnostics`) | Six mechanical line edits per §4.3, plus its private node-name constant redirected to `PlayRuntimeBuilder`'s. | Its verdicts are unchanged in meaning. |

---

## 6. Interactions and data flow

```
   author clicks Play
        |
        v
 LevelEditor.StartPlaytest
        |  EditableLevelSnapshot.ToResolvedLevel(session.Level)
        v
 PlaytestOverlay.Start(level)
        |
        +--> playWorld = new Node2D("Playtest")
        |
        +--> PlayRuntimeBuilder.Populate(playWorld, level)      [SHARED with LevelPlay]
        |        +-- tile layers, player, camera, PlayerHud
        |        +-- BehaviorRuntime "BehaviorRuntime"
        |              Configure(): subscribe -> register subjects -> onLevelStart
        |                             |
        |                             |  any parse error / init failure / onLevelStart
        |                             |  breach ALREADY lands here, synchronously
        |                             v
        |                        Quarantines: [ QuarantinedSubject, ... ]
        |
        +--> runtime = playWorld.GetNode(PlayRuntimeBuilder.BehaviorRuntimeNodeName)
        +--> QuarantineReportHud.Configure(runtime); playWorld.AddChild(hud)   [EDITOR-ONLY]

   ---- every frame while the run is live ----

   BehaviorRuntime._PhysicsProcess -> dispatch -> guard breach
        -> BehaviorScheduler.Quarantined -> OnQuarantined
        -> GD.PrintErr (full reason)  AND  Quarantines.Add(record)

   QuarantineReportHud._Process
        -> if Quarantines.Count > lastRenderedCount:
             Label.Text = QuarantineReportText.Format(runtime.Quarantines)
             Visible = true

   ---- author presses ui_cancel ----

   PlaytestOverlay.ExitRequested -> LevelEditor.StopPlaytest
        -> PlaytestOverlay.Stop() -> playWorld.QueueFree()
             (runtime, PlayerHud and the report HUD all go together)
```

**Two properties this diagram makes explicit:**

- The report node is created *outside* `Populate`, by the editor-only overlay. That single placement decision is the whole of §4.1's editor-only guarantee — there is no flag to get wrong.
- Everything the report reads is inside the subtree that `Stop` frees, and nothing is read after `Stop`. #9952's teardown constraint ("copy the data out first") therefore does not arise: this design never reads across the free.

---

## 7. Data model (conceptual)

| Entity | Fields | Owner | Lifetime |
|---|---|---|---|
| **`QuarantinedSubject`** | subject id · kind (string) · name · cell · triggering event kind (nullable — `null` means "before registration") · reason (full text) | `BehaviorRuntime` | Appended once per subject at quarantine; freed with the runtime node |
| `BehaviorQuarantineEvent` *(existing)* | subject id · triggering event kind · reason | `BehaviorScheduler` | Transient — exists only for the duration of the event invocation |
| `BehaviorSubject` *(existing)* | id · kind · name · cell · position · state | `BehaviorRuntime.subjectsById` | The run |

**Ownership rule:** `QuarantinedSubject` is built by copying from `BehaviorQuarantineEvent` and `BehaviorSubject` at the one moment both are in hand. The runtime **does not** expose `subjectsById` — `BehaviorSubject` is a live script facade with mutating members (`SetState`, `MoveTo`), and handing it to UI code would put a writable script surface behind a label. The copy is what keeps the boundary honest, and it is the reason §4.3's record carries identity fields rather than a reference.

**Nothing here is persisted.** No package field, no save, no editor session state. `.pkg` content is untouched by this milestone except for the new fixture package of §12, which is content, not state.

---

## 8. Contracts (abstract)

### 8.1 `BehaviorRuntime.Quarantines`

- **Reads:** nothing (a property).
- **Yields:** the quarantined subjects of this run, in the order they were quarantined, as a read-only ordered collection.
- **Invariants:** at most one entry per subject id (guaranteed upstream by `CompiledBehavior.Quarantine`'s idempotence plus `Dispatch`'s early return for a quarantined instance — **F1**); the list only ever grows during a run; every entry's identity fields are the subject's own, resolved at quarantine time.
- **Replaces:** `QuarantinedSubjectIds`, which is removed.

### 8.2 `QuarantineReportText.Format`

- **Takes:** an ordered read-only collection of `QuarantinedSubject`.
- **Yields:** the §4.7 block, or the empty string for an empty input.
- **Invariants:** input order is output order; the header count equals the input count; each line contains no line break (the reason is cut at its first); the level-script kind never emits a cell, every other kind always does; a `null` triggering event renders as `init`.
- **Purity:** total, allocation-only, no I/O, no engine types.

### 8.3 `BehaviorSubjectKinds`

- `NameOf(kind)` yields the facade spelling for a `BehaviorSubjectKind`; `Parse(name)` is its inverse. Round-tripping in either direction over the four kinds is the identity. An unrecognised name throws (§4.8).

### 8.4 `QuarantineReportHud`

- **Bound once** to a `BehaviorRuntime` before entering the tree.
- **Shows** the formatted block when the retained count is greater than zero; **stays hidden** while it is zero. Once shown, it does not hide again for the life of the node.
- **Must not** accept input: the run owns the keyboard and gamepad. It is a display-only layer over the running game.
- **Must** hold its text in a real `Label` node's text, not in custom drawing — the acceptance walk reads it with `assert_screen_text`, and a `_Draw`-painted string is not reachable that way. This is the U1-class safeguard written into the contract rather than left to chance.

### 8.5 Invariants trusted rather than guarded (#1136 §6)

| Invariant | Why it is trusted |
|---|---|
| `subjectsById` contains every id that can be quarantined | All four registration sites write the subject on the line before `scheduler.Register`, in one file, with no other writer (**F3**). |
| `Populate` always attaches the runtime child | Unconditional call inside `Populate`, immediately before the overlay's lookup (§4.4). |
| A subject is quarantined at most once | Upstream guarantee, already asserted by `ScriptLimitQuarantineTests` (**F1**). |

None of these gets a null check, a fallback, or a defensive log. Either the invariant holds or the code that broke it is the bug to fix.

---

## 9. Cross-cutting concerns

- **Threading.** `_PhysicsProcess` (where quarantines occur) and `_Process` (where the report renders) both run on Godot's main thread. There is no concurrency here and no synchronisation is designed for.
- **Error handling.** M6 adds no failure mode of its own. Formatting is total; the record is a copy of values already validated upstream.
- **Logging.** Unchanged. `OnQuarantined`'s `GD.PrintErr` keeps the complete reason including any stack trace, and remains the record for anyone reading the Output panel.
- **Performance.** One record allocated per quarantine (bounded by the subject count, once each). One `int` comparison per frame in the report node; a string rebuilt only when the count grows. `PlayerHud` already does strictly more work per frame.
- **Security / sandboxing.** Untouched. The report displays a string the guard produced; it never re-executes anything.
- **Input.** The report layer takes no input. `ui_cancel` continues to reach `PlaytestOverlay._UnhandledInput` exactly as today.
- **Rendering order.** The report layer sits above `PlayerHud`'s canvas layer so a long report is never drawn under the health bar; it is anchored to the bottom edge, where `PlayerHud`'s top-left readout is not.
- **One-frame teardown residue (pre-existing, deliberately not guarded).** `QueueFree` is deferred, so the report layer — like `PlayerHud` today, which has the identical property — can render for one frame after `Stop`. It is invisible to a human and to any assertion taken after a frame delay. Adding a hide-on-stop step to fix a one-frame artefact nobody can observe would be exactly the defensive noise #1136 §6 removes.

---

## 10. Quality attributes and trade-offs

| Attribute | How this design serves it |
|---|---|
| **Author-facing correctness** | The verdict comes from the run itself, computed by the runtime's own guard — not a second opinion. This is #8769 §5.6's principle (*author-sees must equal player-gets*) applied in the direction M5 could not reach. |
| **Maintainability** | Every wording decision is in one pure function with unit tests. The Godot glue is two small nodes with no logic. Two spellings of subject kind become one definition. |
| **Simplicity** | One surface, no persistence, no expiry, no config, no new vocabulary, no content-layer change. Net new production types: three, of which two are ~15 lines. |
| **Reachability** | The report is a `Label` in a bottom-anchored layout built through the sanctioned `EditorLayout` helpers, over a fixture that reaches it with two clicks and no typing. |

**Trade-offs made, with the losing side's cost:**

1. **Transient verdict over persistent mark** (§4.1). Loses: an author who missed the run must re-run. Costs of the alternative: a second surface, an expiry policy, and a mark that lies whenever invalidation is missed.
2. **Subject identity over script name** (§4.5). Loses: one extra navigation step to see the script. Costs of the alternative: a behaviour-layer change #8049's addendum forbids inside an editor milestone, or a reverse map on an unpinned ordering contract (#9753's shape).
3. **Verbatim engine reason over author restatement** (§4.6). Loses: `exceeded budget (ScriptStepLimitExceededException)` is engine-shaped language. Costs of the alternative: a second vocabulary pattern-matching on reason text, drifting from the first the moment a phrase changes.
4. **Removing `QuarantinedSubjectIds` over keeping both** (§4.3). Loses: six mechanical line edits in one file. Costs of the alternative: two parallel accumulators of one fact, forever.

**No DRY override is claimed anywhere in this design** — no multi-line block is specified for inlining at more than one site, so no `block_size × site_count` statement is owed.

---

## 11. Risks and failure modes

| Risk | Likelihood | Mitigation |
|---|---|---|
| The report renders but is off-screen or zero-sized — the **U1 failure class** | Real; it has happened in this project | Layout goes through `EditorLayout` (a `PinBottom` sibling of the existing `PinTop`), never through ad-hoc size assignment; and acceptance A1 asserts on-screen text rather than on the node's existence |
| The report is drawn under `PlayerHud` or under the tile layers | Low | Placed on a canvas layer above `PlayerHud`'s and anchored to the opposite edge (§9) |
| The fixture package is not visible in **Open** on a machine that has run the editor before | **High — this is `SeedPackagesDirIfEmpty`'s documented behaviour** (**F9**) | §12 makes clearing `user://packages` an explicit precondition of the walk, with the path spelled out |
| The fixture stops tripping its budget after a future budget change, silently turning A1 green-for-the-wrong-reason | Low but silent | An engine-free test asserts the fixture's script *does* quarantine under the same budgets the runtime uses (§13, U-7) |
| A future reader assumes M6 covers the parse hang (#9341) | Real — it is the same author-facing symptom | Named in §3 as an inherited ceiling, in its own row, with the mechanism |
| An init-time quarantine is missed because the surface subscribed too late | Would be near-certain under a subscription design | Designed out: the report polls a retained list (§4.2, **F4**) |

---

## 12. The acceptance fixture

**A1 needs a level whose script deterministically trips `MaxSteps`. It cannot be authored by typing** — the harness never sets `event.unicode`, and text controls accept characters only when `Unicode != 0` (#8789). So it is generated content.

**It must not go into `content/sample.pkg`'s probed level.** `BehaviorHeadlessProbe` asserts `Quarantines.Count == 0` against that content at four sites (`:129`, `:212`, `:261`, `:312`); a deliberately-quarantining subject there turns a green probe red for the wrong reason. Adding a *second level* to `sample.pkg` is also rejected: `BehaviorHeadlessProbe.FindLevelReference` and `LevelPlay.FindLevelReference` both take the **first** level resource in the manifest, so the fixture would be invisible or not, depending on manifest order — an unpinned ordering contract of exactly the kind §4.5 declined to build on.

**Decision: a separate package, `content/runaway-script.pkg`, generated by `tools/SampleContent` alongside `sample.pkg`.**

| Aspect | Specification |
|---|---|
| Generator change | `tools/SampleContent`'s single argument becomes the output **directory** (default `content`), and the program writes `sample.pkg` and `runaway-script.pkg` into it. No mode flag, no switch — it generates the sample content set. The argument has no callers in the repo (no script, no CI step references it), so the change costs nothing downstream. |
| Package identity | Name `Runaway Script Demo`; one level at `levels/runaway.json` (so `FindLevelReference` is unambiguous), one script at `scripts/runaway.poo`, its own minimal tileset. |
| Tileset | Its **own** two-tile palette — one solid ground tile, one non-solid backdrop tile — with **no** behavior binding on either. The sample's palette is deliberately not reused: its spike tile carries `hurtOnContact`, and the fixture's report must contain exactly one line. |
| Level | Small (a viewport-sized grid is enough), one collision layer with a ground row, a default spawn on it. No objects, no triggers, no object set. |
| The quarantining subject | The level's `LevelScript`, bound to `scripts/runaway.poo`. Chosen over an object because its `onUpdate` is dispatched every physics frame with **no player input and no contact** (**F8**), which makes the walk deterministic and input-free — and because it needs no object set, graphic, or placement. |
| `scripts/runaway.poo` — content, quoted from `ScriptLimitQuarantineTests`'s already-proven shape | `$onUpdate = $delta => { while(true) { $x = 1; } }` followed by `{ "onUpdate": onUpdate }` |
| Expected verdict | `Behavior stopped (1)` / `Level Script — onUpdate: exceeded budget (ScriptStepLimitExceededException): …` |

**Why the init budget still trips it:** a level script compiles under `BehaviorScriptBudgets.DefaultInit()` (`MaxSteps = 40_000`, `Timeout = 1000 ms`). `while(true)` exhausts 40 000 steps well inside the timeout, so the run stalls for a fraction of a frame once and then the subject is permanently quarantined — which is the guard's freeze-proof guarantee behaving exactly as `ScriptLimitQuarantineTests` already proves.

**Precondition for any walk that opens the fixture, per F9:** `SeedPackagesDirIfEmpty` copies `res://content/*.pkg` into `user://packages` **only when that folder holds no package at all**. On a machine that has run the editor before, the fixture will therefore not appear in **Open** until `user://packages` is emptied. Delete its contents before the walk. On Windows that is `%APPDATA%\Godot\app_userdata\<project>\packages`; resolve it with `ProjectSettings.GlobalizePath("user://packages")` if in doubt.

---

## 13. Acceptance

**Every clause is phrased as something a person does, ending in something they can see** — never *is the code present*. U1 shipped a clause satisfied by code that stayed unreachable for six milestones because a layout call collapsed a control's input rect to 0×0.

**And per #8049's P2 carry-over: every clause is verified red first** — *the question is not "does it pass" but "have I seen it fail for the reason it exists".*

**Before any walk is claimed:** the Godot MCP plugin must be **enabled** (Project → Project Settings → Plugins). It was off for three milestones and that is why they shipped unverified (#8835; #9353 carries the same warning). Live technique is `simulate_sequence` with `frame_delay` in a single call (#8760 §A).

### 13.1 Engine-free (unit tests — no Godot, no harness)

| # | Clause | Red-first form |
|---|---|---|
| **U-1** | `QuarantineReportText` renders an empty list as the empty string. | — (the always-on-noise guard; pairs with A2) |
| **U-2** | It renders each of the four subject kinds correctly: named object with cell, named trigger with cell, unnamed tile with cell, level script **without** a cell. | Seed a level-script record and see the cell present before the rule exists. |
| **U-3** | A `null` triggering event renders as `init`; a non-null one renders in the author's spelling (`onUpdate`, not `OnUpdate`). | Assert against the enum spelling first. |
| **U-4** | A multi-line reason (a `threw:` reason with a stack trace) renders only its first line, and the line contains no line break. | Assert the full reason first and watch it fail. |
| **U-5** | The header count equals the number of records, for one and for several. | — |
| **U-6** | `BehaviorSubjectKinds` round-trips all four kinds in both directions. | — |
| **U-7** | **The fixture is genuinely red content.** Reading `content/runaway-script.pkg` from disk through the same projection `StartPlaytest` uses (`EditableLevelReader` → `EditableLevelSnapshot.ToResolvedLevel`), the level's `LevelScript` is a script binding; compiling and dispatching it under `BehaviorScriptBudgets.DefaultInit()` quarantines with a reason naming a budget breach. | Point it at `content/sample.pkg` and watch it fail — that is the assertion that the fixture is not accidentally healthy. |

U-7 follows `RealSamplePackagePlaytestTests`'s existing shape, including its walk-up package-path helper. It is what stops the fixture rotting silently if a budget or the parser changes.

### 13.2 Driven through godot-mcp (the mechanised substitute)

Preconditions: plugin enabled; `user://packages` emptied so the fixture is seeded (§12).

| # | Walk | Assertion | Red-first |
|---|---|---|---|
| **A1** | Launch the editor. `click_button_by_text` **Open** → pick `runaway-script.pkg` → pick `levels/runaway.json` → `click_button_by_text` **Play**. | `assert_screen_text` finds **`Behavior stopped (1)`** and **`Level Script`** on the running screen. | On pre-change code the identical walk shows neither — that is the milestone's premise (§1) and must be seen. |
| **A2** | Launch the editor (it auto-loads `content/sample.pkg`, which has no quarantining subject) and `click_button_by_text` **Play**. | No report text on screen. The surface is not always-on noise. | Seed a report unconditionally and watch A2 fail. |
| **A3** | From A1's state, `simulate_action` `ui_cancel` to return to the editor. | The verdict is **provably gone** — `assert_screen_text` no longer finds `Behavior stopped`. This pins §4.1's decision that the verdict dies with the run. | Persist the verdict and watch A3 fail. |
| **A4** | From A3's state, `click_button_by_text` **Play** again. | `assert_screen_text` finds `Behavior stopped (**1**)` — not `(2)`. The per-session property of #8049 §6.4 stays observable rather than accumulating across runs. | Accumulate across runs and watch the count read `(2)`. |

Both buttons carry their text, so `click_button_by_text` reaches them and `assert_screen_text` reads the verdict — which is why §8.4 requires the text to live in a real `Label`.

### 13.3 Toni's half — not machine-verifiable, stated up front rather than discovered

- **Authoring a runaway script by typing it** into M5b's source editor. The harness cannot type: `addons/godot_mcp/mcp_input_service.gd` never sets `event.unicode`, and text controls accept characters only when `Unicode != 0` (#8789). Everything else on the path — Open, Play, the assignment picker, the on-screen keyboard's naming keys — is drivable; the body is not.
- **The end-to-end author story:** write a script that loops → playtest → read the verdict → fix it → playtest again → the verdict is gone. A1–A4 are the mechanised substitute proving the surface exists and behaves; this walk is the one that proves it is *useful*.
- **A judgement call only Toni can make:** whether `exceeded budget (ScriptStepLimitExceededException)` is acceptable author-facing wording (§4.6). If it is not, that is a follow-up with #8246 in hand — not a reason to build a mapper now.

This is the same three-way split #8769 and #9070 used, and it is why those milestones' acceptance held up under review.

---

## 14. Build order and implementation guidance

**One milestone, one PR.** M6 is a single user-visible unit and does not decompose into independently-shippable halves: the fixture without the surface reports nothing, and the surface without the fixture cannot be walked. Build in this order so each step compiles and the last one is the one that becomes visible.

1. **The vocabulary and the record.** `BehaviorSubjectKinds` (+ U-6). `QuarantinedSubject`. Route `BehaviorRuntime`'s four registration sites through `NameOf`.
2. **Retention.** `BehaviorRuntime`'s accumulator becomes the ordered list; `QuarantinedSubjectIds` → `Quarantines`; `OnQuarantined` composes the record and keeps its `GD.PrintErr`. Migrate `BehaviorHeadlessProbe`'s six lines (§4.3). *The build is green here and behaviour is unchanged.*
3. **The formatter.** `QuarantineReportText` + U-1…U-5. *Still nothing visible.*
4. **The fixture.** `tools/SampleContent` output-directory change; `content/runaway-script.pkg` generated and committed; U-7. *Now A1 has something to open — and still shows nothing, which is the red-first observation to record.*
5. **The surface.** `EditorLayout.PinBottom`; `PlayRuntimeBuilder`'s node-name constant (and the probe's redirect); `QuarantineReportHud`; `PlaytestOverlay` wiring. *A1 turns green here.*
6. **Walk A1–A4 live**, having recorded step 4's red observation.

**Notes for the implementer:**

- Do not change `PlayRuntimeBuilder.Populate`'s signature or its children. The report's absence from standalone `LevelPlay` is guaranteed by *where the node is added*, and nothing else (§4.1).
- Do not add null checks for the three invariants in §8.5.
- Do not add a configuration knob anywhere in this milestone; there is none in the design.
- Regenerate **both** packages from the tool in one run, so `sample.pkg` is not accidentally rewritten by a partial invocation.

---

## 15. Pre-Design Checklist (#1136 §5)

**KISS / DRY / YAGNI**

- **No new type mirroring an existing one.** `QuarantinedSubject` versus `BehaviorQuarantineEvent` is argued in §4.3: different owner, different addressing (subject identity versus subject id), neither expressible in the other's terms. `BehaviorSubjectKinds` is the single home for two spellings that already exist, modelled on the shipped `BehaviorEventNames` (§4.8) — not a new vocabulary (#1220).
- **No abstraction with one implementation.** None introduced. No interface, no registry, no strategy.
- **Nothing justified by "we might need X later."** The report shows one thing; §3 lists what it deliberately does not do, each with its failure mode. The header count is justified by a named consumer (acceptance A4), not by future needs.
- **No deprecation period, feature flag, compatibility shim or transition window.** `QuarantinedSubjectIds` is removed outright and its call sites are enumerated (§4.3).
- **DRY math:** not owed — no multi-line block is specified for inlining at more than one site (§10).

**Existing systems first**

- **Audited.** `BehaviorQuarantineEvent` is reused rather than re-invented. `BehaviorSubjectLabel` and `BehaviorEventNames` are reused rather than duplicated (§4.7). `EditorLayout` is extended with the sibling of an existing member rather than bypassed. The runtime-node lookup reuses a name the probe already depends on (§4.4).
- **New layers named with a concrete reason.** `QuarantineReportHud` exists because the report needs a per-frame poll and its own canvas layer — `PlaytestOverlay` is a `Control` with neither, and `PlayerHud` is disqualified by attachment point (§4.1). `QuarantineReportText` exists because wording, ordering and truncation must be unit-testable and must not become a dependency of the play runtime (§4.9).
- **New persisted data:** none. Nothing reaches a `.pkg` except the fixture, which is content.
- **Consumer chain recursed.** Every field of `QuarantinedSubject` has a named on-screen consumer: id → the probe's diagnostic join; kind and name → the subject label; cell → the locator; triggering event → the `onUpdate` / `init` clause; reason → the line's tail. No field is carried for a reader that is itself unread.

**Configurability**

- **No new config knob**, no threshold, no cap, no toggle. §4.6 states explicitly that the first-line rule takes no character cap and no flag.
- **No telemetry-then-tune compound.**
- **Magic numbers:** the only constants are the node name (§4.4) and the report's layer ordering, both named `const` in code with no reason to vary.

**Less is better**

- **Delete / merge / inline applied.** `QuarantinedSubjectIds` deleted rather than kept beside the list (§4.3). The runtime lookup merged onto the existing node name rather than a changed signature (§4.4). The subject label reused rather than a second formatter written (§4.7). The one-frame teardown residue left un-guarded (§9).
- **Trade-offs named explicitly:** §10 lists four, each with the losing side's concrete cost.
- **Radical-clean over compromise:** where the existing `QuarantinedSubjectIds` had one consumer that the new surface fully serves, it is removed rather than half-kept.
- **Reader inventory covers AST *and* string-literal references:** `QuarantinedSubjectIds` (**F7**, six lines, one file, plus a prose mention in `docs/architecture/behavior-authoring.md:224` which is historical narrative and stays); the node name `"BehaviorRuntime"` as a string literal in two files (**F5**, both enumerated in §4.4).
- **Carrier-swap enumerated in full:** every `QuarantinedSubjectIds` site is listed in §4.3's table, not sampled.

**Data deliverables:** none — no SQL, no migration, no backfill.

**Document discipline**

- Cites Code Contracts (#114) and Design Contracts (#1136) as load-bearing (header).
- Reader and scope inventories are explicit (**F7**, §4.3, §3).
- Out-of-scope items are listed with their failure modes (§3), not merely absent.
- No multi-paragraph rationale for anything that obviously stays.
- **Supersedes nothing.** This is a new milestone document; `behavior-authoring.md` and `behavior-script-authoring.md` remain current for their own milestones and gain no banner.

---

## 16. Open questions and gaps

### For Toni — none blocking

1. **Is `exceeded budget (ScriptStepLimitExceededException)` acceptable author-facing wording?** §4.6 chose verbatim over restatement and gave the reason. If Toni wants it restated, it is a follow-up with #8246's distinction in hand, not a reason to build a mapper inside this milestone.
2. **Shipping a deliberately-broken package in `content/`.** `content/runaway-script.pkg` will be seeded into `user://packages` on a fresh install and will appear in **Open** beside the demo level. That is intentional — it is the reproduction case for the feature this milestone adds — but it is a visible content decision and Toni may prefer it excluded from export.

### Gaps to file, not built

- **A playtest that fails to *start* is silent.** `LevelEditor.StartPlaytest`'s `catch` (`:1033-1035`) is `GD.PrintErr`-only, so a `LevelContentException` at boot leaves the Play button apparently doing nothing. Excluded in §3 with its reason; worth its own task, since it needs a different surface (an editor-side notice) for a different failure class.
- **`SeedPackagesDirIfEmpty` never seeds a newly shipped package** to a machine that already has one (**F9**). Not a defect for M6 — §12 works around it with a precondition — but it means any future shipped content is invisible to every existing installation.

### Deliberately closed — named so they are not reopened by accident

- **The parse hang (#9341 / #9350)** is not M6's and cannot be made M6's: no quarantine is produced, so no surface can show one.
- **Quarantine policy (#8042)** stays split from visibility, per #8049 decision (b).
- **M5 preview `self` fidelity (#9753 / #9076)** is not folded in; nothing in this design forces it.
- **`ResolvedBehaviorBinding` is not extended** to carry a resource path (§4.5).

---

## 17. Status

Design complete. Every decision the task's seven open questions asked for is made in-document; no fork is left for the implementer. Written in the working tree and committed on `feat/quarantine-visibility`; no PR — Toni owns git from here.
