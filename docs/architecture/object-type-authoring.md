# Design: Object Type Authoring

**Repo (canonical):** `docs/architecture/object-type-authoring.md` on `C:\dev\claude\uberkarl`, branch `feat/object-type-authoring`. The DiVoid documentation node mirrors it.

*Sarah (architect), 2026-08-27. Design-only — no implementation, no PR.*

Task **#8840** (canonical) · twin scope sketch **#8055** · sizing analysis **#9819** · project **#7396** · map root **#8056**.
Design precedent: **#8049** (§5.2 script lifecycle, §6.3 collision roles, §7.3 the active object set as session state) and **#8769** (the M5 design, whose shape this mirrors).
Load-bearing standards: **Design Contracts #1136** (§5 checklist walked as §16), the architect-template addendum **#1220** (*a design may only specify members the phase it belongs to will implement and reach*), **Code Contracts #114 §0**.

**Base: `fix/object-set-fresh-save-round-trip` @ `617aa52` (PR #68, in review) — not `main`.** Every source claim below was read on that tree; file:line citations are against it. **This matters: PR #68 shipped `ObjectSetMergeWriter` and `LevelSaveOrchestration`, which #9819 (read at `ad61f91`) lists as the feature's two hardest missing pieces (S1, S4). They are already built. The remaining gap is materially narrower than the sizing analysis states.**

---

## 1. Problem statement

> *"also i can place objects, but i can not create new objects or anything"*
> — Toni, 2026-08-21 (#8840)

Object **types** come from the package. The editor can place them, erase them, and assign per-instance behavior to them. It cannot **define** one. An author is limited to whatever object types the package already shipped with — for `sample.pkg`, `platform` and `jump-block`, written by a C# generator (`tools/SampleContent/Program.cs:68-93`) that no author will ever run.

Tiles have `TileSetEditor`, reachable from `Actions → More… → Edit Tileset…`. Objects have no equivalent.

**Success criterion, one sentence:** *from a level the author just created, define an object type by importing a PNG, place it, playtest and see the sprite with the right body, save, reopen, and find it intact.*

Note what that forces. It forces the whole chain — not just "a panel exists". #9819's surface **S6** is the reason: `LevelEditor.PopulateObjectPalette` (`game/Editor/LevelEditor.cs:1122`) returns early when the level holds zero placements, and the repo states the consequence itself at `docs/architecture/behavior-authoring.md:525` — *"a level with no placements has an empty palette and object placement is unavailable … a real ceiling on the milestone, not a defect."* A design that ships a panel without dissolving that ceiling ships a feature whose output cannot be used.

It also forces a second, unlisted link. `LevelEditor.PlaceActiveObject` (`LevelEditor.cs:1371-1385`) opens the current package and **returns silently when there is none** — which is exactly the state of a level created with `New` and not yet saved. So the natural first-run sequence (New → define a type → click) currently ends in nothing happening, with no message. See §6.2. This is the U1 failure shape the brief names: the code would be present and correct, and unreachable.

---

## 2. Scope and non-scope

### In scope

| # | Deliverable |
|---|---|
| 1 | `ObjectSetResourcePaths` — the in-package path convention for an object set and its sprites |
| 2 | `ObjectSetEditSession` — the mutable object set: add / rename / set collision role / replace graphic / remove, plus dirty tracking, attachment, and contributions |
| 3 | `ObjectSetEditor` — the summoned panel, reached from `Actions → More… → Edit Objects…` |
| 4 | Object set participates in save: `LevelSaveOrchestration` folds in the session's contributions instead of re-reading the active set from the package |
| 5 | The palette is sourced from the session, not from the first placement — dissolving the `behavior-authoring.md:525` ceiling |
| 6 | Placing a type on an unsaved level works (the null-package path) |
| 7 | Editing a type refreshes already-placed instances (`EditableLevel.RefreshObjectTypes`) |
| 8 | Removal is refused while the open level holds placements of that type |
| 9 | `FocusMemory` — the focus-restore-after-rebuild idiom, extracted once rather than copied a fourth time |

### Explicitly out of scope

- **Type-level default behavior authoring.** `ObjectDefinition.Behavior` and `ObjectDefinition.State` are read, round-tripped and preserved verbatim; they are never authored here. This is the same seam #8770 draws for tile types. See §14.1 — it is prose, not a member.
- **An object-set bind panel.** #8049 §7.3 ruled the active object set is *session* state, not a persisted `LevelDefinition` field, precisely to avoid a persisted field that only remembers a UI preference. Nothing here changes that. (#9819 S12.)
- **Multiple simultaneously-editable object sets.** A level may *reference* many (each placement names its own, and PR #68's `BuildContributionsForLevel` already carries all of them forward). Exactly one is *editable* per session. See §9.1.
- **Drawing object sprites on the editor canvas.** `EditorCanvas.DrawObjectOverlay` (`game/Editor/EditorCanvas.cs:549`) stays schematic. See §9.5.
- **Undo/redo for object-type edits.** Tile-set parity: `TileSetEditSession` has no history, and destructive rows are gated behind a two-press confirm instead. Same here. (The *placement* commands remain undoable — they already are.)
- **Cross-package object graphics.** `EditableObjectSetReader.FromPackage` already refuses them with a typed `LevelContentException`; unchanged.
- **Migrating the three existing copies of the focus-restore idiom.** See §11.3 — the math is stated, the migration is filed, and this change adds no fourth copy.

---

## 3. Assumptions and constraints

### What the baseline already provides — do not rebuild any of it

| Fact | Evidence |
|---|---|
| The package format needs **nothing**. `ObjectSetDefinition`/`ObjectDefinition` have no hand-written converter (unlike `TileDefinition`), so a brand-new type round-trips identically to a shipped one | `LevelContentSerializer.WriteObjectSet`/`ReadObjectSet`; pinned by `ObjectSchemaTests.JsonRoundTrip_PreservesObjectSetAndPlacement` |
| `ResourceKind` is an opaque string below the content layer; the archive layer needs nothing | `src/Uberkarl.Packages/ResourceKind.cs`, `PackageMergeWriter.Compose` |
| **The object-set merge writer exists and has a production caller** | `src/Uberkarl.Editor/ObjectSetMergeWriter.cs`, called from `LevelSaveOrchestration.BuildExtraContributions:28` |
| **Save orchestration already folds in a third participant** | `src/Uberkarl.Editor/LevelSaveOrchestration.cs`, called from `LevelEditor.SaveLevelAndTileSet:1531` |
| Path-collision safety at the assembly boundary is enforced and typed | `ObjectSetRoundTripTests` lines 229, 247, 264, 277 |
| The graphic-import path is written and object-agnostic | `TileSetEditor.BuildImportDialog:151`, `OnGraphicFileSelected:744`, `TileGraphicImport` |
| Placement, erase, undo/redo, the fourth paint mode, the Tiles-menu object segment, per-instance behavior assignment, and the runtime body all exist | `LevelEditSession.PlaceObject:105`, `PaintModeRouting`, `MenuCatalog.BuildTilesMenu`, `ObjectBodyBuilder` |
| #8055's named blocking dependency (merge writers dropping `ObjectDefinition.Behavior`) is **retired** | `ObjectSetMergeWriter` serializes `Definition` verbatim |

### Constraints

1. **The harness cannot type.** `addons/godot_mcp/mcp_input_service.gd` never sets `event.unicode` (verified: no occurrence of `unicode` in the file), so no synthetic keystroke produces a character. This shapes the design, not just the acceptance — see §9.2 and §13.
2. **#8247's rule:** a placed object keeps its authored placement and its type-derived render data *side by side*, never merged. A refresh rebuilds the cache; it must never write type defaults onto `ObjectPlacement`, which would promote one shared default into N per-instance copies on save.
3. **A rename must never move a VFS entry** (design #7572 open question 3). An already-attached object set keeps its path.
4. **`ObjectDefinition.Graphic` is a non-nullable `ResourceReference`.** A default one serializes through `ResourcePath.Value` and throws `InvalidOperationException` from the writer. A graphic-less type cannot be written at all. Hence: **one milestone, not two** — the graphic is not a second phase, it is a precondition of the first. (#9819 §3; the split #8840 proposed does not survive the code.)
5. **A level binds exactly one tile set but may reference many object sets.** `BindTileSet`/`RefreshTiles` have no general object counterpart and must not grow one. §7.3 already ruled this. The one narrow exception this design does add is a *save-time path fixup*, not a UI binding — see §6.3 and §11.2, where it is argued rather than assumed.

---

## 4. Architectural overview

```
        Actions ─▶ More… ─▶ "Edit Objects…"                         game/Editor/
                                │                                  ┌──────────────────┐
                                ▼                                  │  ObjectSetEditor │  panel
                     LevelEditor.SummonObjectSetEditor             │  (summoned)      │
                                │                                  └────────┬─────────┘
                                ▼                                           │ drives directly,
   ┌────────────────────────────────────────────────┐                       │ then rebuilds rows
   │ LevelEditor                                    │                       ▼
   │  objectSetSession ── always non-null once a    │◀── ObjectSetModelChanged
   │                      level is adopted          │        │
   │  objectTypeLabels ── derived, for the menu     │        │  ┌───────────────────────────┐
   └───────┬──────────────────────────┬─────────────┘        └─▶│  ObjectSetEditSession     │
           │ palette / place           │ save                    │  Name, ObjectSetPath      │
           ▼                           ▼                         │  IsAttached, IsDirty      │
   LevelEditSession.PlaceObject   LevelSaveOrchestration          │  Types  (mutable list)    │
           │                           │  .BuildExtraContributions│  AddType/RenameType/…     │
           ▼                           │                          │  EnsureAttached           │
   EditableLevel.Objects  ◀────────────┤                          │  BuildContributions       │
   (placement + cached render data)    │                          └────────────┬──────────────┘
           ▲                           │                                       │
           │ RefreshObjectTypes        │                                       ▼
           └───────────────────────────┘                        ObjectSetMergeWriter   (exists)
                                       │                        ObjectSetResourcePaths (new)
                                       ▼
                              PackageMergeWriter  (exists, untouched)
```

**The shape in one sentence:** one new mutable session type, one new path-convention type, one new panel, and five small edits to existing seams — sitting on top of a write path that PR #68 already finished.

---

## 5. Components and responsibilities

### 5.1 `ObjectSetResourcePaths` (new — `src/Uberkarl.Editor/`)

**Owns:** the in-package path convention for a standalone object set and its sprites.
**Does not own:** slugification (delegated), collision-uniquification against a package (delegated), any IO.

| Member | Semantics |
|---|---|
| `Slugify(name)` | Delegates to `LevelResourcePaths.Slugify`. The rule is generic, not level-specific — exactly as `TileSetResourcePaths.Slugify:18` does. |
| `UniqueSlug(baseSlug, isTaken)` | Delegates to `LevelResourcePaths.UniqueSlug`. |
| `ObjectSetPath(slug)` | `objectsets/<slug>.json` |
| `GraphicPath(slug, objectId)` | `objects/<slug>/<objectId>.png` — **`objectId` is a `string`**, which is the one genuine non-transfer from `TileSetResourcePaths.GraphicPath(slug, int tileId)` (#9819 S3). |
| `SlugFromObjectSetPath(path)` | Inverse of `ObjectSetPath`; `null` when the path does not follow the convention (e.g. the sample's `objectsets/demo.json` — which *does* follow it, slug `demo`). |

This mirrors `TileSetResourcePaths` shape-for-shape minus `FramePath` (objects have no animation frames). It is a new type rather than four more members on `TileSetResourcePaths` because that type's doc-comment and every member address a *tile set's* namespace; a second namespace on it would be a parallel-concern smell. The delegation to `LevelResourcePaths` for the two generic rules is what keeps it DRY.

### 5.2 `ObjectSetEditSession` (new — `src/Uberkarl.Editor/`)

**Owns:** the object set under edit — its identity, its path, its list of `EditableObjectType`, id minting, dirty tracking, one-time attachment, and its resource contributions.
**Does not own:** the level, any placement, any package IO beyond the read done by its factory, undo history, or the removal *policy* (which needs the level — see §5.3).

**This is one class, not the two the tile side uses, and that is a deliberate simplification.** `EditableTileSet` and `TileSetEditSession` are split because `EditableTileSet` is a pure model consumed by `EditableLevel.BindTileSet`/`RefreshTiles` and `EditableTileSetReader`, while the session adds only `IsDirty` and attach policy. On the object side there is no bind, and the existing consumers already speak in `(ResourcePath, IReadOnlyList<EditableObjectType>)` — `ObjectSetMergeWriter.BuildContributions(objectSetPath, objectTypes)` and `EditableObjectSetReader.FromPackage(package, reference) → IReadOnlyList<EditableObjectType>`. A separate `EditableObjectSet` would be a wrapper with one consumer and no distinct lifecycle: it fails #1136 §4's can-it-be-merged check. Merged.

| Member | Semantics |
|---|---|
| `Name` (get) | Display name; the base for the slug. Not persisted anywhere — `ObjectSetDefinition` has no name field (#8170). Used only to derive a path and to title the panel. |
| `ObjectSetPath` (get) | The in-package path. Provisional until `EnsureAttached`. |
| `Reference` (get) | `ResourceReference.ToSelf(ObjectSetPath)` — what a placement records. |
| `IsAttached` (get) | False for a `CreateBlank` set; true for one read from a package or one that has completed `EnsureAttached`. |
| `IsDirty` (get) | True when there are unsaved edits. Set by every successful mutator; cleared by `MarkSaved`; re-set by `MarkDirty`. |
| `Types` (get) | `IReadOnlyList<EditableObjectType>`, in declaration order. |
| `CreateBlank(name)` (static factory) | An empty, unattached set at `ObjectSetPath(Slugify(name))`. |
| `FromPackage(package, reference)` (static factory) | Reads via the existing `EditableObjectSetReader.FromPackage`; `IsAttached` is true and `ObjectSetPath` is `reference.Path` verbatim. Throws the reader's existing typed exceptions unchanged. |
| `AddType(graphic, collisionRole)` → `string` | Mints an id, mints a graphic path, appends a type, marks dirty, returns the new id. **Takes no name** — see §9.2. |
| `RenameType(id, name)` → `bool` | Changes `Name` only. Never touches `Id`. Blank normalises to `null` (mirrors `EditableTileSet.RenameTile`). `false` when the type does not exist or nothing changed. |
| `SetCollisionRole(id, role)` → `bool` | `false` when the type does not exist or the role is unchanged. |
| `ReplaceGraphic(id, graphic)` → `bool` | Replaces the bytes at the type's existing graphic path. `false` when the type does not exist. Rejects empty bytes. |
| `RemoveType(id)` → `bool` | Removes unconditionally. `false` when the type does not exist. **The placement guard is not here** — see §5.3. |
| `EnsureAttached(existingResources)` | No-op when `IsAttached`. Otherwise derives `Slugify(Name)`, uniquifies it against `existingResources` on `ObjectSetPath(candidate)`, sets `ObjectSetPath`, remaps **every** type's graphic path to the new slug, and sets `IsAttached`. |
| `BuildContributions()` | `ObjectSetMergeWriter.BuildContributions(ObjectSetPath, Types)` — the existing writer, unchanged. Returns empty when `Types` is empty (see §9.1). |
| `MarkSaved()` / `MarkDirty()` | Mirrors `TileSetEditSession:249/252`. |

**Attachment is one public method, not three.** `TileSetEditSession` exposes `Attach` + `AttachAsNewResource` + `EnsureAttached` + `AttachToExistingResource`; the last exists only for the tile-set *bind* flow, which objects do not have, and the first two have exactly one caller each. Collapsed to `EnsureAttached` alone. This is the can-it-be-inlined check from #1136 §4 applied literally.

**Id minting.** `AddType` derives the id as `UniqueSlug("object", candidate => Types.Any(t => t.Definition.Id == candidate))` — yielding `object`, `object-2`, `object-3`, … See §9.2 for why a slug and not an int counter, and for the reuse-after-removal consequence.

**Graphic path minting.** `AddType` mints `GraphicPath(CurrentSlug, id)` where `CurrentSlug` is a private accessor returning `SlugFromObjectSetPath(ObjectSetPath) ?? Slugify(Name)`. For an already-attached set this yields the set's established slug, so a newly added type's sprite lands in the same namespace as its siblings and **no existing path ever moves**. For an unattached set it yields a provisional slug, remapped by `EnsureAttached`.

**Mutating an immutable definition.** `EditableObjectType` is a getter-only pair and `ObjectDefinition` is `init`-only, so `RenameType`, `SetCollisionRole` and `EnsureAttached`'s remap each replace the list element with a rebuilt definition. That rebuild copies six properties (`Id`, `Name`, `Graphic`, `CollisionRole`, `Behavior`, `State`). **DRY math: ~8 lines × 3 sites = 24, above the ~15-20 threshold (#1267) → extract.** Specify one private helper on the session that takes the source definition plus the three independently-settable values (`Name`, `CollisionRole`, `Graphic`) and carries `Id`, `Behavior` and `State` verbatim; each call site passes the source's current value for the two fields it is not changing. Carrying `Behavior` and `State` inside the single helper is also what structurally prevents the #8247 field-rot fault — there is one place that can forget them, and it is one line long.

### 5.3 `EditableLevel` — three new members (`src/Uberkarl.Editor/EditableLevel.cs`)

**Owns (unchanged):** the authored level, including `Objects` as `ObjectPlacement` + cached render data side by side.

| Member | Semantics |
|---|---|
| `CountPlacementsOfType(objectSet, objectId)` → `int` | How many placements in this level name that type. Pure, engine-free, unit-testable. The *data* behind the removal guard; the *policy* lives in the panel (§5.4). |
| `RefreshObjectTypes(objectSet, types)` | For each placement whose `Placement.ObjectSet` equals `objectSet`, find the type by `ObjectId` and rebuild that entry's cached `CollisionRole`, `Graphic`, `State` and `EffectiveBehavior` from it. **`Placement` is carried verbatim** — the authored truth is never rewritten (#8247). A placement whose type is not found is left untouched. Placements of other object sets are untouched. |
| `RebindObjectSet(from, to)` | For each placement whose `Placement.ObjectSet` equals `from`, rebuild it with `ObjectSet = to`, every other placement field and every cached field verbatim. Called only from save orchestration when attachment moved the set's path (§6.3). |

`EffectiveBehavior` recomputes as `placement.Behavior ?? type.Definition.Behavior`, with no package access. That is sound *in this milestone* because a newly created type always has `Behavior = null` (type-level behavior is out of scope, §14.1) and a loaded type's binding was already captured into the level's script table at load. State that assumption in the doc-comment so the day §14.1 lands, the refresh is revisited deliberately.

`RefreshObjectTypes` and `RebindObjectSet` each rebuild an `EditableObjectPlacement` via its 5-argument constructor — a single statement, matching the one already at `SetObjectBehavior:335`. **DRY math: the repeated unit is one constructor call, not a >5-line block, so #1267's threshold does not bind** (its own carve-out excludes trivial single-statement sequences). The `ObjectPlacement` object-initializer inside `RebindObjectSet` is a ~7-line block, and with `SetObjectBehavior:336-343` that is **7 × 2 = 14, at 2 sites — below the threshold on both axes (>5 lines *and* >2 sites are both required)**. Inline both; do not extract.

### 5.4 `ObjectSetEditor` (new — `game/Editor/`)

**Owns:** the summoned surface. Rows, focus containment, the import dialog, the rename prompt, the pending-confirm arming, and the removal *policy*.
**Does not own:** the model (it drives `ObjectSetEditSession` directly and then rebuilds its rows from the model's current truth — the pattern stated at `TileSetEditor.cs:15-16`), the level (read-only, for placement counts), or save.

**Shell** — mirrors `TileSetEditor.cs:113-144` exactly: `partial class ObjectSetEditor : Control` in namespace `Uberkarl`; `_Ready` does `EditorLayout.FillParent`, `MouseFilter = Stop`, `FocusMode = All`, `Visible = false`, **`ZIndex = 100`** (the shared `OnScreenKeyboard` sits at 150 and must stack above it), then builds layout and import dialog. Dim backdrop `ColorRect` of `Color(0.05f, 0.06f, 0.08f, 0.75f)`, centered `PanelContainer`, title `Label` reading **"Edit Objects"**, `ScrollContainer` holding the row `VBoxContainer`.

**One deliberate deviation from `TileSetEditor`:** set `FollowFocus = true` on the `ScrollContainer` and call `EnsureControlVisible` on row focus, as `ChoiceList.cs:76` and `:158-159` do. `TileSetEditor` omits this and consequently does not auto-scroll to a focused row far down the list. Do not reproduce that.

**Events exposed to `LevelEditor`** — the payload-free editor-panel shape, not the payload-carrying picker shape:

| Member | Semantics |
|---|---|
| `ObjectSetModelChanged` (`Action`) | "I have already mutated the session; re-read the model's current truth." Raised after every successful mutation. Carries no payload. |
| `Closed` (`Action`) | Raised only from the close path. |
| `IsOpen` (`bool`, get) | `Visible`. **Must be added to `LevelEditor.AnyModalOpen()` (`LevelEditor.cs:318-323`) or global hotkeys fire through the open panel.** |
| `AttachKeyboard(OnScreenKeyboard)` | Wired once at construction with the editor's single shared instance. |
| `Summon(session, level, tileSize)` | Resets focus memory and every pending-confirm field, shows, rebuilds. `level` is read-only, used only for `CountPlacementsOfType`. |

**Rows.** One header label, then a `+ Add Object (import PNG)…` button, then one row per type. Each type row, left to right:

| Column | Focusable | Content |
|---|---|---|
| thumbnail | no | `TextureRect` from the type's PNG bytes, via the `LoadTexture` idiom at `TileSetEditor.cs:714-719` |
| name | yes | `Object #<id>` when `Name` is null/empty, else `<Name> (<id>)`. Pressing it opens the rename prompt. |
| collision role | yes | `Solid` / `Passthrough`. Pressing it toggles. `ObjectCollisionRole` has exactly two members, so this is a toggle, not the 8-entry cycle table `TileSetEditor` needs for collision shapes. |
| graphic | yes | `Replace PNG…` — opens the same import dialog in replace mode. |
| remove | yes | `Remove` → `Confirm Remove?` (two-press), **or `In use ×N`** when `CountPlacementsOfType` returns N > 0. |

Row handlers close over the **string id, never the `EditableObjectType` object** — the rule at `TileSetEditor.cs:281` that makes handlers safe against the list being rebuilt underneath them.

**Removal policy (Q3).** The count is read at row-build time. When it is non-zero the button reads `In use ×N` and pressing it arms nothing, changes nothing, and logs. When it is zero the standard two-press arm-then-fire applies. The author sees the constraint *before* pressing, so there is no error state to design. Reset of the armed state goes through a single `ClearPendingConfirms` called at the top of every non-destructive handler — the tidier of the two conventions `TileSetEditor` mixes (`:705-712` vs. the hand-clearing at `:722`, `:821`, `:841`). Use only the tidy one.

**Import.** Reuse `TileSetEditor.BuildImportDialog:151-161` verbatim in shape: a `Godot.FileDialog`, `FileModeEnum.OpenFile`, `AccessEnum.Filesystem`, `*.png` filter, parented to the panel `Control` (so `importDialog.Visible` is a reliable local guard), summoned with `PopupCentered()`. Gamepad navigability comes free from the project's global `ui_accept`/`ui_cancel` bindings; no extra code. The selection handler reuses `OnGraphicFileSelected`'s sequence: read bytes → `Image.LoadPngFromBuffer` probe → `TileGraphicImport.NeedsResize(w, h, tileSize)` → `Image.Resize(tileSize, tileSize, Lanczos)` + `SavePngToBuffer`. **It has no mode branch beyond add-vs-replace**, because objects have no animation frames — the `pendingFrameTileId` fork is the only reason `TileSetEditor`'s version is forked at all.

**Graphics are squared to the level's `TileSize`,** exactly as tile imports are. This is not laziness: `ObjectDefinition.Graphic`'s own doc says "tile-sized", `ObjectBodyBuilder.CollisionSize(tileSize)` is square, and `TileGraphicImport.NeedsResize` is square-only. Preserving a source aspect ratio would produce a sprite that does not match its own collision body. Cost, stated: a non-square source PNG is squashed rather than letterboxed. Accepted.

**Input.** Both `_GuiInput` and `_UnhandledInput` handle `ui_cancel`, each guarded on the keyboard and the import dialog not being open — the two-path arrangement `TileSetEditor.cs:903-922` documents (a focused `Button` swallows the `_GuiInput` route, so the unhandled route is the one a gamepad B actually takes; the sub-modal guards stop a B meant for the rename keyboard from closing the panel underneath it).

### 5.5 `FocusMemory` (new — `game/Editor/`)

**Owns:** the record-focus-position / restore-clamped-after-rebuild idiom.

The idiom is ~17 lines (`TrackFocusPosition` plus the clamp-and-restore triple) and already exists verbatim at **three** sites: `TileSetEditor.cs:242-260`, `OnScreenKeyboard.cs:140-161`, and `LayerManagerPanel`. A fourth copy in `ObjectSetEditor` would make it **17 × 4 = 68 lines of duplication, far above #1267's ~15-20 threshold**, and per #114 §0's bounce rule a design that authorised it would oblige John to bounce.

So: extract it, and **use it in the new panel only**. A small class holding the last-focused row and column, with a `Track(rows)` that subscribes the recorder and a `Restore(rows)` that clamps twice and issues the deferred `GrabFocus`. Two calls replace two fields and seventeen lines.

**The three existing sites are not migrated in this change.** They are pre-existing duplication, the migration is mechanical but touches focus code in three working panels — and #8654 records that runtime-dependent focus containment is historically the expensive part of panel work on this repo, while the change is not covered by engine-free tests. Per the one-feature-one-PR rule, a pre-existing fix-up is its own unit. **File it as a follow-up task.** This change's own DRY obligation is discharged: it adds zero copies.

### 5.6 `MenuCatalog` / `MenuOutcome` — one row

`BuildActionsOverflowMenu` (`src/Uberkarl.Editor/Input/MenuCatalog.cs:71-84`) gains **`"Edit Objects…"` immediately after `"Bind Tileset…"`**, keeping the tile-set rows adjacent and placing the object row before the script rows. Eight entries total; the overflow renders on the *list* surface, so `MenuCatalog.RadialCap` does not bind. A new `MenuOutcomeKind` member and its factory, and a `case` in `LevelEditor.Dispatch` near `:434`.

`MenuCatalogTests.BuildActionsOverflowMenu_LabelsAndOutcomes_MatchThePinnedMapping_InOrder` (`tests/Uberkarl.Editor.Tests/MenuCatalogTests.cs:228`) pins the exact ordered mapping and **must be extended in the same change** (map #8252 job C step 5).

---

## 6. Interactions and data flow

### 6.1 Defining a type

1. Author opens `Actions` → `More…` → `Edit Objects…`. `LevelEditor.Dispatch` → `SummonObjectSetEditor()`, which cancels any pending trigger placement (every panel summon does — `LevelEditor.cs:621/628/641`) and calls `Summon(objectSetSession, session.Level, session.Level.TileSize)`. **There is no unavailable state to handle** — `objectSetSession` is non-null whenever a level is adopted (§9.1), so unlike `TileSetBindPanel.SummonUnavailable` the panel never has to open and explain.
2. Author activates `+ Add Object (import PNG)…`; the `FileDialog` pops centered.
3. On selection: bytes read, PNG-probed, squared to `TileSize` if needed.
4. Panel calls `session.AddType(bytes, ObjectCollisionRole.Solid)`. The session mints the id, mints the graphic path, appends, marks dirty.
5. Panel raises `ObjectSetModelChanged`, then rebuilds its rows.
6. `LevelEditor.OnObjectSetModelChanged` rebuilds `objectTypeLabels` from `objectSetSession.Types`, calls `session.Level.RefreshObjectTypes(objectSetSession.Reference, objectSetSession.Types)`, re-snapshots the canvas, and refreshes the status line — the same four-step shape as `OnTileSetModelChanged` (`LevelEditor.cs:984-991`).
7. Author closes the panel (`ui_cancel`); `Closed` → `canvas.GrabFocus()`.

The type now appears in the third segment of the Tiles menu (`MenuCatalog.BuildTilesMenu`, already wired) and is selectable, because that segment is fed from `objectTypeLabels`.

### 6.2 Placing it — the two edits that make the feature reachable

`PopulateObjectPalette` (`LevelEditor.cs:1122-1148`) is **rewritten to source the palette from the session**, not from `level.Objects[0]`:

- The `level.Objects.Count == 0` early return is **deleted**. This is the `behavior-authoring.md:525` ceiling; sourcing from the session dissolves it by construction rather than by adding a branch.
- `activeObjectSetReference` is **deleted as a field**; its value is `objectSetSession.Reference`.
- `objectTypes` is **deleted as a field**; its value is `objectSetSession.Types`.
- What remains is: rebuild `objectTypeLabels` from `objectSetSession.Types`, and set `activeObjectType` to the first type when there is one.

`PlaceActiveObject` (`LevelEditor.cs:1371-1385`) **stops bailing on a null package**:

- Delete the `if (package == null) return;` at `:1376-1377`. `using` on a null `Package` is legal.
- `LevelEditSession.PlaceObject`'s first parameter becomes `Package?`, and its unconditional `ArgumentNullException` guard (`:107-108`) becomes conditional: throw only when `objectType.Definition.Behavior` is non-null and `package` is null. This is the same "a null package throws only when it would actually be needed" convention PR #68 established at `ObjectSetMergeWriter.BuildContributionsForLevel:37-38`.
- Inside, the effective-behavior line becomes: when `Definition.Behavior` is null, the effective behavior is null and `CaptureBehavior` is not called at all; otherwise it is called with the (now known non-null) package. `EditableBehaviorBindings.Capture` and `EditableLevel.CaptureBehavior` keep their non-nullable signatures and are not touched. **No unreachable guard is added** — the "script binding with no package" case cannot occur this milestone, because a type created here always has `Behavior = null` and a type loaded from a package always has a package open.

Without both edits, the acceptance sentence fails on its first clause: *from a level the author just created*.

### 6.3 Saving

`LevelSaveOrchestration.BuildExtraContributions` gains an `ObjectSetEditSession?` parameter and becomes, in order:

1. Tile set: `EnsureAttached` → `BindTileSet` → contributions. Unchanged.
2. Object set, when the session holds **at least one type**:
   a. capture the session's `Reference` as `before`;
   b. `EnsureAttached(existingResources)`;
   c. read `Reference` as `after`;
   d. when `after != before`, call `level.RebindObjectSet(before, after)`;
   e. contributions ← `session.BuildContributions()`;
   f. the reference to skip in step 3 ← `after`.
   When the session holds **zero** types: no attach, no contributions, nothing to skip.
3. Every *other* object set the level's placements reference, via `ObjectSetMergeWriter.BuildContributionsForLevel`.
4. Concatenate.

**Why step 2d exists.** Each `ObjectPlacement` bakes its own `ObjectSet` reference at placement time (`LevelEditSession.PlaceObject:117`) — that is the persisted truth per #8049 §7.3, and this design does not change it. On an unsaved level the placements record the session's *provisional* path. If `EnsureAttached` has to uniquify (the target package already holds `objectsets/<baseSlug>.json` — reachable by saving two new levels into one package), the path moves and every placement is left dangling. A dangling `ObjectId` or set path makes the level **unopenable** (`EditableLevelReader.ResolveObjects:150` throws `LevelContentException`), so this is correctness, not polish. `RebindObjectSet` is ~12 lines and is idempotent-by-guard.

This is the narrow exception to §3's constraint 5. #9819 is right that `BindTileSet`/`RefreshTiles` have no object counterpart — those are *UI binding and palette caching* for a level's single tile set, and none of that transfers. `RebindObjectSet` is a save-time path fixup over per-placement references, invoked only when attachment moved a path. It is not a binding surface and grows no UI.

**Why step 3 must skip.** `PackageBuilder` throws a typed `LevelContentException` on differing bytes at the same path (pinned by `ObjectSetRoundTripTests:264`), so emitting both the session's authored set *and* a re-read of the same set from disk is a hard save failure the moment they differ — which is exactly after any edit. **`BuildContributionsForLevel` gains a nullable "already authored" reference parameter and seeds its existing `seen` set with it** — one changed line at `ObjectSetMergeWriter.cs:43`.

**One more change inside `BuildContributionsForLevel`.** Its null-package guard (`:37-38`) currently throws whenever `level.Objects.Count > 0`. On a brand-new unsaved level with placements of the authored type there is no package, and every placement belongs to the authored set — so the throw fires and the very first Save As fails. **Move the guard inside the loop:** throw only when a placement names an object set that is not already in `seen` *and* the package is null. Placements of the authored set are pre-seeded and never reach it. Its two pinned tests (`:201`, `:213`) keep passing; add a third for the authored-set case.

The attach-once rule is unchanged: `EnsureAttached` no-ops for an already-attached set, so a re-save never relocates an object set. `MarkSaved` is called alongside `tileSetSession?.MarkSaved()` in `LevelEditor.SaveLevelAndTileSet:1533`, and `MarkDirty` on the failure paths that already call `session.MarkDirty()`.

### 6.4 Loading

`LevelEditor.LoadFromBytes` builds the object set session alongside the tile set session, using **the first placement's object set when the level has placements, and a blank set otherwise** — see §9.1. `AdoptSession` then calls the rewritten `PopulateObjectPalette`.

---

## 7. Data model (conceptual)

Nothing in `Uberkarl.Content` changes. Stated for the implementer's benefit:

| Entity | Owner | Identity | Notes |
|---|---|---|---|
| `ObjectSetDefinition` | the package, as the `objectset` resource kind | its resource path — it carries **no** id, name or version of its own (#8170) | duplicate object ids are never rejected; a duplicate silently resolves to whichever was declared first, so **the editor must enforce uniqueness itself** |
| `ObjectDefinition` | an `ObjectSetDefinition` | `Id` (string), unique within its set | `Graphic` is a non-nullable `ResourceReference`; `CollisionRole` defaults to `Solid`; `Behavior` and `State` are read/round-tripped but not authored here |
| `ObjectPlacement` | the level | positional — no id | records its own `ObjectSet` reference and `ObjectId`. This is the persisted truth; the active set is *not* a `LevelDefinition` field (#8049 §7.3) |
| `EditableObjectType` | `ObjectSetEditSession` | its `Definition.Id` | immutable pair of definition + graphic bytes; mutation replaces the element |
| `EditableObjectPlacement` | `EditableLevel` | list index | authored placement **plus** cached type-derived render data, side by side, never merged (#8247) |

---

## 8. Contracts and invariants

| # | Invariant | Enforced where |
|---|---|---|
| I1 | Every object id is unique within its set | `AddType` uniquifies against the current ids |
| I2 | An object id is never changed by a rename | `RenameType` writes `Name` only |
| I3 | An already-attached object set never moves | `EnsureAttached` no-ops when `IsAttached` |
| I4 | A newly added type's sprite lands in its set's own slug namespace | `AddType` mints from `CurrentSlug` |
| I5 | After a save, every placement's `ObjectSet` reference resolves in the written package | §6.3 steps 2d and 3, backstopped by the typed `PackageBuilder` collision check |
| I6 | No type with placements in the open level is ever removed | the panel's row policy (§5.4), over `CountPlacementsOfType` |
| I7 | A refresh rebuilds cached render data and never writes onto `ObjectPlacement` | `RefreshObjectTypes` carries `Placement` verbatim |
| I8 | An object set with zero types contributes no resource | `BuildContributions` returns empty; orchestration skips attach |
| I9 | `Behavior` and `State` survive every edit unread | the single definition-rebuild helper carries them verbatim |
| I10 | The panel owns input while open | `IsOpen` added to `AnyModalOpen()` |

---

## 9. The open questions, settled

### 9.1 Q1 — Where does a new level's object set come from?

**Decision: mint a blank set in `NewLevel`, and make an empty set contribute nothing on save.**

`NewLevel` (`LevelEditor.cs:1059`) already mints an unattached `EditableTileSet.CreateBlank("Untitled Tiles", …)` next to the level. It mints `ObjectSetEditSession.CreateBlank("Untitled Objects")` the same way. `LoadFromBytes` builds the session from the level's first placement's object set, or `CreateBlank` when there are none.

The result is the invariant the editor already relies on for tile sets, stated at `LevelEditor.cs:51-54`: **`objectSetSession` is non-null whenever `session` is.** That is what buys the simplicity — `PopulateObjectPalette` loses its early return *and* needs no null branch, `SummonObjectSetEditor` needs no unavailable path, and `PlaceActiveObject` needs no "is there a set" check. The lazy alternative (#9819's Q1b) trades one empty resource for null branches at three sites plus a `TileSetBindPanel.SummonUnavailable`-style explain-and-close path in the panel.

The stated cost of eager minting is that every saved package gains an empty `objectsets/*.json`. **That cost is removed rather than accepted:** `BuildContributions` returns empty for a set with no types, and orchestration skips its attach entirely (I8). This is one predicate, not a compromise shape — it is the honest semantics, since a set with no types is referenced by no placement and describes nothing. A package therefore gains an object-set resource exactly when the author created an object type, and never otherwise.

Residual, named: create a type, place it, save, delete the placement and the type, save again — the now-unreferenced `objectsets/<slug>.json` stays in the package, carried forward by `PackageMergeWriter.Compose` like any other sibling. It resolves for nothing and harms nothing. Orphan collection is out of scope for the same reason #8049 §5.2 kept it out for scripts.

**Multiple object sets in one level.** The session is the *editable* one; the level may still reference others through pre-existing placements, and PR #68's `BuildContributionsForLevel` already carries those forward untouched (pinned by `ObjectSetRoundTripTests:62`). Which one is editable, on a loaded level, is "the first placement's" — the rule `PopulateObjectPalette:1136` already uses. Making that choosable is the bind panel that §7.3 ruled out; not built.

### 9.2 Q2 — Object id versus display name

**Decision: string ids minted by `LevelResourcePaths.UniqueSlug` from the constant base `"object"`; `Name` is a separate, optional, renameable field; the id never changes after minting.**

Three parts to this.

**Why a slug and not an int counter.** `ObjectDefinition.Id` is a string in the format and appears author-facing in the status line (`LevelEditor.cs:1717`, `object: {id}`). `object-2` reads; `2` does not. The slug helpers already exist and are already delegated to by `TileSetResourcePaths`, so this reuses a rule rather than adding a `nextObjectId` field and a never-reuse invariant to maintain. It is also the shape that survives §14.1: if type creation later grows a name prompt, `Slugify(name)` produces `moving-platform` through the same call.

**Why creation takes no name.** This is where constraint §3.1 becomes a design input rather than an acceptance footnote. If defining a type required a name, the *only* naming surface is `OnScreenKeyboard`, and the create path would be untestable end-to-end by the harness. `TileSetEditor.AddTile(bytes, CollisionShapeDefinition.None)` already creates tiles unnamed and renames them later — so creation-without-a-name is the sibling's established shape, not a concession. Rename stays available as a row action for authors who want it. The create path is then reachable with zero text entry.

**Reuse after removal, and why it is acceptable.** `UniqueSlug` checks only the *current* ids, so removing `object-2` and adding again re-mints `object-2`. Within the open level this is unreachable: I6 forbids removing a type that has placements. The residual is a sibling level in the same package holding a placement of the removed id — that placement then resolves to the *new* type. That failure is **strictly milder than the alternative**: it renders the wrong sprite, where a dangling id makes the level unopenable. Buying never-reuse would mean persisting a retired-id set in a format that has nowhere to put one, or scanning every level resource in the package on every removal — real cost against a rare, benign, currently-invisible case. Declined under YAGNI (#1136 §1).

### 9.3 Q3 — Removing a type with placements outstanding

**Decision: refuse. A type with placements in the open level cannot be removed; the row shows the count instead of a Remove action.**

The three options and why the middle one wins:

| Option | Cost | Consequence |
|---|---|---|
| (a) mirror `EditableTileSet.RemoveTile`'s no-cross-check stance | zero | ships a routine authoring sequence that **bricks the level**: define → place → change your mind → remove → save → the level can never be opened again (`ResolveObjects:150`) |
| **(b) refuse while the open level holds placements** | one predicate + a row label | the author is told, before pressing, how many placements block the removal |
| (c) cascade-remove the placements through the history | large | **actively dangerous.** Type edits carry no undo history (tile-set parity); placement removals do. Undoing a cascaded removal restores a placement of a type that no longer exists — reintroducing the unopenable state through the undo stack. Rejected. |

(a) is not the same trade the tile side made. `EditableTileSet.RemoveTile`'s doc-comment argues the loader's typed validation is a sufficient defensive backstop — and for tiles it is, because a dangling *cell* id only mis-renders. For objects the same backstop is a hard failure to open. The asymmetry is in the loader, not in the design's appetite for guards.

Nor is (b) "defensive code for an impossible scenario" (#1136 §6). Define → place → dislike → remove is the ordinary authoring loop of the feature being built.

**Scope of the guard, stated honestly.** It covers the open level only. A sibling level in the same package referencing the removed type is not guarded, because guarding it means scanning every level resource on every removal — cost against an invisible case. That residual is the same one §9.2 accepts, with the same reasoning.

**This changes a settled ruling, and says so.** #8049 §5.2 dismissed the dangling-reference guard on the grounds that *"The editor has no delete-resource operation today for any kind (`PackageBuilder.RemoveResource` exists but nothing calls it from the UI). So P3 cannot create a dangling reference by deletion, because P3 cannot delete. Designing a dangling-reference guard for a deletion path that does not exist is a defensive guard for an impossible scenario."*

**That premise was correct and this feature retires it — for object types only.** `ObjectSetEditSession.RemoveType`, reachable from the panel, is the first delete-resource-content operation the editor has ever offered. The guard is therefore no longer defensive: it protects a path this change creates.

It does **not** propagate. Scripts keep §5.2's stance: a dangling script binding surfaces as a typed `LevelContentException` at load, and there is still no script-delete operation. Tiles keep theirs: a dangling cell id mis-renders and the level still opens. The rule the three share is the honest one — **guard a deletion when the deletion exists and its dangling reference is unrecoverable**; here, only object types satisfy both. `docs/architecture/behavior-authoring.md` §5.2 should gain a one-line pointer to this section in the implementing PR, so a future reader of §5.2 is not left with a retired premise.

### 9.4 Q4 — Does editing a type propagate to placed instances?

**Decision: yes — `RefreshObjectTypes`, called from the same handler the palette rebuild already requires.**

`EditableObjectPlacement` caches `CollisionRole`, graphic bytes, `State` and `EffectiveBehavior` at placement time, and `EditableLevelSnapshot` reads that cache — including for playtest. Without a refresh, changing a type's collision role and playtesting shows the old body: *"I set the platform to Passthrough and it still blocks."* Collision role is the field #8840 itself calls "the field most likely to be got wrong silently", and this is precisely the loop an author uses to get it right.

The KISS argument is that the cost is one line. `LevelEditor.OnObjectSetModelChanged` **must exist regardless**, because creating a type has to reach the Tiles menu — and it must already re-snapshot the canvas, exactly as `OnTileSetModelChanged:984-991` does. Adding the refresh to a handler already being written is a single call. Accepting staleness would save nothing and cost a confusing playtest.

`RefreshObjectTypes` mirrors `RefreshTiles` in *role* — re-sync a cache from the live model after an in-place edit — while remaining per-placement, and it obeys #8247: it rebuilds the cache and carries `Placement` verbatim, never merging a type default onto the authored placement. That distinction is what stops one shared default from being written out as N per-instance copies.

### 9.5 Q5 — Editor feedback for a new type

**Decision: the panel row shows the graphic; the canvas overlay stays schematic.**

`EditorCanvas.DrawObjectOverlay:549` draws an orange rect, the instance name and a behavior marker, and never the sprite. Two things follow. First, this is not a regression this feature introduces — it is equally true of the two shipped sample types today, so drawing sprites on the canvas is a separate concern about *placement* rendering, not about *creation* feedback. Second, and more to the point: **the feedback an author needs when defining a type is "did my PNG import correctly", and that belongs in the panel where the import happened.** The row's `TextureRect` thumbnail (the `LoadTexture` idiom at `TileSetEditor.cs:714-719`) answers it immediately and locally.

The creation act is also confirmed on three further surfaces already wired: the type appears in the Tiles menu's third segment, the status line reads `object: <id>` once selected (`LevelEditor.cs:1717`), and the overlay rect appears at the placed cell. Adding sprite rendering to the canvas would be a change to placement display justified by a creation concern — the wrong seam. If canvas sprites are wanted, they are wanted for all object types and belong in their own task.

### 9.6 Q6 — Does creating a type imply placing one? (already answered)

**No, and the code answers it.** #8840 raises this by analogy to M4b's trigger flow, where placement and binding ship as one act. The analogy does not transfer: a trigger has no identity apart from its binding, which is *why* M4b fused them. An object type is a package-level resource, reusable across levels, with identity independent of any placement — exactly like a tile, and `TileSetEditor.AddTile` does not paint the tile. Creation and placement stay separate acts. Recorded here so it is not reopened.

---

## 10. Cross-cutting concerns

**Error handling.** Unchanged posture: typed `LevelContentException` from the content layer, `ArgumentNullException`/`ArgumentException` at model boundaries, and `try`/`catch` + `GD.PrintErr` at the Godot boundary in `LevelEditor` and the panel — the shape every existing panel and save path uses. No new exception types (#114 §12).

**Concurrency.** None. The editor is single-threaded; the panel mutates the session synchronously and rebuilds.

**Consistency.** The package is the single store. In-memory truth is the session; disk truth is written at save. The one place they can diverge — a path moved by uniquification — is closed by §6.3 step 2d.

**Observability.** `GD.Print` on every successful mutation, naming the id, matching `TileSetEditor`'s existing log lines. `GD.PrintErr` on a refused import.

**Security / resource use.** A PNG is read fully into memory and held on `EditableObjectType.Graphic`, as tile graphics already are. Object counts are author-scale. No new limits.

**Input ownership.** `IsOpen` in `AnyModalOpen()` (I10) is the single guard that canvas cursor capture, toolbar auto-hide and global hotkeys all consult (`LevelEditor.cs:731`, `:678`, `:694`). Omitting it is a silent reachability bug, not a cosmetic one.

---

## 11. Trade-offs made explicit

### 11.1 One session class instead of the tile side's model + session pair

**Simple version chosen.** The downside: the object side no longer mirrors the tile side symbol-for-symbol, so a reader who knows `EditableTileSet`/`TileSetEditSession` must notice the object side is one type. **Probability it costs anything: low** — the session's doc-comment says so in its first line, and the existing object-side consumers (`EditableObjectSetReader`, `ObjectSetMergeWriter`) already speak in `(path, list)` rather than in a set object. **Present cost of the mirrored design: an extra type with one consumer, no distinct lifecycle, and no behavior of its own** — a #1136 §4 indirection. If §14.1 later gives object sets a genuinely separate model-layer consumer, splitting is mechanical.

### 11.2 A narrow `RebindObjectSet`, against #9819's "no object counterpart"

**Complex version chosen, narrowly.** #9819 rules out an object counterpart to `BindTileSet`/`RefreshTiles`; this design adds a per-placement rebind anyway. The distinction is real: those are UI binding and palette caching for a level's single tile set, and neither transfers. `RebindObjectSet` is a ~12-line save-time fixup with one caller inside orchestration, no UI, and no persisted field. **The alternative is not "less code" but "the save silently produces an unopenable package"** whenever a second new level lands in a package that already has `objectsets/untitled-objects.json`. The trade is 12 lines against a data-loss path.

### 11.3 Extracting the focus idiom but not migrating three call sites

**Split chosen deliberately.** DRY says extract (`17 × 4 = 68`); one-feature-one-PR and #8654's warning about runtime-dependent focus work say don't rewrite three working panels inside a feature PR. Doing the extraction and using it *only* in the new panel satisfies both: this change adds no duplication, and the pre-existing duplication is filed with the extraction already available to it. The residual cost is a window in which the helper has one user and three near-copies sit next to it — visible, filed, and cheap to close.

### 11.4 Squaring object sprites to `TileSize`

Named in §5.4. A non-square source is squashed. Accepted because the runtime collision body is square and the content model documents the graphic as tile-sized; preserving aspect would desynchronise sprite and body.

---

## 12. Risks and failure modes

| # | Risk | Mitigation |
|---|---|---|
| R1 | A created type is unplaceable on a fresh level — the feature ships inert | §6.2's two edits; acceptance A3 exercises exactly this path |
| R2 | The panel opens but global hotkeys fire through it | I10 — `IsOpen` in `AnyModalOpen()`; acceptance A8 |
| R3 | Saving emits both the authored set and a stale re-read of it → typed save failure | §6.3 step 3's skip; acceptance A5 |
| R4 | First Save As on a new level with placements throws from the null-package guard | §6.3's guard-inside-the-loop change; acceptance A6 |
| R5 | Uniquification moves the set's path and dangles every placement → unopenable package | §6.3 step 2d; acceptance A7 |
| R6 | A removed type leaves dangling placements → unopenable | I6 (§9.3); acceptance A9 |
| R7 | A type edit silently rots `Behavior`/`State` (the #8247 family) | I9 — one definition-rebuild helper carrying them verbatim; acceptance A10 |
| R8 | A refresh merges type defaults onto `ObjectPlacement`, multiplying a shared default into N copies on save | I7 — `Placement` carried verbatim; acceptance A11 |
| R9 | Panel rows lose focus on every press, making the panel unusable on a gamepad | `FocusMemory` (§5.5) plus `FocusGrid.Contain`; runtime-verified, A13 |
| R10 | The pinned overflow-menu test fails or, worse, is loosened | Extend the pinned mapping in the same change (§5.6) |

---

## 13. Acceptance — and which parts a machine can check

Phrased as *can a user reach this*, never *is the code present*.

### 13.1 Machine-verifiable, engine-free (unit tests)

New file `tests/Uberkarl.Editor.Tests/ObjectSetAuthoringTests.cs`, alongside the existing `TileSetAuthoringTests.cs`; additions to `ObjectSetRoundTripTests.cs` and `MenuCatalogTests.cs`.

| # | Assertion |
|---|---|
| A1 | `AddType` on a blank session mints `object`, then `object-2`; ids are unique; `Types` grows |
| A2 | `RenameType` changes `Name` and leaves `Id` untouched; `SetCollisionRole` toggles; both mark dirty; both return `false` for an unknown id |
| A4 | `EnsureAttached` on a blank session against a package that already holds `objectsets/untitled-objects.json` yields a uniquified path; a second `EnsureAttached` is a no-op |
| A5 | `BuildExtraContributions` with an authored session emits the session's object-set resource **exactly once**, and does not also emit a re-read of it |
| A6 | `BuildContributionsForLevel` with a null package, placements that all belong to the authored (pre-seeded) set, and no other set → returns empty rather than throwing; with a placement of an *unseeded* set and a null package → still throws |
| A7 | A level whose placements name the pre-attach path, saved into a package forcing uniquification, round-trips: reopening resolves every placement (`RebindObjectSet`) |
| A10 | A type with a non-null `Behavior` and a non-empty `State`, renamed and role-toggled, still carries both verbatim through `BuildContributions` → `ReadObjectSet` |
| A11 | `RefreshObjectTypes` after a collision-role change updates `EditableObjectPlacement.CollisionRole` and leaves `Placement` reference-identical; a placement with its own `Behavior` override keeps it |
| A12 | `CountPlacementsOfType` returns the placement count for the right set+id and zero for others |
| A14 | `BuildContributions` on a session with zero types returns empty; orchestration emits no object-set resource for it |
| A15 | The extended `MenuCatalogTests` pinned overflow mapping matches, in order, including the new row |
| A16 | Full save→reopen round trip: blank level, one created type, one placement, `SaveFresh` into a new package, reopen, and the type and placement both resolve — the success criterion of §1, minus the UI |

### 13.2 Machine-verifiable at runtime, via godot-mcp, without typing

| # | Step |
|---|---|
| A8 | Open `Actions → More…` and activate `Edit Objects…` by button text; the panel is visible; a global hotkey (e.g. undo) issued while it is open does **not** reach the editor — `AnyModalOpen` holds |
| A9 | With a package containing a placed type loaded, the type's row reads `In use ×1` and pressing it twice changes nothing |
| A13 | Move focus down the rows and press the collision-role button repeatedly; focus stays on that same button across the rebuilds rather than snapping to `+ Add Object` |
| A17 | `ui_cancel` closes the panel and returns focus to the canvas |

**A note that sharpens the constraint.** `OnScreenKeyboard` builds its keys as `Button { Text = key.DisplayText(...) }` (`game/Editor/OnScreenKeyboard.cs:128`). So although `mcp_input_service.gd` cannot produce a character through a synthetic keystroke, **the rename flow *is* machine-drivable by clicking keyboard buttons by their text**, one character at a time, ending on `Done`. Renaming is therefore verifiable if tedious — it is the *typing* that is impossible, not the *naming*.

### 13.3 Not machine-verifiable — Toni's, explicitly

| # | Step | Why not |
|---|---|---|
| A3 | `New` → `Edit Objects…` → `+ Add Object` → **pick a PNG in the OS file dialog** → close → select the type from the Tiles menu → click the canvas → the object appears | Godot's `FileDialog` presents an `ItemList`, not buttons; reaching a specific file needs either a typed path or coordinate clicking. The dialog is reused verbatim from `TileSetEditor` and its behavior is already established. **Substitute:** A1/A16 drive `AddType` directly with test PNG bytes, proving everything downstream of the file pick. |
| A18 | Playtest after A3 shows the sprite, with a solid body for `Solid` and a pass-through body for `Passthrough` | Requires the imported art and a visual judgement. `ObjectBodyBuilder` is unchanged and already covered. |
| A19 | The panel is comfortable on a gamepad: rows reachable, the scroll follows focus, the import dialog navigable with A/B | Runtime feel. A13/A17 cover the mechanics; comfort is a human call. |

**A3 is the acceptance sentence.** Everything above it exists to make A3 the only step that needs a human, and everything in it except the file pick is separately covered.

---

## 14. Named, not built

Per #1220: these are prose under a heading that says so. **No enum member, registry row, parameter or facade method is to be added for any of them.** The seam is the deliverable.

### 14.1 Type-level default behavior authoring

`ObjectDefinition.Behavior` and `ObjectDefinition.State` round-trip untouched today and will continue to. Authoring them from the object-set panel is the same seam **#8770** draws for tile types, and it is the natural follow-up: a row action opening the existing `BehaviorAssignmentPanel` against the *type* rather than a placement. When it lands, revisit `RefreshObjectTypes`' assumption (§5.3) that a type's binding never needs re-capturing against a package.

### 14.2 An object-set bind panel

Choosing *which* object set is editable, and referencing more than one from new placements. Ruled unnecessary by #8049 §7.3 and not opened here.

### 14.3 Object sprites on the editor canvas

Rendering the graphic under `DrawObjectOverlay`'s rect. A placement-display concern for all object types, not a creation-feedback concern (§9.5).

### 14.4 Migrating the three existing focus-idiom copies onto `FocusMemory`

Mechanical, pre-existing, its own unit (§5.5, §11.3).

### 14.5 Orphan collection for unreferenced object-set resources

The residual named in §9.1. Same posture as #8049 §5.2 took for scripts.

---

## 15. Implementation guidance — ordered

One PR on `feat/object-type-authoring`, branched from `fix/object-set-fresh-save-round-trip` @ `617aa52`. It does not target `main` until PR #68 merges; rebase then.

Each phase leaves the tree building and green.

**Phase 1 — paths and session (engine-free).**
1. `ObjectSetResourcePaths` (§5.1).
2. `ObjectSetEditSession` (§5.2), including the single definition-rebuild helper.
3. Tests A1, A2, A4, A10, A14.

**Phase 2 — level-model seams (engine-free).**
4. `EditableLevel.CountPlacementsOfType`, `RefreshObjectTypes`, `RebindObjectSet` (§5.3).
5. `LevelEditSession.PlaceObject` — parameter to `Package?`, guard made conditional (§6.2).
6. Tests A11, A12.

**Phase 3 — save (engine-free).**
7. `ObjectSetMergeWriter.BuildContributionsForLevel` — the authored-reference parameter, and the null-package guard moved inside the loop (§6.3).
8. `LevelSaveOrchestration.BuildExtraContributions` — the session parameter and the six-step order (§6.3).
9. Tests A5, A6, A7, A16.

**Phase 4 — menu wiring (engine-free).**
10. `MenuOutcomeKind` member + factory; the `MenuCatalog` row after `"Bind Tileset…"`; the `Dispatch` case (§5.6).
11. Extend the pinned `MenuCatalogTests` mapping — A15.

**Phase 5 — panel (Godot).**
12. `FocusMemory` (§5.5).
13. `ObjectSetEditor` (§5.4) — shell, rows, focus, import, rename, role toggle, removal policy.
14. `LevelEditor`: the session field; `NewLevel` and `LoadFromBytes` mint or read it; `PopulateObjectPalette` rewritten off the session with `objectTypes` and `activeObjectSetReference` **deleted as fields**; `PlaceActiveObject`'s null bail deleted; `SummonObjectSetEditor`; `OnObjectSetModelChanged`; `OnObjectSetEditorClosed`; `SaveLevelAndTileSet` passes the session and calls `MarkSaved`; **`AnyModalOpen()` gains the panel**; construction/subscription/`AttachKeyboard`/`AddChild` next to `tileSetEditor` (`LevelEditor.cs:767-771`).
15. Runtime checks A8, A9, A13, A17.

**Phase 6 — docs and follow-ups.**
16. Add the one-line pointer from `docs/architecture/behavior-authoring.md` §5.2 to §9.3 here, and update its line-525 ceiling note to record that the ceiling is dissolved.
17. File the follow-up tasks: the `FocusMemory` migration (§14.4), and the `TileSetEditor` `FollowFocus` defect noted in §5.4.

**Bounce to the orchestrator before implementing** if any phase appears to require a member listed in §14, or if a KISS/DRY/YAGNI call here fails the math on contact with the code (#114 §0).

---

## 16. Design Contracts #1136 §5 — Pre-Design Checklist

**KISS / DRY / YAGNI**
- [x] **No new type mirroring an existing type's value-space.** Checked and acted on: the tile side's model+session pair is *not* mirrored — collapsed to one session class with the reasoning at §5.2 and the trade-off at §11.1.
- [x] **No abstraction with one implementation and no concrete second.** `FocusMemory` (§5.5) has four concrete users, three of them already written. No interfaces introduced.
- [x] **No element justified by "we might need X later."** Everything speculative is in §14 as prose under a heading that says so, per #1220. The one deferred-seam accommodation (§5.3's doc-comment note about `RefreshObjectTypes` and §14.1) is a comment, not a member.
- [x] **No deprecation period, feature flag, compatibility shim or transition window.** None. §14.4 is a filed follow-up, not a shim.
- [x] **DRY math quoted at every inline-vs-extract decision.** Three, all stated: the definition rebuild — `~8 × 3 = 24`, above threshold → **extract** (§5.2); the `ObjectPlacement` initializer — `7 × 2 = 14`, and 2 sites is not >2 → **inline** (§5.3); the focus idiom — `17 × 4 = 68` → **extract**, with the migration scoped out and filed (§5.5, §11.3).

**Existing systems first**
- [x] **Audited whether an existing surface covers the concern.** §3's baseline table is the audit; it removed the two largest items #9819 listed (`ObjectSetMergeWriter`, `LevelSaveOrchestration` — both already built by PR #68) and the whole graphic-import path.
- [x] **Every new layer names the concrete reason it cannot live on an existing surface.** `ObjectSetResourcePaths` — `TileSetResourcePaths` addresses a tile set's namespace and its `GraphicPath` is int-keyed (§5.1). `ObjectSetEditSession` — nothing mutable exists; `EditableObjectType` is immutable and unowned (§5.2). `ObjectSetEditor` — no panel edits object types (§5.4). Not "cleanliness" in any case.
- [x] **No new persisted data point.** Zero content-model changes; `ObjectSetDefinition`/`ObjectDefinition`/`ObjectPlacement` are untouched (§7). The active object set stays session state (#8049 §7.3).
- [x] **Consumer chain recursed on every field kept.** `Behavior` and `State` are carried but not authored — their consumer is the runtime (`ObjectBodyBuilder`, the behavior system), not a dead DTO hop. `Name` on the session is consumed by the slug derivation and the panel title.

**Configurability**
- [x] **No new config knob.** None introduced.
- [x] **No telemetry-then-tune compound.** None.
- [x] **Magic numbers stay named constants in code.** The panel's minimum sizes, colours and `ZIndex` follow the values `TileSetEditor`/`ChoiceList` already use (§5.4); the id base `"object"` is a constant (§5.2).

**Less is better**
- [x] **Deleted / merged / inlined check run on every element.** It removed three members: `Attach` and `AttachAsNewResource` collapsed into `EnsureAttached` (one caller each), and `AttachToExistingResource` dropped entirely (no bind flow) — §5.2. It also *deletes* two existing `LevelEditor` fields (`objectTypes`, `activeObjectSetReference`) and one early return — §6.2.
- [x] **Trade-offs named where the complex option won.** §11.2 (`RebindObjectSet`, 12 lines against an unopenable package) and §11.3 (extract-but-don't-migrate). §11.1 and §11.4 name the costs of the simple options chosen.
- [x] **Radical-clean chosen where the existing surface has no consumer.** `objectTypes` and `activeObjectSetReference` are removed outright rather than kept in sync alongside the session — no half-sourced palette (§6.2).
- [x] **Reader inventory covers AST *and* string-literal references.** The string-literal surfaces are the menu label `"Edit Objects…"` and the pinned `MenuCatalogTests` mapping (§5.6, A15), and the path literals `objectsets/<slug>.json` / `objects/<slug>/<objectId>.png` (§5.1). Both enumerated.
- [x] **Carrier-swap inventory enumerates every affected site, not representatives.** §6.2 and §15 step 14 list every `LevelEditor` site touched by the `objectTypes` → session swap; §6.3 lists every orchestration and writer site.

**Data deliverables** — not applicable: no SQL, no migration, no backfill. No schema identifiers are asserted.

**Document discipline**
- [x] **Cites Code Contracts (#114 §0) and Design Contracts (#1136) as load-bearing.** Header, and applied at §5.2, §5.3, §5.5, §9.2, §9.3, §11, §16.
- [x] **Reader / scope inventories explicit.** §2 (both directions), §3 (baseline), §7 (data model), §8 (invariants), §15 (ordered site list).
- [x] **Out-of-scope listed explicitly, not merely absent.** §2's second table and §14.
- [x] **No multi-paragraph rationale for things that obviously stay.** The content model gets one table (§7) and no argument.
- [x] **No predecessor design superseded.** This supersedes nothing end-to-end. It *amends* one ruling — #8049 §5.2 — and says so, in place, with the quoted premise and the scope of the change (§9.3), plus a doc pointer as step 16.

---

## 17. Open questions

None blocking. Q1–Q6 are settled in §9. Two items are the orchestrator's or Toni's call, and neither gates implementation:

1. **The follow-up tasks in §15 step 17** — `FocusMemory` migration and the `TileSetEditor` `FollowFocus` defect — need filing. The design assumes they are filed, not fixed here.
2. **The menu row's position and label.** `"Edit Objects…"` after `"Bind Tileset…"` is a judgement about grouping, and it is pinned by a test, so changing it later costs one test line. Flagged rather than silently chosen.
