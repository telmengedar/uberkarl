using System.IO;
using Uberkarl.Behavior;
using Uberkarl.Content;
using Uberkarl.Content.Json;
using Uberkarl.Packages;

namespace Uberkarl.Editor;

/// <summary>
/// Loads an <see cref="EditableLevel"/> from a package. Unlike <see cref="LevelLoader"/> (which
/// produces the runtime <see cref="ResolvedLevel"/> and drops the authoring detail — graphic paths,
/// per-tile collide flags, spawn/metadata provenance) this keeps everything needed to edit and
/// re-save the level losslessly. Tile graphics are read out as bytes and held in memory, so the
/// returned model has no further dependency on the source package.
///
/// This increment edits self-contained packages (every tile graphic is a self reference in the same
/// package, as the sample is). A tile graphic that points at another package is out of scope for the
/// MVP and surfaces as a clear <see cref="LevelContentException"/>.
///
/// <b>Shared-tileset correction (DiVoid #7551 Phase 1a):</b> the level's bound tile set is read via
/// <see cref="EditableTileSetReader"/> (the level no longer owns its tile definitions/graphics) — this
/// reader only takes the resulting tile LIST to seed the level's palette cache
/// (<see cref="EditableLevel.Tiles"/>); the tile set's own resource identity/path is exposed separately
/// through <see cref="EditableLevel.TileSetReference"/> for a caller (<c>LevelEditor</c>) that wants to
/// also open it for editing via <see cref="EditableTileSetReader"/> directly.
/// </summary>
public static class EditableLevelReader
{
    /// <summary>Loads an editable level from raw <c>.pkg</c> bytes.</summary>
    public static EditableLevel FromPackageBytes(byte[] packageBytes)
    {
        if (packageBytes is null)
            throw new ArgumentNullException(nameof(packageBytes));

        using var package = PackageReader.Open(new MemoryStream(packageBytes));
        return FromPackage(package);
    }

    /// <summary>Loads an editable level from an already-opened package, using its first level resource.</summary>
    public static EditableLevel FromPackage(Package package)
    {
        if (package is null)
            throw new ArgumentNullException(nameof(package));

        return FromPackage(package, FindLevelPath(package));
    }

    /// <summary>Loads the level at <paramref name="levelPath"/> from an already-opened package.</summary>
    public static EditableLevel FromPackage(Package package, ResourcePath levelPath)
    {
        if (package is null)
            throw new ArgumentNullException(nameof(package));
        if (!package.Contains(levelPath))
            throw new LevelContentException($"Package does not contain a resource at '{levelPath}'.");

        var entry = package.GetEntry(levelPath);
        if (entry.Kind != ResourceKind.Level)
            throw new LevelContentException($"Resource '{levelPath}' is not a level resource (kind '{entry.Kind}').");

        var levelDefinition = LevelContentSerializer.ReadLevel(package.ReadBytes(levelPath));

        var tileSetReference = levelDefinition.TileSet;
        var boundTileSet = EditableTileSetReader.FromPackage(package, tileSetReference);
        var tiles = boundTileSet.Tiles;
        var terrainSets = boundTileSet.TerrainSets;

        var expectedCells = levelDefinition.Width * levelDefinition.Height;
        var layers = new List<EditableLayer>(levelDefinition.Layers.Count);
        foreach (var layer in levelDefinition.Layers)
        {
            // DiVoid #7551 Phase 3: an omitted/empty Terrain channel means "nothing painted" — pad it to a
            // full all-empty array so EditableLayer's invariant (Terrain.Length == Cells.Length) holds for
            // every loaded level, old or new.
            var terrain = layer.Terrain.Count == expectedCells ? layer.Terrain.ToArray() : null;
            layers.Add(new EditableLayer(
                layer.Name,
                layer.Collision,
                layer.ScrollSpeed,
                layer.Repeat,
                layer.Cells.ToArray(),
                terrain));
        }

        // DiVoid #7747: rehydrate the level's own scripted-behavior data (DiVoid #7738) the same way every
        // other level property already round-trips, so EditableLevelSnapshot.ToResolvedLevel — what
        // PlaytestOverlay actually plays — can carry it into the playtest run. Read-through only; there is
        // still no authoring UX for any of this (P3, per #7738's own known-gap note).
        var tileBehaviorOverrides = new Dictionary<(int Layer, GridPosition Cell), ResolvedBehaviorBinding?>();
        foreach (var overrideEntry in levelDefinition.TileBehaviorOverrides) {
            var role = $"Tile behavior override at layer {overrideEntry.Layer} cell ({overrideEntry.Cell.X},{overrideEntry.Cell.Y})";
            tileBehaviorOverrides[(overrideEntry.Layer, overrideEntry.Cell)] = overrideEntry.Removed
                ? null
                : EditableBehaviorBindings.Resolve(package, overrideEntry.Binding, role);
        }

        var triggers = new List<ResolvedAreaTrigger>(levelDefinition.Triggers.Count);
        foreach (var trigger in levelDefinition.Triggers) {
            triggers.Add(new ResolvedAreaTrigger {
                Name = trigger.Name,
                X = trigger.X,
                Y = trigger.Y,
                Width = trigger.Width,
                Height = trigger.Height,
                Binding = EditableBehaviorBindings.Resolve(package, trigger.Binding, $"Trigger '{trigger.Name}'")
                    ?? throw new LevelContentException($"Trigger '{trigger.Name}' declares no behavior binding."),
            });
        }

        var levelScript = EditableBehaviorBindings.Resolve(package, levelDefinition.LevelScript, "Level script");

        // Loaded from a real package resource, so this level already occupies a stable slot: isAttached
        // is true and levelPath is preserved verbatim, even if it predates the per-resource namespacing
        // scheme (a legacy fixed-constant path like "level.json" still round-trips fine — this correction
        // does not force a migration of already-saved content). tileSetReference is preserved verbatim too
        // — whatever shared resource the level already binds, unchanged by loading it into the editor.
        //
        // The level's own display name comes from ITS resource path, never the package's manifest name
        // (DiVoid #7571/#7572 — package identity is independent of level naming; a package can hold many
        // levels, none of which "is" the package). Mirrors FolderPackageSource's DisplayNameFor so a
        // level's name matches exactly what the browser's resource list already shows for it.
        return new EditableLevel(
            DisplayNameFromPath(levelPath),
            levelPath,
            tileSetReference,
            levelDefinition.TileSize,
            levelDefinition.Width,
            levelDefinition.Height,
            levelDefinition.BackgroundColor,
            new Dictionary<string, GridPosition>(levelDefinition.Spawns),
            levelDefinition.DefaultSpawn,
            tiles,
            layers,
            isAttached: true,
            terrainSets: terrainSets,
            tileBehaviorOverrides: tileBehaviorOverrides,
            triggers: triggers,
            levelScript: levelScript);
    }

    private static ResourcePath FindLevelPath(Package package)
    {
        foreach (var entry in package.Manifest.Resources)
        {
            if (entry.Kind == ResourceKind.Level)
                return entry.Path;
        }

        throw new LevelContentException("Package does not contain a level resource.");
    }

    private static string DisplayNameFromPath(ResourcePath path)
    {
        var value = path.Value;
        var slash = value.LastIndexOf('/');
        var fileName = slash >= 0 ? value[(slash + 1)..] : value;
        var dot = fileName.LastIndexOf('.');
        return dot > 0 ? fileName[..dot] : fileName;
    }
}
