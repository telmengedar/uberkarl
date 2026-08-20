using Uberkarl.Behavior;
using Uberkarl.Content;
using Uberkarl.Packages;

namespace Uberkarl.Editor;

/// <summary>
/// The façade the editor UI drives. It owns the <see cref="EditableLevel"/> (the model) and its
/// <see cref="EditHistory"/>, and exposes the whole edit surface as intent-level calls — paint, erase,
/// undo, redo, save — each returning the single <see cref="CellChange"/> the canvas must reflect (or
/// <c>null</c> for a no-op). The UI never mutates the model directly: it calls the session, then
/// applies the returned change to the canvas. This keeps the model authoritative and every mutation
/// on the undoable command path.
/// </summary>
public sealed class LevelEditSession
{
    private readonly EditHistory history = new();

    public LevelEditSession(EditableLevel level)
    {
        Level = level ?? throw new ArgumentNullException(nameof(level));
    }

    /// <summary>The level under edit.</summary>
    public EditableLevel Level { get; }

    /// <summary>True when there are unsaved edits (any applied/undone/redone change since the last save).</summary>
    public bool IsDirty { get; private set; }

    public bool CanUndo => history.CanUndo;

    public bool CanRedo => history.CanRedo;

    /// <summary>
    /// Paints <paramref name="tileId"/> onto the cell on the given layer. No-ops (returns <c>null</c>)
    /// when the cell already holds that tile — this keeps a click-drag that re-touches the same cell
    /// from stacking redundant history entries. Throws when the layer, tile, or cell is invalid.
    ///
    /// <b>Two-channel invariant</b> (DiVoid #7551 Phase 3, design #7580 §7): placing a concrete tile always
    /// clears any terrain paint at that cell too (<see cref="SetCellCommand"/>). Because of that, a
    /// terrain-painted cell's OWN concrete channel already reads <see cref="LayerDefinition.EmptyCell"/> —
    /// so erasing it (<paramref name="tileId"/> == <see cref="LayerDefinition.EmptyCell"/>) must still
    /// execute even though the concrete channel "already matches", or the terrain paint would survive an
    /// Erase. The no-op check therefore also looks at the terrain channel, not just the concrete one.
    /// </summary>
    public CellChange? PaintCell(int layerIndex, int x, int y, int tileId)
    {
        if (layerIndex < 0 || layerIndex >= Level.Layers.Count)
            throw new ArgumentOutOfRangeException(nameof(layerIndex));
        if (!Level.InBounds(x, y))
            return null;
        if (!Level.IsPlaceableTile(tileId))
            throw new ArgumentException($"Tile id {tileId} is not in the level's palette.", nameof(tileId));

        var layer = Level.Layers[layerIndex];
        var index = Level.CellIndex(x, y);
        if (layer.Cells[index] == tileId && layer.Terrain[index] == LayerDefinition.EmptyCell)
            return null;

        var change = history.Execute(new SetCellCommand(layerIndex, x, y, tileId), Level);
        IsDirty = true;
        return change;
    }

    /// <summary>Erases the cell on the given layer (paints the empty marker). No-op when already empty.</summary>
    public CellChange? EraseCell(int layerIndex, int x, int y)
        => PaintCell(layerIndex, x, y, LayerDefinition.EmptyCell);

