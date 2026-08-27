using Uberkarl.Packages;

namespace Uberkarl.Editor;

/// <summary>The resource contributions a level save folds in beyond the level's own: its bound tile set's, and every object set its placements reference.</summary>
public static class LevelSaveOrchestration
{
    /// <summary>Attaches and binds the tile set if needed, then returns its contributions concatenated with every referenced object set's, read from <paramref name="objectSetSourcePackage"/>. The result holds each resource path at most once — the contract <see cref="PackageBuilder"/> requires of any contribution list handed to it — with the tile set's bytes winning over a stale object-set copy of the same path.</summary>
    public static IReadOnlyList<PendingResource> BuildExtraContributions(
        EditableLevel level,
        TileSetEditSession? tileSetSession,
        IReadOnlyList<ResourceEntry> existingResources,
        Package? objectSetSourcePackage)
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

        IReadOnlyList<PendingResource> objectSetContributions = ObjectSetMergeWriter.BuildContributionsForLevel(objectSetSourcePackage, level);

        return tileSetContributions.Concat(objectSetContributions).DistinctBy(contribution => contribution.Path).ToList();
    }
}
