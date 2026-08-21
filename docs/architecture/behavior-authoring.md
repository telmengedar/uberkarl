# Architectural Document: Behavior Authoring (Behavior System Phase 3)

*Sarah (architect), 2026-08-17. Design-only — no implementation, no git, no PR.*
*Source task **DiVoid #8048** · master behavior design **#7704** (`docs/architecture/behavior-system.md`) · phase parent **#7703** · P2 **#7863** (shipped, PR #33/#34).*
*Load-bearing standards: **Design Contracts #1136** (§1 KISS/DRY/YAGNI, §5 Pre-Design Checklist walked as §16 of this doc) and **Code Contracts #114 §0** (implementer-side principles; §4 comments — default NONE).*

> **Amended by `editor-menu-surfaces.md` U3 (DiVoid #8525 §11, QA #8605 W5):** §7.1 below builds M4's context-menu entry point on `EditorAction.OpenContextMenu` and `Trigger.Context` "already exist[ing] in `LevelEditor`." U3 **deleted `Trigger.Context`** — the right-mouse-hold trigger now opens the Tiles list directly (`TriggerOrder` maps it to `Trigger.Tiles`), and there is no longer a context radial. `editor-menu-surfaces.md` §11 U3/§12 re-homes M4's entry surface to the list, not the radial. See §7.1's inline correction below.

---

## The ask, verbatim

Toni, 2026-08-17:

> *"currently behavior works but can not be customized in editor. So getting that to work will probably uncover a few more gaps (and also bring us a huge step forward). Also think of reusability here - scripts should usually be resources in a package so multiple objects can reference the same script."*

Two clauses, and they need separating because they have very different answers:

- **"can not be customized in editor"** — true, and the reason is worse than missing UX. See §2.
- **"scripts should usually be resources in a package so multiple objects can reference the same script"** — **this is already the model.** `ResourceKind.Script` exists, `BehaviorBinding.FromScript(ResourceReference)` binds by reference, `BehaviorBindingResolver.Resolve` reads it through `IResourceResolver` as UTF-8 Pooscript. N subjects sharing one script is the shipped design, not a change to it. What is missing is that **no one has ever exercised it** and there is no way to author it. See §3.

---

## 1. Problem Statement

Behavior runs. Behavior cannot be authored. Every behavior in `content/sample.pkg` was written by `tools/SampleContent/Program.cs` — a C# generator — not by the editor.

The goal of Phase 3 is that a level author, sitting in the editor with a gamepad and a keyboard, can:

1. Place an object and an area trigger into a level.
2. Give any behavior-bearing subject a behavior, and tune its parameters.
3. Write a Pooscript script, save it as a package resource, and bind **several** subjects to that one resource.
4. Play the level, see it work, and save it without losing what they authored.

**Success criteria** (each is a falsifiable acceptance test, not a feeling):

| # | Criterion |
|---|---|
| S1 | Load `sample.pkg` in the editor, save it, reload it: the heal trigger, both object placements, the level script, and the spike's `hurtOnContact` binding all survive byte-equivalently. **This fails today** (§2). |
| S2 | An object placed through the editor persists to the package and runs in standalone `LevelPlay`, not only in the editor's playtest. |
| S3 | An author assigns `patrol` to a placed object from a radial menu and changes `speed` from 24 to 40 with a stepper, entirely on the gamepad. |
| S4 | An author creates a new script resource, types Pooscript into it, binds **two** placed objects to that one `ResourcePath`, and both run. The package contains **one** script resource. |
| S5 | Everything in S2–S4 is undoable and redoable through `LevelEditSession`. |
| S6 | A script that trips a budget tells the author so, by name and reason, instead of silently doing nothing. |

---

## 2. The finding that reshapes this phase: the editor destroys behavior on save

This is not a missing feature. It is live data loss, verified in source on `main`.

**`LevelMergeWriter.BuildContributions`** (`src/Uberkarl.Editor/LevelMergeWriter.cs:29`) constructs a `LevelDefinition` from exactly eight properties — `TileSize, Width, Height, TileSet, BackgroundColor, Spawns, DefaultSpawn, Layers`. It writes **no** `TileBehaviorOverrides`, **no** `Triggers`, **no** `Objects`, **no** `LevelScript`.

**`TileSetMergeWriter.BuildContributions`** (`src/Uberkarl.Editor/TileSetMergeWriter.cs:39-49`) projects each `TileDefinition` with no `Behavior =` assignment.

Consequence: **opening `content/sample.pkg` in the editor and pressing Save silently deletes the heal trigger, both object placements, the level script, and the spike tile's `hurtOnContact` binding.** The package still loads; the level is simply inert.

There is a second, deeper half. `EditableBehaviorBindings.Resolve` (`src/Uberkarl.Editor/EditableBehaviorBindings.cs:37`) converts a `BehaviorBinding` into a `ResolvedBehaviorBinding` — which holds the script's **source text**, not its `ResourceReference` (`ResolvedBehaviorBinding.cs:13`). `EditableLevel` stores only the resolved form (`EditableLevel.cs:154-173`). So even if the merge writers wanted to write bindings, **the authored `BehaviorBinding` no longer exists in the editor model to write.** A script binding cannot be reconstructed at all — the reference is gone the moment the level is read.

This is why the phase cannot start with UX. Authoring into a model that cannot carry or persist what you author produces nothing. **Milestone M1 is the round-trip fix, and it is independently valuable as a bug fix** — filed separately so it can ship without waiting for any of the UX work.

---

## 3. What already works — do not redesign it

Per #1136 §2 ("existing systems first") and #8048's explicit instruction, here is the honest split.

| Concern | State | Verdict |
|---|---|---|
| Script as a package resource kind | `ResourceKind.Script = "script"` (`ResourceKind.cs:17`) | **Works.** Nothing to do. |
| Binding a subject to a script **by reference** | `BehaviorBinding.FromScript(ResourceReference)` (`BehaviorBinding.cs:37`) | **Works.** N-subjects-share-one-script is already the model. |
| Reading that resource at load | `BehaviorBindingResolver.Resolve` → `IResourceResolver.Resolve` → UTF-8 (`BehaviorBindingResolver.cs:21`) | **Works.** |
| Binding serialization | `BehaviorBindingJsonConverter` round-trips `{"script":"pkg:path"}` and `{"predefinedId":…,"params":{…}}` | **Works.** |
| Binding attachment points | `TileDefinition.Behavior`, `LevelDefinition.TileBehaviorOverrides`, `ObjectDefinition.Behavior`, `ObjectPlacement.Behavior`, `AreaTriggerDefinition.Binding`, `LevelDefinition.LevelScript` | **Works.** All six exist. |
| Adding a new resource to a package on save | `PendingResource` → `PackageMergeWriter.Compose` → `PackageBuilder.AddOrReplaceResource` | **Works.** A script resource is one more `PendingResource`; no new seam. |
| Undo/redo spine | `LevelEditSession` + `EditHistory` + `IEditCommand` | **Works**, but is cell-shaped — see §8. |
| In-package resource picking | `PackageBrowser` (`game/Editor/PackageBrowser.cs:29`) | **Works** for the two-step package→resource case; needs a same-package one-step mode (§7.4). |
| Editor carries authored bindings | — | **Broken** (§2). |
| Merge writers persist bindings | — | **Broken** (§2). |
| Editor command surface for objects/triggers/bindings | — | **Absent.** `LevelEditSession` has no object/trigger/binding method at all. |
| Predefined parameter metadata for the editor | — | **Absent.** `PredefinedBehaviors` is a hard-coded `switch`; no descriptor, no schema, no defaults table. #7704 §10.5 promised one; it was never built. |
| Contact direction on `event` | — | **Absent.** See §6. |

**Net:** the reusability *mechanism* needs no redesign. The reusability *path* has never been executed end-to-end — every demo binding in `sample.pkg` is a predefined; the only script resource, `scripts/level.poo`, is the level script, bound once. Making the multi-subject script path real is **in scope** (§4), because it is the most direct reading of Toni's sentence and because a mechanism no test and no content exercises is a mechanism we do not actually know works.

---

## 4. Scope & Non-Scope

### In scope

- Round-trip of authored `BehaviorBinding`s and script sources through the editor model, both merge writers, and the playtest snapshot (M1).
- Object placement (palette + grid-cursor paint) and area-trigger placement (two-corner rect), on the command/history path (M2).
- Contact direction classified once by the host and exposed on `event` (M3).
- Predefined behavior descriptors + gamepad assignment and parameter editing for objects, triggers, tile instance overrides, and the level script (M4).
- Script resource authoring: create, name, edit source, bind, share across subjects (M5).
- Author-visible quarantine reporting during playtest (M6).

### Explicitly out of scope

| Item | Why |
|---|---|
| **Cross-package script references** | #7594 is open future work. `EditableBehaviorBindings.Resolve:46` already throws for cross-package; that restriction stands and is mirrored by every picker in this design (same-package only). |
| **Objectset editing** — creating object *definitions* (graphic, collision role, default behavior, default state) | A `TileSetEditor`-sized surface. P3 authors *levels*: it places definitions that already exist and overrides their behavior per instance. Filed as its own task. |
| **One-way `platform` collision role** (and `trigger`) | Explicit call in §6.3. Filed as its own task. |
| **Quarantine strike-count policy** | #8042, already filed. §6.4 explains why it is not a blocker. |
| **Tile *type* (tileset-level) behavior authoring** | The tileset editor's surface, not the level editor's. M1 makes it *persist*; assigning it stays with `TileSetEditSession`. Per-instance tile overrides on the level ARE in scope (M4). |
| **Rename / delete of a script resource; orphan collection** | §5.2 — a deliberate YAGNI call, argued rather than omitted. |
| **Untrusted / imported / community scripts** | The safety gate that genuinely remains. §6.4. |
| **Gamepad-only source-text authoring** | Keyboard-gated by Toni's own prior ratification (#7440). §7.5. |
| **A visual / node-graph behavior authoring surface** | #7704 Phase 5+. Not now. |

---

## 5. The authoring model

### 5.1 What the editor must hold — and the pattern it copies

The editor already solves this exact problem for tile graphics. `EditableTile` carries **both** a `GraphicPath` (`ResourcePath`) and `Graphic` (the bytes); `TileSetMergeWriter` emits one `PendingResource` per graphic on save. A graphic that has been created but not yet saved lives in the model and materialises into the package on save.

**Scripts follow that pattern exactly** (DRY — this is reuse of an established shape, not a new mechanism):

| Element | Carries | Notes |
|---|---|---|
| `EditableLevel` | `BehaviorBinding?` on each trigger, object placement, tile override, and the level script | The **authored** form, replacing today's `ResolvedBehaviorBinding?`. |
| `EditableLevel` | A script table: `ResourcePath → string source` | Every script resource this level references, source text. Populated at read time (the reader already reads exactly these bytes), mutated by the script editor, emitted as `PendingResource`s of kind `script` on save. |
| `EditableTileSet` / `EditableTile` | `BehaviorBinding?` per tile, plus its own script table | Same shape; the tileset owns its own scripts as it already owns its own graphics. |

**Why the authored form replaces the resolved form rather than sitting beside it.** #1136 §4: when neither extreme has a consumer, take the radical-clean shape rather than the compromise. The resolved form has exactly one consumer — `EditableLevelSnapshot.ToResolvedLevel`, which builds the `ResolvedLevel` the playtest overlay plays. With the script table on `EditableLevel`, that consumer can resolve a binding itself (predefined → pass through; script → look up the `ResourcePath` in the table) and `ToResolvedLevel`'s signature does not change (verified: `LevelEditor.cs:707` calls it with the level alone). Holding both forms would be two representations of one fact, drifting apart the moment the author edits a script — the classic mirror shape #114 §5.4 warns about. **One form, resolved at the single point of use.**

**Why the table is authoritative, with no package fallback.** Every script the level references is loaded into the table when the level is read. Newly created scripts are inserted. So the table is always complete, and a lookup miss is a genuine error (a binding naming a `ResourcePath` that is not in the package) which surfaces as the `LevelContentException` it already surfaces as today. No fallback path, no "try package then table" ambiguity.

**Sharing falls out for free.** Two placements whose `BehaviorBinding.Script` name the same `ResourcePath` are two bindings, one table entry, one `PendingResource`, one resource in the `.pkg`. Nothing special is required to make N-share-one work — which is the point of §3.

### 5.2 Script resource identity and lifecycle — the honest answer

#8048 asks this to be opened honestly rather than waved through, because reference-by-id is easy to write and easy to orphan. Taking each sub-question in turn:

**Creating.** In scope. `scripts/<slug>.poo`, minted with the existing `LevelResourcePaths.Slugify` / `UniqueSlug` helpers (`LevelResourcePaths.cs:31,88`) — the same helpers that already mint level paths. No new naming machinery.

**Naming.** The author supplies a display name; the slug derives from it; the `ResourcePath` is the identity. Same contract as levels and tilesets.

**Renaming — out, deliberately.** A `ResourcePath` is the identity every binding stores. Renaming it means rewriting every binding in every level in the package that references it — a package-wide refactor, and the editor holds exactly one level at a time. Building a package-wide rewrite for a phase whose job is to make authoring work at all is the "flexibility for unknown future needs" shape #1136 §1 rejects. A script keeps the name it was created with. If renaming becomes a real complaint, it comes back with the actual requirement in hand.

**Deleting — out, and it cannot bite us.** The editor has no delete-resource operation today for *any* kind (`PackageBuilder.RemoveResource` exists but nothing calls it from the UI). So P3 cannot create a dangling reference by deletion, because P3 cannot delete. Designing a dangling-reference guard for a deletion path that does not exist is a defensive guard for an impossible scenario (#1136 §6). The pre-existing case — a hand-edited package whose binding names a missing path — already surfaces as a typed `LevelContentException` at load. That is sufficient and unchanged.

**"Which subjects reference this script" — out, and cheap if ever wanted.** No index is needed: a level's referencing subjects are found by walking its own bindings, and a level has tens of subjects, not thousands. Nothing in P3 needs the answer. Building a reverse index now would be a data structure with no consumer (#1136 §2, form 1).

**Orphans — harmless, no collection.** A script resource with no referencing binding costs a few hundred bytes in the archive. There is no garbage-collection story and there should not be one; an unreferenced script is very often a script the author is about to bind.

**The net position:** the identity model is `ResourcePath`, created once, never renamed, never deleted, never indexed. Every one of those omissions is a deliberate YAGNI call with the failure mode named, not an oversight.

---

## 6. The facade and runtime decisions this phase forces

### 6.1 Contact direction — the host classifies it once

**The problem.** `bumpOnHitFromBelow` guards only on `player.velocity.y < 0` — "the player is rising" — and never tests position (`PredefinedBehaviors.cs:90-109`). Touching the block from the side mid-jump satisfies it exactly as well as hitting it from below (#8047). The predefined's name claims a semantic its body never checks.

**The decisive fact.** A script *cannot* fix this correctly. The facade exposes `self.position` and `player.position` (`ISelfFacade.cs:25`, `IPlayerFacade.cs:11`) — points, not extents. Nothing exposes the object's size or the player's half-extents. A script can only hard-code a tile-size guess. Meanwhile the host has both rectangles already: `BehaviorRuntime` builds the player AABB from `Player.CollisionHalfExtents` at `BehaviorRuntime.cs:207-209` and tile/trigger rects at `:233-269`, and `ObjectBodyBuilder` gave every object body a `tileSize × tileSize` shape. **The information exists on exactly one side of the boundary, and it is not the script's side.** That settles it independently of any convenience argument.

**The KISS/YAGNI math.** Cost of *not* doing it: every direction-sensitive behavior re-derives direction from two points it cannot correctly compare. The direction-sensitive family an authoring menu immediately implies is not one case — bump-from-below, stomp-to-break, collect-from-above, hurt-from-the-side — so the duplicated (and incorrect) positional predicate lands at ~4 sites and grows with every free-text author. Cost of doing it: one property on `BehaviorEvent`, one classification method in `BehaviorRuntime` operating on two `Rect2`s it already has. The asymmetry is not close.

**Decision: add a contact direction to `BehaviorEvent`, exposed as `event.direction`.**

- **On `BehaviorEvent`, not `EventParty`.** #8047 floats `EventParty` as a home. `EventParty` is an identity record — *who* the other party is (`Kind`, `Name`, `Cell`). Direction is a property of the *contact*, not of the party, and putting it on the party would mean an `EventParty` outside a contact context carries a meaningless field. `BehaviorEvent` is already the per-event carrier (`Kind`, `Other`, `Delta`, `Tag`, `MessageName`, `MessagePayload`) and is already bound as the `event` global (`BehaviorRuntime.cs:120`). It goes there.
- **Semantics: which side of *me* was touched**, from the subject's perspective. `"below"` means the other party contacted this subject's underside — so `bumpOnHitFromBelow` reads `event.direction == "below"` and means exactly what its name says.
- **A string, not an enum.** `event.kind` is already a string; the facade contract returns value snapshots (#7704 D-1), and a string keeps Pooscript comparisons natural. Values: `"below"`, `"above"`, `"left"`, `"right"`. `null` when the event is not a contact.
- **Classification rule:** minimum-penetration axis of the two rectangles — compute the overlap on each axis, take the axis with the smaller overlap as the contact axis, and the sign of the centre-to-centre delta on that axis gives the side. This is the standard AABB resolution and it is stable for the shallow-penetration case that edge-triggered contact always produces.

This is the mechanism that makes #8047 fixable properly; the fix itself stays #8047's own task.

> **Amended 2026-08-20 (M3 implementation, QA #8741 W-1): the exact-penetration tie resolves toward the horizontal axis.** §6.1's classification rule above is silent on what happens when the two axes' penetrations are exactly equal — a perfect corner hit. The as-implemented `Classify` originally fell through to the vertical branch on a tie, which for a rising player yields `"below"` — the one answer this milestone exists to suppress. QA flagged that as an implementation accident (whatever the `else` branch happens to do), not a decision, and ruled it should resolve the other way: **ties resolve to the horizontal axis** (`"left"`/`"right"`), which is fail-safe for the bump-suppression case and costs nothing on any other direction-sensitive predefined. `ContactDirection.Classify` (`src/Uberkarl.Behavior/ContactDirection.cs`) now treats the comparison as `penetrationX <= penetrationY` rather than `<`; this note is the record of why, per #114 §4 (the code carries no rationale comments).

> **Amended 2026-08-20 (M3 implementation, QA #8741 W-2): `Classify`'s overlap precondition, and why it holds today.** "Minimum penetration" is undefined without penetration — for non-overlapping rects `Classify` returns a plausible-looking answer from negative inputs rather than failing loudly. Callers must gate on overlap first. Tiles do: `DispatchTileContacts` gates on `Rect2.Intersects` against the identical pair of rects it then passes to `Classify`. Triggers never reach `Classify` at all — `DispatchTriggerOverlaps` (`BehaviorRuntime.cs`) dispatches `OnEnter`/`OnLeave` with no direction argument, so the overlap precondition is moot for them; an earlier draft of this note claimed triggers gated the same way tiles do, which was wrong. For solid objects the coupling is less direct: the sensor's overlap gate is `tileSize + 2×SensorMargin` (`ObjectBodyBuilder.cs`) against the player's 12×24 physics shape, while `Classify` receives a synthesised `tileSize`-sized object rect against the 14×26 `playerAabb` (`BehaviorRuntime.cs`). These two boundaries coincide only because `SensorMargin == ContactMargin == 1` today (`9+6 = 15 = 8+7`, `9+12 = 21 = 8+13`) — changing either margin constant alone would silently start feeding `Classify` non-overlapping input. Not reachable today; named so a future change to either constant is made with the coupling in view.
>
> **Amended 2026-08-20 (M3 implementation, QA #8741 W-3): the object contact rect now reads its size from `ObjectBodyBuilder`, not a second hard-coded `tileSize`.** `BehaviorRuntime.DispatchObjectContacts` synthesises the object's classify-rect from `obj.Body.Position` and `tileSize`, which was previously a second, independent place assuming object size == tile size, with nothing tying it to `ObjectBodyBuilder.Build` (which sizes the actual collision shape). `ObjectBodyBuilder.CollisionSize(tileSize)` is now the single source of truth both call sites read, so the assumption can only drift by changing one function.

### 6.2 Predefined descriptors — the metadata the menu needs

An assignment menu must know: which predefineds exist, which subject kinds each is legal for, which parameters each takes, their defaults, and their step/range for `SteppedValueEditor`. None of that is queryable today — `PredefinedBehaviors.TryGetSource` is a `switch` over four ids, and the parameter names and defaults are bare private consts (`PredefinedBehaviors.cs:39-47`).

**Decision: one static descriptor list in `PredefinedBehaviors`.** Per predefined: id, display label, the subject kinds it applies to, and its parameters (name, label, default, min, max, step).

Three notes on why this is not the over-engineering #1136 §3 warns about:

- **It is a DRY win, not new surface.** Today the parameter names and defaults exist twice — as consts and as `FormatParameter(…, fallback)` call arguments. The descriptor becomes the single source and the existing switch reads its defaults from it.
- **min/max/step are UI affordances, not config knobs.** They are `const`-equivalent data describing how a stepper walks a value. There is no operator, no environment difference, no tuning event — and correspondingly no config file, no settings entry.
- **Subject-kind applicability is load-bearing, not decoration.** `hurtOnContact` on a level script is nonsense. Nothing rejects it today; it would compile and silently never fire. The menu must not offer it. That is a concrete behaviour, not tidiness.

It is a data list, not a registry: no interface, no attributes, no reflection, no plugin point. One implementation, no second planned.

This is also what makes #8045 (`rise` hard-coded at a 3-tile bump) author-tunable, since `ResolvedBehaviorBinding.Parameters` already carries per-instance values end-to-end.

### 6.3 Collision roles — the in-or-out call

`ObjectCollisionRole` has `Solid` and `Passthrough` (`ObjectCollisionRole.cs:11`). #7704 D-2 named four.

- **`trigger` — out, permanently. It is a mirror.** `Passthrough` already yields an `Area2D` contact sensor that moves with the object. A `trigger` role would be a second spelling of the same runtime object — precisely the parallel-mirror shape #114 §5.4 rejects. The static case is already `AreaTriggerDefinition`. Drop it from the roadmap rather than carrying it as debt.
- **`platform` (one-way) — out of P3, filed as its own task.** It is genuinely absent and genuinely useful, and it is genuinely *not an authoring change*: it is a new runtime collision capability (one enum member, one-way collision on the shape in `ObjectBodyBuilder`) that P3's UX would then expose. No level in hand needs it. Bundling a runtime capability into an authoring phase also violates the one-feature-one-PR discipline. It ships when a level wants it, with the level as its acceptance test.

Recording this as an explicit call rather than a silent omission is the whole point — the authoring menu will otherwise raise it on day one.

### 6.4 Where the free-text safety boundary now sits

**The old gate.** #7704 Phase 4 read: *"Free-text editor + untrusted/community scripts (GATED on #7409)"*, because a tight `while(true)` could not then be interrupted.

**The premise is stale, and it also conflated two different things.** #7409 is closed. The sandbox is allow-listed and secure-by-default — verified at `BehaviorLoader.CreateSandboxedParser:57`: `TypeInstanceProvidersEnabled=false`, `TypeCastsEnabled=false`, `ImportsEnabled=false`. Since 1.1.0 (PR #33) `MaxSteps` / `Timeout` / depth / memory guards fire **per dispatch**, verified in-engine: a runaway handler quarantines and the game keeps running.

The old gate covered two populations under one word:

1. **A script the person at the keyboard writes for their own level.**
2. **A script that arrives from somewhere else** — imported, community, cross-package.

Those have different threat models, and only the second is a trust question at all. For (1) the author *is* the principal: desktop-only, single-player, editing a local package they own. They can already achieve any effect by editing the `.pkg` in a text editor — the editor's text surface grants no new capability, only convenience. The sandbox bounds reach regardless of authorship; the per-dispatch budget bounds cost. The residual risk is "the author writes a slow script and their own playtest degrades", which is an authoring-UX problem, not a safety one.

**Decision: free-text authoring of the author's own scripts moves into Phase 3. The gate stays on scripts authored by someone other than the person playing** — import, community, and cross-package sharing (#7594, already open). The gate's *name* was wrong: it was never free text that needed gating, it was provenance.

This matters because "customize behavior in editor" is not honestly satisfied by parameter-tuning four shipped predefineds. Free text is the feature.

**On #8042, and why it is not a blocker.** A transient wall-clock breach permanently quarantines a healthy behavior, which would be poisonous for an author iterating on a script — cold start alone was measured at 29.6 ms. But in the *editor* loop, quarantine is already per-playtest-session: `StopPlaytest` frees the whole play-world subtree (`LevelEditor.cs:722-730`) and `StartPlaytest` rebuilds it through `PlayRuntimeBuilder.Populate`, minting fresh `CompiledBehavior`s. Stopping and restarting playtest clears quarantine, and it is one button.

So the P3-forced requirement is **not** the policy fix — it is that the author must be *told*. Today a quarantined behavior is indistinguishable from a behavior that does nothing: `BehaviorRuntime` raises `OnQuarantined` and tracks `QuarantinedSubjectIds` (`BehaviorRuntime.cs:43`), and none of it reaches the screen. That is M6. #8042 remains its own task, correctly scoped as a policy change for shipped levels.

---

## 7. Authoring UX — shape and reuse

Gamepad-first is not optional (#7440, #7466). Every element below reuses an existing editor mechanism; nothing here is a new interaction paradigm.

### 7.1 Selection: the grid cursor is the selection

Assigning behavior to a placed object requires selecting it. There is no selection concept on the canvas today — only `GridCursor`.

**Decision: the cursor's cell is the selection.** `EditorAction.OpenContextMenu` exists in `LevelEditor` (`EditorAction.cs:15`). **Correction (`editor-menu-surfaces.md` U3, see the amendment banner above): `Trigger.Context` and its radial no longer exist** — the right-mouse-hold trigger (the physical gesture this decision hangs off) now maps to `Trigger.Tiles` and opens the same Tiles **list** the Tiles trigger does. M4's entry point is therefore a "Behavior…" / "Tile behavior…" row offered by that list at the cursor's cell, not a wedge on a context radial; the surface differs, the decision itself — cursor cell as selection, no selection model, no hit-testing layer, no new state — is unaffected. This is the can-it-be-deleted check applied to a whole subsystem: the simple version covers the requirement.

### 7.2 Placement: a third paint mode

`LevelEditor` already carries a `paintingTerrain` flag that changes what `OnCellPressed` does (`LevelEditor.cs:63,838`). Object placement is the same shape — a third mode. The object palette is a radial built exactly like `BuildTilesMenu` (`LevelEditor.cs:237`).

The trigger rect tool is a two-corner mode: first `OnCellPressed` records a corner, the second commits the rect as one command. Two integers of transient state on `LevelEditor`.

### 7.3 The active objectset — session state, not level state

An object palette needs a source of object definitions. `ObjectPlacement.ObjectSet` is per-placement; `LevelDefinition` has no level-wide objectset field (unlike `TileSet`).

**Decision: the active objectset is editor session state and is not persisted.** Each placement already records its own `ObjectSet` reference, which is the persisted truth. The editor adopts the objectset of the level's first placement, or lets the author pick one from the package. **This needs no `LevelDefinition` change** — the alternative (adding a level-wide objectset field) would add a persisted data point whose only job is to remember a UI preference, failing the named-decision test (#868).

### 7.4 Pickers: extend `PackageBrowser`, do not fork it

Picking a script resource (and an objectset) needs an in-package resource list filtered by `ResourceKind`. `PackageBrowser` already does package→resource in two steps and already filters by kind (`PackageBrowser.cs:136`). Since cross-package is out of scope, the need is step two alone, against the currently-open package.

**Decision: `PackageBrowser` gains a one-step summon mode** — "list this package's resources of kind K". Same window, same focus containment, same gamepad handling, same `ResourceChosen` event shape. The brief's constraint ("reuse the existing package/resource browser, not a parallel picker") is met by extension.

### 7.5 Free text: keyboard, by prior ratification

Toni already settled this (#7440): *"scripting is only possible with keyboard (except assignment of predefined scripts)."* Desktop-only (#7407) guarantees a physical keyboard.

- **Assignment and parameter tuning are fully gamepad-operable** — radial menus and `SteppedValueEditor`. This is the gamepad-first requirement, and it is met.
- **Source editing is a summoned panel with a multi-line text surface**, focus-contained like `LayerManagerPanel` / `TileSetBindPanel` / `LevelResizePanel`, opened and closed on the gamepad, typed on the physical keyboard.
- **`OnScreenKeyboard` stays the *naming* path** — short strings like a script's display name — and is explicitly not the source-editing path. Using a per-key on-screen keyboard to type a program is a bad experience we would be building on purpose.
- **Parse feedback on commit.** `BehaviorLoader.Compile` already returns a quarantined `CompiledBehavior` carrying a reason for a parse error (`BehaviorLoader.cs:46`). The panel compiles on commit and shows that reason. Cheap, and the difference between an authoring tool and a text box.

### 7.6 Panels follow the established shape

Every summoned panel in this editor is a `Control` exposing plain `Action` events, not Godot signals (`LayerManagerPanel.LayerModelChanged/Closed`, `TileSetBindPanel.TileSetChosen/Cancelled`, …). The behavior panel and script edit panel follow that shape verbatim. `MenuOutcome` gains factories and `MenuOutcomeKind` gains members, which is the established extension pattern for this menu system (`MenuOutcome.cs:51`).

---

## 8. Commands, undo, and the `CellChange` friction

Everything authored must go through `LevelEditSession` so it is undoable (brief constraint; and tile/layer editing already is).

`LevelEditSession` today has **no** object, trigger, or binding method. It gains them — placement, removal, move, rect commit, and set-binding — each executing an `IEditCommand` through `EditHistory`, exactly as `PaintCell` does.

**The friction:** `IEditCommand.Apply/Revert` return `CellChange` (`IEditCommand.cs:12`), a `readonly record struct (LayerIndex, X, Y, TileId)` consumed by `EditorCanvas.Apply(CellChange)` to patch one tilemap cell. An object placement is not a tilemap cell change.

Options considered:

| Option | Assessment |
|---|---|
| A parallel non-cell command path | Two histories, two undo stacks, or a merged one with two shapes. Rejected — a parallel layer (#1136 §2 form 2). |
| Widen `CellChange` into a variant/union type | A new type whose value-space subsumes the existing one; every existing call site churns for one new case. Rejected. |
| **Non-cell commands return `null`; the canvas redraws its overlays** | Chosen. |

`LevelEditSession.PaintCell` and `EditHistory.Undo` already return `CellChange?`, and the existing null already means "no cell patch to apply". Object/trigger/binding commands return `null` and the editor issues a `QueueRedraw()` after any history operation, which is what the object/trigger overlay needs anyway since `EditorCanvas._Draw` renders from the level model.

**The trade-off, stated plainly:** `null` now carries two meanings — "nothing happened" and "something happened that is not a cell patch". That ambiguity is benign because **both** callers' correct response is identical: do not patch a cell, redraw. If a future case needs to distinguish them, it arrives with a concrete reason and the shape it actually needs. Adding a discriminator now, with both branches doing the same thing, would be indirection rather than abstraction (#1136 §4).

`EditorCanvas` also gains an object/trigger overlay in `_Draw` — objects at their cell, triggers as rect outlines. There is no such rendering today (`SetLevel` builds tile layers only, `EditorCanvas.cs:101`), and placing invisible things is not authoring.

---

## 9. Conceptual data model

No new persisted entity, and **no change to any `Uberkarl.Content` definition type** — every attachment point already exists (§3). The changes are confined to the editor's in-memory model and the writers.

```
EditableLevel  ──owns──▶  script table: ResourcePath → source        [new]
      │                        ▲
      │                        │ resolved at playtest snapshot time
      ├── ObjectPlacement ──── BehaviorBinding?     [was ResolvedBehaviorBinding?]
      ├── AreaTrigger ──────── BehaviorBinding      [was ResolvedBehaviorBinding ]
      ├── TileBehaviorOverride BehaviorBinding?     [was ResolvedBehaviorBinding?]
      └── LevelScript ──────── BehaviorBinding?     [was ResolvedBehaviorBinding?]

BehaviorBinding  ──┬── Script: ResourceReference ──▶ one script resource
                   │        ▲         ▲
                   │        └─────────┴── N subjects, one resource  (the reusability ask)
                   └── PredefinedId + Parameters ──▶ PredefinedBehaviors descriptor
```

On save, `LevelMergeWriter.BuildContributions` emits the level JSON **including** its bindings, plus one `PendingResource` of kind `script` per script-table entry. `TileSetMergeWriter` does the same for tile bindings and tileset-owned scripts. Both feed the unchanged `PackageMergeWriter.Compose`.

---

## 10. Contracts

| Contract | Input → Output | Invariants |
|---|---|---|
| **Editor read-through** | `Package` + level resource → `EditableLevel` | Every binding is carried in **authored** form; every referenced script's source is loaded into the script table. A binding naming an absent path raises the existing typed `LevelContentException`. |
| **Merge contribution** | `EditableLevel` → `PendingResource[]` | Level JSON carries all four binding families; one `script` resource per table entry. Two bindings sharing a `ResourcePath` produce **one** resource. Pure — no IO (unchanged property). |
| **Playtest snapshot** | `EditableLevel` → `ResolvedLevel` | Predefined bindings pass through; script bindings resolve against the script table. Signature unchanged. |
| **Behavior command** | subject reference + `BehaviorBinding?` → `null` | Undoable/redoable through `EditHistory`; sets the session dirty. `null` binding removes the behavior. |
| **Predefined descriptor query** | subject kind → descriptors | Only predefineds legal for that kind. Each carries parameters with default/min/max/step. |
| **Script resource creation** | display name → `ResourcePath` | Slug unique within the package. New entry in the script table with empty source. Nothing is written to the package until save. |
| **`event.direction`** | contact event → `"below"`/`"above"`/`"left"`/`"right"`/`null` | From the **subject's** perspective. `null` for non-contact events. Computed once by the host per contact. |
| **Quarantine report** | scheduler event → author-visible line | Subject identity and reason. Cleared by restarting playtest. |

---

## 11. Cross-cutting concerns

- **Undo/redo:** every authoring action routes through `LevelEditSession` → `EditHistory`. No authoring path bypasses it.
- **Consistency:** the script table is the single in-editor truth for script source; bindings hold references only. There is no second copy to drift.
- **Failure modes:** a parse error surfaces in the script panel on commit; a runtime budget breach surfaces as a quarantine line during playtest; an unresolvable binding surfaces as `LevelContentException` at load. All three already have carriers — the gap is that none reach the author's eyes.
- **Determinism:** unchanged. Direction classification is a pure function of two rectangles, evaluated inside the existing behavior phase, recording no intents.
- **Engine boundary:** `src/Uberkarl.{Content,Packages,Editor,Behavior}` stay Godot-free. `BehaviorEvent.Direction` and the descriptor list are core; the rect classification, the overlay rendering, and every panel are `game/` glue.
- **Security:** the sandbox and per-dispatch budgets are unchanged and apply to authored scripts identically. The provenance gate (§6.4) is the only trust boundary and it stays closed.

---

## 12. Quality attributes & trade-offs

| Attribute | How this design addresses it |
|---|---|
| **Simplicity** | No new subsystem. Selection reuses the grid cursor; placement reuses the paint-mode flag; pickers reuse `PackageBrowser`; panels reuse the summoned-`Control` shape; script persistence reuses the tile-graphic pattern; resource creation reuses `LevelResourcePaths`. The only genuinely new concepts are one property on `BehaviorEvent` and one descriptor list. |
| **Correctness** | S1 turns a silent data-loss bug into a regression test. Direction classification moves a computation to the only side that has the inputs. |
| **Maintainability** | One representation of a binding in the editor, not two. One source of parameter defaults, not two. |
| **Reviewability** | Six independently shippable units, in dependency order, each with its own acceptance test. |

**Trade-offs made explicitly:**

1. **Authored form replaces resolved form in the editor model** (§5.1). Cost: `ToResolvedLevel` gains a resolution step. Benefit: no dual representation to drift. The alternative was a mirror pair — rejected per #114 §5.4.
2. **`null` from non-cell commands is overloaded** (§8). Cost: one ambiguous sentinel. Benefit: no parallel command path, no union type, no churn at existing call sites. Justified because both meanings share one correct response.
3. **Direction as a string, not an enum** (§6.1). Cost: no compile-time checking on the value inside scripts — but Pooscript is dynamically typed, so there never was any. Benefit: consistent with `event.kind`, natural comparisons, no type leaking through the facade boundary.
4. **Free text enters P3** (§6.4). Cost: an author can write a slow script and degrade their own playtest. Mitigated by per-dispatch budgets (already shipped), by quarantine being per-playtest-session, and by M6 making it visible. Benefit: the feature Toni actually asked for.
5. **P3 is six PRs, not one.** Cost: more review events. Benefit: M1 alone fixes live data loss and can ship immediately; a single bundled PR would hold that fix hostage to the whole authoring surface. Required by the one-feature-one-PR discipline.

---

## 13. Risks

| Risk | Mitigation |
|---|---|
| M1 changes the editor's read path and could regress level loading | S1 is a round-trip test against the real `sample.pkg`; the existing editor tests cover the non-behavior path. |
| Direction classification is wrong at shallow penetration or corner contact | Minimum-penetration axis is the standard resolution and is stable for the shallow case edge-triggered contact produces. Acceptance includes the negative case #8047 names: approach from the side while rising must **not** bump. |
| The predefined descriptor list drifts from `TryGetSource`'s switch | The switch reads its defaults from the descriptor — one source, so drift is structurally prevented rather than tested for. |
| An author writes a script and the facade action silently does nothing | Resolved — the 9 unimplemented intent types and the facade methods that emitted them were deleted, not shipped half-working (§14, gap G3, DiVoid #8237). The facade now only exposes actions that work; M5's acceptance is written against that (now-complete) action set. |
| Guard sizing: an acceptance test that passes for the wrong reason | The P2 lesson (#7863): *"for every guard, the question is not 'does it pass' but 'have I seen it fail for the reason it exists'."* Every acceptance test in §15 must be verified **red** against the pre-change code. |

---

## 14. Gaps uncovered by this design

Toni predicted this phase would uncover gaps. It did. Each is filed as its own DiVoid task rather than absorbed into this design's scope.

| # | Gap | Severity |
|---|---|---|
| **G1** (DiVoid #8050) | **Editor save destroys all behavior data.** Both merge writers drop bindings; the editor model cannot represent an authored script binding at all. Live data loss on `main`. | Bug — high. This is M1. |
| **G2** (DiVoid #8051) | **`ILevelFacade.Object(name)` can never succeed.** `BehaviorRuntime.cs:185` keys `levelFacade.Objects` by subject id (`"object:0"`), not by `ObjectPlacement.Name`. The facade promises name lookup; `ObjectPlacement.Name` exists and is authored. Only `ObjectsNamed` works. A free-text author hits this immediately. | Bug. |
| **G3** (DiVoid #8052, resolved by #8237) | **9 of 15 intent types were silently discarded — resolved by deletion, not implementation.** `Spawn`, `SetTile`, `Message`, `Despawn`, `SetGraphic`, `ScheduleTimer`, `Teleport`, `SetSpawn`, `SetPhysics` and the facade methods that emitted them were removed per the DiVoid #8237 ruling (a declared-but-unreachable member does not exist). `BehaviorRuntime.ApplyIntents` now handles every intent type that exists (Hurt/Heal/SetState/MoveToCell/MoveToPosition/MoveBy). | Resolved. |
| **G4** (DiVoid #8053) | **Same-frame state reads are stale.** `SetStateIntent` applies only after the whole dispatch pass, so `self.getState` inside the same frame sees the pre-frame value. Deliberate (the intent buffer is what makes dispatch deterministic) but undocumented and surprising — `bumpOnHitFromBelow` is written around it. Needs a decision: document it in the facade contract, or make self-state reads see pending writes. | Design question. |
| **G5** (DiVoid #8054) | **One-way `platform` collision role not implemented.** §6.3. | Feature — explicit P3 omission. |
| **G6** (DiVoid #8055) | **No objectset editor.** Object *definitions* can only be created by the C# sample generator. P3 places existing definitions; creating them has no surface. | Feature — explicit P3 omission. |

Pre-existing and already filed, not re-filed: #8042 (quarantine policy), #8045 (bump amplitude), #8047 (side-contact bug), #7594 (cross-package references).

---

## 15. Implementation guidance — six units, dependency-ordered

One feature per PR. Each unit branches fresh from `origin/main`.

**M1 — Behavior round-trip in the editor model.** *No UX.* Editor model carries authored `BehaviorBinding` plus a script table; both merge writers persist bindings and emit script resources; the playtest snapshot resolves from the table. **Acceptance (S1):** load `sample.pkg`, save, reload — heal trigger, both placements, level script, and spike binding all survive. Verified red against current `main`, where all four vanish. *Ships independently; fixes G1.*

**M2 — Object palette and trigger rect tool.** Placement/removal/move commands on `LevelEditSession`; object and trigger overlay rendering on `EditorCanvas`; object placement as a third paint mode; two-corner trigger rect; active objectset as session state. **Acceptance (S2, S5):** an object placed in the editor persists and runs in standalone `LevelPlay`; placement and removal undo and redo. *Depends on M1.*

**M3 — Contact direction on `event`.** `BehaviorEvent.Direction` in core; minimum-penetration-axis classification in `BehaviorRuntime` for tile, trigger, and object contacts. **Acceptance:** a subject contacted from each of four sides reports the matching direction; the #8047 negative case (side approach while rising) reports `"left"`/`"right"`, not `"below"`. *Independent of M1/M2 — may ship in parallel.*

**M4 — Predefined descriptors and assignment UX.** Descriptor list in `PredefinedBehaviors` with the existing switch reading its defaults from it; behavior panel reached from the context radial; predefined pick via radial; parameters via `SteppedValueEditor`; applies to objects, triggers, tile instance overrides, and the level script. **Acceptance (S3):** assign `patrol` and change `speed` 24→40 entirely on the gamepad; the change persists and takes effect in playtest. *Depends on M1, M2. Should follow M3 so direction-sensitive predefineds do not lie in the menu.*

**M5 — Script resource authoring.** Create/name a script resource; one-step in-package resource picker on `PackageBrowser`; script edit panel with parse feedback on commit; bind a script to any subject. **Acceptance (S4):** create one script, bind **two** placed objects to it, both run, and the saved package contains exactly one script resource. *Depends on M1, M4.*

**M6 — Quarantine visibility in playtest.** Surface `BehaviorScheduler.Quarantined` (already raised, already tracked at `BehaviorRuntime.cs:43`) in the playtest overlay: subject and reason. **Acceptance (S6):** a script that trips `MaxSteps` produces a visible, named report rather than silence. *Depends on M5 for its motivating case; small enough to ride with it if review prefers.*

**Do not build:** a script rename/delete/reverse-index facility (§5.2); an objectset editor (G6); the `platform` role (G5); the #8042 strike-count policy; any cross-package resolution (#7594); a scope/plugin registry for predefineds (§6.2).

---

## 16. Design Contracts §5 audit

**KISS / DRY / YAGNI**
- No new type mirroring an existing one — the authored form **replaces** the resolved form in the editor rather than sitting beside it (§5.1); `trigger` collision role rejected precisely as a mirror of `Passthrough` (§6.3).
- No abstraction with one implementation — the predefined descriptor is a static data list, explicitly not a registry/interface/attribute scheme (§6.2).
- No element justified by "might need later" — rename, delete, reverse index, and orphan collection are each rejected with the concrete failure mode named (§5.2); `platform` and the objectset editor are deferred to the level that needs them (§6.3, §4).
- No deprecation period, feature flag, compatibility shim, or transition window anywhere.
- Inline-vs-extract math: §6.1 quotes the duplication the alternative would create (~4 sites of a positional predicate) **and** the decisive fact that the script side lacks the inputs to compute it correctly at all.

**Existing systems first**
- Audited per-concern in §3, with a works/broken/absent verdict for each.
- Every new layer's reuse target is named: cursor-as-selection (§7.1), paint-mode flag (§7.2), `PackageBrowser` extension (§7.4), summoned-panel shape (§7.6), tile-graphic pattern for script persistence (§5.1), `LevelResourcePaths` for slugs (§5.2), `PendingResource`/`PackageMergeWriter` for save (§3).
- New persisted data: **none.** No `Uberkarl.Content` definition type changes; the active objectset is deliberately session state, not a persisted field, precisely because it would fail the named-decision test (§7.3).
- Consumer chain recursed: the resolved binding form has exactly one consumer (`ToResolvedLevel`), which is why it can be replaced rather than preserved (§5.1).

**Configurability**
- No config knob is introduced. Descriptor min/max/step are UI affordances in code with no operator and no environment difference, stated as such (§6.2).

**Less is better**
- Delete/merge/inline run on every element: selection subsystem deleted in favour of the cursor; dual binding representation merged to one; `CellChange` variant type rejected in favour of the existing `null`.
- Trade-offs named concretely with costs, not adjectives (§12, five entries).
- Radical-clean chosen over compromise where the existing surface has one consumer (§5.1).

**Document discipline**
- Cites #114 and #1136 as load-bearing (header).
- Out-of-scope enumerated explicitly in a table (§4), not left absent.
- Real type names throughout — `BehaviorBinding`, `ResolvedBehaviorBinding`, `ResourceReference`, `ResourceKind.Script`, `LevelEditSession`, `EditHistory`, `IEditCommand`, `CellChange`, `EditableLevel`, `LevelMergeWriter`, `TileSetMergeWriter`, `PendingResource`, `PackageMergeWriter`, `PackageBrowser`, `MenuOutcome`, `SteppedValueEditor`, `OnScreenKeyboard`, `BehaviorEvent`, `EventParty`, `ObjectCollisionRole`, `BehaviorRuntime`, `PredefinedBehaviors` — with file:line anchors on every load-bearing claim (#6836: expose the real structures under their real names).
- This document does **not** supersede `behavior-system.md`; it is Phase 3 of it. It does correct that document's Phase 3/4 boundary (§6.4) and its D-2 collision-role list (§6.3).

**Open forks:** none. Every decision this design was asked to make is made in-document.

---

## ADDENDUM 2026-08-18 — the trigger tool does not belong in M2. `AreaTriggerDefinition.Binding` stays required; the milestone boundary was wrong, not the content model

Raised from M2 implementation (#8463). John implemented the trigger rect tool and found that `AreaTriggerDefinition.Binding` is `required` while assignment UX is M4 — so a newly placed trigger must be given *some* binding at placement time and there is no neutral one. He defaulted to `healOnEnter` and flagged it rather than deciding silently. That was the right call: the flag is the deliverable, not the default.

**This addendum does not revise the sections above. They stay readable — the phase has been implementing against them. It corrects one boundary they drew wrong.**

### The decision

**Keep `AreaTriggerDefinition.Binding` required. Remove the trigger *creation* path from M2 and land it in a new milestone M4b, immediately after M4, where placement and binding assignment ship as one act. The read-only trigger overlay stays in M2.**

§16's audit above closed with **"Open forks: none. Every decision this design was asked to make is made in-document."** That was wrong. This was the fork it missed.

### What decides it: a trigger has no identity apart from its binding

`AreaTriggerDefinition` is `{Name, X, Y, Width, Height, Binding}`. Subtract the binding and what remains is a named rectangle with no semantics anywhere in the system:

- **Nothing reads a trigger except its own dispatch.** `ILevelFacade` exposes `TileAt`, `Object`, `ObjectsNamed`, `GetState`, `SetState` — **no trigger query at all**. A script cannot ask whether the player is inside a named region. A trigger is reachable only as the *subject* of its own `onEnter`/`onLeave`.
- **`BehaviorRuntime.RegisterTriggers`** (`game/Behavior/BehaviorRuntime.cs:149`) compiles the binding, registers a `BehaviorInstance`, and only then adds the `ScriptedTrigger` world rect. The rect exists *in order to* dispatch. There is no other consumer.
- **`Name` is not identity.** It is a non-unique author label surfaced as `BehaviorSubject.Name`; it does nothing on its own.

So an unbound trigger is not "a rect that may later carry a behavior". It is a piece of level content that **exists and does nothing, with no way to observe it** — precisely the shape #8237's ruling deleted from the vocabulary: *"we implement them as soon as we know what they do and then they work."* Making `Binding` nullable would move that anti-pattern from the engine's vocabulary into the author's saved data, where it is worse: it persists into every `.pkg`.

**Contrast with `ObjectPlacement`, which is why the asymmetry is correct and not an oversight.** `ResolvedObjectPlacement.Binding` *is* nullable, and `RegisterObjects` already guards `if (placement.Binding is { } binding)`. That is right, because a placed object has intrinsic content without any binding: its `ObjectDefinition` supplies graphic, collision role and default state. A decorative object is a real thing that renders and collides. **An object is a thing that may have a behavior; a trigger is a behavior that has a shape.** The two placement subjects are not symmetric, and the content model already says so correctly.

That also settles the candidates as posed:

| Candidate | Verdict |
|---|---|
| **Nullable `Binding`** — a trigger is a rect that may carry a behavior | **Rejected.** Not a weakening of a constraint but a redefinition of the type into something with no observable meaning. It also spends a behaviour-layer change (`AreaTriggerDefinition`, `LevelLoader.ResolveTriggers`, `ResolvedAreaTrigger`, `EditableLevelReader`, `EditableLevelSnapshot`, `BehaviorRuntime.RegisterTriggers`) to buy the ability to author inert content. |
| **`healOnEnter` default** | **Rejected**, for the reason John flagged: placing a "trigger" silently grants a heal zone nobody asked for, baked into every saved package until M4. |
| **Defer the trigger tool** | **Accepted**, with the milestone boundary moved rather than the model. |

### The cost accepted, stated plainly

**M2 loses half its announced scope, and working, reviewed code is removed from the M2 PR rather than merged.** #8463's acceptance bar (*"…same for a trigger rect"*) loses its trigger half, which moves to M4b. That is real waste and it is mine — the fork existed in this design, not in the implementation.

Two things make it the cheaper side of the trade:

1. **The work is not lost, it is re-homed.** The two-corner mode, `PlaceTriggerCommand`/`RemoveTriggerCommand`, and the session methods come back at M4b essentially as written, with a binding argument threaded through. What changes is *when*, and that they arrive with something to bind.
2. **It shortens the path to M5**, which is the milestone Toni actually wants (*"it is still not possible to actually edit a script in the level editor"*). M2 gets smaller; M4b sits **after** M4 and does **not** block M5. The critical path M2 → M4 → M5 is unchanged in shape and lighter at M2.

**What is explicitly not accepted as a cheaper variant:** keeping `LevelEditSession.PlaceTrigger` in M2 with a required `BehaviorBinding` parameter and simply not calling it from the UI. That leaves a public authoring member with no author-reachable call site — the exact thing the 2026-08-17 template addendum forbids designing, and what #8237's ruling deleted. Tests are not a consumer that makes a member reached; #8237's closing lesson is precisely that a green core suite says nothing about whether anything honours the contract.

### Concrete change list

**M2 (#8463) — remove the trigger *creation* path, keep everything else.**

| Site | M2 action |
|---|---|
| `src/Uberkarl.Editor/PlaceTriggerCommand.cs` | Remove from the M2 PR. Returns at M4b. |
| `src/Uberkarl.Editor/RemoveTriggerCommand.cs` | Remove from the M2 PR. Returns at M4b. |
| `LevelEditSession.PlaceTrigger` / `EraseTriggerAt` | Remove. **Both** — an erase-only tool lets an author destroy a hand-authored trigger they cannot recreate until M4b. That is a worse trap than not shipping the tool. |
| `EditableLevel.InsertTrigger` / `RemoveTriggerAt` / `FindTriggerIndexAt` | Remove (added by M2; no other consumer). The mutable backing list stays only insofar as the object path needs it. |
| `EditableLevel.Objects` mutators, `CaptureBehavior`, object commands | **Unchanged.** Object placement is M2's remaining scope and is complete. |
| `MenuOutcomeKind.SelectTriggerTool` + `MenuOutcome.SelectTriggerTool()` | Remove. `SelectObjectType` stays. |
| `LevelEditor.PaintMode` | Back to three cases: `Tile`, `Terrain`, `Object`. Drop `TriggerRect`. |
| `LevelEditor` two-corner state (`triggerCornerX/Y`, `CancelTriggerRect`, its commit branch, the `"Trigger Rect"` radial item) | Remove. |
| `EditorCanvas.SetOverlay` + `DrawTriggerOverlay` | **Keep, both halves.** `sample.pkg` already contains a hand-authored trigger; drawing it is honest and useful — *placing invisible things is not authoring* applies equally to *inspecting* them. The overlay is read-only and reached today. |
| `AreaTriggerDefinition`, `ResolvedAreaTrigger`, `LevelLoader.ResolveTriggers`, `EditableLevelReader`, `EditableLevelSnapshot`, `BehaviorRuntime.RegisterTriggers` | **Untouched.** That is the point of the decision: no behaviour-layer change lands inside an editor milestone. The `!` at `EditableLevelSnapshot.cs:93` stays correct because `Binding` stays required. |
| `tests/Uberkarl.Editor.Tests/ObjectAndTriggerPlacementTests.cs` | Trigger placement/erase/undo cases move to M4b with the code; object cases stay. Rename to match. |
| **M2 acceptance** | *An editor-placed **object** persists and runs in standalone `LevelPlay`; undo/redo work.* The trigger clause moves to M4b. Still an end-to-end bar crossing editor model → merge writers → package → play runtime — the seam that matters. Still verified red first, per the P2 carry-over. |

**M4b (new milestone) — trigger rect tool.** Two-corner mode + placement/removal commands + naming, where the commit step **requires** a binding chosen through M4's assignment surface. Acceptance: *place a trigger, deliberately choose `healOnEnter` from the picker, it persists and fires in standalone `LevelPlay`; undo/redo work.* Note this is the same default John chose — the difference is that the author chose it, which is the entire disagreement.

### Does M4 or M5 change shape?

**M4: no change to its own scope or acceptance, but it gains a successor.** M4 remains *predefined descriptors + assignment/param UX*, still after M1/M2 and still after M3 so menu items do not lie. M4b is a separate milestone and a separate PR (one feature, one PR) — the trigger tool is not folded into M4's diff, it consumes M4's output.

M4 does gain **one requirement it did not previously have to satisfy**: its assignment surface must be usable at *creation* time, not only to re-assign an existing subject's binding. This is not new UX — it is the constraint that the picker **returns** a `BehaviorBinding` to a caller rather than only mutating a selected subject in place. Named now so M4 is not designed as an in-place editor and then re-shaped at M4b.

**M5: no change of shape.** M5 binds a script resource through the same assignment seam M4 builds; nothing in it depended on triggers being placeable. Its acceptance — *one script, two placed objects bound to it, both run, package contains exactly one script resource* — is stated over objects and is unaffected. M4b may run before or after M5.

**Ordering after this addendum:** M1 done → M2 (objects only) → M3 (independent) → M4 → **M4b** → M5 → M6, with M4b and M5 independent of each other. M5 should go first: it is the milestone that answers the original ask.

### What this forces open, deliberately left closed

Named with size, not decided:

1. **M5: what is a newly-created script's initial source text?** *(Small — one decision, at M5 briefing.)* The same fork shape one milestone later: creation-time required content with no neutral value. Since PR #38 a file that does not evaluate to a handler map is quarantined **with a reason**, so an empty new script is loud rather than silent — which is why this is a UX choice (empty handler map vs. commented template vs. a working `onUpdate` stub) and not a repeat of this defect. Watch it at M5; do not pre-decide it here.
2. **Do triggers need author-facing naming at M4b?** *(Small.)* `Name` is currently write-only from the author's perspective — nothing reads it but the subject label. `OnScreenKeyboard` is already the naming path (§"Authoring UX"). Decide at M4b whether naming is part of the placement act or skipped until something reads names.
3. **#8055 (objectset editor) is load-bearing for M2's palette and still out of scope.** `LevelEditor.PopulateObjectPalette` adopts the palette from the level's *first existing placement's* object set, so **a level with no placements has an empty palette and object placement is unavailable**. M2 ships correctly under that constraint; it is a real ceiling on the milestone, not a defect. *(Medium — already filed as #8055. Not opened here.)*

**Not opened, and should not be:** nullable `Binding` (rejected above), a `noop` predefined (rejected by #8237 and by the 2026-08-17 template addendum — a member that exists and does nothing), and any re-planning of the phase beyond the single milestone boundary moved here.

---

## ADDENDUM 2026-08-21 — showing the assigned behavior on the canvas (task #8826)

Raised by Toni after #62–#64 merged: assigning a behavior produced no visible result — the picker closed and the level looked identical, so an author could not tell "assigned" from "silently did nothing" without saving and reading the package.

### What the label says

`BehaviorBindingLabel` (`src/Uberkarl.Editor/BehaviorBindingLabel.cs`) is the one place that turns a `BehaviorBinding` into an author-facing string. A predefined binding shows the same `PredefinedBehaviorDescriptor.Label` the assignment picker already listed it under ("Patrol", not `patrol`) — recognition, not translation. A script binding shows the slug `ScriptResourcePaths.DisplayLabel` derives from its `scripts/<slug>.poo` path — the exact same expression the assignment picker's "existing script" row uses (`BehaviorAssignmentPicker.cs`), not two independent copies of it — falling back to the raw path for a script outside that convention.

Two formatters exist for two different audiences: `Format` is cell-bounded (see below); `FormatFull` appends every parameter value and has no length bound at all, for the status line (see "Cursor status line" further down).

### REVISED 2026-08-21 — the first version of this design was rejected (QA #8834)

The original addendum below this line claimed `DrawString`'s `width` parameter was "an alignment box, not a clip" and that Godot "does not truncate a `DrawString` call the way a `Label`'s `TextOverrunBehavior` truncates a control." **Both claims were false**, and were never checked against the running engine before being written down. Verified against Godot 4.7.1 by QA: `TextLine`'s default `text_overrun_behavior` is `OVERRUN_TRIM_ELLIPSIS`, `draw_string` inherits it, and it visibly clips — `"Bump on Hit From Below"`, natural width 157px, drawn with `width: 44` (the actual default-zoom budget for a 16px tile), measures 41px on screen.

The practical consequence: `BehaviorBindingLabel.MaxLength = 24` bounds *characters*; the engine's own clip is a *pixel* budget that character count was never tracking. At default zoom the pixel clip fires first for every predefined label except the shortest ("Patrol" itself lands at `▸ Patr`), so `MaxLength` was never actually the constraint standing between an author and a readable label on the sample content — it was inert. A wider `MaxLength` cannot fix a pixel shortage; the label needs room a single cell does not have, at any character-count bound. That is a change of mechanism, not of number, which is why the shape below replaces the in-cell label with a marker rather than tuning the bound further.

`BehaviorBindingLabel.Bound` (used by `Format`) still exists and is still exercised by tests — it is a real ceiling on how long a label can grow before *anything* draws it (an author-typed script name has no ceiling of its own) — it is simply no longer the thing an author relies on to read what is assigned in a cell. That job now belongs to the marker and the cursor status line below.

### The marker: a drawn shape in the level, never a font glyph

`EditorCanvas.DrawBehaviorMarker` draws a small filled triangle in a subject's top-right corner, sized `min(12px, 60% of the smaller rect dimension)` — a fixed physical size so it reads at a glance at any of the six zoom levels, capped so it can never overflow a pathologically small cell. It replaces the `"▸ " + BehaviorBindingLabel.Format(...)` line every subject (object, trigger, tile override) used to draw underneath its name/outline. That line carried two defects at once: the character-vs-pixel clip above, and its `▸` prefix (`U+25B8`) has no glyph in Godot's default theme font and rendered as a tofu box that itself consumed roughly a third of the already-too-small width budget. A drawn polygon has neither problem — no font is involved, so there is nothing to clip and nothing that can fail to have a glyph.

The marker means *"this subject has behavior data attached"* — nothing more specific than that; it is a flag, not a summary. It draws for: an object whose `EffectiveBehavior is { }` (own override, else the type default); every trigger, unconditionally, since `AreaTriggerDefinition.Binding` is schema-required; and a tile cell with an entry in `EditableLevel.TileBehaviorOverrides` on the *active* layer — `LevelEditor.ActiveLayerTileBehaviorOverrides` still filters before the data reaches the canvas, unchanged from the original design. A `Removed = true` override draws the same marker a bound one does; the marker's job is only "look here", and *what* "here" means (an actual binding, or the type default explicitly silenced) is answered by the status line, not by two different marker shapes.

### Cursor status line: the untruncated, parameter-including answer to "what is it"

A marker answers *"does this subject have a behavior"* for every subject on screen, at any zoom, at a glance. It cannot also answer *"what is it"* — that answer needs more pixels than any cell has, by definition, since the marker's entire purpose is to fit inside one. So `LevelEditor.BuildStatusText` — the toolbar's existing always-recomputed session-state line — gains an `at cursor: <label>` segment: it resolves the grid cursor's cell with `EditableLevel.FindBehaviorSubjectAt` (the identical lookup `AssignBehaviorAtCursor` already uses to open the picker, so "what the status line names" and "what the picker would edit" are never two different answers) and renders it through `BehaviorBindingLabel.FormatFull` — unbounded, with every parameter value appended.

This also closes a gap the original report's acceptance criterion didn't explicitly call out but Toni's complaint was specifically about: the parameter menu. Re-assigning the same predefined with a different parameter value (`speed 24` → `speed 40`) does not change the label text `Format` returns, so nothing about it was ever going to be visible on the canvas regardless of clipping. `FormatFull` is the only formatter that renders parameters at all, so it is the only one that makes a parameter-only reassignment visible.

`LevelEditor._Process` polls the grid cursor's cell once per frame and recomputes the status line only when that cell actually changed, rather than depending on an event from every call site that can move the cursor (keyboard/gamepad step, mouse hover, mouse click each move it through a different path in `EditorCanvas`). This is a deliberate choice made *because of* CF-3 below, not despite it: an event-per-mutation-site design is exactly the shape that left the overlay itself stale on two call sites its author didn't think to wire.

`LevelEditor.TileOverrideStatusText` deliberately mirrors the marker's own scope (`ActiveLayerTileBehaviorOverrides` / `DrawTileBehaviorOverrideOverlay`), not the object case's fallback: a tile cell with no explicit `TileBehaviorOverride` entry reads `"none"`, the same way it draws no marker — it does not reach for the tile type's default the way an object's `EffectiveBehavior` falls back to its type default. Those are two different, deliberately unmerged facts: "this cell has an authored override" versus "this tile type has a default behavior", and the status line only ever claims to answer the first, matching what the overlay visibly commits to.

### Tile behavior overrides — the overlay that did not exist, refresh sites now audited

`EditorCanvas.DrawTileBehaviorOverrideOverlay` draws a per-cell outline (a third color, distinct from the existing object-orange and trigger-cyan) plus the marker, for exactly the entries `EditableLevel.TileBehaviorOverrides` on the *active* layer. Two call sites that change what "the active layer" or "the current overrides" mean were missing the corresponding `RefreshOverlay()` and were found stale by QA #8834 CF-3: `LevelEditor.OnLayerSelected` (switching the active layer via the Layers radial or `CycleLayer`) and `LevelEditor.OnLayerModelChanged` (layer add/delete/move/rename, which can shift `TileBehaviorOverride.Layer` indices out from under an already-rendered overlay). Both now call `RefreshOverlay()` before `UpdateState()`, matching every other mutation site (`AdoptSession`, `OnBehaviorAssigned`, object place/erase, `Undo`, `Redo`).

### The level script — unchanged: the status line, not the canvas

`LevelScript` is not cell-addressed (`BehaviorSubjectTarget.ForLevelScript` has no cell), so it still does not belong in `EditorCanvas`'s per-cell overlay. `LevelEditor.BuildStatusText` carries its own `level script: <label or "none">` segment via the bounded `Format` (a fixed toolbar label, not a hover readout, so bounding is fine here), refreshed by the same `UpdateState()` call `OnBehaviorAssigned` already makes — including after undo/redo, which route through the same call.

### Pure/impure split

`BehaviorBindingLabel` (`src/Uberkarl.Editor`) is pure — a `BehaviorBinding`/`TileBehaviorOverride` in, a `string` out (bounded via `Format`, full via `FormatFull`), no engine types, exercised directly by `BehaviorBindingLabelTests`. Drawing — `DrawObjectOverlay`/`DrawTriggerOverlay`/`DrawTileBehaviorOverrideOverlay`/`DrawBehaviorMarker` in `game/Editor/EditorCanvas.cs`, and the status-line assembly in `LevelEditor.BuildStatusText`/`CursorSubjectStatusText` — is glue: it decides *where* and *whether* to call the formatter, never *what the text says*. Every defect QA #8834 found lived in this impure half, which had (and, for the overlay's draw calls, still has) no test coverage — the split put the easy half under test and left the hard half unexercised; that imbalance is noted here, not solved by this pass.

## ADDENDUM 2026-08-21 (2) — naming the subject in the status line, not only its behavior (task #8826, PR #65 follow-up)

Toni tested `main` at #64 — without the addendum above — and independently confirmed the clipping QA had already measured: the pre-existing in-level name overlay truncates to about five characters (`jump-block-1` → `jump-`). That is expected and unchanged; the marker/status-line split above exists precisely because a cell has no room for a full name. But the addendum above closed only half the gap: `CursorSubjectStatusText` named the *behavior* and never the *subject*, so hovering `jump-block-1` still read `at cursor: Patrol (speed 40, range 48)` — a correct behavior, attributed to nothing. With the in-level name clipped and the status line silent on identity, the full name of the thing under the cursor appeared **nowhere on screen**.

### The rule this establishes

**The level shows you *where* things are; the status line names them in full.** A clipped in-level label is harmless by design as long as the status line's naming is never also bounded to fit a cell — the two are deliberately not held to the same width budget. This addendum is the naming half of that rule; the marker/status-line split above is the behavior half. Together they are the answer to "where does an unbounded fact get shown" for this feature: never on the canvas, always in the one-line status readout.

### What changed

`BehaviorSubjectLabel` (`src/Uberkarl.Editor/BehaviorSubjectLabel.cs`) is a new pure formatter, `(BehaviorSubjectKind, string) -> string`, joining `BehaviorBindingLabel` under the same tested, engine-free umbrella. `CursorSubjectStatusText` now reads `{BehaviorSubjectLabel.Format(target.Kind, SubjectDisplayName(target))} — {behaviorText}`, reusing the same `SubjectDisplayName` the picker already calls from `SummonBehaviorAssignment` — the subject the status line names and the subject the picker would open are structurally the same lookup, not two independent ones that could drift.

`BehaviorAssignmentPanel.OpenChoiceList` (the picker title, `"Assign Behavior — {…}"`) carried its own private `PickerTitleSuffix`/`SubjectLabel` pair doing the identical kind/name combination. Both call sites now go through `BehaviorSubjectLabel.Format` — one formatter, two callers, not two copies that could disagree on how an unnamed subject or a long name reads.

### Decisions

- **Unnamed subjects.** `string.IsNullOrEmpty(name)` renders the kind alone (`"Object"`, not `"Object ''"`) — the same guard the picker title already used, kept rather than reinvented.
- **Tiles.** `BehaviorSubjectLabel.Format` renders `BehaviorSubjectKind.Tile` as `"Tile"` regardless of the name argument — tiles are cell-addressed, not named, and `SubjectDisplayName` already returns `null` for them. No cell coordinates are appended: the cursor is already *on* the cell the status line describes, so `(x, y)` would repeat information the player's own cursor position already carries, for every status-line refresh, at zero added value.
- **Bounding.** The subject name is the newly-introduced unbounded input on this line — the same field (`Placement.Name` / trigger `Name`) that drove the picker-title overflow QA #8834 W-1 measured (~96 characters before the centered panel exceeded the viewport). `BehaviorSubjectLabel.MaxNameLength = 32` ellipsis-bounds it before it reaches the line; 32 characters was the safely-in-viewport tier in W-1's own measurement (595px against a 1152px viewport), reused here as a data point rather than a fresh guess. The behavior segment (`FormatFull`) stays deliberately unbounded, per the addendum above — that precedent does not change.

  **Measured against the real font, not asserted** (`ThemeDB.fallback_font.get_string_size`, run in the editor process, `project_info` confirms `viewport_width = 1152`): the whole status line at a realistic worst case (longest real predefined label with parameters, `"Bump on Hit From Below (speed 40, range 48, threshold 12)"`, plus the existing package/tileset/layer/tool/level-script segments) was **already 1382px — over the 1152px viewport — before this addendum**, from the behavior segment alone. This addendum's bounded name adds a further 372px (1754px total). So the overflow is pre-existing, not introduced here, but this change makes it worse and touches the one Godot control involved, which makes fixing it in the same diff the right call rather than a separate task.

  Unlike the picker's `PanelContainer` (`CenterInParent`, sized to its content — the literal W-1 mechanism), `LevelEditor`'s toolbar `bar` is pinned with `EditorLayout.PinTop` (`Control.LayoutPreset.TopWide`), a **fixed**, viewport-wide rect — so an over-wide `statusLabel` cannot grow the panel past the viewport the way the picker title did. What it can do, un-mitigated, is overflow its own row silently, with no visual signal that text is missing. `LevelEditor`'s `statusLabel` now sets `TextOverrunBehavior = TrimEllipsis`, the same property `ChoiceList.titleLabel` already carries (W-1's fix). Measured directly (`Label.get_minimum_size()`, same 1754px worst-case text): **without** the property the label's minimum width is 1754px; **with** it, 1px — the label stops demanding its full text width and instead ellipsis-trims to whatever the row actually gives it. This is not defense-in-depth on top of an already-safe line; given the pre-existing 1382px figure above, it is the thing that makes the line degrade to a visibly-truncated single line instead of silently overflowing.

## ADDENDUM 2026-08-21 (3) — the status line cannot be read at the moment it is needed; the answer moves onto the canvas (task #8826, PR #65 follow-up)

Toni ran `main`, not this branch — the working tree this editor session runs from is on `feat/editor-behavior-overlay`, so what he saw was PR #65 plus addendum (2). His report: *"i see nothing about which script is assigned and what parameters or anything about that."* The status line segment addenda (2) and the one above added was never visible to him.

**The cause is a real interaction defect in the toolbar's own reveal rule, not a missing feature.** `LevelEditor.UpdateReveals` (`game/Editor/LevelEditor.cs`) hides `topBar` — and with it `statusLabel` — by default, revealing it only while a focus zone rests on it, a child of it holds focus, or the mouse sits in the top-edge band (`mouse.Y <= TopBarHeight`). But the grid cursor **follows the pointer** (PR #60): moving the mouse up into that band to read the status line drags the cursor away from the subject the author was just looking at, and `at cursor:` then reports whatever is under the top edge instead of the subject. **The status line and the subject it is meant to describe cannot be on screen, correctly, at the same time**, for a mouse author — which is precisely the population addenda (1)/(2) were written for. This is a ruling error in this document, not a defect in what was implemented: cursor-relative information was placed on a surface that hides exactly when the cursor is where it needs to be.

### The correction: draw the label on the canvas, near the cursor, unclipped

The floating label (`EditorCanvas.DrawCursorSubjectLabel`, `CursorLabelAnchor.Resolve` in `src/Uberkarl.Editor/Input/CursorLabelAnchor.cs`) carries exactly what the status line's `at cursor:` segment carried — `{BehaviorSubjectLabel.Format(...)} — {BehaviorBindingLabel.FormatFull(...)}`, unbounded, via the same `LevelEditor.CursorSubjectLabelText()` the status line now also reads (one computation, two consumers, per the same DRY discipline addendum (2) established for `SubjectDisplayName`). It appears whenever that text is non-null — i.e. whenever the cursor sits on a subject with something to say — and tracks the cursor every frame the cell changes, the same cadence the status line already used.

**Not bound by the cell.** That was the whole defect the marker/status-line split (addendum, 2026-08-21 first entry) already diagnosed once for the in-level name (44px against labels needing 116–172px) — the floating label repeats the fix rather than the mistake: drawn on the canvas rather than inside a cell rect, its available width is `EditorCanvas.Size`, not one tile.

**Placement.** `CursorLabelAnchor.Resolve` centres the label horizontally on the cursor's cell and prefers to sit above it, offset by `CellGap = 6px`; when there is no room above (the cursor is in the level's top rows, or zoomed in enough that the cell's screen position is near the canvas's own top edge), it places the label below instead. Both axes are then clamped, independently, to keep the whole label rect inside the canvas control's own local rect (`0, 0, Size.X, Size.Y` — the same "viewport" `MenuAnchor.Clamp` already means when `LevelEditor.ResolveMenuCenter` calls it with `canvas.GlobalPosition`/`canvas.Size`, not the OS window), with a degenerate collapse-to-centre on an axis too small to hold the label at all — mirroring `MenuAnchor.Clamp`'s existing precedent for exactly this shape (#8663). **The label can never draw off-screen**: unlike #8805's picker panel, which grew past the viewport because "wide" was mistaken for "bounded", here the clamp is the last step on every path, not a hoped-for consequence of a width cap.

**Legibility over the level.** The label is backed by a solid, semi-opaque panel (`DrawRect` fill plus a 1px translucent border), not a text outline or drop shadow. A panel guarantees contrast against arbitrary tile art regardless of the art's own colour; an outline or shadow is a per-pixel contrast trick that can still lose against art that happens to share the text's hue or the shadow's. The existing overlay idiom in this file (the hover highlight, the marker triangle) is already "translucent filled shape", so the panel is the established idiom extended one step, not a new one.

**No delay, no suppression while painting.** The label recomputes on the same cursor-cell-changed cadence the status line already used (`LevelEditor.UpdateCursorSubjectStatus`, called from `_Process`) — it only ever shows when the cursor is actually on a subject with something to say, so it is silent, not noisy, while painting empty cells. Adding a debounce or a "suppress while a paint/erase is in progress" rule would be new behaviour with no reported problem motivating it; the existing hover highlight and grid cursor already redraw every cell the pointer crosses during a drag with no such throttle, and this label follows that precedent rather than inventing a second timing rule for one readout.

**The status line segment stays.** It costs nothing to keep and is the pad-and-keyboard reader's route when the toolbar *is* revealed (gamepad/keyboard reveal the toolbar by focus-zone, not by a pointer position that also drives the cursor) — it is simply no longer the primary surface for a mouse author.

### `UpdateReveals` pointer-source finding — reported, not fixed here

`UpdateReveals` reads `GetViewport().GetMousePosition()` — the one polled read of pointer position left in this editor; every other consumer (`EditorCanvas.PointerGlobalPosition`, `lastPointerLocal`) is event-driven, updated only from `_GuiInput` motion events, deliberately, because the polled position is unreliable under the test harness (#7445) and because the canvas's own pointer tracking is already event-driven. The two sources can disagree: while `topBar` is `Visible` (revealed) and the real pointer is over it, Godot routes motion events to the toolbar's controls rather than to `EditorCanvas` below it, so `lastPointerLocal`/`PointerGlobalPosition` freeze at wherever the pointer last was over the canvas for as long as it stays over the toolbar — while `UpdateReveals`'s own polled read keeps tracking correctly, since it queries the viewport directly rather than depending on canvas event delivery. For the reveal band itself this is benign: the polled read is the correct one for that specific check. The latent risk is on the other side — `LevelEditor.ResolveMenuCenter` calls `canvas.PointerGlobalPosition` to centre a pop-in menu, and that value would be silently stale if a radial were opened while the mouse sits over a currently-revealed toolbar. Today none of the toolbar's own buttons open a radial, so the window looks unreachable in practice, but it is the same root-cause class #7445 already named, now demonstrably reproducible whenever `topBar.Visible` is true rather than only under the headless harness. Not fixed here: closing it needs either the toolbar (and any other Control that can overlap the canvas) to forward motion events to the canvas, or `PointerGlobalPosition` to fall back to a polled read when no recent event exists — both are real changes, not a one-liner, and out of scope for a label-placement fix. Filed for a follow-up look, not fixed in this diff.
