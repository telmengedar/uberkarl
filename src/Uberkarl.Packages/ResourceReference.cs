namespace Uberkarl.Packages;

public readonly struct ResourceReference : IEquatable<ResourceReference>
{
    public ResourceReference(PackageId package, ResourcePath path)
    {
        Package = package;
        Path = path;
    }

    public PackageId Package { get; }

    public ResourcePath Path { get; }

    public bool IsSelf => Package.IsSelf;

    public static ResourceReference ToSelf(ResourcePath path) => new(PackageId.Self, path);

    public static ResourceReference Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new FormatException("Resource reference must not be empty.");

        var separator = text.IndexOf(':');
        if (separator <= 0 || separator == text.Length - 1)
            throw new FormatException($"'{text}' is not a valid resource reference.");

        var package = PackageId.Parse(text[..separator]);
        var path = ResourcePath.Create(text[(separator + 1)..]);
        return new ResourceReference(package, path);
    }

    public bool Equals(ResourceReference other) => Package.Equals(other.Package) && Path.Equals(other.Path);

    public override bool Equals(object? obj) => obj is ResourceReference other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Package, Path);

    public override string ToString() => $"{Package}:{Path}";

    public static bool operator ==(ResourceReference left, ResourceReference right) => left.Equals(right);

    public static bool operator !=(ResourceReference left, ResourceReference right) => !left.Equals(right);
}
