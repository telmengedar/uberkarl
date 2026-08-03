using System.Text;
using NUnit.Framework;
using Uberkarl.Packages;

namespace Uberkarl.Packages.Tests;

[TestFixture]
public sealed class PackageSourceTests
{
    private string tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        tempDir = Path.Combine(Path.GetTempPath(), $"uberkarl-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, recursive: true);
    }

    [Test]
    public void ListPackages_ReturnsASummaryForEachRealPackage_OrderedByName()
    {
        WritePackage("b.pkg", "Zebra Pack", "1.0.0");
        WritePackage("a.pkg", "Antelope Pack", "2.0.0");

        var source = new FolderPackageSource(tempDir);
        var summaries = source.ListPackages();

        Assert.Multiple(() =>
        {
            Assert.That(summaries, Has.Count.EqualTo(2));
            Assert.That(summaries[0].Name, Is.EqualTo("Antelope Pack"));
            Assert.That(summaries[1].Name, Is.EqualTo("Zebra Pack"));
        });
    }

    [Test]
    public void ListPackages_EmptyFolder_ReturnsEmpty()
    {
        var source = new FolderPackageSource(tempDir);
        Assert.That(source.ListPackages(), Is.Empty);
    }

    [Test]
    public void ListPackages_MissingFolder_ReturnsEmpty()
    {
        var missing = Path.Combine(tempDir, "does-not-exist");
        var source = new FolderPackageSource(missing);
        Assert.That(source.ListPackages(), Is.Empty);
    }

    [Test]
    public void ListPackages_SkipsCorruptPackage_ButListsTheRest()
    {
        WritePackage("good.pkg", "Good Pack", "1.0.0");
        File.WriteAllText(Path.Combine(tempDir, "corrupt.pkg"), "not a zip file");

        var source = new FolderPackageSource(tempDir);
        var summaries = source.ListPackages();

        Assert.Multiple(() =>
        {
            Assert.That(summaries, Has.Count.EqualTo(1));
            Assert.That(summaries[0].Name, Is.EqualTo("Good Pack"));
        });
    }

    [Test]
    public void GetContents_ListsThePackagesResources()
    {
        WritePackage("pack.pkg", "Pack", "1.0.0",
            ("levels/one.json", ResourceKind.Level),
            ("sprites/hero.png", ResourceKind.Sprite));

        var source = new FolderPackageSource(tempDir);
        var handle = source.ListPackages().Single().Handle;

        var contents = source.GetContents(handle);

        Assert.Multiple(() =>
        {
            Assert.That(contents, Has.Count.EqualTo(2));
            Assert.That(contents.Select(entry => entry.Kind), Is.EquivalentTo(new[] { ResourceKind.Level, ResourceKind.Sprite }));
        });
    }

    [Test]
    public void Open_ReturnsAReadablePackage()
    {
        WritePackage("pack.pkg", "Pack", "1.0.0", ("sprites/hero.png", ResourceKind.Sprite));

        var source = new FolderPackageSource(tempDir);
        var handle = source.ListPackages().Single().Handle;

        using var package = source.Open(handle);

        Assert.That(package.ReadBytes(ResourcePath.Create("sprites/hero.png")), Is.EqualTo(Encoding.UTF8.GetBytes("PAYLOAD")));
    }

    [Test]
    public void Open_WithAStaleHandle_ThrowsPackageUnavailable()
    {
        WritePackage("pack.pkg", "Pack", "1.0.0");
        var source = new FolderPackageSource(tempDir);
        var handle = source.ListPackages().Single().Handle;

        File.Delete(Path.Combine(tempDir, "pack.pkg"));

        Assert.That(() => source.Open(handle), Throws.TypeOf<PackageUnavailableException>());
    }

    [Test]
    public void GetContents_WithAStaleHandle_ThrowsPackageUnavailable()
    {
        WritePackage("pack.pkg", "Pack", "1.0.0");
        var source = new FolderPackageSource(tempDir);
        var handle = source.ListPackages().Single().Handle;

        File.Delete(Path.Combine(tempDir, "pack.pkg"));

        Assert.That(() => source.GetContents(handle), Throws.TypeOf<PackageUnavailableException>());
    }

    [Test]
    public void Open_WhenTheFileIsLockedByAnotherProcess_ThrowsPackageUnavailableWithInnerException()
    {
        WritePackage("pack.pkg", "Pack", "1.0.0");
        var source = new FolderPackageSource(tempDir);
        var handle = source.ListPackages().Single().Handle;

        using var exclusiveLock = new FileStream(
            Path.Combine(tempDir, "pack.pkg"), FileMode.Open, FileAccess.Read, FileShare.None);

        var exception = Assert.Throws<PackageUnavailableException>(() => source.Open(handle));
        Assert.That(exception!.InnerException, Is.Not.Null);
    }

