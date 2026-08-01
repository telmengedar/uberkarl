namespace Uberkarl.Packages;

public interface IResourceResolver
{
    byte[] Resolve(ResourceReference reference);

    bool TryResolve(ResourceReference reference, out byte[] payload);
}
