using System.Security.Cryptography;
using System.Text;
using Uberkarl.Content;
using Uberkarl.Content.Json;
using Uberkarl.Packages;

namespace Uberkarl.Editor;

/// <summary>
/// The mechanical migration pass design #7580 §12 asks for: within a package, dedup tile set resources
/// that multiple levels reference and that render identically, and rewrite every affected level's
/// <see cref="LevelDefinition.TileSet"/> onto ONE shared, surviving resource — removing the copy-per-level
/// redundancy Toni flagged without discarding any level's data or touching a genuinely distinct tile set.
///
/// <b>Scope (content-identical, same package):</b> two tile sets are considered "the same tileset" when
/// they declare the exact same tiles — same ids, same <see cref="TileDefinition.Collides"/>/<see cref="TileDefinition.Name"/>,
/// and byte-identical GRAPHIC content per tile (order-independent). This is deliberately content-aware
/// rather than a raw byte-comparison of the tileset.json payload: the redundancy this fixes is namespaced
/// PER LEVEL (<c>tilesets/&lt;level-slug&gt;.json</c> referencing <c>graphics/&lt;level-slug&gt;/&lt;id&gt;.png</c>),
/// so two levels saved from an unmodified starter palette produce tile sets whose PATHS (and therefore
/// whose serialized JSON, which embeds those paths) always differ even though the actual tiles are
/// pixel-for-pixel identical — a raw byte comparison would never catch the exact redundancy in play. A
/// level whose <c>TileSet</c> reference points cross-package is left untouched (out of scope — nothing to
/// migrate locally). This is intentionally NOT semantic/fuzzy deduplication (differently-authored tile
/// sets that happen to describe equivalent-but-not-identical content stay separate) — mechanical and
/// reviewable, per the design's "mechanical, reviewed pass" mandate.
///
/// For each group of content-identical tile sets, the FIRST one encountered (manifest order) becomes the
/// surviving canonical resource; every other level in the group is rewritten to reference it; the
/// now-orphaned duplicate tile set resources — and any tile graphic resource ONLY they used (not shared
/// with the surviving copy or any other surviving tile set) — are removed from the archive.
/// </summary>
public static class TileSetMigration
{
    public sealed class Result
    {
        public Result(byte[] bytes, int levelsRewritten, int duplicateTileSetsRemoved, int orphanedGraphicsRemoved)
        {
            Bytes = bytes;
            LevelsRewritten = levelsRewritten;
            DuplicateTileSetsRemoved = duplicateTileSetsRemoved;
            OrphanedGraphicsRemoved = orphanedGraphicsRemoved;
        }

        /// <summary>The migrated package's bytes (identical to the input when <see cref="LevelsRewritten"/> is 0).</summary>
        public byte[] Bytes { get; }

        /// <summary>How many levels had their <c>tileSet</c> reference rewritten onto a surviving shared resource.</summary>
        public int LevelsRewritten { get; }

        /// <summary>How many duplicate tile set resources were removed as a result.</summary>
        public int DuplicateTileSetsRemoved { get; }

        /// <summary>How many tile graphic resources, exclusively used by a removed duplicate, were removed alongside it.</summary>
        public int OrphanedGraphicsRemoved { get; }
    }

