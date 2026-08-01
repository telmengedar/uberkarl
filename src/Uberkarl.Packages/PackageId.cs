namespace Uberkarl.Packages;

public readonly struct PackageId : IEquatable<PackageId>
{
    private const string SelfToken = "self";

    private readonly Guid value;

    private PackageId(Guid value)
    {
        this.value = value;
    }

    public static PackageId Self => default;

    public bool IsSelf => value == Guid.Empty;

    public static PackageId New() => new(Guid.NewGuid());

    public static PackageId Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new FormatException("Package id text must not be empty.");
        if (string.Equals(text, SelfToken, StringComparison.OrdinalIgnoreCase))
            return Self;
        if (!Guid.TryParse(text, out var parsed))
            throw new FormatException($"'{text}' is not a valid package id.");
        return new PackageId(parsed);
    }

    public static bool TryParse(string? text, out PackageId id)
    {
        if (!string.IsNullOrWhiteSpace(text) && string.Equals(text, SelfToken, StringComparison.OrdinalIgnoreCase))
        {
            id = Self;
            return true;
        }

        if (Guid.TryParse(text, out var parsed))
        {
            id = new PackageId(parsed);
            return true;
        }

        id = Self;
        return false;
    }

    public bool Equals(PackageId other) => value.Equals(other.value);

    public override bool Equals(object? obj) => obj is PackageId other && Equals(other);

    public override int GetHashCode() => value.GetHashCode();

    public override string ToString() => IsSelf ? SelfToken : value.ToString("D");

    public static bool operator ==(PackageId left, PackageId right) => left.Equals(right);

    public static bool operator !=(PackageId left, PackageId right) => !left.Equals(right);
}
