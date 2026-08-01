using NUnit.Framework;
using Uberkarl.Packages;

namespace Uberkarl.Packages.Tests;

[TestFixture]
public sealed class IdentityAndReferenceTests
{
    [Test]
    public void PackageId_NewValuesAreDistinctAndRoundTripAsText()
    {
        var first = PackageId.New();
        var second = PackageId.New();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(PackageId.Parse(first.ToString()), Is.EqualTo(first));
            Assert.That(first.IsSelf, Is.False);
        });
    }

    [Test]
    public void PackageId_SelfTokenRoundTrips()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PackageId.Self.IsSelf, Is.True);
            Assert.That(PackageId.Self.ToString(), Is.EqualTo("self"));
            Assert.That(PackageId.Parse("self"), Is.EqualTo(PackageId.Self));
        });
    }

    [Test]
    public void ResourcePath_NormalizesBackslashesAndTrims()
    {
        var path = ResourcePath.Create("  sprites\\hero  ");
        Assert.That(path.Value, Is.EqualTo("sprites/hero"));
    }

    [TestCase("")]
    [TestCase("/absolute")]
    [TestCase("../escape")]
    [TestCase("a/../b")]
    [TestCase("trailing/")]
    [TestCase("double//slash")]
    public void ResourcePath_RejectsUnsafeOrEmptyPaths(string candidate)
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => ResourcePath.Create(candidate), Throws.TypeOf<ArgumentException>());
            Assert.That(ResourcePath.TryCreate(candidate, out _), Is.False);
        });
    }

    [Test]
    public void ResourceReference_EqualityIsByPackageAndPath()
    {
        var id = PackageId.New();
        var path = ResourcePath.Create("sprites/hero");

        var a = new ResourceReference(id, path);
        var b = new ResourceReference(id, path);
        var c = new ResourceReference(PackageId.New(), path);

        Assert.Multiple(() =>
        {
            Assert.That(a, Is.EqualTo(b));
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
            Assert.That(a, Is.Not.EqualTo(c));
        });
    }

    [Test]
    public void ResourceReference_ParsesTextForm()
    {
        var id = PackageId.New();
        var reference = new ResourceReference(id, ResourcePath.Create("sprites/hero"));

        var parsed = ResourceReference.Parse(reference.ToString());

        Assert.That(parsed, Is.EqualTo(reference));
    }
}
