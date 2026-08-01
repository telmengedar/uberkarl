namespace Uberkarl.Packages;

public readonly struct ResourcePath : IEquatable<ResourcePath>
{
    private readonly string value;

    private ResourcePath(string value)
    {
        this.value = value;
    }

    public string Value => value ?? throw new InvalidOperationException("Resource path is uninitialized.");

    public static ResourcePath Create(string text)
    {
        var normalized = Normalize(text);
        Validate(normalized, text);
        return new ResourcePath(normalized);
    }

    public static bool TryCreate(string? text, out ResourcePath path)
    {
        if (text is not null)
        {
            var normalized = Normalize(text);
            if (IsValid(normalized))
            {
                path = new ResourcePath(normalized);
                return true;
            }
        }

        path = default;
        return false;
    }

    private static string Normalize(string text)
    {
        if (text is null)
            throw new ArgumentNullException(nameof(text));
        return text.Replace('\\', '/').Trim();
    }

    private static bool IsValid(string normalized)
    {
        if (normalized.Length == 0)
            return false;
        if (normalized.StartsWith('/'))
            return false;
        if (normalized.EndsWith('/'))
            return false;

        var segments = normalized.Split('/');
        foreach (var segment in segments)
        {
            if (segment.Length == 0)
                return false;
            if (segment is "." or "..")
                return false;
            foreach (var character in segment)
            {
                if (char.IsControl(character))
                    return false;
            }
        }

        return true;
    }

    private static void Validate(string normalized, string original)
    {
        if (!IsValid(normalized))
            throw new ArgumentException($"'{original}' is not a valid resource path.", nameof(original));
    }

    public bool Equals(ResourcePath other) => string.Equals(value, other.value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is ResourcePath other && Equals(other);

    public override int GetHashCode() => value is null ? 0 : StringComparer.Ordinal.GetHashCode(value);

    public override string ToString() => Value;

    public static bool operator ==(ResourcePath left, ResourcePath right) => left.Equals(right);

    public static bool operator !=(ResourcePath left, ResourcePath right) => !left.Equals(right);
}
