using System.Text;
using NUnit.Framework;
using Uberkarl.Packages;

namespace Uberkarl.Packages.Tests;

[TestFixture]
public sealed class CrossPackageReferenceTests
{
    [Test]
    public void Registry_ResolvesResourceFromADependencyPackage()
    {
        var spritePath = ResourcePath.Create("sprites/hero");
        var spriteBytes = Encoding.UTF8.GetBytes("shared-hero-sprite");

        var spritePack = new PackageBuilder().WithName("Sprite Pack");
        spritePack.AddResource(ResourceKind.Sprite, spritePath, spriteBytes);
        var spritePackId = spritePack.Id;

        var levelPack = new PackageBuilder().WithName("Level Pack");
        levelPack.AddResource(ResourceKind.Level, ResourcePath.Create("levels/1-1"), Encoding.UTF8.GetBytes("LEVEL"));
        levelPack.AddDependency(new PackageDependency { Package = spritePackId, Name = "Sprite Pack", Version = "1.0.0" });

        using var levelBuffer = new MemoryStream();
        using var spriteBuffer = new MemoryStream();
        levelPack.Write(levelBuffer);
        spritePack.Write(spriteBuffer);
        levelBuffer.Position = 0;
        spriteBuffer.Position = 0;

        using var levelPackage = PackageReader.Open(levelBuffer, leaveOpen: true);
        using var spritePackage = PackageReader.Open(spriteBuffer, leaveOpen: true);

        var registry = new PackageRegistry(levelPackage).Add(spritePackage);

        var crossReference = new ResourceReference(spritePackId, spritePath);
        var resolved = registry.Resolve(crossReference);

        Assert.That(resolved, Is.EqualTo(spriteBytes));
    }

    [Test]
    public void Registry_ResolvesSelfReferenceAgainstOrigin()
    {
        var scriptPath = ResourcePath.Create("scripts/win");
        var scriptBytes = Encoding.UTF8.GetBytes("goal reached");

        var pack = new PackageBuilder().WithName("Level Pack");
        pack.AddResource(ResourceKind.Script, scriptPath, scriptBytes);

        using var buffer = new MemoryStream();
        pack.Write(buffer);
        buffer.Position = 0;

        using var package = PackageReader.Open(buffer, leaveOpen: true);
        var registry = new PackageRegistry(package);

        var resolved = registry.Resolve(ResourceReference.ToSelf(scriptPath));

        Assert.That(resolved, Is.EqualTo(scriptBytes));
    }

    [Test]
    public void Registry_FailsWhenReferencedPackageIsNotRegistered()
    {
        var pack = new PackageBuilder().WithName("Level Pack");
        pack.AddResource(ResourceKind.Level, ResourcePath.Create("levels/1-1"), Encoding.UTF8.GetBytes("LEVEL"));

        using var buffer = new MemoryStream();
        pack.Write(buffer);
        buffer.Position = 0;

        using var package = PackageReader.Open(buffer, leaveOpen: true);
        var registry = new PackageRegistry(package);

        var danglingReference = new ResourceReference(PackageId.New(), ResourcePath.Create("sprites/hero"));

        Assert.Multiple(() =>
        {
            Assert.That(() => registry.Resolve(danglingReference), Throws.TypeOf<UnresolvedReferenceException>());
            Assert.That(registry.TryResolve(danglingReference, out _), Is.False);
        });
    }
}