    /// <summary>
    /// Paints the LOGICAL terrain <paramref name="terrainId"/> onto the cell on the given layer (DiVoid
    /// #7551 Phase 3, design #7580 §6.4 — "painting writes the logical (terrainSet, terrain) into the
    /// layer's terrain channel, not a concrete id"). Always clears any concrete tile at that cell too (the
    /// two-channel invariant). No-ops (returns <c>null</c>) when the cell already holds that terrain AND is
    /// already concrete-empty. Returned <see cref="CellChange"/> always carries <see cref="LayerDefinition.EmptyCell"/>
    /// as its tile id — the caller applies it via the same canvas path a concrete edit uses (clearing any
    /// stale concrete visual), then re-issues the engine's terrain-connect resolution to paint the actual
    /// matching variant (design #7580 §6.4 — "the editor immediately re-drives terrain-connect... so the
    /// canvas shows the resolved variants live"); this session never touches Godot, so it cannot do that
    /// part itself. Throws when the layer, terrain, or cell is invalid.
    /// </summary>
    public CellChange? PaintTerrain(int layerIndex, int x, int y, int terrainId)
    {
        if (layerIndex < 0 || layerIndex >= Level.Layers.Count)
            throw new ArgumentOutOfRangeException(nameof(layerIndex));
        if (!Level.InBounds(x, y))
            return null;
        if (!Level.IsPlaceableTerrain(terrainId))
            throw new ArgumentException($"Terrain id {terrainId} is not declared by the level's bound tile set.", nameof(terrainId));

        var layer = Level.Layers[layerIndex];
        var index = Level.CellIndex(x, y);
        if (layer.Terrain[index] == terrainId && layer.Cells[index] == LayerDefinition.EmptyCell)
            return null;

        var change = history.Execute(new SetTerrainCommand(layerIndex, x, y, terrainId), Level);
        IsDirty = true;
        return change;
    }

    /// <summary>Erases the terrain paint on the cell on the given layer (paints the terrain-empty marker). No-op when already unpainted.</summary>
    public CellChange? EraseTerrain(int layerIndex, int x, int y)
        => PaintTerrain(layerIndex, x, y, LayerDefinition.EmptyCell);

    /// <summary>Places an instance of <paramref name="objectType"/> from <paramref name="objectSet"/> at cell (x,y). No-op when out of bounds. Undoable.</summary>
    public void PlaceObject(Package package, ResourceReference objectSet, EditableObjectType objectType, int x, int y, string name = "")
    {
        if (package is null)
            throw new ArgumentNullException(nameof(package));
        if (objectType is null)
            throw new ArgumentNullException(nameof(objectType));
        if (!Level.InBounds(x, y))
            return;

        var effectiveBehavior = Level.CaptureBehavior(package, objectType.Definition.Behavior, $"Object type '{objectType.Definition.Id}'");
        var placement = new ObjectPlacement
        {
            ObjectSet = objectSet,
            ObjectId = objectType.Definition.Id,
            Cell = new GridPosition(x, y),
            Name = name ?? string.Empty,
        };
        var editablePlacement = new EditableObjectPlacement(
            placement, objectType.Definition.CollisionRole, objectType.Graphic, effectiveBehavior, objectType.Definition.State);

        history.Execute(new PlaceObjectCommand(editablePlacement), Level);
        IsDirty = true;
    }

    /// <summary>Removes the object occupying cell (x,y), if any — the object paint mode's erase. Returns <c>false</c> (no-op) when the cell holds no object. Undoable.</summary>
    public bool EraseObjectAt(int x, int y)
    {
        var index = Level.FindObjectIndexAt(x, y);
        if (index < 0)
            return false;

        history.Execute(new RemoveObjectCommand(index), Level);
        IsDirty = true;
        return true;
    }

    /// <summary>Assigns <paramref name="binding"/> as the placed object at <paramref name="index"/>'s own behavior override. Undoable.</summary>
    public void AssignObjectBehavior(int index, BehaviorBinding binding)
    {
        history.Execute(new SetObjectBehaviorCommand(index, binding), Level);
        IsDirty = true;
    }

    /// <summary>Assigns <paramref name="binding"/> as the trigger at <paramref name="index"/>'s binding. Undoable.</summary>
    public void AssignTriggerBehavior(int index, BehaviorBinding binding)
    {
        history.Execute(new SetTriggerBehaviorCommand(index, binding), Level);
        IsDirty = true;
    }

    /// <summary>Assigns <paramref name="binding"/> as the per-instance behavior override for the tile at (layerIndex,x,y). Undoable.</summary>
    public void AssignTileBehaviorOverride(int layerIndex, int x, int y, BehaviorBinding binding)
    {
        history.Execute(new SetTileBehaviorOverrideCommand(layerIndex, x, y, binding), Level);
        IsDirty = true;
    }

