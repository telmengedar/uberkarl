namespace Uberkarl.Packages;

public sealed class PackageRegistry : IResourceResolver, IDisposable
{
    private readonly Dictionary<PackageId, Package> packages = new();

    public PackageRegistry(Package origin)
    {
        Origin = origin ?? throw new ArgumentNullException(nameof(origin));
        packages[origin.Id] = origin;
    }

    public Package Origin { get; }

    public PackageRegistry Add(Package package)
    {
        if (package is null)
            throw new ArgumentNullException(nameof(package));
        packages[package.Id] = package;
        return this;
    }

    public byte[] Resolve(ResourceReference reference)
    {
        var targetId = reference.IsSelf ? Origin.Id : reference.Package;
        if (!packages.TryGetValue(targetId, out var package))
            throw new UnresolvedReferenceException(reference, "referenced package is not registered.");
        return package.Resolve(ResourceReference.ToSelf(reference.Path));
    }

    public bool TryResolve(ResourceReference reference, out byte[] payload)
    {
        var targetId = reference.IsSelf ? Origin.Id : reference.Package;
        if (packages.TryGetValue(targetId, out var package) && package.Contains(reference.Path))
        {
            payload = package.Resolve(ResourceReference.ToSelf(reference.Path));
            return true;
        }

        payload = Array.Empty<byte>();
        return false;
    }

    public void Dispose()
    {
        foreach (var package in packages.Values)
            package.Dispose();
        packages.Clear();
    }
}
