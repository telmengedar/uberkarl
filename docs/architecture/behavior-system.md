# Architectural Document: Uberkarl Behavior System (Pooscript reactive-behavior layer)

*Design pass by Sarah (software architect), 2026-08-05. Source task DiVoid #7703 · vision #7407 · Pooscript language/sandbox #2946 · cancellation watchdog #7409 · package format #7413 · package-VFS #7572 · level model #7420 · play runtime #7418 · project #7396.*

**Design-only. No code, no PR.** This document is the blueprint an implementer follows. It defines the model, the event/facade surface, the safety story, the runtime execution model, the editor authoring model, a phased build plan (each phase a shippable PR), and the decisions Toni must ratify before build starts.

---

## 0. Toni's vision (verbatim, from #7407 / #7703)

> - **Scripted tiles:** grid-fixed; optional script on **contact / contact-leave**; a tile-level **default script** (spikes always hurt) that's **overridable/removable per instance**. Limited unless the script mutates level structure.
> - **Objects (key new primitive):** **placed on the grid** like tiles but **free-moving at runtime**; scriptable (`moveTo` / move another object / change state) → moving platforms, jumping question-block, etc.
> - **Area triggers:** a **grid rect** firing **on-enter / on-leave**.
> - **Level script:** a **global** behavior script per level.
> - **Action vocabulary:** curated Pooscript facade (`moveTo` / move / setState / hurt / spawn / …) — 'behavior in a few words'.
>
> *"the main thing is for you to get a picture of the idea."*

This is the meta-engine core: it is what makes levels **react**, turning a level editor into a data-driven game engine.

---

## 1. Problem Statement