    /// <summary>Assigns <paramref name="binding"/> as the level's global lifecycle/<c>onUpdate</c> script binding. Undoable.</summary>
    public void AssignLevelScript(BehaviorBinding binding)
    {
        history.Execute(new SetLevelScriptCommand(binding), Level);
        IsDirty = true;
    }

    /// <summary>Upserts a script's source text into the level's script table. Not on the undo stack.</summary>
    public void UpsertScriptSource(ResourcePath path, string source)
    {
        Level.UpsertScript(path, source);
        IsDirty = true;
    }

    /// <summary>
    /// The "is this slug taken" predicate a new-script name is checked against: this level's own script
    /// table, plus <paramref name="packageResources"/> when supplied. Case-insensitive: a slug that only
    /// differs in case from an existing script still collides on extraction to a
    /// case-insensitive filesystem, so <c>doorway</c> must not be mintable alongside a sibling
    /// <c>Doorway.poo</c> even though <see cref="ResourcePath"/> equality itself stays ordinal for every
    /// other purpose (in-archive routing, where case sensitivity is correct).
    /// </summary>
    public Func<string, bool> NewScriptSlugTaken(IReadOnlyList<ResourceEntry>? packageResources = null) => slug =>
    {
        var path = ScriptResourcePaths.ScriptPath(slug);
        if (ContainsCaseInsensitive(Level.Scripts.Keys, path))
            return true;

        return packageResources is not null && ContainsCaseInsensitive(packageResources, path);
    };

    /// <summary>Undoes the last edit and returns the cell to refresh, or <c>null</c> when nothing to undo.</summary>
    public CellChange? Undo()
    {
        var change = history.Undo(Level);
        if (change is not null)
            IsDirty = true;
        return change;
    }

    /// <summary>Redoes the last undone edit and returns the cell to refresh, or <c>null</c> when nothing to redo.</summary>
    public CellChange? Redo()
    {
        var change = history.Redo(Level);
        if (change is not null)
            IsDirty = true;
        return change;
    }

    /// <summary>
    /// The set of resource contributions (level.json + tileset.json + tile graphics, at this level's own
    /// namespaced paths) this level owns — the merge unit <see cref="Save"/>/<see cref="SaveFresh"/>
    /// compose onto an archive. Exposed so a caller (or a test) can inspect exactly what a save will
    /// touch without also needing an existing package to merge onto.
    /// </summary>
    public IReadOnlyList<PendingResource> BuildContributions() => LevelMergeWriter.BuildContributions(Level);

    /// <summary>
    /// Merges this level's contributions onto <paramref name="existingPackage"/> — every sibling resource
    /// and the archive's own identity are carried forward unchanged (DiVoid #7571/#7572's package-as-VFS
    /// correction: a level save must never clobber a package's other contents). <paramref name="extraContributions"/>
    /// lets the caller (the Godot glue, <c>LevelEditor</c>) fold a bound-but-not-yet-saved tile set's own
    /// contributions (<see cref="TileSetEditSession.BuildContributions"/>) into the SAME archive write —
    /// under the shared-tileset correction (DiVoid #7551 Phase 1a) a level's own contribution is just its
    /// <c>level.json</c>, so this is how a level and its tile set land in one save when the tile set is
    /// not yet persisted anywhere else. The caller opens the package (file IO stays outside this
    /// engine-agnostic core) and writes the returned bytes back to storage. The dirty flag clears on the
    /// assumption the write succeeds; a failed write should re-mark dirty via <see cref="MarkDirty"/>.
    /// </summary>
    public byte[] Save(Package existingPackage, IReadOnlyList<PendingResource>? extraContributions = null)
    {
        var bytes = LevelMergeWriter.Compose(existingPackage, Combine(extraContributions));
        IsDirty = false;
        return bytes;
    }