    [Test]
    public void Handles_ForTheSamePackage_AreEqual()
    {
        WritePackage("pack.pkg", "Pack", "1.0.0");
        var source = new FolderPackageSource(tempDir);

        var first = source.ListPackages().Single().Handle;
        var second = source.ListPackages().Single().Handle;

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(second));
            Assert.That(first.Equals((object)second), Is.True);
            Assert.That(first == second, Is.True);
            Assert.That(first != second, Is.False);
            Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()));
        });
    }

    [Test]
    public void Handles_ForDifferentPackages_AreNotEqual()
    {
        WritePackage("a.pkg", "A", "1.0.0");
        WritePackage("b.pkg", "B", "1.0.0");
        var source = new FolderPackageSource(tempDir);
        var summaries = source.ListPackages();

        Assert.Multiple(() =>
        {
            Assert.That(summaries[0].Handle, Is.Not.EqualTo(summaries[1].Handle));
            Assert.That(summaries[0].Handle.Equals("not a handle"), Is.False);
        });
    }

    [Test]
    public void Write_OverwritesThePackageAtTheHandle()
    {
        WritePackage("pack.pkg", "Pack", "1.0.0", ("sprites/hero.png", ResourceKind.Sprite));
        var source = new FolderPackageSource(tempDir);
        var handle = source.ListPackages().Single().Handle;

        var builder = new PackageBuilder().WithName("Pack").WithVersion("2.0.0");
        builder.AddResource(ResourceKind.Sprite, ResourcePath.Create("sprites/hero.png"), Encoding.UTF8.GetBytes("NEWPAYLOAD"));
        using var buffer = new MemoryStream();
        builder.Write(buffer);

        ((IWritablePackageSource)source).Write(handle, buffer.ToArray());

        using var reopened = source.Open(handle);
        Assert.That(reopened.Manifest.Version, Is.EqualTo("2.0.0"));
    }

    [Test]
    public void Write_LeavesNoTempFileBehindAfterASuccessfulAtomicReplace()
    {
        // DiVoid #7571/#7572 step 7: Write is now temp-then-rename (a merged save now carries every
        // sibling resource, so a torn write would corrupt the whole archive, not just one level).
        WritePackage("pack.pkg", "Pack", "1.0.0", ("sprites/hero.png", ResourceKind.Sprite));
        var source = new FolderPackageSource(tempDir);
        var handle = source.ListPackages().Single().Handle;

        var builder = new PackageBuilder().WithName("Pack").WithVersion("2.0.0");
        builder.AddResource(ResourceKind.Sprite, ResourcePath.Create("sprites/hero.png"), Encoding.UTF8.GetBytes("NEWPAYLOAD"));
        using var buffer = new MemoryStream();
        builder.Write(buffer);

        ((IWritablePackageSource)source).Write(handle, buffer.ToArray());

        var leftoverTempFiles = Directory.GetFiles(tempDir, "*.tmp-*");
        Assert.That(leftoverTempFiles, Is.Empty, "a successful write must not leave its temp file behind.");
    }

    [Test]
    public void Create_WritesANewPackageAndReturnsAResolvableHandle()
    {
        var source = new FolderPackageSource(tempDir);
        var builder = new PackageBuilder().WithName("Fresh Pack").WithVersion("0.1.0");
        builder.AddResource(ResourceKind.Sprite, ResourcePath.Create("sprites/hero.png"), Encoding.UTF8.GetBytes("PAYLOAD"));
        using var buffer = new MemoryStream();
        builder.Write(buffer);

        var handle = ((IWritablePackageSource)source).Create("Fresh Pack", buffer.ToArray());

        using var package = source.Open(handle);
        Assert.That(package.Manifest.Name, Is.EqualTo("Fresh Pack"));
    }

    private void WritePackage(string fileName, string name, string version, params (string path, string kind)[] resources)
    {
        var builder = new PackageBuilder().WithName(name).WithVersion(version);
        if (resources.Length == 0)
            builder.AddResource(ResourceKind.Sprite, ResourcePath.Create("sprites/placeholder.png"), Encoding.UTF8.GetBytes("PAYLOAD"));
        else
        {
            foreach (var (path, kind) in resources)
                builder.AddResource(kind, ResourcePath.Create(path), Encoding.UTF8.GetBytes("PAYLOAD"));
        }

        builder.Write(Path.Combine(tempDir, fileName));
    }
}
