namespace Uberkarl.Packages;

/// <summary>
/// What a package source's contents-selection step renders and selects, projected from a package's
/// <see cref="ResourceEntry"/> without reading the resource's payload.
/// </summary>
public sealed class ResourceSummary
{
    public ResourcePath Path { get; init; }

    public string Kind { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string MediaType { get; init; } = PackageFormat.DefaultMediaType;

    public long ByteLength { get; init; }
}