    /// <summary>
    /// Mints a brand-new archive containing only this level (Save-As's "＋ New package" outcome, or a
    /// never-before-saved level's first save). <paramref name="newPackageName"/> is the archive's own
    /// display name — independent of this level's <see cref="EditableLevel.Name"/>. See <see cref="Save"/>
    /// for <paramref name="extraContributions"/>.
    /// </summary>
    public byte[] SaveFresh(string newPackageName, IReadOnlyList<PendingResource>? extraContributions = null)
    {
        var bytes = LevelMergeWriter.BuildFresh(newPackageName, Combine(extraContributions));
        IsDirty = false;
        return bytes;
    }

    private IReadOnlyList<PendingResource> Combine(IReadOnlyList<PendingResource>? extraContributions)
    {
        var own = BuildContributions();
        return extraContributions is { Count: > 0 } extra ? own.Concat(extra).ToList() : own;
    }

    /// <summary>Re-marks the session dirty (used if a save write fails after <see cref="Save"/> returned bytes).</summary>
    public void MarkDirty() => IsDirty = true;

    /// <summary>
    /// Establishes this level as a brand-new resource in its target package (Save-As's "＋ New level…"
    /// outcome): derives a slug from the level's current (already-renamed) display name, uniquified
    /// against <paramref name="existingResources"/> so it can never collide with a sibling level already
    /// in that package, and attaches the level to the namespaced paths that slug produces.
    /// </summary>
    public void AttachAsNewResource(IReadOnlyList<ResourceEntry> existingResources)
    {
        if (existingResources is null)
            throw new ArgumentNullException(nameof(existingResources));

        var baseSlug = LevelResourcePaths.Slugify(Level.Name);
        var slug = LevelResourcePaths.UniqueSlug(baseSlug, candidate => Contains(existingResources, LevelResourcePaths.LevelPath(candidate)));
        Level.Attach(slug, overwriteLevelPath: null);
        IsDirty = true;
    }

    /// <summary>
    /// Establishes this level as the (explicitly-picked) replacement for an existing level resource
    /// (Save-As's "pick existing level to overwrite" outcome): reuses <paramref name="levelPath"/>
    /// verbatim — the author's pick IS the slot — and derives the tile-set/graphics slug from that same
    /// path when it follows the namespaced convention (<see cref="LevelResourcePaths.SlugFromLevelPath"/>),
    /// so re-saving into the same slot is idempotent rather than drifting to a fresh slug on every save.
    /// Falls back to slugifying the level's current display name only for a legacy path that predates the
    /// namespacing scheme.
    /// </summary>
    public void AttachToExistingResource(ResourcePath levelPath)
    {
        var slug = LevelResourcePaths.SlugFromLevelPath(levelPath) ?? LevelResourcePaths.Slugify(Level.Name);
        Level.Attach(slug, overwriteLevelPath: levelPath);
        IsDirty = true;
    }

    private static bool Contains(IReadOnlyList<ResourceEntry> resources, ResourcePath path)
    {
        foreach (var entry in resources)
        {
            if (entry.Path == path)
                return true;
        }

        return false;
    }

