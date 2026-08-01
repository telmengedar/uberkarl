namespace Uberkarl.Packages;

public sealed class PackageBuilder
{
    private readonly List<PendingResource> resources = new();
    private readonly List<PackageDependency> dependencies = new();
    private readonly HashSet<string> takenPaths = new(StringComparer.Ordinal);

    public PackageId Id { get; private set; } = PackageId.New();

    public string Name { get; private set; } = string.Empty;

    public string Version { get; private set; } = "0.1.0";

    public Attribution? Attribution { get; private set; }

    public PackageId? ForkedFrom { get; private set; }

    public IReadOnlyList<PendingResource> Resources => resources;

    public IReadOnlyList<PackageDependency> Dependencies => dependencies;

    public PackageBuilder WithId(PackageId id)
    {
        if (id.IsSelf)
            throw new ArgumentException("A package must have a concrete id, not the self reference.", nameof(id));
        Id = id;
        return this;
    }

    public PackageBuilder WithName(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        return this;
    }

    public PackageBuilder WithVersion(string version)
    {
        Version = version ?? throw new ArgumentNullException(nameof(version));
        return this;
    }

    public PackageBuilder WithAttribution(Attribution attribution)
    {
        Attribution = attribution ?? throw new ArgumentNullException(nameof(attribution));
        return this;
    }

    public PackageBuilder WithForkedFrom(PackageId origin)
    {
        ForkedFrom = origin;
        return this;
    }

    public PackageBuilder AddResource(string kind, ResourcePath path, byte[] payload, string? mediaType = null, Attribution? attribution = null)
    {
        if (string.IsNullOrWhiteSpace(kind))
            throw new ArgumentException("Resource kind must not be empty.", nameof(kind));
        if (payload is null)
            throw new ArgumentNullException(nameof(payload));
        if (!takenPaths.Add(path.Value))
            throw new ArgumentException($"Duplicate resource path '{path}'.", nameof(path));

        resources.Add(new PendingResource(path, kind, mediaType ?? PackageFormat.DefaultMediaType, payload, attribution));
        return this;
    }

    public PackageBuilder AddLicense(ResourcePath path, string license, byte[] payload)
    {
        AddResource(ResourceKind.License, path, payload, "text/plain");
        Attribution ??= new Attribution { License = license, LicenseResource = path };
        return this;
    }

    public PackageBuilder AddDependency(PackageDependency dependency)
    {
        dependencies.Add(dependency ?? throw new ArgumentNullException(nameof(dependency)));
        return this;
    }

    public PackageManifest BuildManifest()
    {
        var entries = resources
            .OrderBy(resource => resource.Path.Value, StringComparer.Ordinal)
            .Select(resource => new ResourceEntry
            {
                Path = resource.Path,
                Kind = resource.Kind,
                MediaType = resource.MediaType,
                ByteLength = resource.Payload.LongLength,
                Attribution = resource.Attribution,
            })
            .ToArray();

        return new PackageManifest
        {
            FormatVersion = PackageFormat.CurrentFormatVersion,
            Id = Id,
            Name = Name,
            Version = Version,
            Attribution = Attribution,
            ForkedFrom = ForkedFrom,
            Resources = entries,
            Dependencies = dependencies.ToArray(),
        };
    }

    public void Write(Stream destination) => PackageWriter.Write(this, destination);

    public void Write(string path)
    {
        using var stream = File.Create(path);
        Write(stream);
    }
}