Uberkarl is a data-driven 2D-platformer **meta-engine** (#7407): levels, sprites, music, and *behavior* are all portable, sandboxed data interpreted at runtime — never hand-authored Godot scenes. The spine (package format, level model, playable runtime) already exists and works (PRs #2/#4/#5, nodes #7413/#7418/#7420). What is missing is the layer that makes a level **do** anything beyond "player walks on static tiles": reactive behavior.

**Goal:** design a behavior layer where an author attaches **Pooscript** behavior to four kinds of subject — **scripted tiles**, **free-moving objects**, **area triggers**, and a **level-global script** — reacting to engine events (contact, area enter/leave, per-frame update, timers, lifecycle) through a **narrow curated facade** ("behavior in a few words"). Because **community content is the product** and scripts are **untrusted**, the layer must be **incapable of freezing the game** no matter how malicious or buggy a shared script is.

**Success criteria:**

1. An author can attach a curated behavior to a tile / object / trigger / level and see the level react when played.
2. A behavior script can only touch the facade the host hands it — never reflection, file, network, arbitrary `new`, or the raw Godot tree (this is *already free* from Pooscript's allow-list sandbox — #2946).
3. A runaway or hostile script **cannot freeze the engine** — a watchdog cancels or abandons it within a bounded budget, the frame keeps ticking.
4. The whole model is **engine-agnostic in `src/`** (unit-tested, Godot-free) with a thin Godot glue layer in `game/`, matching the established repo architecture (§3).
5. Predefined behaviors are **gamepad-authorable**; only free-text scripting requires a keyboard.

---

## 2. Scope & Non-Scope

**In scope (this design):**

- The behavior/entity model for scripted tiles, objects, area triggers, and the level script — storage (JSON + Pooscript text as package resources) and authoring shape.
- The event model (which events exist, how they dispatch).
- The Pooscript **facade / action vocabulary** — both the "few words" surface and the capability boundary.
- The **safety architecture** (capability sandbox + interruption watchdog + single-thread mutation contract + determinism posture), including the hard dependency on #7409 and an interim that ships without it.
- The **runtime execution model** in the play runtime, including how free-moving objects integrate with the grid level + physics/collision.
- The **editor authoring model** (place objects/triggers; assign predefined behaviors on gamepad; edit free-text scripts on keyboard; tile default-script + per-instance override UX).
- A phased build plan and the decisions Toni must ratify.

**Out of scope (explicitly):**

- Writing any code, Pooscript grammar changes, or the actual facade method list beyond a *representative* vocabulary (the exact list is a Phase-1 deliverable Toni ratifies).
- The Pooscript engine fixes themselves (#7409) — that is a separate Pooscript-repo task; this design *consumes* it and specifies the interim.
- Music, sprite editor, online archive, multiplayer (other vision pillars).
- A full visual scripting / node-graph editor (predefined-template assignment is the gamepad path; a node graph is a possible far-future evolution, not designed here).
- Replay/ghost/networked determinism *guarantees* — the design keeps the door open (deterministic scheduler + intent buffer) but the global determinism verdict is blocked by the still-open "Godot physics vs custom stepper" question in #7407 and is not resolved here.

---

## 3. Assumptions & Constraints

**Grounded in the codebase (read, not assumed):**

- **C-1 — The engine-agnostic-core / Godot-glue split is load-bearing.** `src/Uberkarl.Content`, `src/Uberkarl.Packages`, `src/Uberkarl.Editor` are plain `net8.0` libraries with **no Godot dependency**, unit-tested with NUnit; `game/` holds the Godot glue (partial classes over Godot nodes). The behavior system MUST follow this — a new `Uberkarl.Behavior` core library (Godot-free) + `game/Behavior` glue.
- **C-2 — The established content pipeline is `Definition → Resolved → Builder`.** `LevelDefinition` (JSON-friendly, `init`-only, carries `ResourceReference`s) → `LevelLoader.Load(resolver, ref)` → `ResolvedLevel` (fully resolved, payload bytes inlined, Godot-free) → `TileMapLevelBuilder` / `PlayRuntimeBuilder` (Godot-side). Behavior definitions follow the same three-stage shape.
- **C-3 — Packages are a ZIP VFS of typed resources** (#7413/#7572). `ResourceKind` already includes `script = "script"`. A `ResourceReference` = `PackageId` + `ResourcePath`. Resolution flows through `IResourceResolver`. Scripts and (new) object-set definitions become package resources.
- **C-4 — There is ONE shared play runtime.** `PlayRuntimeBuilder.Populate(root, ResolvedLevel)` is used by both `LevelPlay` (standalone) and the editor's `PlaytestOverlay`. Behavior must plug into this single builder so playtesting and standalone play are identical.
- **C-5 — The player is a `CharacterBody2D`** reading the global `Input` singleton in `_PhysicsProcess`; physics constants (`MoveSpeed`/`JumpSpeed`/`Gravity`) are `[Export]` seams already earmarked "per-level and script-driven overrides later."
- **C-6 — The editor is a session-façade + command/history architecture.** `LevelEditSession` owns an `EditableLevel` + `EditHistory`; the UI issues intent calls and never mutates the model directly. Placement is grid-cell based via `GridCursor`. Gamepad + on-screen-keyboard input exists (`PopInMenu`, `RadialMenuModel`, `SteppedValueEditor`, `TextEntryEditor`, `OnScreenKeyboard`). Save uses the package-VFS merge-writer (`BuildContributions` → `Compose`), never clobbering siblings.
- **C-7 — Desktop-only, forever-ish** (#7407 locked decision 1: Windows/Linux/macOS, web out). **A physical keyboard is therefore always available** — this materially eases the "free-text scripting needs a keyboard" constraint: the on-screen keyboard is a gamepad fallback, not the only path.

**Pooscript constraints (from #2946 / #7409):**

- **C-8 — Allow-list sandbox is already free.** A script only ever touches host-bound facade objects; no reflection/file/net/`new`/static members. Widening = registering a type/var; restricting = registering nothing. **But once an object is exposed the script reaches its *whole public instance graph*** — so facades must be *narrow, purpose-built* objects, never the live Godot node.
- **C-9 — No built-in execution timeout; `wait` ignores the cancellation token (#2899); loop CT-coverage is incomplete/unverified (#7409 is OPEN as of 2026-08-05).** This is the single most load-bearing constraint on the safety design (§8).
- **C-10 — `ExecuteAsync(vars, ct)` is the interruption seam;** `foreach` checks CT per iteration today, `while`/`for` unverified, `wait` blocks. Cancellation is *cooperative* — the interpreter must observe the token; the host cannot safely preempt a thread mid-mutation.
- **C-11 — Two execution paths:** the **interpreter** (`Execute`/`ExecuteAsync`) supports the full surface; the **compiled delegate** path (`ParseDelegate`) is faster but a subset (no lambdas/`new`/`wait`/etc.). Assume the interpreter; the compiled path is a possible future hot-path optimization only.

**Flagged assumptions (Toni to confirm — see §14):**

- **A-1** Objects are a *new resource kind* (`objectset`) mirroring the tileset pattern, with placements stored in the level. (Alternative: inline object defs in the level.)
- **A-2** Behavior scripts run **on the main thread**, synchronously, once per frame in a dedicated behavior phase, with a **cooperative** watchdog — *conditional on #7409 landing*. Interim posture in §8.4.
- **A-3** `wait`/blocking sleeps are **banned from behavior scripts**; timing is host-driven (scheduled callbacks), sidestepping the `wait`-ignores-CT defect entirely.

---

## 4. Architectural Overview

The behavior system is a new **engine-agnostic core** (`Uberkarl.Behavior`) plus a **Godot glue** layer (`game/Behavior`), slotted into the existing content pipeline and the single shared play runtime.

```
 AUTHORING (editor)                     STORAGE (package VFS)                 RUNTIME (play)
 ┌──────────────────────┐               ┌───────────────────────┐            ┌───────────────────────────────┐
 │ LevelEditSession     │  writes       │ .pkg (ZIP)            │  load      │ PlayRuntimeBuilder.Populate    │
 │  + BehaviorEditIntents│──resources──▶ │  manifest.json        │──resolve──▶│  builds world + BehaviorRuntime│
 │ Predefined library    │               │  levels/<slug>.json   │            │                               │
 │ Free-text editor      │               │   ├ objects[]         │            │  ┌─────────────────────────┐  │
 └──────────────────────┘               │   ├ triggers[]        │            │  │ BehaviorRuntime (core)  │  │
                                         │   ├ tileScriptOverrides            │  │  event bus + scheduler  │  │
 CORE (src/, Godot-free)                 │   └ levelScript ref   │            │  │  watchdog + intent buffer│  │
 ┌──────────────────────┐               │  objectsets/<slug>.json            │  └───────────┬─────────────┘  │
 │ Uberkarl.Behavior     │               │  scripts/<name>.poo   │            │              │ intents        │
 │  *Definition models   │◀──parsed by── │  (Pooscript text)     │            │  ┌───────────▼─────────────┐  │
 │  Resolved* models     │  LevelLoader  └───────────────────────┘            │  │ Godot glue: applies      │  │
 │  IBehaviorHost facade │                                                    │  │ intents to bodies/tiles  │  │
 │  contracts (interfaces)│                                                   │  │ Player, ObjectBody nodes │  │
 │  BehaviorScheduler     │                                                   │  └─────────────────────────┘  │
 │  Watchdog wrapper      │                                                   └───────────────────────────────┘
 └──────────────────────┘
```

**Major components:**

| Component | Layer | Role |
|---|---|---|
| **Behavior definition models** | `Uberkarl.Behavior` (core) | JSON-friendly `init`-only records: `ObjectSetDefinition`/`ObjectDefinition`, `ObjectPlacement`, `AreaTriggerDefinition`, tile-script binding, level-script binding. |
| **Behavior resolved models** | core | Fully-resolved counterparts (script text inlined, refs resolved) hung off `ResolvedLevel`. |
| **Facade contracts (`IBehaviorHost` & friends)** | core | The *interfaces* the curated facade exposes to scripts — the capability boundary, defined Godot-free so scripts + tests never need Godot. |
| **Behavior compiler/loader** | core | Parses Pooscript text once, resolves handler entry points, produces reusable `CompiledBehavior` values. |
| **BehaviorScheduler + event bus** | core | Owns per-entity behavior instances, dispatches events, runs the per-frame behavior phase in deterministic order, collects intents. |
| **Watchdog** | core | Wraps every script invocation in a time/instruction budget over `ExecuteAsync`+CT; on breach cancels (or, interim, abandons) and quarantines the offending behavior. |
| **Intent buffer + intent types** | core | Scripts never mutate directly; the facade records *intents* (`MoveTo`, `SetState`, `Hurt`, `Spawn`, …). The host applies them on the main thread after scripts run. This IS the single-thread mutation contract. |
| **Predefined behavior library** | core | Curated, first-party, parameterized behaviors addressable by stable id — the gamepad-authorable, safety-audited set. |
| **Godot behavior glue** | `game/Behavior` | Builds runtime object bodies, feeds engine collision/enter/leave events into the bus, applies intents to Godot nodes (`Player`, object bodies, tilemap). |

---

## 5. Components & Responsibilities

### 5.1 `Uberkarl.Behavior` (new core library, Godot-free)

**Owns:** the behavior definition + resolved models, the facade *contracts* (interfaces), the Pooscript compile/load pipeline, the scheduler/event-bus, the watchdog, the intent model, and the predefined library. **Does NOT own:** anything Godot (nodes, physics, rendering, input), file IO, or package ZIP mechanics (that stays in `Uberkarl.Packages`).

Single responsibility framing: *"given a resolved level's behavior data and a host that implements the facade contracts, decide what should happen each frame and in response to each event, and emit intents — without ever touching an engine."*

### 5.2 Behavior definition models (core)

- **`ObjectSetDefinition`** (a new `objectset` resource, mirrors `TileSetDefinition`): a named set of `ObjectDefinition`s. **Owns:** reusable object *types*. **Does not own:** placements (those live in the level).
- **`ObjectDefinition`**: id, name, graphic/sprite reference, **collision role** (§9.4: `solid` / `platform` / `passthrough` / `trigger`), a **default behavior binding** (script ref or predefined id), a **default state** (initial key/value map), and physics hints (free-moving, gravity-affected?). **Owns:** the *type-level* defaults. **Does not own:** per-instance overrides.
- **`ObjectPlacement`** (in the level): object-def reference + **grid cell** + instance name + optional **behavior override** (add / replace / remove relative to the type default) + **initial-state overrides**. **Owns:** where an instance sits and how it differs from its type.
- **`AreaTriggerDefinition`** (in the level): a **grid rect** (x, y, w, h in cells) + a behavior binding + name. **Owns:** the region and its enter/leave behavior.
- **Tile-script binding:** `TileDefinition` (in the tileset) gains an optional **default behavior binding** (contact / contact-leave). The **level** carries a sparse **tile-script-override map** keyed by (layerIndex, cell) → { override binding | *removed* } — this expresses "spikes always hurt by default, but this one spike is inert / does something else." **Owns:** tile-level defaults live on the tileset; per-instance deltas live on the level.
- **Level-script binding:** the level gains an optional `levelScript` behavior binding (global). **Owns:** the level-global reactive script.

A **behavior binding** is the small shared value all four subjects use: either a `ResourceReference` to a `script`-kind Pooscript resource, **or** a `{ predefinedId, params }` pair referencing the predefined library. This uniformity is what lets the same authoring + runtime paths serve tiles, objects, triggers, and the level.

### 5.3 Behavior compile/load pipeline (core)

**Owns:** turning Pooscript text (or a predefined template + params) into a reusable **`CompiledBehavior`** — parsed once at level load, carrying the discovered **event handlers** (§7). **Does not own:** execution timing (the scheduler) or the sandbox policy object graph (the host provides that per invocation).

### 5.4 BehaviorScheduler + event bus (core)

**Owns:** the registry of live **behavior instances** (one per scripted entity: tile-instance, object, trigger, level), the mapping event→handler, the **per-frame behavior phase** (deterministic iteration order), and **intent collection**. **Does not own:** *producing* the raw engine events (collision, enter/leave, delta) — those are pushed in by the glue — nor *applying* intents (the glue does that).

### 5.5 Watchdog (core)

**Owns:** enforcing a per-invocation and per-frame **budget** (wall-clock and/or instruction count) via `ExecuteAsync` + CT; **quarantining** a behavior that breaches budget (disable it, log once, keep the game running). **Does not own:** the Pooscript CT fixes themselves (#7409) — it *depends on* them and degrades gracefully per §8.4.

### 5.6 Intent buffer + intent types (core)

**Owns:** the closed set of mutation *intents* a script can request (`MoveTo`, `MoveBy`, `SetState`, `Hurt`, `Spawn`, `Despawn`, `SetTile`, `SendMessage`, `SetPlayerPhysics`, …), collected during the behavior phase and returned to the host for main-thread application in a deterministic order. **This is the single-thread `Read`+mutation contract:** scripts *read* a consistent snapshot and *write* only intents; nothing mutates mid-phase. **Does not own:** the actual mutation (glue applies it).

### 5.7 Predefined behavior library (core)

**Owns:** the curated, first-party, **safety-audited**, parameterized behaviors (e.g. "Hurt on contact", "Patrol between two cells", "Rise on hit then fall", "Teleport player to spawn on enter"). Each is addressable by a **stable id** and declares its **parameters** (typed, with ranges/pickers) so the editor can offer them on a gamepad. **Does not own:** free-text scripts (those are author-supplied resources).

### 5.8 `game/Behavior` (Godot glue)

**Owns:** the runtime object **bodies** (Godot nodes for objects), feeding engine collision + area enter/leave + per-frame delta into the bus, and **applying intents** to Godot nodes (`Player`, object bodies, the tilemap). **Does not own:** any behavior decision logic — it is a translator between Godot and the core, nothing more.

---

## 6. Data Model (Conceptual)

Entities and ownership (conceptual — no schema/DDL):

```
Package (VFS)
 ├─ Level (levels/<slug>.json)          owns: layout + behavior WIRING
 │   ├─ layers[] (existing)
 │   ├─ objects[]        ── ObjectPlacement ─▶ references ObjectDefinition (by objectset ref + id)
 │   ├─ triggers[]       ── AreaTriggerDefinition (grid rect + behavior binding)
 │   ├─ tileScriptOverrides  (sparse: (layer,cell) → binding | removed)
 │   └─ levelScript      ── behavior binding
 ├─ TileSet (tilesets/<slug>.json)      owns: tile TYPE defaults
 │   └─ tiles[] each optionally ── default contact/contactLeave behavior binding
 ├─ ObjectSet (objectsets/<slug>.json)  owns: object TYPE defaults   [NEW resource kind]
 │   └─ objects[] each ── default behavior binding + default state + collision role + graphic
 └─ Script (scripts/<name>.poo)         owns: Pooscript behavior text  [existing 'script' kind]
```

**Ownership rules:**

- **Type-level defaults** (a tile's "spikes hurt", an object type's default patrol) live on the **tileset / objectset** (reusable across levels).
- **Instance-level wiring and deltas** (this placement's override, this trigger's rect, per-cell tile overrides, the level script) live on the **level**.
- **Behavior text** lives in **`script` resources**; **predefined** behaviors live in the **engine's library**, not the package (they ship with Uberkarl and are referenced by stable id — a package that uses a predefined id is portable because every Uberkarl install has the library).
- **Runtime state** (an object's live pixel position, its state map, timers) is **not** stored in the package — it is born from the definition's defaults at play start and lives only for the play session. Authoring stores *initial* state only.

**Runtime entities (play session only):** `BehaviorInstance` (per scripted subject) holds its `CompiledBehavior`, its live **state map**, its scheduled timers, and a **quarantine flag**. An **object** additionally has a live free position + velocity + Godot body handle.

---

## 7. Event Model

Scripts react to a **closed, curated event set**. Each behavior binding may implement any subset of the events relevant to its subject; unimplemented events are simply no-ops.

| Subject | Events | Payload (conceptual) |
|---|---|---|
| **Tile** (scripted) | `onContact`, `onContactLeave` | the other party (player or object), the tile's cell |
| **Object** | `onSpawn`/`onReady`, `onUpdate(delta)`, `onContact`, `onContactLeave`, `onMessage(name, data)`, `onDespawn` | contacting party, delta seconds, message name/payload |
| **Area trigger** | `onEnter(who)`, `onLeave(who)` | who entered/left (player or object), the region |
| **Level (global)** | `onLevelStart`, `onUpdate(delta)`, `onPlayerDeath`/`onPlayerRespawn`, `onMessage` | delta, lifecycle context |
| **Any (via host timers)** | scheduled callbacks (`after` / `every`, host-driven) | the timer's tag |

**Handler discovery (recommended shape — Decision D-1):** a behavior script exposes handlers by **assigning named handler lambdas** to conventional variables during a one-time initialization execute (e.g. the script body sets `onContact`, `onUpdate`, …). The loader runs the script **once** at level load with an init facade, reads back the handler delegates, and caches them on the `CompiledBehavior`. Per event, the scheduler invokes only the cached delegate for that event — **parse + init once, cheap invoke per event.** Closures naturally give the behavior a place to keep helper values, while durable per-instance state uses the explicit `self.state` facade (§8.5 determinism/reset). *Alternative considered and rejected:* re-executing the whole script per event with an `event.kind` the script branches on — simpler to explain but re-runs top-level work every event and muddies per-instance state; rejected on cost + clarity.

**Timers — no `wait` (Decision D-3):** blocking `wait` ignores the CT (#2899) and would defeat the watchdog, so it is **banned from behavior scripts**. Timing is expressed as **host-driven scheduled callbacks** — a script calls `self.after(ms, tag)` / `self.every(ms, tag)`; the scheduler fires an `onTimer(tag)` handler later. All time advances between frames under host control, never inside a blocking script call. This sidesteps the single worst Pooscript safety gap by construction.

**Dispatch order (per frame, deterministic):**

1. Godot physics step runs (player + object bodies move, collisions resolve).
2. Glue collects raw engine signals (new contacts, contact-leaves, area enter/leave) and pushes them to the bus.
3. Scheduler runs the **behavior phase** in a fixed order: lifecycle → contact/leave → area enter/leave → per-frame `onUpdate` (level, then objects) → fired timers. Each handler runs under the watchdog; each records intents only.
4. Scheduler returns the collected intents; glue **applies them on the main thread** in intent order.
5. Next frame.

This ordering + the intent buffer means **no handler observes another handler's mid-frame mutation** — every script in a frame reads the same snapshot, which is what makes ordering deterministic and keeps the door open to replay (§8.5).

---

## 8. Contracts & Interfaces (Abstract) — the facade / action vocabulary + safety

The facade is **both** the "few words" product surface **and** the capability boundary. Per C-8 it must be a set of **narrow, purpose-built objects** — never the live Godot node — because a script reaches the entire public graph of anything exposed.

### 8.1 Facade surface (representative — exact list is a Phase-1 deliverable, Decision D-1)

Grouped by the bound object a script sees. All are *interfaces defined in the core*, implemented once by the glue-backed host and once by a test double.

| Bound object | Reads (queries) | Actions (recorded as intents) | Gamepad-authorable? |
|---|---|---|---|
| **`self`** (the running entity) | `cell`, `position`, `state[key]`, `name`, `kind` | `moveTo(cell/pos)`, `moveBy(dx,dy)`, `setState(key,val)`, `setGraphic(id)`, `despawn()`, `after/every(ms,tag)` | moveTo/setState/setGraphic: **yes** (pick target/value); logic combining them: no |
| **`level`** | `tileAt(layer,cell)`, `objectsNamed(name)`, `object(name)`, `state[key]` | `spawn(objectDefRef, cell)`, `setTile(layer,cell,tileId)`, `setState(key,val)`, `message(target,name,data)` | spawn/setTile: **yes**; queries in logic: no |
| **`player`** | `position`, `velocity`, `isOnGround`, `state[key]` | `hurt(amount)`, `heal(amount)`, `teleport(cell)`, `setSpawn(name)`, `setPhysics(field,val)` | hurt/teleport/setSpawn: **yes** |
| **`event`** | `kind`, `other` (contacting party), enter/leave `who` | — | n/a (payload) |
| **another `object`** (via `level.object(name)`) | its `cell`, `state[key]` | `moveTo`, `setState`, `message` | via "move another object" template: **yes** |

**Design stance: keep it narrow.** Start with roughly this set; add verbs only when a concrete author need appears. Every added verb is a permanent capability-surface and product-surface commitment.

**Gamepad vs keyboard split (Decision D-4):** an *action with fixed, pickable parameters* (moveTo a chosen cell, setState to a chosen value, hurt N) is expressible as a **predefined template + parameter pickers** and is fully **gamepad-authorable** using the existing `PopInMenu` / `RadialMenuModel` / `SteppedValueEditor`. *Composed logic* (conditionals, loops, arithmetic, multi-step sequences) requires the **free-text editor** (keyboard). Because the platform is desktop-only (C-7), the free-text path can use the **physical keyboard** directly; the on-screen keyboard is the gamepad fallback.

### 8.2 Capability boundary (the sandbox)

- The host builds a `ScriptParser` that registers **only** the facade objects as globals and registers **no** additional types/extensions/imports. Per C-8 this yields the tight allow-list: literals, operators, control flow, and the facade — nothing else. No reflection, file, net, `new`, static members, or Godot.
- Facade objects **return only value snapshots or other narrow facade objects** — never a Godot node, never a mutable engine object, never a collection that leaks the live world. (E.g. `event.other` is a small read-only descriptor, not the `CharacterBody2D`.)
- `import` is **disabled** for behavior scripts (`ImportsEnabled = false`) unless/until a curated, resolver-backed import story is designed — a community script must not pull arbitrary code.
- `TypeCasts` / `TypeInstanceProviders` (`new`) are **disabled** for behavior scripts — behavior needs neither and both widen the surface.

### 8.3 Watchdog contract (the freeze-proofing)

- Every handler invocation runs via **`ExecuteAsync(vars, ct)`** under a **budget** (a per-invocation wall-clock cap and a per-frame aggregate cap for `onUpdate` across all entities).
- On budget breach the watchdog **cancels the CT**; a cooperative interpreter (post-#7409) unwinds promptly. The breaching **behavior instance is quarantined**: disabled for the rest of the session, logged **once** (not per frame — no log spam), and the game keeps running.
- Budgets are **host config**, not script-visible. A quarantined object simply stops reacting (a moving platform freezes in place) — a *degraded* level, never a *frozen game*.

### 8.4 The #7409 dependency and the interim (LOAD-BEARING)

**Status check:** DiVoid **#7409 is `open`** as of 2026-08-05 — Pooscript's cooperative cancellation is **incomplete**: `wait` ignores the CT (#2899), and `while`/`for` CT-coverage is unverified. **Therefore the pure cooperative-cancellation watchdog cannot yet guarantee interruption of a tight `while(true)`.** The design must ship safely *before* #7409 lands.

**Two mitigations, layered:**

1. **Design out the worst case (independent of #7409):** ban `wait` from behavior scripts (D-3) — removing the un-cancellable blocking sleep entirely. Timing goes through host timers.
2. **Interim execution posture until #7409 lands (Decision D-5):** ship **predefined-library-only** (or first-party/local-authored) scripts in the early phases. Predefined behaviors are **authored and audited by us** to be bounded (no unbounded loops) — so the cooperative watchdog is sufficient for them *today*. **Untrusted free-text + imported community scripts are gated to a later phase that lands only after #7409** (or after an equivalent guarantee). This turns the safety gap into a **phase ordering** rather than a blocker — and it happens to coincide with the natural UX ordering (predefined-first is simpler to build and to author anyway).

**Alternative interim (offered for Toni, §14 D-5):** if Toni wants untrusted free-text *before* #7409, use a **worker-thread + abandon-on-timeout** model: run a script on a pool thread producing only intents (it can't touch Godot — the core is Godot-free); the host awaits with a hard timeout; on breach it **abandons** the thread (drops its intents, quarantines the behavior). A leaked `while(true)` thread still burns one core until process exit, but **the game never freezes**. This is strictly worse than #7409 landing (leaked threads) and only makes sense as a bridge; it is not the recommendation.

### 8.5 Single-thread mutation contract & determinism

- **Single-thread `Read`+mutation:** the intent buffer (§6.6) is the contract — every handler reads a consistent snapshot and writes only intents; the host applies all intents on the **main thread** after the whole behavior phase, in a deterministic order. No script ever mutates shared state mid-phase, so there are no data races and no order-of-evaluation surprises between scripts.
- **Determinism where feasible:** deterministic scheduler iteration order + the intent buffer make the *behavior layer* deterministic. **Overall level determinism (for future replay/ghosts) remains gated by the unresolved "Godot physics vs custom stepper" question (#7407 open question).** This design does not foreclose determinism (no hidden nondeterminism in the behavior layer, timers are frame-quantized not wall-clock) but cannot *guarantee* it until the physics-model decision is made. Flagged, not resolved (§14 Q-6).
- **State reset:** per-instance runtime state is rebuilt from definition defaults on each play start, so a level plays identically from a clean start regardless of a prior playtest — important for the editor's playtest→edit→playtest loop.

---

## 9. Runtime Execution Model

### 9.1 Where behavior plugs in

`PlayRuntimeBuilder.Populate` (the one shared runtime, C-4) gains a step: after building tile layers + player + camera, it constructs a **`BehaviorRuntime`** (glue) that wraps a core `BehaviorScheduler` seeded from the `ResolvedLevel`'s behavior data, and spawns the **object bodies**. Because it is in the shared builder, standalone play (`LevelPlay`) and editor playtest (`PlaytestOverlay`) get behavior identically — and the playtest snapshot path already rebuilds a fresh world each run (state reset comes for free).

### 9.2 Per-frame loop

Each frame (driven from the glue's `_PhysicsProcess`): physics step → collect engine signals → **run the core behavior phase under the watchdog** (produces intents) → **apply intents on the main thread** → render. (Sequence detailed in §7.)

### 9.3 Scripted tiles at runtime

Tiles are grid-fixed and numerous, so **tiles do not each get a live body.** Contact is detected by the glue from the player/object physics contact against the collision tilemap, mapped back to a **cell → tile-behavior binding** lookup (resolved from tileset default + level per-cell override). Only cells whose resolved binding is non-empty are tracked. `onContact` fires on first contact of a cell, `onContactLeave` when the contact ends. Tile scripts may mutate level structure via `level.setTile(...)` (per the vision's "limited unless it mutates level structure").

### 9.4 Free-moving objects at runtime (the key new primitive)

- **Placement → body:** an `ObjectPlacement` at a grid cell becomes, at play start, a **Godot body at the cell's pixel position** that is thereafter **free-moving** (not grid-locked). Grid placement is an *authoring convenience*; runtime position is continuous.
- **Collision role (Decision D-2 detail)** determines the body type and interaction:
  - `solid` → an `AnimatableBody2D` that the player collides with and that **carries the player when it moves** (Godot's move-and-slide handles rider transport) — this is a **moving platform**.
  - `platform` → one-way / passthrough-from-below variant of solid.
  - `passthrough` → a body that detects contact but does not block (a collectible, the question-block's contact sensor).
  - `trigger` → detection only (rarely needed as an object since area triggers exist; kept for completeness).
- **Motion via intents:** `self.moveTo(cell/pos)` / `moveBy(dx,dy)` record intents; the glue applies them by moving the body (tweened toward the target across frames, or an immediate set, per intent semantics). A **moving platform** is a `patrol` predefined behavior issuing `moveTo` between two cells on a loop; a **jumping question-block** is a behavior that, on `onContact` from below, issues a short rise-then-fall `moveBy` sequence and a `setState`/`setGraphic` (used → inert).
- **Object↔object & object↔player:** `level.object("gate")?.setState("open", true)` etc. — cross-entity actions are just intents targeting another instance, applied in the same main-thread pass. Player interaction (`player.hurt`, `player.teleport`) rides the same intent path onto the existing `Player` node (whose physics fields are already `[Export]` seams, C-5).
- **Objects vs tiles runtime model (Decision D-2):** tiles = static grid cells, no per-tile node, contact-via-tilemap; objects = live free bodies with per-frame update. This split is the recommendation — it keeps the (numerous) tiles cheap while giving the (fewer) objects full freedom.

### 9.5 Cost posture

Per-frame `onUpdate` runs the interpreter once per updating object per frame. At platformer object counts (tens, not thousands) under the per-frame watchdog budget this is acceptable for the MVP. The **compiled-delegate path (C-11)** is a noted future optimization for hot per-frame handlers *if* profiling demands it — not built now (it lacks lambdas/`wait` anyway, which suits the banned-`wait` + could-be-adapted-handler model, but is a Phase-5+ concern).

---

## 10. Editor Authoring

All of this extends the **existing** editor session-façade + command/history architecture (C-6), adds intents to `LevelEditSession` (or a sibling behavior session), and reuses the existing gamepad/keyboard input toolkit.

### 10.1 Placing objects & triggers (grid, gamepad — like tiles)

- **Objects:** a new **object palette** alongside the tile palette; the author picks an object type from a bound objectset and places instances at grid cells with the existing `GridCursor` — exactly the paint gesture used for tiles, but placing an `ObjectPlacement` instead of a cell id.
- **Area triggers:** a **rect tool** — the author marks two grid corners (cursor-driven) to define the grid rect; the trigger is created and named. Reuses the cursor + confirm flow.
- Placement/removal go through session intents on the command/history path so they are undoable, matching every other edit.

### 10.2 Assigning a predefined behavior (gamepad — the primary path)

- Select a placed object / trigger / tile-instance / the level → open a **pop-in / radial menu** of predefined behaviors (from the library, filtered to those valid for that subject) → pick one → fill its **parameters** with the existing `SteppedValueEditor` / pickers (target cell, state value, amount, etc.).
- This is **fully gamepad-doable** and is the expected 90% authoring path. The result is a `{ predefinedId, params }` binding stored on the subject.

### 10.3 Editing a free-text script (keyboard — the power path)

- A **script-editing surface** (a text/code control) lets the author write/edit Pooscript for a subject or a shared `script` resource. Because desktop-only (C-7), this uses the **physical keyboard**; the **on-screen keyboard** (`OnScreenKeyboard`) remains the gamepad fallback for the occasional edit.
- On commit, the text is saved as a `script` resource (package-VFS merge-writer, C-6) and the subject's binding points at it. The editor should surface **parse errors** (Pooscript `ScriptParserException`) inline before save.
- Per §8.4/D-5, free-text authoring for **untrusted/community** use is gated to the post-#7409 phase; first-party/local free-text can exist earlier behind a "not yet safe for sharing" understanding.

### 10.4 Tile default-script + per-instance override UX

- **Default** (type-level): in the **tileset editor**, a tile type gets an optional default contact/contact-leave behavior ("spikes hurt"). Stored on the tileset.
- **Per-instance override** (level): selecting a placed tile instance in the **level editor** offers **override / remove / revert-to-default** for its behavior — writing into the level's sparse tile-script-override map. "Remove" makes *this* spike inert without touching the tile type; "revert" drops the override. This directly realizes the vision's "default script overridable/removable per instance."

### 10.5 Predefined library authoring/shipping

The predefined library ships **with the engine** (embedded), not in packages, so a package referencing a predefined id is portable across installs. Each predefined behavior declares its parameter schema for the editor to render pickers. (Whether the library is itself authored as Pooscript resources embedded in the engine, or as native host behaviors, is an implementation choice — the *contract* is the stable id + param schema.)

---

## 11. Cross-Cutting Concerns

| Concern | Approach |
|---|---|
| **Security / untrusted code** | Allow-list sandbox (C-8) + narrow value-only facades + `import`/`new`/`cast` disabled for behavior scripts. The threat model is *community-shared scripts*; capability restriction is free, resource-exhaustion is handled by the watchdog. |
| **Resource safety (freeze-proofing)** | Watchdog budget + quarantine + `wait` ban (§8.3/8.4). Hard dependency on #7409 for the tight-loop case; interim = curated-only (D-5). **Primary invariant: the engine never freezes on a bad script.** |
| **Error handling** | A script that throws (`ScriptRuntimeException`) or breaches budget **quarantines its own behavior instance and is logged once**; it never crashes the frame or the game. Parse errors surface at authoring time, not runtime. Degrade locally, never globally. |
| **Observability** | Per-behavior quarantine events + budget-breach logs (once each) + a debug overlay (later) showing which behaviors are active/quarantined and per-frame behavior-time. Mirrors the existing `GD.Print`/`get_editor_errors` diagnostic style. |
| **Determinism / consistency** | Deterministic scheduler order + intent buffer (§8.5). Frame-quantized timers (no wall-clock inside scripts). Global determinism deferred to the physics-model decision. |
| **Concurrency / threading** | Recommended: **main-thread, single-threaded** behavior phase + intent buffer (no shared-state races by construction). The worker-thread interim (D-5 alt) is the only threaded option and is a bridge, not the target. |
| **Idempotency / re-entrancy** | Intents applied once per frame in order; a handler re-firing (e.g. repeated contact) is the author's concern, but `onContact`/`onContactLeave` are **edge-triggered** by the glue (fire on transition, not every frame of contact) to make authoring sane. |
| **State & save** | Only *initial* state is authored/stored; runtime state is session-only and reset on each play start (clean playtest loop). |
| **Versioning** | Behavior definitions ride the package `formatVersion` (C-3). The facade vocabulary is itself a compatibility surface — additive changes are safe; removing/renaming a verb or predefined id breaks packages, so the facade + predefined ids are a **stability commitment** once shipped (treat like a public API). |

---

## 12. Quality Attributes & Trade-offs

| Attribute | How addressed | Trade-off / rejected alternative |
|---|---|---|
| **Safety (the point)** | Sandbox + watchdog + `wait` ban + curated-first ordering | Curated-first delays untrusted free-text until #7409 — accepted, and it aligns with UX ordering anyway. Rejected: shipping untrusted free-text now on cooperative cancellation that can't yet interrupt a tight loop (would violate the never-freeze invariant). |
| **Authoring simplicity ("few words")** | Narrow facade + predefined templates + gamepad param pickers | Narrow facade limits early expressiveness — accepted; verbs added on demand. Rejected: a big facade up-front (larger permanent surface, harder to author). |
| **Maintainability** | Engine-agnostic core + Godot glue split (C-1); Definition→Resolved→Builder reuse (C-2); session/command reuse (C-6) | Some duplication of the "facade interface + two implementations (glue + test double)" — accepted; it is what makes the core unit-testable Godot-free. |
| **Performance** | Parse+init once, cheap per-event invoke; edge-triggered contacts; tiles have no per-tile node; per-frame watchdog budget | Interpreter-per-object-per-frame is not free; compiled path deferred. Accepted at platformer scale; profile before optimizing. |
| **Portability of content** | Predefined ids ship with engine; scripts are text resources; refs are `PackageId`+path | Predefined ids become a cross-version compatibility commitment — accepted (versioned like an API). |
| **Determinism (future replay)** | Deterministic scheduler + intent buffer + frame-quantized timers | Cannot *guarantee* global determinism until the physics-model choice — deliberately not foreclosed, not resolved here. |

---

## 13. Risks & Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| **#7409 slips / never fully lands** | Untrusted free-text can't be made freeze-proof cooperatively | Curated-only ships value now (D-5); worker-thread abandon interim available as a bridge; the never-freeze invariant holds regardless via the interim. |
| **Facade too narrow → authors blocked** | Behavior feels toylike | Facade is additively extensible; gather real author needs in Phase-1/2 and grow deliberately. |
| **Facade too wide → capability/leak risk** | A returned object leaks the live world graph (C-8) | Contract rule: facades return *value snapshots or other narrow facades only*; enforce in review + tests that assert a script cannot reach a Godot type. |
| **Per-frame script cost spikes** | Frame drops with many updating objects | Per-frame budget + quarantine; edge-triggered events; compiled-path optimization in reserve. |
| **Moving-platform rider physics feel wrong** | Objects designed but janky to ride | Use Godot-native `AnimatableBody2D` carry semantics; treat "feel" as a tuning pass, physics fields already `[Export]` seams. |
| **Behavior state leaks between playtests** | Editor loop shows stale behavior | State reset on each play start; playtest already rebuilds a fresh world (C-4). |
| **Determinism assumed but not delivered** | Replays/ghosts break later | Explicitly *not* promised; behavior layer kept deterministic; global verdict deferred to physics decision (Q-6). |

---

## 14. Phased Build Plan + Decisions for Toni to Ratify

Each phase is an independently shippable PR (per the one-feature-one-PR discipline, phases split further where a unit stands alone). Ordering is dependency-driven **and** safety-driven (curated-first is both).

### Phase 0 — Behavior core (engine-agnostic), no gameplay yet
*Deliverable:* new `src/Uberkarl.Behavior` library + `tests/Uberkarl.Behavior.Tests`.
- Behavior definition + resolved models (bindings, object/objectset, placement, trigger, tile-override map, level-script).
- Facade **contracts** (interfaces) + the intent model + intent types.
- Behavior compile/load pipeline (parse once, discover handlers) with a **test-double host** (no Godot).
- `BehaviorScheduler` + event bus + deterministic behavior phase + intent collection.
- **Watchdog** wrapper over `ExecuteAsync`+CT with budget + quarantine, unit-tested with a fake slow/looping script (proving quarantine, not freeze).
- *No Godot, no gameplay* — this is the testable spine. **Ships the safety architecture first.**

### Phase 1 — Scripted tiles + area triggers + level script (event→action MVP)
*Deliverable:* wire the core into `PlayRuntimeBuilder`; facade v0 (minimal action set: `setState`, `hurt`, `setTile`, a couple of reads); a small **predefined library** seed; contact/contact-leave on tiles, enter/leave on triggers, level `onLevelStart`/`onUpdate`.
- Godot glue: feed engine contacts/area signals into the bus; apply intents to `Player`/tilemap.
- Safety posture: **curated/first-party scripts only** (D-5 interim).
- A sample level that reacts (e.g. spikes hurt, a trigger teleports).

### Phase 2 — Free-moving objects
*Deliverable:* object bodies (collision roles), `moveTo`/`moveBy`/`spawn`/`despawn`, object↔object messaging, per-frame `onUpdate`.
- Demo: a **moving platform** (patrol predefined) that carries the player + a **jumping question-block** (rise-on-hit predefined).
- Extends facade with the object/motion verbs.

### Phase 3 — Authoring UX: predefined library + assignment (gamepad)
*Deliverable:* editor tools to place objects (palette) + triggers (rect tool); assign predefined behaviors via pop-in/radial menus + param steppers; tile default-script (tileset editor) + per-instance override/remove/revert (level editor).
- This is the "behavior in a few words" surface authors actually touch; **fully gamepad-authorable.**

### Phase 4 — Free-text script editor + untrusted/community scripts (GATED on #7409)
*Deliverable:* the free-text (physical-keyboard) script-editing surface with inline parse errors; enabling **untrusted free-text + imported community scripts** once #7409's cooperative cancellation lands (or the worker-thread interim is chosen).
- Until #7409 lands, first-party/local free-text may exist but sharing untrusted free-text is off.

### Phase 5+ (not scheduled) — optimizations & richer authoring
- Compiled-path hot handlers if profiling demands; debug/observability overlay; possible visual node-graph authoring; curated `import` story.

---

### Decisions for Toni to ratify

- **D-1 — Facade vocabulary shape + handler model.** Ratify (a) the *narrow, additively-grown* facade stance and the representative verb set in §8.1, and (b) the **handler-registration** dispatch model (named handler lambdas discovered at init) over whole-script-per-event. *(Sarah recommends both.)*
- **D-2 — Objects-vs-tiles runtime model.** Ratify: **tiles = static grid cells (no per-tile node, contact via tilemap); objects = live free bodies (`AnimatableBody2D` carry semantics) with collision roles** `solid`/`platform`/`passthrough`/`trigger`; objects are a **new `objectset` resource kind** with placements stored on the level (A-1). *(Sarah recommends this split.)*
- **D-3 — Ban `wait`; timing via host-driven timers.** Ratify banning blocking `wait`/sleep in behavior scripts and expressing time as scheduled `after`/`every` callbacks. *(Recommended — sidesteps the worst Pooscript safety gap.)*
- **D-4 — Scripting UX split.** Ratify: **predefined behaviors = gamepad (menu + param pickers)**; **free-text = physical keyboard (desktop-only), on-screen keyboard as fallback**; predefined-first ordering. *(Recommended.)*
- **D-5 — Safety / #7409 dependency posture.** #7409 is **open** (incomplete). Ratify the **curated-first interim**: cooperative watchdog + `wait`-ban now, untrusted free-text/import gated to Phase 4 (post-#7409). *Or* choose the **worker-thread abandon-on-timeout** bridge to allow untrusted free-text sooner (accepting leaked-thread cost). *(Sarah recommends curated-first; it also matches the natural UX ordering.)*
- **D-6 — Phase boundaries.** Ratify the five-phase ordering (core → tiles/triggers/level → objects → authoring UX → free-text/untrusted), each a shippable PR.

---

## 15. Open Questions

- **Q-1 — Object state shape:** a free-form string→value `state` map (flexible, no schema) vs a typed per-object-def schema (validated, better pickers)? *(Leaning free-form for MVP, typed later for editor pickers.)*
- **Q-2 — Timer semantics:** one-shot + repeat + cancel-by-tag — is that the full timer vocabulary, or do we need pause/resume?
- **Q-3 — Tile scripts mutating structure:** the vision says tiles are "limited unless the script mutates level structure." How far does `level.setTile` go — single cell only, or region ops? Structural mutation mid-play has physics/collision implications (the tilemap collision must rebuild) — bound this.
- **Q-4 — `hurt`/death model:** does the behavior layer own player health + death/respawn, or does a separate gameplay-rules layer? `player.hurt(n)` implies *some* health model exists — where does it live, and what are the defaults?
- **Q-5 — Predefined library packaging:** ship predefined behaviors as embedded Pooscript resources (dogfoods the language, editable) or as native host implementations (faster, but a second behavior mechanism)? Affects portability + versioning.
- **Q-6 — Determinism intent:** how much does Toni want to preserve the *option* of replay/ghosts? This reinforces (or relaxes) how strict the deterministic-scheduler + physics-model decision needs to be. Ties to the still-open #7407 physics question — should the behavior work *force* that decision, or stay agnostic?
- **Q-7 — Cross-package script/object references & trust:** a level pack using another pack's objectset/scripts (remix culture, #7407 pillar 4). When behavior comes from a *dependency* package, does the trust boundary / attribution change? (Likely a later phase, but flag now.)

---

## 16. Implementation Guidance for the Next Agent (build order)

1. **Stand up `Uberkarl.Behavior` (Phase 0)** exactly on the `Uberkarl.Content` pattern: Godot-free `net8.0` lib + NUnit tests, kept out of the Godot-managed `.sln` like its siblings. Models first (definitions + resolved + bindings + intents), then the facade *interfaces*, then compile/load, then scheduler, then watchdog — each unit-tested with a fake host before any Godot exists. **The watchdog + quarantine test (bad script cannot freeze) is the acceptance gate for Phase 0.**
2. **Extend the content pipeline (Phase 0/1 seam):** add `objectset` to `ResourceKind`; extend `LevelDefinition`/`ResolvedLevel` + `LevelLoader` with `objects`/`triggers`/`tileScriptOverrides`/`levelScript`; extend `TileDefinition` with the default binding. Keep every parse/validation Godot-free (C-2). Update the sample content + merge-writer paths (namespaced per-resource paths, C-6/#7572).
3. **Wire into the ONE runtime (Phase 1):** add the `BehaviorRuntime` step to `PlayRuntimeBuilder.Populate` so `LevelPlay` + `PlaytestOverlay` both get it. Glue feeds engine signals in, applies intents out. Curated-only safety posture.
4. **Objects (Phase 2):** object bodies + collision roles + motion intents; moving-platform + question-block demos.
5. **Authoring (Phase 3):** extend `LevelEditSession` (or a behavior sibling session) with placement + binding intents on the command/history path; object palette + trigger rect tool; predefined-assignment menus + param pickers; tileset default-script + per-instance override UX. Reuse `GridCursor`/`PopInMenu`/`SteppedValueEditor`.
6. **Free-text + untrusted (Phase 4):** only after #7409 (or the chosen interim). Physical-keyboard script surface with inline parse errors; enable community/imported free-text.

**Non-negotiables for every phase:** engine-agnostic core stays Godot-free and unit-tested; the never-freeze invariant is proven by test before untrusted code runs; the facade returns only narrow value snapshots; save never clobbers package siblings.
