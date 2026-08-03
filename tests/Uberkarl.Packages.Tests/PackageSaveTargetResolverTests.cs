using System.Text;
using NUnit.Framework;
using Uberkarl.Packages;

namespace Uberkarl.Packages.Tests;

/// <summary>
/// Covers <see cref="PackageSaveTargetResolver"/> (DiVoid #7552 — the package browser's Save-As
/// confirm-overwrite safety net): a "+ New package…" name that collides with an existing package's
/// display name resolves to that package's handle instead of a distinct file. Pure, no Godot.
/// </summary>
[TestFixture]
public sealed class PackageSaveTargetResolverTests
{
    [Test]
    public void FindCollision_NoMatchingName_ReturnsNull()
    {
        var packages = new[] { Summary("Antelope Pack"), Summary("Zebra Pack") };

        Assert.That(PackageSaveTargetResolver.FindCollision(packages, "Giraffe Pack"), Is.Null);
    }

    [Test]
    public void FindCollision_ExactMatch_ReturnsThatPackagesHandle()
    {
        var match = Summary("Zebra Pack");
        var packages = new[] { Summary("Antelope Pack"), match };

        var collision = PackageSaveTargetResolver.FindCollision(packages, "Zebra Pack");

        Assert.That(collision, Is.EqualTo(match.Handle));
    }

    [Test]
    public void FindCollision_IsCaseInsensitive()
    {
        var match = Summary("Zebra Pack");
        var packages = new[] { match };

        Assert.That(PackageSaveTargetResolver.FindCollision(packages, "zebra pack"), Is.EqualTo(match.Handle));
    }

    [Test]
    public void FindCollision_TrimsSurroundingWhitespaceBeforeComparing()
    {
        var match = Summary("Zebra Pack");
        var packages = new[] { match };

        Assert.That(PackageSaveTargetResolver.FindCollision(packages, "  Zebra Pack  "), Is.EqualTo(match.Handle));
    }

    [Test]
    public void FindCollision_EmptyPackageList_ReturnsNull()
    {
        Assert.That(PackageSaveTargetResolver.FindCollision(System.Array.Empty<PackageSummary>(), "Anything"), Is.Null);
    }

    [Test]
    public void FindCollision_BlankProposedName_ReturnsNull()
    {
        var packages = new[] { Summary("Zebra Pack") };

        Assert.Multiple(() =>
        {
            Assert.That(PackageSaveTargetResolver.FindCollision(packages, ""), Is.Null);
            Assert.That(PackageSaveTargetResolver.FindCollision(packages, "   "), Is.Null);
            Assert.That(PackageSaveTargetResolver.FindCollision(packages, null!), Is.Null);
        });
    }

    [Test]
    public void FindCollision_NullPackageList_Throws()
    {
        Assert.Throws<System.ArgumentNullException>(() => PackageSaveTargetResolver.FindCollision(null!, "Name"));
    }

    private static PackageSummary Summary(string name)
    {
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"uberkarl-resolver-{System.Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(tempDir);
        var builder = new PackageBuilder().WithName(name).WithVersion("1.0.0");
        builder.AddResource(ResourceKind.Sprite, ResourcePath.Create("sprites/placeholder.png"), Encoding.UTF8.GetBytes("PAYLOAD"));
        var path = System.IO.Path.Combine(tempDir, "pack.pkg");
        builder.Write(path);

        var source = new FolderPackageSource(tempDir);
        return source.ListPackages()[0];
    }
}
