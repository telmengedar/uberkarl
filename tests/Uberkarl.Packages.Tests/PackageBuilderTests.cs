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

    // ----- AddOrReplaceResource (DiVoid #7571/#7572 — package-as-VFS merge primitive) -----

    [Test]
    public void AddOrReplaceResource_OnAFreshPath_AddsIt()
    {
        var builder = new PackageBuilder().WithName("Pack");

        builder.AddOrReplaceResource(ResourceKind.Sprite, ResourcePath.Create("sprites/hero.png"), Encoding.UTF8.GetBytes("payload"));

        var manifest = builder.BuildManifest();
        Assert.That(manifest.Resources, Has.Count.EqualTo(1));
    }

    [Test]
    public void AddOrReplaceResource_OnAnExistingPath_ReplacesInPlace_DoesNotDuplicate()
    {
        var path = ResourcePath.Create("levels/forest.json");
        var builder = new PackageBuilder().WithName("Pack");
        builder.AddOrReplaceResource(ResourceKind.Level, path, Encoding.UTF8.GetBytes("first"));

        builder.AddOrReplaceResource(ResourceKind.Level, path, Encoding.UTF8.GetBytes("second"));

        var manifest = builder.BuildManifest();
        Assert.Multiple(() =>
        {
            Assert.That(manifest.Resources, Has.Count.EqualTo(1));
            Assert.That(manifest.Resources[0].ByteLength, Is.EqualTo(Encoding.UTF8.GetByteCount("second")));
        });
    }

    [Test]
    public void AddResource_StillThrowsOnDuplicate_EvenAfterAnAddOrReplaceOnTheSamePath()
    {
        var path = ResourcePath.Create("levels/forest.json");
        var builder = new PackageBuilder().WithName("Pack");
        builder.AddOrReplaceResource(ResourceKind.Level, path, Encoding.UTF8.GetBytes("first"));

        Assert.That(
            () => builder.AddResource(ResourceKind.Level, path, Encoding.UTF8.GetBytes("second")),
            Throws.TypeOf<ArgumentException>());
    }

    // ----- SeedFrom (DiVoid #7571/#7572 — the seed-from-existing-package capability) -----

    [Test]
    public void SeedFrom_CopiesIdentityAndEveryResource()
    {
        var forkedFrom = PackageId.New();
        var original = new PackageBuilder()
            .WithName("Original Pack")
            .WithVersion("2.0.0")
            .WithAttribution(new Attribution { Author = "Toni", License = "CC-BY-4.0" })
            .WithForkedFrom(forkedFrom);
        original.AddResource(ResourceKind.Level, ResourcePath.Create("levels/a.json"), Encoding.UTF8.GetBytes("A"));
        original.AddResource(ResourceKind.Script, ResourcePath.Create("scripts/x.lua"), Encoding.UTF8.GetBytes("X"));
        using var package = Open(original);

        var seeded = new PackageBuilder().SeedFrom(package);

        Assert.Multiple(() =>
        {
            Assert.That(seeded.Id, Is.EqualTo(package.Id));
            Assert.That(seeded.Name, Is.EqualTo("Original Pack"));
            Assert.That(seeded.Version, Is.EqualTo("2.0.0"));
            Assert.That(seeded.Attribution!.Author, Is.EqualTo("Toni"));
            Assert.That(seeded.ForkedFrom, Is.EqualTo(forkedFrom));
            Assert.That(seeded.Resources, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void SeedFrom_ThenAddOrReplace_PreservesSiblingsAndIdentity_ReplacesOnlyThePath()
    {
        // This is the #7570 §16.7 boundary the package-as-VFS correction fixes: saving a level into a
        // package that already holds OTHER resources must not clobber them.
        var original = new PackageBuilder().WithName("Multi Pack").WithVersion("1.0.0");
        original.AddResource(ResourceKind.Level, ResourcePath.Create("levels/a.json"), Encoding.UTF8.GetBytes("A-original"));
        original.AddResource(ResourceKind.Script, ResourcePath.Create("scripts/x.lua"), Encoding.UTF8.GetBytes("X"));
        using var package = Open(original);
        var originalId = package.Id;

        var merged = new PackageBuilder().SeedFrom(package);
        merged.AddOrReplaceResource(ResourceKind.Level, ResourcePath.Create("levels/a.json"), Encoding.UTF8.GetBytes("A-edited"));
        using var mergedPackage = Open(merged);

        Assert.Multiple(() =>
        {
            Assert.That(mergedPackage.Id, Is.EqualTo(originalId));
            Assert.That(mergedPackage.Manifest.Name, Is.EqualTo("Multi Pack"));
            Assert.That(mergedPackage.Manifest.Resources, Has.Count.EqualTo(2));
            Assert.That(mergedPackage.ReadBytes(ResourcePath.Create("levels/a.json")), Is.EqualTo(Encoding.UTF8.GetBytes("A-edited")));
            Assert.That(mergedPackage.ReadBytes(ResourcePath.Create("scripts/x.lua")), Is.EqualTo(Encoding.UTF8.GetBytes("X")));
        });
    }

    [Test]
    public void SeedFrom_ThenAddNewPath_AddsASecondResource_WithoutDisturbingTheFirst()
    {
        // Two distinctly-pathed levels in one package must coexist.
        var original = new PackageBuilder().WithName("Two Levels");
        original.AddResource(ResourceKind.Level, ResourcePath.Create("levels/a.json"), Encoding.UTF8.GetBytes("A"));
        using var package = Open(original);

        var merged = new PackageBuilder().SeedFrom(package);
        merged.AddOrReplaceResource(ResourceKind.Level, ResourcePath.Create("levels/b.json"), Encoding.UTF8.GetBytes("B"));
        using var mergedPackage = Open(merged);

        Assert.Multiple(() =>
        {
            Assert.That(mergedPackage.Manifest.Resources, Has.Count.EqualTo(2));
            Assert.That(mergedPackage.ReadBytes(ResourcePath.Create("levels/a.json")), Is.EqualTo(Encoding.UTF8.GetBytes("A")));
            Assert.That(mergedPackage.ReadBytes(ResourcePath.Create("levels/b.json")), Is.EqualTo(Encoding.UTF8.GetBytes("B")));
        });
    }

    // ----- RemoveResource (DiVoid #7551 Phase 1a — the migration-tool primitive) -----

    [Test]
    public void RemoveResource_DropsAStagedResource_AndFreesItsPathForReuse()
    {
        var path = ResourcePath.Create("tilesets/duplicate.json");
        var builder = new PackageBuilder().WithName("Pack");
        builder.AddResource(ResourceKind.TileSet, path, Encoding.UTF8.GetBytes("first"));

        builder.RemoveResource(path);
        // The path is free again — re-adding it (rather than replacing) must not throw a duplicate error.
        builder.AddResource(ResourceKind.TileSet, path, Encoding.UTF8.GetBytes("second"));
        using var package = Open(builder);

        Assert.Multiple(() =>
        {
            Assert.That(package.Manifest.Resources, Has.Count.EqualTo(1));
            Assert.That(package.ReadBytes(path), Is.EqualTo(Encoding.UTF8.GetBytes("second")));
        });
    }

    [Test]
    public void RemoveResource_ForAPathNotStaged_IsANoOp()
    {
        var builder = new PackageBuilder().WithName("Pack");
        builder.AddResource(ResourceKind.Script, ResourcePath.Create("scripts/x"), Encoding.UTF8.GetBytes("x"));

        builder.RemoveResource(ResourcePath.Create("scripts/does-not-exist"));
        using var package = Open(builder);

        Assert.That(package.Manifest.Resources, Has.Count.EqualTo(1));
    }

    [Test]
    public void SeedFrom_ThenRemoveResource_DropsExactlyThatSiblingAndKeepsTheRest()
    {
        var original = new PackageBuilder().WithName("Pack");
        original.AddResource(ResourceKind.TileSet, ResourcePath.Create("tilesets/a.json"), Encoding.UTF8.GetBytes("A"));
        original.AddResource(ResourceKind.TileSet, ResourcePath.Create("tilesets/b.json"), Encoding.UTF8.GetBytes("B"));
        using var package = Open(original);

        var merged = new PackageBuilder().SeedFrom(package);
        merged.RemoveResource(ResourcePath.Create("tilesets/b.json"));
        using var mergedPackage = Open(merged);

        Assert.Multiple(() =>
        {
            Assert.That(mergedPackage.Contains(ResourcePath.Create("tilesets/a.json")), Is.True);
            Assert.That(mergedPackage.Contains(ResourcePath.Create("tilesets/b.json")), Is.False);
        });
    }

    private static Package Open(PackageBuilder builder)
    {
        using var buffer = new MemoryStream();
        builder.Write(buffer);
        return PackageReader.Open(new MemoryStream(buffer.ToArray()));
    }
}