    private static bool ContainsCaseInsensitive(IEnumerable<ResourcePath> paths, ResourcePath path)
    {
        foreach (var candidate in paths)
        {
            if (string.Equals(candidate.Value, path.Value, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool ContainsCaseInsensitive(IReadOnlyList<ResourceEntry> resources, ResourcePath path)
    {
        foreach (var entry in resources)
        {
            if (string.Equals(entry.Path.Value, path.Value, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Renames the level under edit (DiVoid #7552 — Save-As level naming via the on-screen keyboard).
    /// Blank/whitespace-only input is treated as a no-op (mirrors <see cref="RenameLayer"/>) rather than
    /// throwing across the UI boundary — the browser's Save-As flow passes whatever the keyboard commits
    /// straight through. The name is trimmed before being applied.
    /// </summary>
    public bool RenameLevel(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var happened = Level.Rename(name.Trim());
        if (happened)
            IsDirty = true;
        return happened;
    }

    // ----- layer management intents -----
    //
    // History policy (the layer-index aliasing hazard, design §9.3): recorded SetCellCommands store an
    // absolute layer index. AddLayer appends at the end and SetCollision/StepScrollSpeed/SetRepeat replace
    // a layer in place — both are index-stable, so cell-edit history is preserved. DeleteLayer and
    // MoveLayer shift indices, so a successful one clears cell-edit history (the same Clear() used on
    // load/save-as) rather than let recorded undo alias onto the wrong layer. Layer operations themselves
    // are not on the undo stack this increment.

    /// <summary>
    /// Appends a new auto-named ("Layer N") display layer (<c>collision:false, scrollSpeed:1.0, repeat:false</c>)
    /// at the front-of-array end. Index-stable, so cell-edit history is preserved.
    /// </summary>
    public LayerEditResult AddLayer()
    {
        var name = LayerNaming.NextName(Level.Layers.Select(layer => layer.Name));
        var index = Level.AppendLayer(name, collision: false, scrollSpeed: 1.0f, repeat: false);
        IsDirty = true;
        return new LayerEditResult(true, index);
    }

    /// <summary>
    /// Deletes the layer at <paramref name="index"/>. No-op (returns <c>Happened:false</c>) when
    /// <paramref name="index"/> is out of range or it is the level's last layer. A real delete clears
    /// cell-edit history and reconciles the returned index into the remaining layer range (clamped).
    /// </summary>
    public LayerEditResult DeleteLayer(int index)
    {
        if (!Level.RemoveLayerAt(index))
            return new LayerEditResult(false, index);

        history.Clear();
        IsDirty = true;
        var reconciled = Math.Clamp(index, 0, Level.Layers.Count - 1);
        return new LayerEditResult(true, reconciled);
    }

    /// <summary>
    /// Moves the layer at <paramref name="index"/> one step in <paramref name="direction"/> (positive =
    /// toward the front, negative = toward the back) — an adjacent swap that changes draw order. No-op at
    /// either end. A real move clears cell-edit history; the returned index is the moved layer's new
    /// position, so the controller can keep it the active layer.
    /// </summary>
    public LayerEditResult MoveLayer(int index, int direction)
    {
        if (index < 0 || index >= Level.Layers.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        var newIndex = Level.MoveLayer(index, direction);
        if (newIndex == index)
            return new LayerEditResult(false, index);

        history.Clear();
        IsDirty = true;
        return new LayerEditResult(true, newIndex);
    }

    /// <summary>
    /// Renames the layer at <paramref name="index"/> to <paramref name="name"/> (DiVoid #7513 — the
    /// on-screen keyboard's proving ground). Blank/whitespace-only input is treated as a no-op (the
    /// panel's rename affordance passes whatever the keyboard commits straight through — this is the one
    /// place that guards against an author backing out to an empty buffer without hitting Cancel) rather
    /// than throwing across the UI boundary, mirroring every other intent here. The name is trimmed before
    /// being applied. Index-stable, so cell-edit history is preserved.
    /// </summary>
    public LayerEditResult RenameLayer(int index, string name)
    {
        LayerAt(index); // validates index; the layer itself is not needed — Level.RenameLayer re-resolves it
        if (string.IsNullOrWhiteSpace(name))
            return new LayerEditResult(false, index);

        var happened = Level.RenameLayer(index, name.Trim());
        if (happened)
            IsDirty = true;
        return new LayerEditResult(happened, index);
    }

    /// <summary>
    /// Sets the collision flag on the layer at <paramref name="index"/>. Collision is dominant: turning
    /// it on coerces scroll speed to 1.0 and repeat to off (<see cref="LayerPropertyRules"/>). Always
    /// reachable (this is the control that governs whether scroll/repeat are editable at all).
    /// Index-stable, so cell-edit history is preserved.
    /// </summary>
    public LayerEditResult SetCollision(int index, bool collision)
    {
        var layer = LayerAt(index);
        var happened = Level.SetLayerProperties(index, collision, layer.ScrollSpeed, layer.Repeat);
        if (happened)
            IsDirty = true;
        return new LayerEditResult(happened, index);
    }

    /// <summary>
    /// Steps the layer at <paramref name="index"/>'s scroll speed through the preset ladder
    /// (<see cref="ScrollSpeedLadder"/>). No-op while the layer's collision flag is on (scroll is locked
    /// to 1.0 then — <see cref="LayerPropertyRules.Editable"/>). Index-stable, so cell-edit history is
    /// preserved.
    /// </summary>
    public LayerEditResult StepScrollSpeed(int index, int direction)
    {
        var layer = LayerAt(index);
        if (!LayerPropertyRules.Editable(layer.Collision))
            return new LayerEditResult(false, index);

        var stepped = ScrollSpeedLadder.Step(layer.ScrollSpeed, direction);
        var happened = Level.SetLayerProperties(index, layer.Collision, stepped, layer.Repeat);
        if (happened)
            IsDirty = true;
        return new LayerEditResult(happened, index);
    }

    /// <summary>
    /// Sets the layer at <paramref name="index"/>'s scroll speed to an absolute <paramref name="scrollSpeed"/>
    /// (as opposed to <see cref="StepScrollSpeed"/>'s relative ladder step). The panel's edit-mode stepper
    /// (DiVoid #7512) uses this: it steps a local pending value through <see cref="ScrollSpeedLadder"/>
    /// without touching the model, then applies the final value here on commit only — so a cancelled edit
    /// never reaches the model and needs no revert. No-op while the layer's collision flag is on (scroll is
    /// locked to 1.0 then — <see cref="LayerPropertyRules.Editable"/>). Index-stable, so cell-edit history
    /// is preserved.
    /// </summary>
    public LayerEditResult SetScrollSpeed(int index, float scrollSpeed)
    {
        var layer = LayerAt(index);
        if (!LayerPropertyRules.Editable(layer.Collision))
            return new LayerEditResult(false, index);

        var happened = Level.SetLayerProperties(index, layer.Collision, scrollSpeed, layer.Repeat);
        if (happened)
            IsDirty = true;
        return new LayerEditResult(happened, index);
    }

    /// <summary>
    /// Sets the repeat flag on the layer at <paramref name="index"/>. No-op while the layer's collision
    /// flag is on (repeat is locked off then). Index-stable, so cell-edit history is preserved.
    /// </summary>
    public LayerEditResult SetRepeat(int index, bool repeat)
    {
        var layer = LayerAt(index);
        if (!LayerPropertyRules.Editable(layer.Collision))
            return new LayerEditResult(false, index);

        var happened = Level.SetLayerProperties(index, layer.Collision, layer.ScrollSpeed, repeat);
        if (happened)
            IsDirty = true;
        return new LayerEditResult(happened, index);
    }

    // ----- grid resize intent (DiVoid #7550) -----

    /// <summary>
    /// Resizes the level's grid, applied identically across every layer. Growing preserves every existing
    /// cell at its original coordinates and fills the new cells empty; shrinking crops. No-op (returns
    /// <c>false</c>) when the requested size equals the current one. A real resize clears cell-edit
    /// history — the same layer-index-aliasing hazard as <see cref="DeleteLayer"/>/<see cref="MoveLayer"/>,
    /// but for coordinates rather than layer indices: <see cref="SetCellCommand"/> re-resolves an absolute
    /// <c>(x,y)</c> to a cell index via the level's <b>current</b> <see cref="EditableLevel.Width"/> at
    /// apply/revert time, so a recorded command from before a resize would alias onto the wrong cell (or
    /// throw, for a coordinate a shrink cropped away) after one. Resize itself is not on the undo stack
    /// this increment — layer-op parity. Callers should check
    /// <see cref="EditableLevel.WouldDropPaintedCells"/> before calling this to decide whether to prompt
    /// the author for confirmation (mirrors the layer-manager's delete confirm) — this method performs the
    /// resize unconditionally once called.
    /// </summary>
    public bool Resize(int width, int height)
    {
        if (!Level.Resize(width, height))
            return false;

        history.Clear();
        IsDirty = true;
        return true;
    }

    private EditableLayer LayerAt(int index)
    {
        if (index < 0 || index >= Level.Layers.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        return Level.Layers[index];
    }
}
