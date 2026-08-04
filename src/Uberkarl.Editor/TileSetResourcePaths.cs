using Uberkarl.Packages;

namespace Uberkarl.Editor;

/// <summary>
/// Derives the per-resource, namespaced in-package paths a standalone <b>shared</b> tile-set resource
/// uses (DiVoid #7551 Phase 1a — promoting the tileset out of level ownership, per design #7580). Mirrors
/// <see cref="LevelResourcePaths"/>'s shape exactly (same slug scheme, same collision-avoidance strategy)
/// but addresses a tile set's own namespace, independent of any level: <c>tilesets/&lt;slug&gt;.json</c> and
/// <c>graphics/&lt;slug&gt;/&lt;tileId&gt;.png</c>, where <c>&lt;slug&gt;</c> is derived from the tile set's OWN
/// display name — never a level's. Two distinctly-named tile sets in one package can never collide; a
/// level that binds one references it by <see cref="Uberkarl.Packages.ResourceReference"/>, so this
/// namespace exists independently of however many levels reference it.
/// </summary>
public static class TileSetResourcePaths
{
    /// <summary>Sanitizes a name into a lowercase, hyphenated slug. Delegates to <see cref="LevelResourcePaths.Slugify"/> — the rule is generic, not level-specific.</summary>
    public static string Slugify(string name) => LevelResourcePaths.Slugify(name);

    /// <summary>Disambiguates a candidate slug against a taken-check. Delegates to <see cref="LevelResourcePaths.UniqueSlug"/> — the rule is generic, not level-specific.</summary>
    public static string UniqueSlug(string baseSlug, Func<string, bool> isTaken) => LevelResourcePaths.UniqueSlug(baseSlug, isTaken);

    /// <summary>The in-package path a tile set with this slug is stored at.</summary>
    public static ResourcePath TileSetPath(string slug) => ResourcePath.Create($"tilesets/{slug}.json");

    /// <summary>The in-package path a tile graphic with this slug and tile id is stored at.</summary>
    public static ResourcePath GraphicPath(string slug, int tileId) => ResourcePath.Create($"graphics/{slug}/{tileId}.png");

    /// <summary>
    /// The in-package path an animation frame beyond the tile's primary graphic is stored at (DiVoid #7551
    /// Phase 2, design #7580). <paramref name="frameNumber"/> is the OVERALL frame number (2, 3, …) — frame
    /// 1 is the tile's primary graphic at <see cref="GraphicPath"/>, never stored here.
    /// </summary>
    public static ResourcePath FramePath(string slug, int tileId, int frameNumber) => ResourcePath.Create($"graphics/{slug}/{tileId}-{frameNumber}.png");

    /// <summary>
    /// Extracts the slug back out of a <c>tilesets/&lt;slug&gt;.json</c> path (the inverse of
    /// <see cref="TileSetPath"/>), or <c>null</c> when <paramref name="path"/> does not follow that
    /// convention — used to keep a re-saved, already-attached tile set on its own established slug rather
    /// than re-deriving one from whatever it is currently named (mirrors
    /// <see cref="LevelResourcePaths.SlugFromLevelPath"/>).
    /// </summary>
    public static string? SlugFromTileSetPath(ResourcePath path)
    {
        const string directory = "tilesets/";
        const string suffix = ".json";

        var value = path.Value;
        if (!value.StartsWith(directory, StringComparison.Ordinal) || !value.EndsWith(suffix, StringComparison.Ordinal))
            return null;

        var slug = value[directory.Length..^suffix.Length];
        return slug.Length == 0 ? null : slug;
    }
}