    public static Result Migrate(Package package)
    {
        if (package is null)
            throw new ArgumentNullException(nameof(package));

        var levelEntries = package.Manifest.Resources.Where(entry => entry.Kind == ResourceKind.Level).ToList();

        // Pass 1: group levels by their (self-referenced) tile set's CONTENT signature (see class doc for
        // why this is not a raw byte comparison). First sighting per signature wins as canonical; every
        // later level referencing a distinct PATH with the same signature is queued for rewrite.
        var canonicalPathBySignature = new Dictionary<string, ResourcePath>(StringComparer.Ordinal);
        var referencedPaths = new HashSet<string>(StringComparer.Ordinal);
        var rewrites = new List<(ResourcePath LevelPath, LevelDefinition Definition)>();

        foreach (var levelEntry in levelEntries)
        {
            var levelDefinition = LevelContentSerializer.ReadLevel(package.ReadBytes(levelEntry.Path));
            if (!levelDefinition.TileSet.IsSelf)
                continue; // cross-package reference: out of migration scope.

            var tileSetPath = levelDefinition.TileSet.Path;
            var signature = ContentSignature(package, LevelContentSerializer.ReadTileSet(package.ReadBytes(tileSetPath)));

            if (!canonicalPathBySignature.TryGetValue(signature, out var canonicalPath))
            {
                canonicalPath = tileSetPath;
                canonicalPathBySignature[signature] = canonicalPath;
            }

            referencedPaths.Add(canonicalPath.Value);

            if (!canonicalPath.Equals(tileSetPath))
                rewrites.Add((levelEntry.Path, WithTileSet(levelDefinition, ResourceReference.ToSelf(canonicalPath))));
        }

        if (rewrites.Count == 0)
        {
            using var unchanged = new MemoryStream();
            new PackageBuilder().SeedFrom(package).Write(unchanged);
            return new Result(unchanged.ToArray(), 0, 0, 0);
        }

        // Pass 2: every tile-set resource whose path never ended up "referenced" (i.e. it was some level's
        // OWN duplicate, now superseded) is orphaned. Graphics are orphaned too, but ONLY if no surviving
        // tile set (canonical or otherwise untouched) still points at them.
        var allTileSetPaths = package.Manifest.Resources.Where(entry => entry.Kind == ResourceKind.TileSet).Select(entry => entry.Path).ToList();
        var orphanedTileSetPaths = allTileSetPaths.Where(path => !referencedPaths.Contains(path.Value)).ToList();

        var survivingGraphicPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in allTileSetPaths.Where(path => !orphanedTileSetPaths.Contains(path)))
        {
            foreach (var tile in LevelContentSerializer.ReadTileSet(package.ReadBytes(path)).Tiles)
                if (tile.Graphic.IsSelf)
                    survivingGraphicPaths.Add(tile.Graphic.Path.Value);
        }

        var orphanedGraphicPaths = new List<ResourcePath>();
        foreach (var tileSetPath in orphanedTileSetPaths)
        {
            foreach (var tile in LevelContentSerializer.ReadTileSet(package.ReadBytes(tileSetPath)).Tiles)
                if (tile.Graphic.IsSelf && !survivingGraphicPaths.Contains(tile.Graphic.Path.Value))
                    orphanedGraphicPaths.Add(tile.Graphic.Path);
        }

        var builder = new PackageBuilder().SeedFrom(package);
        foreach (var (levelPath, definition) in rewrites)
            builder.AddOrReplaceResource(ResourceKind.Level, levelPath, LevelContentSerializer.WriteLevel(definition));
        foreach (var path in orphanedTileSetPaths)
            builder.RemoveResource(path);
        foreach (var path in orphanedGraphicPaths)
            builder.RemoveResource(path);

        using var buffer = new MemoryStream();
        builder.Write(buffer);
        return new Result(buffer.ToArray(), rewrites.Count, orphanedTileSetPaths.Count, orphanedGraphicPaths.Count);
    }

    // A tile set's render-determining content, order-independent over tiles: per declared tile (sorted by
    // id) — id, collides, name, and the hash of its graphic's actual bytes (not its path, which is exactly
    // what differs between two otherwise-identical per-level copies). A cross-package or missing graphic
    // cannot be content-compared, so it is folded in as an always-distinct marker (never dedups).
    private static string ContentSignature(Package package, TileSetDefinition tileSet)
    {
        var parts = new List<string>(tileSet.Tiles.Count);
        foreach (var tile in tileSet.Tiles.OrderBy(t => t.Id))
        {
            var graphicHash = tile.Graphic.IsSelf && package.Contains(tile.Graphic.Path)
                ? Convert.ToBase64String(SHA256.HashData(package.ReadBytes(tile.Graphic.Path)))
                : $"unresolved:{tile.Graphic}";
            parts.Add($"{tile.Id}|{tile.Collides}|{tile.Name}|{graphicHash}");
        }

        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(";", parts))));
    }

    private static LevelDefinition WithTileSet(LevelDefinition source, ResourceReference tileSet) => new()
    {
        TileSize = source.TileSize,
        Width = source.Width,
        Height = source.Height,
        TileSet = tileSet,
        BackgroundColor = source.BackgroundColor,
        Spawns = source.Spawns,
        DefaultSpawn = source.DefaultSpawn,
        Layers = source.Layers,
    };
}
