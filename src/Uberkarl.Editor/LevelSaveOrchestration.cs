using Uberkarl.Packages;

namespace Uberkarl.Editor;

/// <summary>The resource contributions a level save folds in beyond the level's own: its bound tile set's, and every object set its placements reference.</summary>
public static class LevelSaveOrchestration
{
    /// <summary>Attaches and binds the tile set if needed, then returns its contributions concatenated with the authored object set's and every other referenced object set's.</summary>
    public static IReadOnlyList<PendingResource> BuildExtraContributions(
        EditableLevel level,
        TileSetEditSession? tileSetSession,
        IReadOnlyList<ResourceEntry> existingResources,
        Package? objectSetSourcePackage,
        ObjectSetEditSession? objectSetSession = null)
    {
        if (level is null)
            throw new ArgumentNullException(nameof(level));
        if (existingResources is null)
            throw new ArgumentNullException(nameof(existingResources));

        IReadOnlyList<PendingResource> tileSetContributions = Array.Empty<PendingResource>();
        if (tileSetSession != null)
        {
            tileSetSession.EnsureAttached(existingResources);
            level.BindTileSet(ResourceReference.ToSelf(tileSetSession.TileSet.TileSetPath), tileSetSession.TileSet.Tiles, tileSetSession.TileSet.Scripts);
            tileSetContributions = tileSetSession.BuildContributions();
        }

        IReadOnlyList<PendingResource> objectSetContributions = Array.Empty<PendingResource>();
        ResourceReference? authoredObjectSet = null;
        if (objectSetSession != null && objectSetSession.Types.Count > 0)
        {
            ResourceReference before = objectSetSession.Reference;
            objectSetSession.EnsureAttached(existingResources);
            ResourceReference after = objectSetSession.Reference;
            if (after != before)
                level.RebindObjectSet(before, after);

            objectSetContributions = objectSetSession.BuildContributions();
            authoredObjectSet = after;
        }

        IReadOnlyList<PendingResource> otherObjectSetContributions = ObjectSetMergeWriter.BuildContributionsForLevel(objectSetSourcePackage, level, authoredObjectSet);

        return tileSetContributions.Concat(objectSetContributions).Concat(otherObjectSetContributions).ToList();
    }
}
