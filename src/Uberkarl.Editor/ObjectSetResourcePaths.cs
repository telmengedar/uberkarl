using Uberkarl.Packages;

namespace Uberkarl.Editor;

/// <summary>The in-package path convention for a standalone object set and its sprites.</summary>
public static class ObjectSetResourcePaths
{
    /// <summary>Sanitizes a name into a lowercase, hyphenated slug.</summary>
    public static string Slugify(string name) => LevelResourcePaths.Slugify(name);

    /// <summary>Disambiguates a candidate slug against a taken-check.</summary>
    public static string UniqueSlug(string baseSlug, Func<string, bool> isTaken) => LevelResourcePaths.UniqueSlug(baseSlug, isTaken);

    /// <summary>The in-package path an object set with this slug is stored at.</summary>
    public static ResourcePath ObjectSetPath(string slug) => ResourcePath.Create($"objectsets/{slug}.json");

    /// <summary>The in-package path an object type's graphic with this slug and object id is stored at.</summary>
    public static ResourcePath GraphicPath(string slug, string objectId) => ResourcePath.Create($"objects/{slug}/{objectId}.png");

    /// <summary>Extracts the slug out of an <c>objectsets/&lt;slug&gt;.json</c> path, or <c>null</c> when <paramref name="path"/> does not follow that convention.</summary>
    public static string? SlugFromObjectSetPath(ResourcePath path)
    {
        const string directory = "objectsets/";
        const string suffix = ".json";

        string value = path.Value;
        if (!value.StartsWith(directory, StringComparison.Ordinal) || !value.EndsWith(suffix, StringComparison.Ordinal))
            return null;

        string slug = value[directory.Length..^suffix.Length];
        return slug.Length == 0 ? null : slug;
    }
}
