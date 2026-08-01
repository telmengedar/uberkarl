using System.Text;
using NUnit.Framework;
using Uberkarl.Packages;

namespace Uberkarl.Packages.Tests;

[TestFixture]
public sealed class PackageBuilderTests
{
    [Test]
    public void AddResource_RejectsDuplicatePathWithinPackage()
    {
        var path = ResourcePath.Create("sprites/hero");
        var builder = new PackageBuilder().WithName("Pack");
        builder.AddResource(ResourceKind.Sprite, path, Encoding.UTF8.GetBytes("first"));

        Assert.That(
            () => builder.AddResource(ResourceKind.Sprite, path, Encoding.UTF8.GetBytes("second")),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void WithId_RejectsSelfIdentity()
    {
        var builder = new PackageBuilder();

        Assert.That(() => builder.WithId(PackageId.Self), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void BuildManifest_RecordsByteLengthAndDependencies()
    {
        var dependencyId = PackageId.New();
        var builder = new PackageBuilder().WithName("Pack");
        builder.AddResource(ResourceKind.Script, ResourcePath.Create("scripts/x"), Encoding.UTF8.GetBytes("abcde"));
        builder.AddDependency(new PackageDependency { Package = dependencyId, Name = "Other", Version = "2.0.0" });

        var manifest = builder.BuildManifest();

        Assert.Multiple(() =>
        {
            Assert.That(manifest.Resources, Has.Count.EqualTo(1));
            Assert.That(manifest.Resources[0].ByteLength, Is.EqualTo(5));
            Assert.That(manifest.Dependencies, Has.Count.EqualTo(1));
            Assert.That(manifest.Dependencies[0].Package, Is.EqualTo(dependencyId));
        });
    }

    [Test]
    public void Builder_MintsDistinctIdentitiesByDefault()
    {
        var first = new PackageBuilder().Id;
        var second = new PackageBuilder().Id;

        Assert.That(first, Is.Not.EqualTo(second));
    }
}
