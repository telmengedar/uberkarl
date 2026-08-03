# Architectural Document: In-Engine Package / Resource Browser (source-abstracted, gamepad-first)

> Editor UI v2, part 2 — the radial-centric follow-up to part 1 (gamepad `Control` activation,
> merged in PR #13 / DiVoid #7466). This document is the canonical design deliverable; the DiVoid
> node mirrors it and links it to task #7465, project #7396, and the package format #7413.

## 1. Problem Statement

Loading a level today goes through a mouse-only Godot `FileDialog` that browses the **operating-system
file system** (`FileDialog.AccessEnum.Filesystem`, rooted at `res://content`). That is wrong on two axes:

1. **It exposes the host file system.** The user sees OS paths, can wander anywhere, and the experience
   is tied to a machine layout rather than to *the game's content*.
2. **It is mouse-only.** Part 1 made classic `Control`s gamepad-operable via the `ui_accept`/`ui_cancel`
   pad bindings and the focus-zone model, but the load/save flow still assumes a pointer.

Toni's framing (verbatim, DiVoid #7465):

> *"we don't want to browse the file system - we should be system agnostic. There is a package source,
> for our case its a predefined package folder where they have to reside in... Then we only need to
> select the package and then we need to be able to browse the contents. This source later on could
> also be simply replaced with an online resource... but thats for way later, local is our focus now."*

**Goal.** Replace the load `FileDialog` with an in-engine, gamepad-first, two-step browser driven by an
**abstract package source**: (1) select a package from the source, (2) browse that package's resources
(filtered to loadable kinds, e.g. levels) and open the chosen one. The source is the important
architectural seam — a local folder implementation now, an online implementation later, with no change
to the browser or the editor when that swap happens.

**Success criteria.**
- The user never sees a host file-system path. Package selection is by package identity/name; resource
  selection is by in-package resource, both surfaced through the source abstraction only.
- Point the source at a folder holding **2+ multi-resource packages** → browse packages → select →
  browse that package's levels → select a specific one → **it loads** (the load targets the *chosen*
  resource, not "the first level" — this closes the in-package-selection gap).
- Operable on **gamepad, keyboard, and mouse**, reusing part-1's `ui_accept`/`ui_cancel` bindings and
  the Canvas⇄Toolbar focus-zone discipline.
- The source abstraction and the local folder implementation are **unit-tested** in `src/` with no Godot
  dependency; a future online implementation drops in behind the same contract.

## 2. Scope & Non-Scope

**In scope**
- An engine-agnostic `IPackageSource` abstraction in `src/Uberkarl.Packages`, plus a local
  `FolderPackageSource` over a predefined content folder.
- Loading a **specific** chosen resource from within a selected package (an `EditableLevelReader`
  overload that resolves a caller-supplied resource path).
- A summoned, gamepad-first two-step browser window in `game/` (packages → resources), wired to the
  Actions radial's **Open**, replacing the load `FileDialog`.
