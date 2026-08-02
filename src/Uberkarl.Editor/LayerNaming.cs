namespace Uberkarl.Editor;

/// <summary>
/// Auto-names new layers "Layer N" (rename itself is deferred — it needs gamepad text entry, exactly
/// like Save-As naming). Deterministic so callers/tests can pin the result: the smallest N ≥ 1 whose
/// name is not already in use.
/// </summary>
public static class LayerNaming
{
    private const string Prefix = "Layer ";

    /// <summary>The next unused "Layer N" name given the level's current layer names.</summary>
    public static string NextName(IEnumerable<string> existingNames)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        if (existingNames is not null)
        {
            foreach (var name in existingNames)
                used.Add(name);
        }

        var n = 1;
        while (used.Contains(Prefix + n))
            n++;

        return Prefix + n;
    }
}
