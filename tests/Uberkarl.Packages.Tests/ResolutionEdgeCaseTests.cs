using System.Text;
using NUnit.Framework;
using Uberkarl.Packages;

namespace Uberkarl.Packages.Tests;

[TestFixture]
public sealed class ResolutionEdgeCaseTests
{
    [Test]
    public void PackageId_TryParseHandlesValidInvalidAndSelf()
    {
        var id = PackageId.New();

        Assert.Multiple(() =>
        {
            Assert.That(PackageId.TryParse(id.ToString(), out var parsed), Is.True);
            Assert.That(parsed, Is.EqualTo(id));
            Assert.That(PackageId.TryParse("self", out var self), Is.True);
            Assert.That(self.IsSelf, Is.True);
            Assert.That(PackageId.TryParse("not-a-guid", out _), Is.False);
            Assert.That(PackageId.TryParse(null, out _), Is.False);
        });
    }

    [Test]
    public void Identities_InequalityOperatorsBehave()
    {
        var a = PackageId.New();
        var b = PackageId.New();
        var pathA = ResourcePath.Create("a");
        var pathB = ResourcePath.Create("b");

        Assert.Multiple(() =>
        {
            Assert.That(a != b, Is.True);
            Assert.That(pathA != pathB, Is.True);
            Assert.That(new ResourceReference(a, pathA) != new ResourceReference(a, pathB), Is.True);
        });
    }

    [Test]
    public void Package_ReadingUnknownPathThrowsResourceNotFound()
    {
        var pack = new PackageBuilder().WithName("Pack");
        pack.AddResource(ResourceKind.Sprite, ResourcePath.Create("sprites/hero"), Encoding.UTF8.GetBytes("data"));

        using var buffer = new MemoryStream();
        pack.Write(buffer);
        buffer.Position = 0;

        using var package = PackageReader.Open(buffer, leaveOpen: true);
        var missing = ResourcePath.Create("sprites/villain");

        Assert.Multiple(() =>
        {
            Assert.That(() => package.ReadBytes(missing), Throws.TypeOf<ResourceNotFoundException>());
            Assert.That(() => package.GetEntry(missing), Throws.TypeOf<ResourceNotFoundException>());
            Assert.That(package.Contains(missing), Is.False);
        });
    }

    [Test]
    public void Registry_TryResolveReturnsTrueForKnownReference()
    {
        var path = ResourcePath.Create("sprites/hero");
        var bytes = Encoding.UTF8.GetBytes("hero");
        var pack = new PackageBuilder().WithName("Pack");
        pack.AddResource(ResourceKind.Sprite, path, bytes);
        var id = pack.Id;

        using var buffer = new MemoryStream();
        pack.Write(buffer);
        buffer.Position = 0;

        using var package = PackageReader.Open(buffer, leaveOpen: true);
        var registry = new PackageRegistry(package);

        Assert.Multiple(() =>
        {
            Assert.That(registry.TryResolve(new ResourceReference(id, path), out var resolved), Is.True);
            Assert.That(resolved, Is.EqualTo(bytes));
        });
    }

    [Test]
    public void Package_ResolveRejectsReferenceToAnotherPackage()
    {
        var path = ResourcePath.Create("sprites/hero");
        var pack = new PackageBuilder().WithName("Pack");
        pack.AddResource(ResourceKind.Sprite, path, Encoding.UTF8.GetBytes("hero"));

        using var buffer = new MemoryStream();
        pack.Write(buffer);
        buffer.Position = 0;

        using var package = PackageReader.Open(buffer, leaveOpen: true);
        var foreignReference = new ResourceReference(PackageId.New(), path);

        Assert.That(() => package.Resolve(foreignReference), Throws.TypeOf<UnresolvedReferenceException>());
    }
}
