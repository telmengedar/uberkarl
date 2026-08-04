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

    /// <summary>
    /// Adds a resource at <paramref name="path"/>, or replaces it in place if the path is already
    /// staged — unlike <see cref="AddResource"/>, which throws on a duplicate path. This is the
    /// primitive the package-as-VFS merge (DiVoid #7571/#7572) needs: composing a level's contributions
    /// onto an existing archive's carried-forward resources must be able to overwrite the level's own
    /// paths while every sibling path is added exactly once via <see cref="SeedFrom"/>. Preserves the
    /// existing entry's position when replacing (so manifest ordering stays stable across a resave).
    /// </summary>
    public PackageBuilder AddOrReplaceResource(string kind, ResourcePath path, byte[] payload, string? mediaType = null, Attribution? attribution = null)
    {
        if (string.IsNullOrWhiteSpace(kind))
            throw new ArgumentException("Resource kind must not be empty.", nameof(kind));
        if (payload is null)
            throw new ArgumentNullException(nameof(payload));

        var replacement = new PendingResource(path, kind, mediaType ?? PackageFormat.DefaultMediaType, payload, attribution);
        var existingIndex = resources.FindIndex(resource => resource.Path.Value == path.Value);
        if (existingIndex >= 0)
            resources[existingIndex] = replacement;
        else
        {
            takenPaths.Add(path.Value);
            resources.Add(replacement);
        }

        return this;
    }

    /// <summary>
    /// Resets this builder to the identity and full resource set of <paramref name="existingPackage"/> —
    /// every existing resource's payload is read back into memory and staged, so a caller can then use
    /// <see cref="AddOrReplaceResource"/> to overwrite just the paths it owns and <see cref="Write"/> to
    /// produce merged bytes that carry every untouched sibling forward unchanged. This is the "seed from
    /// an existing package" capability the package-as-VFS save model (DiVoid #7572) composes onto: the
    /// archive's <see cref="PackageManifest.Id"/>/Name/Version/Attribution/ForkedFrom and its dependency
    /// list are copied verbatim — a merge never mutates identity, only content.
    /// </summary>
    public PackageBuilder SeedFrom(Package existingPackage)
    {
        if (existingPackage is null)
            throw new ArgumentNullException(nameof(existingPackage));

        var manifest = existingPackage.Manifest;
        Id = manifest.Id;
        Name = manifest.Name;
        Version = manifest.Version;
        Attribution = manifest.Attribution;
        ForkedFrom = manifest.ForkedFrom;

        dependencies.Clear();
        dependencies.AddRange(manifest.Dependencies);

        resources.Clear();
        takenPaths.Clear();
        foreach (var entry in manifest.Resources)
        {
            var payload = existingPackage.ReadBytes(entry.Path);
            takenPaths.Add(entry.Path.Value);
            resources.Add(new PendingResource(entry.Path, entry.Kind, entry.MediaType, payload, entry.Attribution));
        }

        return this;
    }

    /// <summary>
    /// Removes the staged resource at <paramref name="path"/>, if any — a no-op when nothing is staged
    /// there. The primitive a content-rewriting pass (e.g. <c>Uberkarl.Editor.TileSetMigration</c>
    /// deduplicating identical per-level tile sets, DiVoid #7551 Phase 1a) needs on top of
    /// <see cref="SeedFrom"/>: seed the whole archive, add-or-replace whatever moved, then drop whatever
    /// became orphaned.
    /// </summary>
    public PackageBuilder RemoveResource(ResourcePath path)
    {
        var index = resources.FindIndex(resource => resource.Path.Value == path.Value);
        if (index >= 0)
        {
            resources.RemoveAt(index);
            takenPaths.Remove(path.Value);
        }

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
