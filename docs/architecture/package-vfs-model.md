# Architectural Document: Package-as-VFS Editor & Save Model

*Corrects the editor's "1 package = 1 level" conceptual flaw. Design-only — hands a build order to the implementer. DiVoid #7571. Refs: format #7413, browser v2 #7570, vision #7407, shared-tileset #7551.*

---

## 1. Problem Statement

Toni, testing browser v2 (PR #21):

> *"the concept here maps to 1 package = 1 level - while a package should obviously be an archive which can contain a number of items - levels, bgm tracks, tiles. so a virtual file system. Something is pretty wrong with the concept like its implemented."*

The **package format** (`Uberkarl.Packages`, #7413) is already correct: a `.pkg` is a ZIP of one `manifest.json` + N typed resources, each at its own `ResourcePath`, each with a `Kind` (`level`, `tileset`, `tilegraphic`, `track`, `sprite`, `script`, `license`). The reader, `Package`, and `PackageBuilder` all handle arbitrary resource sets today.

The **editor** does not reflect this. It treats a package as the serialized form of a single level:

- `EditableLevel` **carries the package identity** (`PackageId`, `Name`, `Version`, `Attribution`, `ForkedFrom`). Level content and archive identity are fused.
- `EditableLevelWriter.ToPackageBytes(level)` **fabricates a whole package around one level** — it emits the tile graphics, the tileset, and the level, and stamps `level.Name` as the package's manifest `Name`. Any sibling resources that were in the package are dropped.
- `EditableLevel.Rename(name)` renames the level **and**, through the writer, the package (its own doc comment: *"renaming the level is exactly renaming the package it will be saved into"*).
- The write seam overwrites the whole `.pkg` file with that single-level rebuild.
- Load (`EditableLevelReader.FromPackage`) reads exactly one `level` resource and discards any knowledge that siblings exist.

**Consequence:** saving a level into a package that already holds *other* resources clobbers them (documented as the known scope boundary in `package-browser.md` §16.7). "Save-As" saves *a level AS a package* named after the level. Package identity is a shadow of a level name.

**Goal / success criteria:** the editor must treat a package as an **archive / virtual file system** of typed resources and edit **one level resource inside it**. Saving a level must **merge** that one resource into its package, preserving every sibling and the package's own identity. This must be corrected before the dimension/tileset features build further on the save model.

---

## 2. Scope & Non-Scope

**In scope**
- The editor's conceptual relationship to a package (the *workflow*, for Toni to ratify).
- Save as a **resource-merge** that preserves siblings, replacing today's single-level full rebuild.
- Save-As as **(target package) + (level resource name/path)** with package identity independent of level names.
- Reusing browser v2's file-browser UI + on-screen keyboard for load **and** save (no shell redesign).
- The migration from the package=level implementation across `Uberkarl.Packages`, `Uberkarl.Editor`, and the Godot glue.
- A design that generalizes to the other resource kinds a package will hold (tracks, tiles, scripts).

**Out of scope (this correction)**
- Authoring any resource kind other than levels (tracks/sprites/scripts editors are later phases). The model must *accommodate* them; it does not implement them.
- A full package-management dashboard (rename package, delete/reorder resources, view all kinds). A minimal path is designed; the dashboard is a future packaging-polish increment (#7407 Phase 4).
- Cross-package resource references / dependency resolution (format records dependencies but resolves none — #7413).
- Shared-tileset authoring (#7551) — the model must not *foreclose* it; it is not built here.
- The browser's visual shell, focus-containment, and on-screen keyboard (kept verbatim from PR #21).

---

## 3. Assumptions & Constraints

| # | Assumption / Constraint | Confidence |
|---|---|---|
| A1 | The `.pkg` ZIP is rebuilt whole on every save — there is no in-place ZIP entry patching, and per #7413's KISS stance that is acceptable. "Merge" therefore means *read the existing archive, carry its resources forward, swap in the level's resources, rewrite the whole archive.* | High (matches `PackageWriter`) |
| A2 | Single-user desktop editor, one package open at a time, no concurrent writers. Desktop-only is a locked decision (#7407). | High |
| A3 | For the MVP each level still owns its own tileset + tile graphics (self-references within the same package). Shared tilesets are a later increment; the design keeps resource *ownership* per-path so it can arrive without reworking the merge. | High |
| A4 | `Uberkarl.Editor` and `Uberkarl.Packages` stay engine-agnostic (no Godot types); all file IO stays in the Godot glue. | High (existing constraint) |
| A5 | `PackageHandle` remains the only cross-boundary reference to a stored package; the storage location stays opaque behind `IPackageSource`. | High |
| A6 | Package identity uses the Hybrid scheme (UUID `PackageId` + human name/version) from #7413; a level's identity within a package is its `ResourcePath`. | High |

---

## 4. Architectural Overview

The correction moves **package identity up, out of the level**, and makes the editor hold two distinct things: the **archive it is working in** and the **one level resource open on the canvas**. Save composes the level back into the archive.

```
                    ┌──────────────────────────────────────────────────────────┐
                    │                     LevelEditor (Godot glue)             │
                    │                                                          │
                    │   CURRENT PACKAGE CONTEXT        CURRENT LEVEL RESOURCE   │
                    │   ┌───────────────────────┐      ┌────────────────────┐  │
                    │   │ handle (where it lives)│      │ resourcePath        │  │
                    │   │ PackageId / Name / Ver │      │ EditableLevel       │  │
                    │   │ Attribution / deps     │      │  (content only)     │  │
                    │   │ resource inventory ────┼──┐   │  + EditHistory      │  │
                    │   │  (paths + kinds, no    │  │   └────────────────────┘  │
                    │   │   payloads)            │  │            ▲              │
                    │   └───────────────────────┘  │            │ edits         │
                    └───────────────┬──────────────┼────────────┼──────────────┘
                                    │ open/write    │ list        │
                                    ▼               │             │ merge on Save
        ┌───────────────────────────────────────┐  │   ┌─────────┴───────────────┐
        │ IPackageSource / IWritablePackageSource│  │   │ Resource-merge writer    │
        │  (FolderPackageSource today)           │  │   │  existing package        │
        │  ListPackages / GetContents / Open     │◄─┘   │   + level contributions  │
        │  Write(handle,bytes) / Create          │◄─────┤   → merged package bytes │
        └───────────────────────────────────────┘ write └──────────────────────────┘
                                    │
                                    ▼
                    ┌───────────────────────────────┐        ┌───────────────────────┐
                    │  Uberkarl.Packages (format)    │        │  PackageBrowser (UI)   │
                    │  Package / PackageReader        │        │  package → resource     │
                    │  PackageBuilder / PackageWriter │        │  for LOAD and SAVE      │
                    │  (+ NEW: seed-from-existing)    │        │  + OnScreenKeyboard     │
                    └───────────────────────────────┘        └───────────────────────┘
```

The three seams that already exist and stay: **`IPackageSource`/`IWritablePackageSource`** (storage), **`PackageBrowser`** (navigation UI), and the **format library**. The correction adds one capability to the format (compose a package from an existing one) and restructures the editor's model and save path.

---

## 5. Components & Responsibilities

| Component | Owns | Does NOT own | Change |
|---|---|---|---|
| **Package Context** (new concept, engine-agnostic) | The identity + metadata of the archive being edited (`PackageId`, `Name`, `Version`, `Attribution`, `ForkedFrom`, `Dependencies`) and its **resource inventory** (the manifest's entries: path, kind, media type, byte length — no payloads). The `PackageHandle` locating it, when persisted. | Any resource payload; level content; file IO. | **New.** Extracts identity that today lives on `EditableLevel`. |
| **`EditableLevel`** (`Uberkarl.Editor`) | Pure level *content*: dimensions, tile palette (graphics held in memory), layer grids, spawns, background, and the **resource paths this level occupies** in its package (`levelPath`, `tileSetPath`, graphic paths). The level's own **display name**. | Package identity/name/version/attribution. Which *other* resources exist. | **Modified.** Strips package identity; `Rename` renames the level only. |
| **`LevelEditSession`** (`Uberkarl.Editor`) | The undoable edit surface over one `EditableLevel`; dirty tracking. On save, producing the level's **resource contributions**. | Merging into storage; file IO; the package inventory. | **Modified.** `Save()` yields contributions (or merged bytes given the source package), not a fabricated single-level package. `RenameLevel` no longer implies a package rename. |
| **Resource-merge writer** (replaces `EditableLevelWriter`, `Uberkarl.Editor`) | Turning an `EditableLevel` into its set of resource contributions (level.json, tileset.json, tile graphics) at their namespaced paths, and composing them **onto an existing package** (add-or-replace by path, siblings + identity preserved) to yield package bytes. A **from-scratch** build for the brand-new-package case. | Where bytes are written; the package inventory beyond what it composes. | **Rewritten** from "fabricate whole package around a level." |
| **`PackageBuilder`/`PackageWriter`** (`Uberkarl.Packages`) | Building a package's ZIP from an identity + resource set. | Editor concepts. | **Extended** with a *seed-from-existing-package* capability + add-or-replace-by-path semantics. |
| **`IPackageSource` / `IWritablePackageSource`** | Discovering packages; listing a package's resources; opening for read; writing bytes back; creating new packages. | The merge (it just persists finished bytes). Atomicity of the file write (recommended addition below). | **Unchanged interface** (the merge happens above it). One recommended robustness change: atomic replace in `Write`. |
| **`PackageBrowser`** (Godot) | Navigating package → resources for **load and save**, driving the on-screen keyboard for names. | Merge logic; identity semantics. | **Small change.** Save flow gains a "pick level resource / ＋ New level" step reusing the resource list. |
| **`LevelEditor`** (Godot glue) | Holding the current package context + current level resource; orchestrating load (retain context) and save (open current package → merge → write); status display. | Merge internals; storage internals. | **Modified.** Retains package context; save reads the existing package and merges. |

---

## 6. Interactions & Data Flow

### 6.1 Load (retain the archive context)

```
Author → Open → PackageBrowser.SummonLoad(source)
  source.ListPackages() ──────────────► [package list]
  pick package  → source.GetContents(handle) ──► [resources, filtered to kind=level]
  pick level    → ResourceChosen(handle, resourcePath)
LevelEditor:
  package = source.Open(handle)                 // opened archive
  context = PackageContext.From(package.Manifest, handle)   // identity + inventory  ◄── NEW: retained
  level   = EditableLevelReader.FromPackage(package, resourcePath)  // content only
  session = new LevelEditSession(level)
  → canvas shows the level; editor now KNOWS its package + siblings
```

The single new behavior: the editor **keeps `context`** (handle + identity + the manifest's resource inventory) rather than dropping everything but the one level.

### 6.2 Save (merge one resource into the current archive)

```
Author → Save
LevelEditor.Save():
  if no current package context (blank/new, never saved) → route to Save-As
  else:
    existing = source.Open(context.handle)              // read the whole current archive
    contributions = session.BuildContributions()        // level.json + tileset.json + graphics (namespaced paths)
    mergedBytes  = MergeWriter.Compose(existing, contributions)
                   // = existing manifest identity + every sibling resource preserved,
                   //   with the contribution paths added-or-replaced
    writable.Write(context.handle, mergedBytes)          // atomic replace
    session.MarkClean(); refresh inventory from merged manifest
```

Contrast with today: today `session.Save()` returns bytes built from *only the level* and `Write` overwrites the file with them. The corrected path reads the existing archive first and carries its other resources forward.

### 6.3 Save-As (choose target archive + level resource name/path)

```
Author → Save As → PackageBrowser.SummonSave(source, currentLevelName)
  Step 1 — target package:  [ ＋ New package… ]  +  existing packages
     • existing → selected handle (identity is that package's, untouched)
     • ＋ New   → type PACKAGE name → collision check (PackageSaveTargetResolver) → new-package identity
  Step 2 — level resource (NEW step, reuses the resource list):
     • when target is an existing package: show its existing LEVEL resources + [ ＋ New level… ]
         - pick existing level → overwrite that resource (seed name+path)  → confirm-overwrite
         - ＋ New level → type LEVEL name → derive a unique resource path
     • when target is ＋ New package: straight to typing the LEVEL name
  → SaveRequested(SaveTarget{ package: existingHandle | newPackageName ; levelResourceName ; resourcePath|overwritePath })
LevelEditor.OnBrowserSaveRequested(target):
  session.RenameLevel(target.levelResourceName)          // renames the LEVEL only
  if target.newPackageName != null:
     bytes = MergeWriter.BuildFresh(newPackageName, contributions)   // brand-new archive, one level
     handle = writable.Create(newPackageName, bytes); adopt handle + fresh context
  else:
     existing = source.Open(target.existingHandle)
     bytes = MergeWriter.Compose(existing, contributions at target.resourcePath)
     writable.Write(target.existingHandle, bytes); adopt handle + refreshed context
```

All communication is **synchronous, in-process** (single desktop app). No brokers, no events beyond the existing Godot UI signals (`ResourceChosen`, `SaveRequested`, `Cancelled`).

---

## 7. Data Model (Conceptual)

```
PackageContext (the archive being worked in)          EditableLevel (one resource inside it)
├─ PackageId            (UUID, archive identity)        ├─ Name           (LEVEL display name only)
├─ Name                 (archive display name)          ├─ LevelPath      (levels/<slug>.json)
├─ Version                                              ├─ TileSetPath    (tilesets/<slug>.json)
├─ Attribution / ForkedFrom / Dependencies             ├─ Tiles[]  (id, graphicPath, bytes, collides)
├─ Handle?              (where it lives, if saved)      ├─ Layers[] (name, flags, cells)
└─ ResourceInventory[]  (path, kind, mediaType, size)  ├─ TileSize / Width / Height / Background
        ▲  one entry per resource in the manifest      └─ Spawns / DefaultSpawn
        │
        └── a level resource is ONE entry of kind=level; siblings may be other levels, tracks, tiles…
```

**Ownership & the key relationship change:** identity (`PackageId`, `Name`, `Version`, `Attribution`, `ForkedFrom`) migrates **from `EditableLevel` to `PackageContext`**. A level is addressed by its `ResourcePath` within a package; the pair `(PackageId, ResourcePath)` is the globally-unique level reference (#7413's Hybrid full reference). One package owns many resource entries; one level open at a time.

**Resource-path scheme (load-bearing migration point).** Today `EditableLevel.DefaultLevelPath = "levels/level.json"` and `DefaultTileSetPath = "tileset.json"` are **fixed constants** — two levels saved into one package would both target `tileset.json` (and `levels/level.json`) and **collide**. The VFS model requires **per-resource paths derived from the resource name**:

| Resource | Path pattern | Note |
|---|---|---|
| Level definition | `levels/<slug>.json` | `<slug>` derived from the level resource name, unique within the package |
| Tileset (while per-level) | `tilesets/<slug>.json` | Becomes a shareable, independently-named resource under #7551 |
| Tile graphics | `graphics/<slug>/<n>.png` | Namespaced so two levels' palettes never collide |

Uniqueness within the package is enforced at merge time. `<slug>` is a sanitized derivation of the level name; the design does not require the slug to equal the display name (name is cosmetic, path is the address).

---

## 8. Contracts & Interfaces (Abstract)

### 8.1 Resource-merge writer (the heart of the correction)

| Operation | Inputs | Output | Semantics / Invariants |
|---|---|---|---|
| **BuildContributions** | An `EditableLevel` | An ordered set of *resource contributions* — each `(ResourcePath, Kind, mediaType, payload bytes)` — comprising the level definition, its tileset, and its tile graphics, at their namespaced paths. | Pure; no IO. The set is exactly the resources this level *owns*. Under shared-tilesets (#7551) the tileset/graphics drop out of a level's contributions and become a reference instead — the contract already expresses "the paths this save claims." |
| **Compose (merge)** | An existing opened package (identity + all sibling payloads) + a contribution set | New package bytes | The result's manifest identity = the existing package's identity **unchanged**. Every existing resource whose path is **not** in the contribution set is carried forward byte-for-byte. Contribution paths are **added if new, replaced if present**. Never drops a sibling; never mutates identity. |
| **BuildFresh** | A new package name + a contribution set | New package bytes | Mints a fresh `PackageId`, sets `Name` = the given package name, default version/attribution, contains only the contribution set. The *only* path that fabricates a package — and legitimately so, the archive is new. |

### 8.2 `PackageBuilder` extension (`Uberkarl.Packages`)

| Capability | Semantics |
|---|---|
| **Seed from an existing package** | Initialize a builder with an existing package's identity (Id, Name, Version, Attribution, ForkedFrom, Dependencies) and its resource payloads, so a caller can then add/replace a subset. |
| **Add-or-replace by path** | Distinct from today's `AddResource` (throws on duplicate path). Replace semantics are required for merge; the throw-on-duplicate path stays for fresh builds where a collision is a bug. |

### 8.3 Save-target DTO (`PackageSaveTarget`, sharpened, not reshaped)

| Field | Meaning (corrected) |
|---|---|
| `ExistingHandle?` | The target **archive** to merge into (picked, or resolved from a `＋ New package` name collision). Its identity/name are preserved. |
| `NewPackageName?` | The **package** display name when minting a brand-new archive. XOR with `ExistingHandle`. Never a level name. |
| `LevelResourceName` | The **level's** display name; drives the resource path derivation and the `EditableLevel.Rename`. Independent of the package name. |
| `OverwriteResourcePath?` | When Save-As targets an existing level resource to replace, its path; else null and a fresh unique path is derived. |

The DTO shape is nearly what PR #21 already returns. The **semantic** correction: `LevelResourceName` addresses a *resource inside* a package, never the package itself.

### 8.4 Storage seam (unchanged interface, one robustness recommendation)

`IWritablePackageSource.Write(handle, bytes)` and `Create(name, bytes)` stay. **Recommendation:** implement `Write` as **atomic replace** (write a temp file, then rename over the target). The merge now carries *other resources' data*, so a torn write corrupts not just the level but the whole archive — atomicity matters more than it did.

---

## 9. Cross-Cutting Concerns

- **Data preservation (the whole point).** The invariant to protect everywhere: *a level save never destroys a sibling resource or the package's identity.* Enforced structurally by Compose carrying forward all non-contribution paths and copying identity from the source manifest.
- **Reference integrity.** Because resource paths are per-level-namespaced, replacing level A's tileset cannot affect level B. When shared tilesets arrive (#7551), a level that *references* (not owns) a shared tileset simply excludes it from its contribution set — Compose then never touches it. The contract already models this.
- **Write atomicity & failure.** On any merge/write failure: do not write partial bytes (temp-then-rename), surface the error, keep the session **dirty** (existing `MarkDirty` on exception is retained). A failed save must leave the on-disk archive exactly as it was.
- **Concurrency / staleness.** Single-user desktop (A2): Compose reads the archive at save time, so it picks up the latest on-disk state. Cross-process concurrent edits are out of scope; note the last-writer-wins behavior.
- **Idempotency.** Saving the same level twice with no edits yields an archive identical in content (paths + identity stable); re-saving is safe.
- **Observability.** Keep the existing `GD.Print`/`GD.PrintErr` breadcrumbs; sharpen the status line to show **package name AND level name separately** (today it conflates them — see §11).
- **Security.** Unchanged: `PackageReader` already guards ZIP traversal and refuses newer format versions; the merge goes through the same reader/writer, so untrusted-package hardening is inherited.

---

## 10. Quality Attributes & Trade-offs

| Attribute | How the design addresses it |
|---|---|
| **Correctness** | The primary driver. Identity is de-conflated from content; siblings are structurally preserved. |
| **Simplicity (KISS/YAGNI, #7413 ethos)** | No in-place ZIP patching, no dependency solver, no package dashboard. The merge is read-all/rewrite-all — the smallest change that preserves siblings on a whole-archive format. |
| **Maintainability** | Each resource kind's save is "produce contributions → Compose." Adding a track/sprite/script editor later reuses Compose unchanged. |
| **Generality** | `PackageContext` + contribution-set + Compose are resource-kind-agnostic; only levels are wired now. |
| **Performance** | Whole-archive rewrite per save. For desktop, package sizes, and manual save cadence this is negligible; explicitly accepted over the complexity of entry-level patching. |

**Trade-offs & rejected alternatives**

1. **Read-all/rewrite-all merge vs. in-place ZIP entry patching.** Chosen: rewrite-all. Patching individual ZIP entries would avoid re-reading siblings but adds real complexity against a format designed (A1, #7413) to be rebuilt whole. Rejected as premature optimization.
2. **Keep identity on `EditableLevel` and "just don't clobber siblings" vs. de-conflate identity.** Chosen: de-conflate. Leaving `PackageId`/`Name` on the level is exactly what produced "level name = package name"; the bug is conceptual, not incidental. Moving identity to `PackageContext` is the root fix.
3. **Explicit "Open Package" dashboard vs. reuse browse-to-level entry.** Chosen: reuse the existing package→level browse as the entry, adopt the *project mental model* internally (retain context, merge on save). A dashboard is deferred (only levels are authored now) — see the workflow decision in §13. This keeps PR #21's shell intact and ships the correctness fix without new UI surface.
4. **Session owns the merge vs. glue supplies the source package.** Chosen: the session/writer stays engine-agnostic and produces *bytes* given the existing package; the Godot glue does the IO (open current package, write result). Preserves the no-IO-in-core constraint (A4).

---

## 11. Risks & Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| **Fixed resource-path constants** (`levels/level.json`, `tileset.json`) silently collide for a second level in a package. | Data loss the moment multi-level packages exist. | Per-resource namespaced paths (§7); uniqueness enforced at merge. **Must land with this change.** |
| Torn write corrupts the *whole* archive (now carries siblings). | Loss of unrelated resources. | Atomic temp-then-rename in `Write` (§8.4); keep session dirty on failure. |
| Merge accidentally drops a sibling (regression). | Data loss — the exact bug being fixed. | Compose contract: preserve every non-contribution path; cover with a test that saves into a multi-resource package and asserts siblings survive (the #7570 §16.7 verification scenario). |
| Blank "New" level has no package yet — save path must handle "no context." | Crash or fabricated package. | New level = unattached; first Save routes to Save-As (§13 open question). |
| Overwriting an existing level resource without confirmation. | Surprise data loss within a package. | Save-As "pick existing level to overwrite" step raises a confirm (reuse the existing `ConfirmOverwrite` step pattern). |

---

## 12. Migration / Rollout Strategy

This **revises PR #21's branch `feat/package-browser-v2`** (or a branch derived from it). PR #21 is **held, not merged**, precisely because its save *model* is what we are correcting; its UI, focus-containment, and on-screen keyboard are kept.

**What changes, by layer:**

- **`Uberkarl.Packages`** — add the seed-from-existing-package capability + add-or-replace-by-path to `PackageBuilder` (or a thin composer over it). No interface change to the storage seams. Recommended: atomic replace in `FolderPackageSource.Write`.
- **`Uberkarl.Editor`**
  - Introduce **`PackageContext`** (identity + inventory + handle).
  - **`EditableLevel`**: remove `PackageId`/`Version`/`Attribution`/`ForkedFrom`/package-`Name` role; keep level display name + resource paths; `Rename` renames the level only; `CreateBlank` no longer fabricates package identity; resource paths become per-level-derived, not fixed constants.
  - **`EditableLevelReader`**: read package identity into a `PackageContext`; read the chosen level's content into `EditableLevel`; retain the inventory.
  - **Replace `EditableLevelWriter`** with the merge writer (`BuildContributions` / `Compose` / `BuildFresh`).
  - **`LevelEditSession`**: `Save`/`RenameLevel` semantics per §5–§6; expose `BuildContributions`.
- **Godot glue**
  - **`LevelEditor`**: hold `PackageContext`; on load retain it; on Save open the current package and merge; on Save-As merge into chosen/new package; split package vs level name in the status line; "New" = unattached blank.
  - **`PackageBrowser`**: add the "pick level resource / ＋ New level" step in Save mode (reuse the resource-list rendering + `ConfirmOverwrite`); `PackageSaveTarget` semantics sharpened (§8.3).

**Build order (for the implementer — no code here, this is the sequence):**

1. **Format capability** — `PackageBuilder` seed-from-existing + add-or-replace; unit tests: compose into a multi-resource package preserves siblings + identity; replace-by-path updates one entry; build-fresh mints a new id. *(Foundational; nothing else compiles cleanly without it.)*
2. **Per-resource path scheme** — derive namespaced level/tileset/graphic paths from the level name; enforce uniqueness. Tests for slug derivation + collision.
3. **De-conflate identity** — introduce `PackageContext`; strip identity from `EditableLevel`; update `EditableLevelReader` to populate both. Update existing editor tests.
4. **Merge writer** — replace `EditableLevelWriter`; `BuildContributions`/`Compose`/`BuildFresh`; `LevelEditSession.Save` yields bytes given the source package. Tests: load→edit→save into a package with siblings→reload asserts edit landed AND siblings intact.
5. **Editor glue** — `LevelEditor` retains context, Save opens+merges+writes, Save-As routes new vs existing; status line split; "New" unattached.
6. **Browser Save step** — add the level-resource / ＋ New level step; sharpen `PackageSaveTarget`; confirm-overwrite on existing resource.
7. **Robustness** — atomic write in `FolderPackageSource.Write`.
8. **Live verification** (the #7570 §16.7 scenario, now expected to pass): save a level into a package that already holds other resources → others preserved, package keeps its own name, the level is one named resource inside it; Save-As into new + existing packages; Load browses multiple resources.

Each step is independently testable; steps 1–4 are pure engine-agnostic library work (fast unit tests), steps 5–6 the Godot glue, step 8 the manual/live confirmation whose final sign-off is Toni's real-pad test.

**PR decomposition note (for the orchestrator).** This correction is one coherent feature (the save model) and revises the held PR #21 branch — it ships as **one PR**, not split. It does **not** bundle any *new* capability (dimensions, tileset editor) — those build on top afterward.

---

## 13. Workflow Decisions — framed for Toni to ratify

**Decision 1 — the editing model: package-as-project (RECOMMENDED) vs. level-carries-its-package.**

> **Recommendation: package-as-project.** The editor always has a *current package* (the archive) and a *current level resource* (the one level on the canvas). Save merges the level into the archive; siblings are preserved; the package's identity/name is independent of any level name. This is the model Toni's own framing points at ("an archive which can contain a number of items… a virtual file system").

**Decision 2 — the entry gesture: keep browse-to-level (RECOMMENDED) vs. a separate "Open Package → manage resources" dashboard.**

> **Recommendation: keep PR #21's browse-to-level flow as the entry, adopt the project model internally.** The author still lands on a level to edit; the difference is the editor now *remembers the archive that level came from and what else is in it*, and Save merges rather than rebuilds. A dedicated package-management dashboard (list all resource kinds, rename package, delete/reorder resources) is a natural **later** increment (#7407 Phase 4) — not needed to fix the conceptual bug, and only levels are authored today. This keeps PR #21's shell fully intact.

**Decision 3 — package identity vs. resource naming.**

> **Recommendation: fully independent.** Package name = the archive's display name, set at ＋ New-package time (and editable later via package management), never derived from a level. Level name = a resource's display name + its path within the archive. Save-As asks for both separately.

**Decision 4 — Save-As second step: offer "pick an existing level to overwrite / ＋ New level" (RECOMMENDED) vs. always type a fresh level name.**

> **Recommendation: offer the resource list.** When Save-As targets an existing package, show its existing level resources plus "＋ New level…", exactly mirroring the "＋ New package…" pattern already shipped. Picking an existing level = replace it (with a confirm); ＋ New level = add. This makes resource-path selection concrete and reuses the browser shell with no new widget kind.

---

## 14. Open Questions

1. **What does "New" mean in the project model?** Recommended MVP: **New = a blank, unattached level** (no package until first save, which routes through Save-As). Does Toni also want an explicit **"New level in the *current* package"** action (add a resource to the open archive without leaving it)? It is a small, natural addition on top of this model but is not required for the correction.
2. **Package-management surface timing.** Renaming a package, deleting/reordering resources, and viewing non-level resource kinds are deferred to a later increment. Confirm that deferral (the correction ships without a dashboard).
3. **Resource path vs. display name coupling.** Recommended: the resource **path** is a sanitized slug of the level name, but is *not* renamed if the level is later renamed (renaming content should not move a VFS entry and break references). Confirm that a level rename changes only the display name, not the resource path, once saved.
4. **Confirm-overwrite granularity.** Two overwrite prompts now exist: package-name collision (＋ New package) and resource-path collision (Save-As onto an existing level). Confirm both should prompt, or whether replacing an explicitly-picked existing level is confirm-free (the pick itself being the intent).
5. **Attribution/licensing on merge.** When merging a level into an existing package, package-level attribution stays the package's; per-resource attribution (format supports it, #7413) is not authored yet. Confirm we leave per-resource attribution untouched for now.

---

## 15. Implementation Guidance for the Next Agent

Follow the **build order in §12** (steps 1→8). Altitude reminders:

- The format library already supports multi-resource packages — **do not** touch the ZIP/manifest model except to add the *seed-from-existing* + *add-or-replace* capability (§8.2).
- The single conceptual root cause is **identity living on `EditableLevel`** and **the writer fabricating a package**. Fix both; everything else follows.
- **Per-resource paths (§7) are not optional** — the fixed-constant paths are a live data-loss latent bug for the first multi-level package.
- Keep `Uberkarl.Editor`/`Uberkarl.Packages` **engine-agnostic**; all file IO stays in the Godot glue (A4).
- **Preserve PR #21's UI shell verbatim** — focus-containment, on-screen keyboard, list styling, back-nav. Only the Save *model* and the Save flow's resource-selection step change.
- The acceptance test is #7570 §16.7's scenario, now expected to **pass**: save into a package holding other resources → siblings preserved, package name unchanged, level is one named resource inside.
```
