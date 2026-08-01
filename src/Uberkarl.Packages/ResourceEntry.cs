namespace Uberkarl.Packages;

public sealed class ResourceEntry
{
    public ResourcePath Path { get; init; }

    public string Kind { get; init; } = string.Empty;

    public string MediaType { get; init; } = PackageFormat.DefaultMediaType;

    public long ByteLength { get; init; }

    public Attribution? Attribution { get; init; }
}