- Unit tests for `IPackageSource` + `FolderPackageSource` (list packages, list a package's contents).

**Non-scope (explicitly out)**
- **The online package source implementation.** The interface is designed so it drops in later; it is
  *not* built now (Toni: "way later"). See §10 for the seam that guarantees the swap.
- **The tile category→list window (#7450)** — a sibling summoned window, separate arc.
- **Layer editing** and any authoring feature beyond existing save.
- **Save-As with a new user-supplied name over the source.** Gamepad text entry is not cheap; the write
  seam is designed (§8, §9) but the naming UI is a named follow-up. Save-to-current-package is retained.
- Package authoring / creation UI, dependency resolution, online sync — all pre-existing non-goals of
  the package format (#7413).

## 3. Assumptions & Constraints

| # | Assumption / Constraint | Confidence | Impact if wrong |
|---|---|---|---|
| A1 | `Uberkarl.Packages` (`PackageReader`, `Package`, `PackageManifest`, `ResourceEntry`, `ResourceKind`) is the enumeration substrate; a package's resources are `Package.Manifest.Resources`. | High (read in-repo) | Interface would need a different backing reader. |
| A2 | Reading a manifest is cheap (ZIP central-directory + one small JSON entry); payloads are read lazily on demand. So `ListPackages()` may open every package's manifest without loading tile graphics. | High | If manifests were expensive, listing would need a cached index. |
| A3 | The predefined content folder is a directory of `*.pkg` files. Each `.pkg` is one self-contained ZIP package (per #7413). | High | Folder impl enumeration changes. |
| A4 | The editor process can read the content folder via plain `System.IO`/BCL `File`, given a globalized absolute path (as it already does for save via `File.WriteAllBytes`). | High | Would need Godot `FileAccess` in the engine layer only. |
| A5 | Current sample packages contain a single `level`-kind resource; multi-resource packages are the *target* of verification but the format already supports N resources per package. | Medium | If a package has 0 levels, the browser shows an empty second step (handled: clear "no loadable resources" state). |
| A6 | Gamepad text entry (naming a new package) is a poor experience and out of scope for this increment. | High (Toni: load-browse is the headline) | Save-As naming would need its own UX design. |

**Hard constraints.**
- `src/Uberkarl.Packages` stays **Godot-independent** (`Microsoft.NET.Sdk`, `net8.0`, no Godot ref) —
  the abstraction and the folder impl must not reach for any Godot type. Path resolution to an absolute
  directory (`res://`/`user://` globalization) happens in the `game/` layer and is handed to the source
  as a plain string.
- No new third-party dependency (KISS, per #7413 and #1184). BCL only.
- One feature, one PR (global PR-scope discipline).

## 4. Architectural Overview

Three layers, matching the codebase's existing engine-agnostic-core / Godot-glue split:

```
                          ┌──────────────────────────────────────────────┐
  game/ (Godot glue)      │  LevelEditor (composition root)              │
                          │    Actions radial ─ "Open" ───┐              │
                          │                               ▼              │
                          │   PackageBrowser (summoned Control window)   │
                          │     step 1: package list  ── selects ──▶     │
                          │     step 2: resource list ── opens ───▶      │
                          └───────┬───────────────────────────┬─────────┘
                                  │ IPackageSource            │ EditableLevelReader
                                  │ (list / contents / open)  │ .FromPackage(pkg, chosenPath)
  src/Uberkarl.Packages   ┌───────▼───────────────────────────▼─────────┐
  (engine-agnostic)       │  IPackageSource  ◀── FolderPackageSource     │
                          │     PackageSummary / PackageHandle /         │
                          │     ResourceSummary  (opaque, no FS paths)   │
                          │  (future) OnlinePackageSource ── same contract│
                          └───────┬──────────────────────────────────────┘
                                  │ PackageReader.Open(stream) → Package
                          ┌───────▼──────────────────────────────────────┐
                          │  Package / PackageManifest / ResourceEntry    │  (existing #7413)
                          └──────────────────────────────────────────────┘
```

**The seam that matters.** `IPackageSource` is the *only* thing the browser knows about. It has no notion
of files, folders, ZIP, or URLs. `FolderPackageSource` is one implementation; a later
`OnlinePackageSource` is another. Swapping the source is a one-line composition-root change and touches
no UI and no editor logic.

## 5. Components & Responsibilities

### 5.1 `IPackageSource` — the abstraction (engine-agnostic, `src/Uberkarl.Packages`)
- **Owns:** the contract for *discovering* packages and *enumerating and opening* a package's contents,
  independent of where they live.
- **Owns:** the opaque handle vocabulary (`PackageHandle`, `PackageSummary`, `ResourceSummary`) — the
  browser addresses packages and resources through these, never through a path or URL.
- **Does NOT own:** how packages are stored (folder / HTTP), ZIP mechanics (delegated to
  `PackageReader`), any Godot type, any UI, any edit logic, resource *interpretation* (that stays in
  `EditableLevelReader`/`LevelContentSerializer`).

### 5.2 `FolderPackageSource` — the local implementation (engine-agnostic, `src/Uberkarl.Packages`)
- **Owns:** enumerating a **single predefined directory** for `*.pkg` files and turning each into a
  `PackageSummary` by reading its manifest (id, name, version, light metadata) via `PackageReader`.
- **Owns:** resolving a `PackageHandle` back to its `.pkg` file and opening it (`PackageReader.Open`) so
  callers can read the chosen resource.
- **Owns:** graceful handling of a folder that is missing, empty, or contains an unreadable/invalid
  `.pkg` (a bad package is skipped from the listing, not fatal to the whole list).
- **Does NOT own:** recursion into sub-folders (flat directory only — the folder *is* the source, not a
  tree to browse), the choice of *which* directory (handed in at construction as an absolute path),
  writing packages (see §8 write seam), or exposing the directory path to the UI.

### 5.3 `PackageBrowser` — the summoned browser window (Godot glue, `game/`)
- **Owns:** the two-step summoned UI. Step 1 lists `PackageSummary`s (name + version) for selection;
  step 2 lists the selected package's loadable `ResourceSummary`s (filtered to `ResourceKind.Level`) for
  selection; a commit raises an "open this resource of this package" outcome; cancel/back returns.
- **Owns:** being **gamepad-first and device-neutral** — reusing part-1's `ui_accept` (pad A / Enter /
  Space) to confirm, `ui_cancel` (pad B / Esc) to go back/close, focus grabbed on summon so a pad drives
  it immediately, and the Canvas⇄(browser) focus discipline so the grid cursor is frozen while it is open
  (same `CursorInputGate` gate the radial uses).
- **Does NOT own:** any file/ZIP knowledge (it only ever holds `IPackageSource` + the opaque
  handles/summaries), edit logic, or the actual level parsing (delegates to the editor's load path).

### 5.4 `LevelEditor` (composition root) — wiring changes (Godot glue, `game/`)
- **Owns:** constructing the concrete `FolderPackageSource` with the resolved content-folder absolute
  path, owning the `PackageBrowser` instance, routing the Actions radial's **Open** to *summon the
  browser* instead of popping the `FileDialog`, and on the browser's "open" outcome calling the editor's
  load path against the **chosen package + chosen resource**.
- **Does NOT own:** the abstraction (it depends on `IPackageSource`, constructs the folder impl only at
  the composition root — the single line that a future online swap edits).

### 5.5 `EditableLevelReader` — a targeted-load addition (engine-agnostic, `src/Uberkarl.Editor`)
- **Change:** add the ability to load a **caller-specified** level resource path from an opened
  `Package`, alongside the existing "find the first level" behaviour. This is what makes in-package
  selection real: the browser's step-2 choice is a `ResourcePath`, and the load resolves *that*.
- The existing `FromPackage(package)` (first-level convenience) remains for the boot/default path.

## 6. Interactions & Data Flow

**Load flow (the headline), device-neutral:**

```
User holds Actions trigger ▶ radial opens ▶ aims at "Open" ▶ releases/confirms (pad A / Enter / mouse)
   │
   ▼  LevelEditor.InvokeFileCommand(Open)  — no longer PopupCentered(openDialog)
Summon PackageBrowser ▶ browser.ShowPackages(source.ListPackages())
   │  step 1: package list has focus; D-pad/stick/arrows/mouse move selection; ui_accept confirms
   ▼  onPackageChosen(PackageSummary.Handle)
browser.ShowContents( source.GetContents(handle) filtered to Level kind )
   │  step 2: resource list has focus; ui_accept confirms; ui_cancel = back to step 1
   ▼  onResourceChosen(handle, ResourcePath)
LevelEditor:  using package = source.Open(handle);
              EditableLevel level = EditableLevelReader.FromPackage(package, chosenPath);
              AdoptSession(level);  currentSource=handle;  // remembers origin for Save
   ▼
Browser closes ▶ focus returns to canvas ▶ status line shows loaded level.
```

**Communication style.** All synchronous, in-process, single-threaded (Godot main thread). No broker, no
async, no events across a boundary beyond Godot signals/`Action` callbacks the editor already uses
(`PopInMenu.Chosen`, `FileDialog.FileSelected`). Enumeration is eager and cheap (A2); if a folder ever
grows large enough to stutter on summon, an indexed/cached listing is the noted evolution — not built.

**Failure paths in the flow.**
- Empty/missing folder → step 1 shows an explicit "no packages in content folder" state; no crash.
- A `.pkg` that fails to open → skipped from `ListPackages()` (logged), the rest still list.
- A package with no `level`-kind resource → step 2 shows "no loadable resources"; `ui_cancel` backs out.
- A resource that fails to parse on open → the existing `EditableLevelReader` exception surfaces as the
  editor's current clear error print; browser stays closed, prior level intact.

## 7. Data Model (Conceptual)

New value types (engine-agnostic, in `Uberkarl.Packages`), all **opaque to the UI**:

| Type | Purpose | Fields (conceptual) | Notes |
|---|---|---|---|
| `PackageHandle` | Opaque locator the UI passes back to the source to open/enumerate a package. | An internal locator only the source interprets (folder impl: the file path; online impl later: an id/URL). | The UI treats it as a token — **never renders it**. This is the "no FS path exposed" guarantee, structurally enforced. |
| `PackageSummary` | What step 1 renders and selects. | `PackageId Id`, display `Name`, `Version`, optional light metadata (resource count / attribution), `PackageHandle Handle`. | Built from `PackageManifest` without loading payloads. |
| `ResourceSummary` | What step 2 renders and selects. | `ResourcePath Path`, `Kind` (string, cf. `ResourceKind`), display name, `MediaType`/`ByteLength` (optional). | Projected from `ResourceEntry`; UI filters by `Kind`. |

**Relationships & ownership.** A source *has many* packages (summaries); a package *has many* resources
(summaries). Identity of a package is its `PackageId` (existing UUID scheme, #7413); identity of a
resource within a package is its `ResourcePath`. A full "load this" address is `PackageHandle` +
`ResourcePath`. No new persisted entity — everything derives from the on-disk `.pkg` manifests at
enumeration time.

## 8. Contracts & Interfaces (Abstract)

### 8.1 `IPackageSource` (read contract — the one the online impl also honours)

| Operation | Input | Output | Semantics & Invariants |
|---|---|---|---|
| **List packages** | (none) | Ordered collection of `PackageSummary` | Returns every *valid* package the source can currently offer. Never throws for individual bad packages (skips them); may return empty. Ordering is source-defined but stable (folder impl: by name). Reads only manifests, never payloads. Must not surface any storage path. |
| **Get contents** | `PackageHandle` | Ordered collection of `ResourceSummary` | Enumerates the package's resources from its manifest. Opens and closes the package; holds no lingering handle. Throws a typed "package unavailable" error if the handle no longer resolves (e.g. file deleted since listing). |
| **Open** | `PackageHandle` | A live, disposable `Package` | Opens the package for reading resource bytes. Caller owns disposal (mirrors `PackageReader.Open`). Used by the editor to read the chosen resource. Throws typed "unavailable"/"invalid" on failure. |

Invariants across the contract:
- **No path leakage.** No operation returns or requires a host path/URL; the `PackageHandle` is the only
  cross-boundary reference and is opaque.
- **Read-only and side-effect-free** for listing/enumeration.
- **Deterministic within a snapshot.** A handle obtained from `ListPackages()` is valid to `Open` until
  the underlying storage changes; a stale handle fails cleanly, it does not read the wrong package.

### 8.2 `IWritablePackageSource` (write seam — designed, not fully surfaced in UI this increment)

A **separate, optional** capability interface so the read contract stays honest for a read-only online
source. `FolderPackageSource` implements it; a future read-only online source would not.

| Operation | Input | Output | Semantics |
|---|---|---|---|
| **Write / overwrite package** | `PackageHandle` (existing) + package bytes | (success/failure) | Overwrites the package the handle refers to — this is the target for **Save** of a level loaded from the source. |
| **Create package** | proposed name + package bytes | new `PackageHandle` | Creates a new package in the source. Backs a future **Save-As** once a gamepad-friendly naming UI exists (deferred, §2). |

This increment **wires Save** (overwrite-current) to the write seam if the level was loaded from the
source; **Save-As naming UI is deferred** (interface present, no UI). Rationale: gamepad text entry is
not cheap (constraint A6); the headline is load-browse.

### 8.3 Editor load contract addition

| Operation | Input | Output | Semantics |
|---|---|---|---|
| `EditableLevelReader` targeted load | opened `Package` + `ResourcePath` of a level resource | `EditableLevel` | Loads the *specified* level resource (validates it is a `level`-kind entry present in the package), instead of "first level found". The chosen-resource path comes straight from step 2. Existing first-level overload retained for boot. |

## 9. Cross-Cutting Concerns

- **Security / safe input.** `PackageReader` already guards ZIP traversal and rejects newer format
  versions; the folder source inherits that. The folder source must itself not follow `..`/absolute
  entries — it only *enumerates* a flat directory and hands file streams to `PackageReader`, which does
  the untrusted-input validation. No path from the UI ever reaches the file system.
- **Error handling.** Consistent with the editor's existing philosophy: bad individual package → skipped
  + logged; failed open/parse → typed exception surfaced as the editor's clear `GD.PrintErr` line; the
  browser never leaves the editor in a broken state (prior level stays loaded on failure).
- **Observability.** Reuse `GD.Print`/`GD.PrintErr` for load/skip/failure, matching the existing editor
  logging. No new logging framework.
- **Concurrency.** None introduced — single-threaded main-thread flow. Enumeration is synchronous.
- **Consistency / staleness.** The listing is a snapshot; a handle can go stale if the folder changes
  underneath. Contract handles this by failing cleanly on `Open`/`GetContents`, not by reading a wrong
  package. Re-summoning the browser re-lists.
- **Idempotency.** Listing and enumeration are pure reads, naturally idempotent. Loading is idempotent
  w.r.t. the source (opening the same handle+path yields the same level).
- **Configuration.** The content-folder location is **config, not user-facing** (see §11).

## 10. Quality Attributes & Trade-offs

| Attribute | How addressed |
|---|---|
| **Extensibility (the priority)** | The whole point: `IPackageSource` isolates *where content lives* behind one contract. Online support later = a new impl + one composition-root line. Read/write split (§8.2) keeps a read-only online source honest. |
| **Maintainability** | The source lives beside `PackageReader` in the already-unit-tested engine-agnostic library; the browser is thin glue like `PopInMenu`. No new project, no new dependency. |
| **Testability** | `IPackageSource` + `FolderPackageSource` are pure C#, testable against a temp directory of real `.pkg` files with zero Godot. |
| **Usability** | Gamepad-first, reusing part-1's proven `ui_accept`/`ui_cancel` + focus-zone model; no host paths shown; two clear steps. |
| **Performance** | Manifest-only listing (A2); payloads lazy. Adequate for a local folder of tens of packages. |

**Trade-offs & rejected alternatives.**
- **Opaque `PackageHandle` vs. exposing the path.** Chosen opaque so "system-agnostic, no FS browsing"
  is *structurally* true and the online swap needs no UI change. Cost: one indirection type. Accepted.
- **Read/write split (`IWritablePackageSource`) vs. one fat interface.** Chosen split so a read-only
  online source is not forced to implement writes. Cost: a second small interface. Accepted per ISP.
- **Reuse the radial for the browser vs. a dedicated list window.** Chosen a dedicated summoned
  **list** window (not a radial): a package/level list is variable-length and text-heavy — a scrollable
  focus-navigable list fits far better than fixed radial wedges, and it mirrors the sibling tile
  category→list window (#7450) shape. The radial's role is only to *summon* it (the Open wedge).
- **New source project vs. folding into `Uberkarl.Packages`.** Chosen to fold in — the source is *about*
  packages, depends only on `PackageReader`, and adds no dependency. A separate project would be
  ceremony (#1184, don't over-engineer).
- **Building the online source now.** Rejected outright (Toni: "way later"). Interface-only.
- **Eager cached index.** Rejected as premature (A2); noted as the evolution if a folder grows large.

## 11. Risks & Mitigations

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Content folder location wrong for exported builds (`res://` is read-only when exported). | Med | Med | Ship default = **writable `user://` content dir** (§ below); `res://content` is only the read-only seed. Source takes an absolute globalized path, so the decision is one line in the glue. |
| In-package selection still loads "first level" (the stated current gap). | Med | High | Explicit targeted-load contract (§8.3): step-2 choice is a `ResourcePath` and load resolves *that*; verification asserts a non-first resource loads. |
| Gamepad text entry needed for Save-As. | High | Low | Deferred by design (§2, §8.2); Save-overwrite still works; interface seam present. |
| Stale handle after folder change mid-session. | Low | Low | Contract fails cleanly on open; re-summon re-lists. |
| Browser focus escapes to the canvas under it (the class of bug part-1 fought). | Med | Med | Reuse the exact part-1 discipline: freeze cursor via `CursorInputGate` while open, contain focus within the browser's own controls, grab focus on summon. Verify with injected pad nav. |

## 12. Content-Folder Location Decision (config, not user-facing)

**Decision:** the canonical, system-agnostic package source folder is **`user://packages/`**.

- `user://` is Godot's per-user writable location, stable across platforms and surviving export — the
  correct home for "a predefined package folder where packages must reside" that users can add to and
  that Save can write back to. `res://content` is read-only in an exported build, so it cannot be the
  live source there.
- **Seeding:** `res://content` (the shipped read-only sample content, e.g. `sample.pkg`) is copied into
  `user://packages/` on first run if the target is empty. This gives a fresh install visible content
  without the user touching the file system.
- The absolute path is obtained in the `game/` layer via `ProjectSettings.GlobalizePath("user://packages")`,
  the directory is created on demand, and the resulting absolute string is handed to
  `FolderPackageSource`. The engine-agnostic source never knows about `user://`/`res://`.
- **Not user-facing:** nowhere does the UI show this path; the user only ever sees package names.

**Verification note.** For the honest verification gate, the source is pointed at a folder seeded with
**2+ multi-resource packages** (the sample plus at least one more, each with a level). This can be the
seeded `user://packages` or, for a deterministic harness run, an explicit temp folder — the source takes
any absolute directory.

## 13. Migration / Rollout Strategy

Additive; the edit/undo/save spine and `LevelEditSession` are untouched (same discipline as part 1).

1. The load `FileDialog` (`openDialog`) is **removed from the load path** — the Actions-radial **Open**
   and the toolbar **Open** button both summon the browser instead. (The toolbar Open button is updated
   for parity; the toolbar itself is unchanged otherwise.)
2. The save `FileDialog` remains for now as the Save-As fallback (naming UI deferred), so no regression
   in the ability to save under a new name during the transition. Save-overwrite prefers the write seam
   when the level came from the source.
3. Boot still auto-loads the sample (now via the source / seeded folder), so first-run behaviour is
   unchanged from the user's view.

## 14. Open Questions (for Toni)

1. **Folder location** — confirm **`user://packages/`** as the canonical source dir with `res://content`
   as first-run seed, or prefer a different location (e.g. a folder next to the executable so packages
   are shareable without digging into the OS user-data path)?
2. **Save-As naming UX** — accept deferring new-package naming (gamepad text entry) to a later increment,
   with Save-overwrite working now? Or is naming-a-new-package needed in this increment (would require an
   on-screen keyboard / name-picker design)?
3. **Resource filter** — step 2 filters to `level` kind for "open". Should it show *all* resource kinds
   (read-only, greyed) for transparency, or only the loadable ones?
4. **Multi-level packages** — the format allows N levels per package; is a package expected to routinely
   hold several levels (making step 2 substantive), or is it usually one-level (step 2 often trivial)?
   Shapes how much step-2 UX polish is worth now.
5. **Seed-on-first-run** — is copying `res://content` into `user://packages` the desired bootstrap, or
   should the folder start empty and rely on the user/installer to populate it?

## 15. Implementation Guidance for the Next Agent

Build in this order; each milestone is independently verifiable. **Engine-agnostic core first, fully
unit-tested, before any Godot glue.**

**M1 — Source abstraction + local impl (engine-agnostic, `src/Uberkarl.Packages`).**
- Introduce `PackageHandle`, `PackageSummary`, `ResourceSummary` (opaque value types; no Godot).
- Introduce `IPackageSource` (list / get-contents / open) and `IWritablePackageSource` (write seam).
- Implement `FolderPackageSource` over an injected absolute directory: enumerate `*.pkg`, build summaries
  from each manifest via `PackageReader`, resolve a handle to open, skip bad packages gracefully.
- **Unit tests (`tests/Uberkarl.Packages.Tests`):** list packages from a temp folder of 2+ real `.pkg`
  files; list a package's contents; empty/missing folder → empty list; a corrupt `.pkg` is skipped not
  fatal; open returns a readable `Package`; stale handle fails cleanly. Keep coverage in line with the
  library's existing bar.

**M2 — Targeted load (engine-agnostic, `src/Uberkarl.Editor`).**
- Add the `EditableLevelReader` overload that loads a **caller-specified** `ResourcePath` level from an
  opened `Package` (validate kind + presence), retaining the first-level overload.
- Unit test: a package with 2 level resources loads the *second* when asked — proves in-package
  selection is honoured, not "first".

**M3 — Browser window (Godot glue, `game/`).**
- Add `PackageBrowser` (a summoned `Control`): step-1 package list, step-2 resource list (filtered to
  `level`), gamepad-first (grab focus on summon, `ui_accept` confirm, `ui_cancel` back/close), focus
  contained within its own controls, cursor frozen while open via `CursorInputGate`. It holds only
  `IPackageSource` + opaque summaries/handles — no file/ZIP knowledge.

**M4 — Composition-root wiring (Godot glue, `LevelEditor`).**
- Construct `FolderPackageSource` with `GlobalizePath("user://packages")` (create + seed from
  `res://content` if empty). Own the `PackageBrowser`.
- Route Actions-radial **Open** and toolbar **Open** to summon the browser; remove `openDialog` from the
  load path. On the browser's open outcome: `Open(handle)` → targeted `FromPackage(package, path)` →
  `AdoptSession`; remember the origin handle for Save.
- Wire **Save** to the write seam (overwrite current package) when the level originated from the source;
  keep the save `FileDialog` as the Save-As fallback for now.

**M5 — Verification (Godot MCP + honest gate, per #7407 / §6-audit).**
- Seed a folder with **2+ multi-resource packages** (levels). Drive via injected `ui_*` /
  `InputEventJoypadButton` (part-1 method) **and** keyboard **and** mouse: summon Open → package list →
  select → resource list → select a specific (non-first) level → it **loads**. Screenshot the package
  list and the contents list. Assert in-package selection loads the chosen resource (the current gap).
- `get_editor_errors` clean; `dotnet build` 0/0; `src/` unit tests green; §6.10 comment-grep 0 on
  changed files.
- **Real-pad confirmation is Toni's** — state it; harness injects `ui_accept`/raw pad, so no false
  "verified on hardware".

---

## 16. Part 2 — Save Flow + File-Browser UI (DiVoid #7552, 2026-08-03)

Status: implemented on branch `feat/package-browser-v2` by John (backend dev). Closes out the deferred
Save-As naming item from §2/§8.2/§14 Q2 and the browser-polish backlog **#7500** (back-navigation, visual
theming, Save-As naming) in the same increment. Depends on the on-screen keyboard (**#7513**, merged) for
gamepad-friendly text entry.

Toni (verbatim, task #7552): *"package browser for save... package browser in general should look more
like a file browser - list of entries, for save specify filename and so on."*

### 16.1 What changed vs. Part 1

- **`PackageBrowser` is no longer load-only.** It gained a `Mode` (`Load`/`Save`) alongside its existing
  `Step` (`Packages`/`Resources`), plus a third step, `ConfirmOverwrite`, for the save flow's
  collision safety net. Public surface: `SummonLoad(source)` (unchanged behaviour) and the new
  `SummonSave(source, currentLevelName)`; the load path still raises `ResourceChosen`, the save path
  raises the new `SaveRequested(PackageSaveTarget)`.
- **File-browser look.** Each list row is now an `HBoxContainer` of a primary, expand-fill `Button` (the
  focus/selection target — the single-column `FocusGrid`-style chain from Part 1 is unchanged) plus a
  dim, non-focusable secondary `Label` showing light metadata (`v{Version} · N items` for packages,
  a byte-size for resources) — "name + light metadata," no thumbnails, per the anti-complexity brief. A
  header row carries a breadcrumb-style title (`"{Package} — Select Level"` at the resources step,
  `"A package named “X” already exists."` at the confirm step) and a mouse-clickable **Back/Close**
  button that mirrors whatever `ui_cancel` currently does, since a file browser needs an on-screen
  affordance for a mouse-only user, not just a gamepad B / Esc binding.
- **Back-navigation, generalized.** `HandleCancel()` is now the single place that decides what "cancel"
  means for the *current* step: `Resources`/`ConfirmOverwrite` step back to `Packages`; `Packages` itself
  closes the browser and raises `Cancelled`. Both `_GuiInput` and `_UnhandledInput` route through it
  (belt-and-suspenders, matching `LayerManagerPanel`/`OnScreenKeyboard`'s existing pattern), each guarded
  on the attached keyboard being closed.
- **Theming.** No new theme code was needed: `LevelEditor.Theme = EditorTheme.Build()` (already set) is a
  Godot `Theme` property that cascades to every descendant `Control`, so `PanelContainer`/`Button`/`Label`
  styleboxes already applied automatically once the panel's structure and metadata labels were rebuilt to
  actually use them (dim `TextDim` for metadata/breadcrumbs, `Accent` for the title) — the "grey window"
  complaint in #7500 was about the *content* (a bare list, one string per row, no header) more than a
  missing Theme.

### 16.2 The save flow (state machine)

```
SummonSave(source, currentLevelName)
   │
   ▼
ShowPackages()  — Step.Packages, Mode.Save
   rows: [ "+ New package…" , existing package #1, existing package #2, … ]
   │
   ├─ pick an existing package ──────────────────────────────────────────┐
   │                                                                     │
   └─ pick "+ New package…"                                              │
        │                                                                │
        ▼ keyboard: "New package name" (seed: empty)                    │
      OnNewPackageNameCommitted(name)                                    │
        │                                                                │
        ├─ blank ─────────────────► ShowPackages() (bounce back)         │
        │                                                                │
        ├─ PackageSaveTargetResolver.FindCollision(packages, name)       │
        │    finds a match ──► ShowConfirmOverwrite()                    │
        │                        │  rows: "Overwrite it" / "Choose a     │
        │                        │  different name"                     │
        │                        ├─ Overwrite ───────────────────────────┤
        │                        └─ Choose different ──► ShowPackages()  │
        │                                                                │
        └─ no collision ─► pendingNewPackageName = name                  │
                              │                                          │
                              ▼                                          ▼
                     keyboard: "Level name — saving into “{target}”" (seed: currentLevelName)
                              │
                              ▼
                     OnLevelNameCommitted(name)
                        │
                        ├─ blank ─► ShowPackages() (bounce back)
                        │
                        └─ Close(); SaveRequested(new PackageSaveTarget(
                               existingHandle-or-null, pendingNewPackageName-or-null, name))
```

`PackageSaveTarget` (new, `game/Editor/PackageSaveTarget.cs`) is a small POCO carrying exactly one of
`ExistingHandle`/`NewPackageName` plus the always-set `LevelName`. It is deliberately dumb — no logic, no
Godot dependency — so the browser's job stops at "what did the author pick," and `LevelEditor` alone
decides what to *do* with it.

### 16.3 The collision safety net — `PackageSaveTargetResolver`

New, pure, engine-agnostic type in `src/Uberkarl.Packages/PackageSaveTargetResolver.cs`:
`FindCollision(IReadOnlyList<PackageSummary> packages, string proposedName)` → `PackageHandle?`. Without
it, typing a "+ New package…" name that happens to match an existing package's display name would silently
land on `FolderPackageSource.Create`'s own uniquifying suffix (`name-1.pkg`) — a confusing duplicate next
to the package the author probably meant to overwrite. The resolver folds that into the browser's own
`ConfirmOverwrite` step instead, entirely as a UI decision over data the browser already has from
`ListPackages()` — no additional I/O. Unit-tested in isolation
(`tests/Uberkarl.Packages.Tests/PackageSaveTargetResolverTests.cs`): no match, exact match, case-
insensitive match, whitespace-trimmed match, empty list, blank proposed name, null-list guard.

### 16.4 Level naming — `EditableLevel.Rename` / `LevelEditSession.RenameLevel`

A package built by this editor holds exactly one level (`EditableLevelWriter.ToPackageBytes` writes
`level.Name` straight through as the package manifest's `Name`), so "specify the level name" *is*
"rename the level," and that name is what a future `ListPackages()` will display for the saved package.
`EditableLevel.Name` gained a private setter and a `Rename(string)` mutator (throws on
null/empty, no-ops on an unchanged name — mirrors the existing `RenameLayer` shape exactly).
`LevelEditSession.RenameLevel(string)` wraps it with the same blank-or-whitespace-is-a-no-op guard
`RenameLayer` already uses (the browser passes whatever the keyboard committed straight through) and
marks the session dirty on a real change. `LevelEditor.OnBrowserSaveRequested` calls
`session.RenameLevel(target.LevelName)` *before* serializing, so both the in-package manifest name and
(for a new package) the on-disk file's uniquifying base name reflect the typed name. Unit-tested in
`tests/Uberkarl.Editor.Tests/LevelSaveTests.cs`: rename/no-op/throw at both the model and session layers,
history preservation, and a save→reload round-trip asserting the new name lands in the reloaded manifest.

### 16.5 `LevelEditor` wiring — the FileDialog is gone

- `saveDialog` (`FileDialog`), `BuildFileDialogs()`, and `OnSaveFileSelected` are deleted outright — no
  `FileDialog` remains anywhere in the editor.
- **New** (`InvokeFileCommand(New)` / toolbar **New**): unchanged — `NewLevel()` never touched a dialog.
- **Save** (`Save()`): same branching as before (current package-source handle → overwrite; else a
  legacy direct `currentFilePath` → overwrite that path, unchanged pre-existing behaviour for the
  boot-loaded sample; else …), only the final fallback changed from popping `saveDialog` to
  `SummonSaveBrowser()`.
- **Save As** (`InvokeFileCommand(SaveAs)` / toolbar **Save As**): now unconditionally
  `SummonSaveBrowser()`, which guards on having a session and a writable source, then calls
  `packageBrowser.SummonSave(packageSource, session.Level.Name)`.
- `OnBrowserSaveRequested(PackageSaveTarget)` is the new single landing point for the save flow's result:
  rename the level, then either `WriteToSource` (existing package — reusing the same helper Save's
  overwrite path already uses) or the new `WriteToNewPackage` (calls
  `IWritablePackageSource.Create(target.NewPackageName, bytes)` and adopts the returned handle as the new
  `sourceHandle`, so a subsequent plain Save overwrites it directly without reopening the browser).
- `packageBrowser.AttachKeyboard(textKeyboard)` wires the same shared `OnScreenKeyboard` instance
  `LayerManagerPanel` already uses — one keyboard, summoned by whichever panel currently needs text
  entry, exactly per #7513's reusability goal.

### 16.6 Verification

Driven live via Godot MCP (screenshots + `get_editor_errors`) against a `user://packages` folder seeded
with 2+ multi-level packages:
- **Load:** file-browser-style package list (name + version/item-count) → select → level list (name +
  size) → `ui_cancel`/Back steps from the level list back to the package list, and from there closes;
  select a level → it opens.
- **Save:** edit the loaded level → Save-As → `+ New package…` → type a package name + level name on the
  on-screen keyboard → package is created in `user://packages`; reopen the browser (Load) → the new
  package appears with the typed name → load it back → the edits are present. Save-As into an *existing*
  package overwrites it (reload confirms). Typing a new-package name that collides with an existing
  package's name surfaces the confirm-overwrite step.
- Gamepad (injected `ui_accept`/`ui_cancel` + joypad button events, part-1's method), keyboard, and mouse
  all drive every step. `dotnet build Uberkarl.csproj` 0 warnings / 0 errors; `Uberkarl.Packages.Tests`
  and `Uberkarl.Editor.Tests` green; comment-grep (`TODO`/`FIXME`/`XXX`/`HACK`) 0 across every changed file.
- **Real hardware-pad confirmation remains Toni's** — the harness injects `ui_*` actions and raw
  `InputEventJoypadButton`s; nothing here claims verification on physical hardware.

### 16.7 Still open (unchanged from §14, not addressed by this increment)

- **Q1 — folder location.** `user://packages/` remains the source dir; "a folder next to the executable
  for shareability" is still an open question for Toni, out of scope here (no folder-location code
  changed).
- **Q3/Q4 — resource filter / multi-level packages.** Step 2 still filters to `level`-kind only; a
  package saved by this editor is still exactly one level per package (`EditableLevelWriter` is
  unchanged) — Save/Save-As overwrite the *entire* target package with that single level, same as Part
  1's Save. Building "add a level into an existing multi-level package without clobbering its other
  levels" was explicitly out of scope for this increment (anti-complexity: no tileset/dimensions/
  online-source, and this is the same shape of scope expansion) and is noted here as a candidate
  follow-up if packages are found to routinely hold several levels in practice.

---

*Design authored by Sarah (software architect). Implementation to follow per §15. Part 2 of the Editor
UI v2 arc; additive over the merged part-1 foundation (#7466). §16 (save flow + file-browser UI, #7552)
implemented by John (backend dev), 2026-08-03.*
