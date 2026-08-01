# Architectural Document: Uberkarl Package / Resource Format (Phase 1a)

> **Repo path:** `docs/architecture/package-format.md` (this file).
> **DiVoid:** filed as a `documentation` node linked to task #7408, project #7396 (Uberkarl), vision #7407.
> **Scope of this doc:** the `.pkg` container format + resource identity/reference model + the first implementation increment (Godot-independent C# library `Uberkarl.Packages` + NUnit round-trip tests). Level runtime, editors, music format, dependency *solver*, and online archive are explicitly out of scope.

---

## 1. Problem Statement

Uberkarl (vision #7407) is a data-driven 2D-platformer **meta-engine**: levels, music, sprites, and behavior are all portable data + sandboxed script, never hand-authored Godot scenes. **Community content is the product**, so everything optimizes for simple-but-powerful authoring and frictionless packaging/sharing.

The package format is the base of it all. Every asset — a level, a music track, a sprite, a behavior script — is bundled into and loaded from a package. This document settles *how a package is structured, versioned, licensed, and — the hard part — how a resource inside one package is referenced from another package without identifier collisions across arbitrary third-party content, given there is no backend and no central registry.*

Toni's framing (verbatim, the north star for this design — do not drift from these words):

> "the package format is the base of it all - start with that. It needs to be simple, yet support versioning/licencing (licencing could just be a resource in it), and contents need to be referenced somehow when used - so like resource path in a package or whatever - so resources should have some identifier which also doesn't conflict with arbitrary other packages. Resource paths, package itself needs to be referencable (not sure whether we just say, pay attention to the same because conflicts or whether we introduce something like uuid - let sarah do something)."

**Success criteria.** A package can be written to a single `.pkg` file and read back on another machine; a resource can be resolved by a reference that is unique across arbitrary independently-authored packages; the package itself is referenceable so packages can depend on each other's resources; licensing/attribution is baked in from day one as a first-class (resource-shaped) concern; the model is simple, and the concrete identity strategy is swappable so ratifying a different scheme later does not invalidate the format.

## 2. Scope & Non-Scope

**In scope**
- The `.pkg` container mechanism and its internal layout.
- The manifest: format version, package identity, package version, resource index, dependency list, attribution/licensing metadata.
- The resource model: typed (kinded) resources, addressed by an in-package resource path.
- The **identity scheme** decision (the delegated key call): how a package and a resource are named such that cross-package references never collide — analyzed and **recommended** here for Toni to ratify.
- The **reference seam**: an abstract reference type + a resolver interface, so intra-package resolution works now and cross-package/dependency resolution can be layered on later without reshaping the format.
- First implementation increment: model/types + reader + writer + a round-trip test (write → read → resolve by reference), as a Godot-independent, unit-testable C# library.

**Explicitly NOT in scope (YAGNI — do not build)**
- The level runtime (Phase 1b) and any consumer of the format.
- Editors (level/music/sprite), the music/tracker format, the online archive/backend.
- A **dependency-resolution / version-constraint solver.** We design the seam so a resolver can be added; we do not build one. Dependencies are recorded, not resolved.
- Compression tuning, encryption/signing, delta/patch packaging, streaming-partial-load optimization. Not asked for; not now.
- Any identity feature beyond what the one ratifiable decision below requires (no reverse-domain registry, no provenance graph — a single optional `forkedFrom` field is the only nod to remix provenance, and it is inert metadata).

## 3. Assumptions & Constraints

| # | Assumption / Constraint | Confidence |
|---|---|---|
| A1 | Desktop-only, C#/.NET stack (Godot .NET + AudioSynth + Pooscript). Library targets `net8.0` to match the Godot 4.7.1 project. | High (locked, #7407) |
| A2 | File-based distribution first: a self-contained `.pkg` is exported and handed to someone; no backend, no network, no central registry at resolve time. | High (locked, #7407) |
| A3 | Packages come from **mutually untrusting, uncoordinated third parties.** Identity collision-avoidance cannot rely on a central authority or on authors coordinating. | High |
| A4 | Resources are arbitrary binary blobs (a sprite PNG, a compiled/text script, a track definition). The format is payload-agnostic; it does not parse resource contents. | High |
| A5 | The set of asset kinds (level/track/sprite/script/license) will grow. Kind must be extensible **without a format/code change** (open vocabulary). | High (#7407 "extensible") |
| A6 | Licensing is "just a resource" (Toni). The format must let a license live as a normal resource, and must carry attribution metadata pointing at it. | High |
| A7 | The Godot project at repo root globs `**/*.cs`; the new library sources must be excluded from its compile set to avoid double-compilation. | High (verified) |
| A8 | Human authors will read and hand-edit dependency references and resource paths during authoring (remix culture). Readability has real product value, not just aesthetics. | Medium-High |

## 4. Architectural Overview

A **package** is a single `.pkg` file — a **ZIP archive** with a known internal layout — containing exactly one **manifest** plus a set of **typed resource payloads**.

```
  foo.pkg  (ZIP archive)
  ├── manifest.json                 the one manifest: identity, versions, resource index,
  │                                 dependencies, attribution/licensing
  └── resources/                    opaque resource payloads, one zip entry per resource
      ├── license                   a resource of kind "license" (licensing = just a resource)
      ├── sprites/hero              a resource of kind "sprite"
      ├── tracks/level1-theme       a resource of kind "track"
      └── scripts/hero-behavior     a resource of kind "script"
```

The manifest is the single source of truth for *what is in the package and how to address it*; the `resources/` tree is the payload store. A reader opens the archive, parses the manifest, and can then hand back any resource's bytes given a **reference**.

The **reference model** is the crux:

```
  ResourceReference  =  PackageId   +   ResourcePath
                        └─ globally      └─ unique within
                           unique           its package
                           (identity)       (human-readable)

  Intra-package ref:  PackageId = "self"  → resolves inside the current package
  Cross-package ref:  PackageId = <the target package's identity>
                      → resolved via a resolver that knows other opened packages
```

Major components (all in the `Uberkarl.Packages` library):

| Component | Role (one responsibility) |
|---|---|
| **Identity & reference types** (`PackageId`, `ResourcePath`, `ResourceReference`) | Value types that *are* the identity scheme. The concrete strategy hides behind `PackageId`, making it swappable. |
| **Manifest model** (`PackageManifest`, `ResourceEntry`, `Attribution`, `PackageDependency`) | The in-memory shape of `manifest.json`. Pure data. |
| **Package (read model)** | An opened package: its manifest + access to resource payloads. Resolves *intra-package* references directly. |
| **PackageReader** | Opens a `.pkg` (ZIP), validates format version, parses the manifest, exposes a `Package`. |
| **PackageBuilder + PackageWriter** | Accumulates resources + metadata (incl. a license resource + versions) and writes a well-formed `.pkg`. |
| **IResourceResolver / PackageRegistry** | The seam: resolve *any* `ResourceReference`, including cross-package, against a set of opened packages. The future dependency-solver plugs in here — not built now. |

## 5. Components & Responsibilities

**Identity & reference types**
- `PackageId` — the globally-unique identity of a package. Owns: equality, string round-tripping, the notion of "self" for intra-package references. Does NOT own: human display name, versioning, discovery. Internally a UUID (see §10) but callers treat it opaquely, so the representation is swappable.
- `ResourcePath` — a validated, slash-delimited, human-readable path unique *within* one package (`sprites/hero`). Owns: normalization and validation (non-empty, no `..`, no leading `/`, no backslash, no control chars) so it is both collision-checkable and ZIP-safe. Does NOT own: global uniqueness (the package scopes it).
- `ResourceReference` — the pair `(PackageId, ResourcePath)`; the *only* way anything points at a resource. Owns: the distinction between self-package and cross-package references. Does NOT own: resolution (that is the resolver's job) — a reference is inert until resolved.

**Manifest model**
- `PackageManifest` — format version, package id, package version, human name/author, attribution, the resource index, the dependency list. It does NOT own payload bytes.
- `ResourceEntry` — one row of the index: resource path, kind, media type, byte length, optional per-resource attribution. Does NOT own the bytes (they live in the archive; the entry points at them).
- `Attribution` — author, license (SPDX-ish id or free text), optional pointer to a license *resource* in this package, optional source URL, optional notes. Present at package level and optionally per-resource.
- `PackageDependency` — a reference to another package: its `PackageId`, an informational human name, and an informational version constraint string. Recorded, **not resolved** (no solver).

**Read/write/resolve**
- `Package` — the opened read model. Resolves intra-package references and streams/returns resource bytes. Owns nothing about *other* packages.
- `PackageReader` — turns a `.pkg` file/stream into a `Package`; enforces format-version compatibility and structural validity; guards against ZIP-slip. Read-only.
- `PackageBuilder` — the authoring API: set identity/version/attribution, add resources (bytes + kind + path), add a license resource. Mints a fresh `PackageId` if none supplied. Validates uniqueness of resource paths.
- `PackageWriter` — serializes a built package to a `.pkg` (ZIP + `manifest.json`). Deterministic output (stable entry order) so packages diff cleanly.
- `IResourceResolver` — abstract resolution of any `ResourceReference`. `PackageRegistry` is the concrete in-memory implementation that holds a set of opened `Package`s keyed by `PackageId` and resolves both intra- and cross-package references. The eventual dependency solver is a richer `IResourceResolver`; the format and callers do not change when it arrives.

## 6. Interactions & Data Flow

**Write (packing) flow**
1. Author creates a `PackageBuilder`, sets human name/version/author/license and (optionally) an explicit `PackageId`; otherwise the builder mints one.
2. Author adds resources: for each, supplies kind + resource path + payload bytes (+ optional per-resource attribution). At least one `license`-kind resource is added, and package attribution may point at it.
3. `PackageWriter` writes `manifest.json` and one `resources/<path>` entry per resource into a ZIP, producing the `.pkg`.

**Read + resolve flow**
1. `PackageReader.Open(path)` opens the ZIP, reads `manifest.json`, checks `formatVersion`, validates structure, returns a `Package`.
2. Caller resolves a resource by `ResourceReference`:
   - **Self reference** (`PackageId == self`): the `Package` looks up the `ResourceEntry` by path and returns the bytes from its own archive.
   - **Cross-package reference**: the caller goes through a `PackageRegistry` holding the referenced package too; the registry dispatches to the owning `Package`. If the owning package is not loaded, resolution fails with a clear "unresolved dependency" error (the *point* where a future solver would fetch/choose a version).

Communication is entirely **synchronous, in-process, file-backed** — no queues, no protocols, no services. That is the correct altitude for a file format library.

## 7. Data Model (Conceptual)

```
Package
 ├─ manifest : PackageManifest
 │   ├─ formatVersion : int              (the .pkg schema version; reader-enforced)
 │   ├─ id            : PackageId        (globally-unique identity — the reference target)
 │   ├─ version       : string           (semver-ish content version; human + dependency use)
 │   ├─ name          : string           (human display name; NOT identity)
 │   ├─ attribution   : Attribution      (package-level author/license, may point at a license resource)
 │   ├─ forkedFrom    : PackageId?        (optional inert provenance for remix/fork)
 │   ├─ resources     : ResourceEntry[]  (the index)
 │   │   └─ ResourceEntry
 │   │       ├─ path        : ResourcePath   (unique within package; human-readable)
 │   │       ├─ kind        : string          (open vocabulary: level/track/sprite/script/license/…)
 │   │       ├─ mediaType   : string          (e.g. image/png, text/plain; payload-agnostic hint)
 │   │       ├─ byteLength  : long
 │   │       └─ attribution : Attribution?    (optional per-resource override)
 │   └─ dependencies  : PackageDependency[]   (recorded, not resolved)
 │       └─ PackageDependency { id : PackageId, name : string, version : string }
 └─ payloads : (ResourcePath → bytes)   physically the resources/ tree in the ZIP
```

Ownership: the **package owns its resources and their identities within itself**; it does **not** own the resources of packages it depends on (it only holds *references* to them). Identity (`PackageId`) is minted once at creation and immutable; a fork mints a *new* id (and may record `forkedFrom`).

## 8. Contracts & Interfaces (Abstract)

| Interface / Type | Input | Output | Semantics & Invariants |
|---|---|---|---|
| `PackageReader.Open` | a `.pkg` path or stream | an opened `Package` | Fails cleanly on: unknown/newer `formatVersion`, missing/invalid manifest, ZIP-slip entry, a manifest entry whose payload is absent. Never partially exposes a malformed package. |
| `Package.Resolve` (self ref) | a `ResourceReference` whose id is this package | resource bytes/stream + its `ResourceEntry` | Reference path must exist; unknown path → clear not-found error. |
| `IResourceResolver.Resolve` | any `ResourceReference` | resource bytes/stream + entry | Self and cross-package uniform. Cross-package where the target package is not registered → "unresolved reference" (the solver seam). Two references are equal iff `(PackageId, ResourcePath)` are equal. |
| `PackageBuilder.AddResource` | kind, resource path, payload bytes, optional attribution | (mutates builder) | Rejects a duplicate resource path within the package (the in-package uniqueness invariant). Rejects invalid paths. |
| `PackageBuilder.AddLicense` | a license id/text + payload bytes | (mutates builder) | Convenience that adds a `license`-kind resource and, if unset, points package attribution at it. Licensing is a resource, per Toni. |
| `PackageWriter.Write` | a built package, a destination | a `.pkg` | Output is deterministic (stable ordering) and self-describing; re-reading it yields an equal manifest. |

**Core invariant (the whole point):** a `ResourceReference` is globally unambiguous because `PackageId` is globally unique (identity) and `ResourcePath` is unique within that package (scoped human name). No two independently-authored packages can produce colliding references, with no coordination and no registry.

## 9. Cross-Cutting Concerns

- **Identity collision-safety** — the central concern; addressed by the identity scheme (§10) — UUID-backed `PackageId` gives a hard, registry-free guarantee.
- **Untrusted input hardening** — packages come from strangers. The reader validates format version, rejects ZIP-slip (`..`/absolute entries), and treats resource payloads as opaque bytes it never executes or parses. (Script *execution* safety is Pooscript's watchdog concern, #7407 — out of scope here; this format only carries the bytes.)
- **Error handling** — a dedicated `PackageFormatException` for malformed/incompatible packages and a distinct unresolved-reference failure, so callers can tell "bad file" from "missing dependency." No silent fallback.
- **Versioning / forward-compat** — `formatVersion` is checked on read; a newer major is refused rather than mis-parsed. `packageVersion` is content-level and carried through references for the future solver.
- **Determinism** — writer emits stable ordering so `.pkg`s are reproducible and diffable (matters for remix/version-control workflows).
- **Observability** — errors carry the offending path/id. No logging framework is imposed on a pure library.
- **Concurrency** — a `Package`/`PackageReader` is read-only after open and safe to read concurrently; the builder is single-threaded authoring state. No shared mutable global state.

## 10. Quality Attributes & Trade-offs — the two decisions

### 10.1 Container mechanism: **ZIP** (recommended, and used in the increment)

`.pkg` is a ZIP archive with a fixed internal layout. Rationale (KISS): ZIP is in the .NET BCL (`System.IO.Compression`) with **zero third-party dependency**, gives per-entry random access via its central directory (resolve one resource without inflating the whole package), optional compression, and is trivially inspectable (rename to `.zip`, unzip, read `manifest.json`). 

| Alternative | Why rejected |
|---|---|
| Custom binary container (TLV / TAR-like) | More code, more bugs, custom tooling, no benefit over ZIP. Violates KISS. |
| Single JSON with base64-embedded payloads | Inflates binary assets ~33%, forces whole-package load into memory, no random access. |
| A DB file (SQLite) | Heavy dependency and a query engine for a problem that is "a bag of named blobs + a manifest." YAGNI. |

Trade-off accepted: ZIP's own per-entry compression/central-directory overhead is negligible next to its ubiquity and tooling. The container is abstracted behind reader/writer, so it could be swapped without touching the model or references.

### 10.2 Identity scheme: **Hybrid — UUID identity + human-readable name/path** (RECOMMENDED; Toni ratifies)

This is the delegated key decision (task #7408). Three candidates:

| Scheme | Collision-safety (no registry) | Human-readability | Remix/fork ergonomics | Cross-package dep refs |
|---|---|---|---|---|
| **A. Human-readable namespaced names** (reverse-domain / `author/package`) | ❌ relies on social convention; two hobbyists both pick `com.cool.pack` and silently collide | ✅ excellent | ⚠️ fork ambiguity: is `com.cool.pack` v2 by a forker the *same* identity? | ✅ readable but ambiguous under collision |
| **B. Pure UUIDs** for package *and* resources | ✅ 128-bit, collision-proof by construction | ❌ opaque; a level's dependency list is unreadable | ✅ fork = new UUID, cleanly distinct | ✅ unambiguous but unreadable |
| **C. Hybrid (recommended)**: package identity = **UUID**; package also carries human name/version/author; resources addressed by a **human-readable `ResourcePath` unique within the package** | ✅ UUID gives the hard guarantee with no backend | ✅ names + paths stay readable for everything humans touch | ✅ fork = mint new UUID (+ optional `forkedFrom`), unambiguous *and* keeps a friendly name | ✅ ref = `UUID + readable path`: unambiguous **and** legible |

**Recommendation: C (Hybrid).** It directly answers Toni's open question ("same-name-conflicts vs uuid") by using **both, at different layers**:
- **UUID for the hard identity** of a package — the thing a `ResourceReference`/dependency points at. Collision-proof without a central authority, which is mandatory given "arbitrary third-party packages, no backend" (A2, A3). Minted once at creation, immutable.
- **Human-readable everywhere humans work** — package `name`/`version`/`author` for display and discovery (not identity, so two packages may share a display name harmlessly), and resource **paths** (`sprites/hero`) that are unique only *within* their package, which the package's UUID scopes. So a full reference reads as "package `a3f2…` (‘Forest Pack’) → `sprites/hero`": the machine part is exact, the human part is legible.
- **Fork/remix**: copying a package and minting a new UUID makes the fork a distinct identity cleanly (no "is this the same pack?" ambiguity that scheme A suffers), while an optional inert `forkedFrom: <origin UUID>` preserves provenance for remix culture — without building any provenance system now.

Why not A alone: it pushes collision-avoidance onto a registry/social convention we deliberately don't have; for untrusted uncoordinated authors that is a real, silent hazard. Why not B alone: it sacrifices the readability that the "frictionless authoring/sharing" product pillar depends on.

**Swappability guarantee (so ratification can't invalidate the increment):** `PackageId` is a dedicated value type and callers treat it opaquely; the concrete representation (UUID today) lives *inside* it. If Toni later wants, say, an *additional* reverse-domain slug as an alternate address, it slots into `PackageId`/the manifest without reshaping `ResourceReference`, the reader/writer, or any consumer. The increment commits to the *seam*, not irreversibly to the representation.

### 10.3 Other quality attributes
- **Scalability** — per-entry random access means resolve cost is O(1)-ish in package size; a package with thousands of resources loads its manifest, not its payloads.
- **Maintainability** — model is pure data; container, identity representation, and resolution are each behind a seam.
- **Simplicity** — no solver, no compression tuning, no crypto: the smallest thing that satisfies the requirement and does not foreclose the follow-ups.

## 11. Risks & Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| UUIDs make dependency lists unreadable in raw form | Authoring friction | Human `name`/`version` carried alongside every dependency and package; tools/editors show the friendly layer, UUID stays under it. |
| ZIP-slip / malicious entry paths from untrusted `.pkg` | Path traversal on read | Reader rejects entries with `..`/absolute/backslash; `ResourcePath` validation enforces the same on write. |
| Two forks diverge but share content paths | Ambiguous references | Distinct package UUIDs make forks distinct identities; `forkedFrom` records lineage without merging identity. |
| Format evolves and old readers choke | Broken imports | `formatVersion` checked on read; newer-major refused explicitly rather than mis-read. |
| Temptation to build the dependency solver now | Scope creep, KISS violation | Dependencies are *recorded only*; resolution is an `IResourceResolver` seam with an in-memory impl. The solver is a later, drop-in resolver. |

## 12. Migration / Rollout Strategy

Greenfield — no migration. Rollout is additive:
- The library lands as a standalone project pair (`src/Uberkarl.Packages` + `tests/Uberkarl.Packages.Tests`). It has no Godot dependency and is unit-tested without a Godot runtime (`dotnet test tests/Uberkarl.Packages.Tests`). No root solution file is added on this branch, to avoid a merge collision with the game's own solution that lands on a separate branch.
- **Required follow-up when the root Godot `Uberkarl.csproj` lands** (it is not on `main` yet — the game/AudioSynth work is on an unmerged branch): that project globs `**/*.cs`, so it must exclude the library from its compile set — `<Compile Remove="src/**/*.cs" />` and `<Compile Remove="tests/**/*.cs" />` — otherwise the game build double-compiles the library and pulls in the test project's NUnit reference (A7). This is a one-line guard on the game csproj, tracked as a merge-integration step.
- Phase 1b (level runtime) consumes the library via a normal project reference; nothing in this format needs to change for that.

## 13. Open Questions

1. **Identity scheme ratification (the one real decision).** This doc recommends **Hybrid: UUID identity + human-readable name/path** (§10.2). Toni to ratify or redirect. The increment is built on the swappable `PackageId` seam so a redirect (e.g. add reverse-domain slugs) does not invalidate it.

*(Per KISS/YAGNI and the task brief, no further speculative design questions are raised — dependency-solving, signing, compression policy, and the kind vocabulary are deliberately deferred until a consumer needs them.)*

## 14. Implementation Guidance for the Next Agent

Ordered milestones (all at the architectural-unit level; the first two ship in *this* increment):

1. **Identity & model types** — `PackageId` (UUID-backed, opaque, `Self` sentinel), `ResourcePath` (validated), `ResourceReference`, `ResourceKind` constants, `Attribution`, `PackageDependency`, `ResourceEntry`, `PackageManifest`. Pure data; no I/O.
2. **Reader + writer + resolver + round-trip test** — `PackageBuilder`/`PackageWriter` (ZIP + `manifest.json`, deterministic), `PackageReader`/`Package` (validated open, intra-package resolve), `IResourceResolver`/`PackageRegistry` (uniform self + cross-package resolve). Prove with NUnit: write → read → resolve by reference (incl. a license resource, versions, and a cross-package reference).
3. *(later, Phase 1b+)* Level runtime consumes the library; kinds get typed loaders per asset.
4. *(later)* A CLI packer reuses the same library to build `.pkg`s from a folder.
5. *(later, only when a consumer needs it)* A real `IResourceResolver` that does dependency resolution / version-constraint solving — dropped into the existing seam.

---

*Design authored by Sarah (software architect). Identity-scheme recommendation (§10.2) is pending Toni's ratification; everything else is a committed design for Phase 1a.*
