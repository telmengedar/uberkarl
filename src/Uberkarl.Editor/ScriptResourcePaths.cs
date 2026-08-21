using Uberkarl.Packages;

namespace Uberkarl.Editor;

/// <summary>Derives the in-package path of an authored script resource.</summary>
public static class ScriptResourcePaths
{
    private const string Directory = "scripts/";
    private const string Suffix = ".poo";

    /// <summary>Sanitizes a name into a per-character-lowercased, hyphenated slug, falling back to <c>"script"</c> for empty input.</summary>
    public static string Slugify(string name) => LevelResourcePaths.Slugify(name, fallback: "script");

    /// <summary>Disambiguates a candidate slug against a taken-check.</summary>
    public static string UniqueSlug(string baseSlug, Func<string, bool> isTaken) => LevelResourcePaths.UniqueSlug(baseSlug, isTaken);

    /// <summary>The in-package path a script with this slug is stored at.</summary>
    public static ResourcePath ScriptPath(string slug) => ResourcePath.Create($"{Directory}{slug}{Suffix}");

    /// <summary>The slug a <c>scripts/&lt;slug&gt;.poo</c> path was built from, or <c>null</c> when <paramref name="path"/> does not follow that convention.</summary>
    public static string? SlugFromScriptPath(ResourcePath path)
    {
        var value = path.Value;
        if (!value.StartsWith(Directory, StringComparison.Ordinal) || !value.EndsWith(Suffix, StringComparison.Ordinal))
            return null;

        var slug = value[Directory.Length..^Suffix.Length];
        return slug.Length == 0 ? null : slug;
    }
}
