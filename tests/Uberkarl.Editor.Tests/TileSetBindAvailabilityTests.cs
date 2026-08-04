using NUnit.Framework;
using Uberkarl.Packages;

namespace Uberkarl.Editor.Tests;

/// <summary>Covers <see cref="TileSetBindAvailability"/> — the engine-agnostic half of the "Bind Tileset… silently does nothing" bugfix (DiVoid #7551).</summary>
[TestFixture]
public sealed class TileSetBindAvailabilityTests
{
    [Test]
    public void UnavailableReason_NoSessionAndNoPackage_ReturnsANoLevelMessage()
    {
        string? reason = TileSetBindAvailability.UnavailableReason(hasSession: false, hasPackageContext: false);
        Assert.That(reason, Is.Not.Null);
    }

    [Test]
    public void UnavailableReason_SessionButNoPackageContext_ReturnsAnExplanation()
    {
        string? reason = TileSetBindAvailability.UnavailableReason(hasSession: true, hasPackageContext: false);

        Assert.That(reason, Is.Not.Null.And.Not.Empty, "must give feedback, never silently do nothing.");
    }

    [Test]
    public void UnavailableReason_SessionAndPackageContext_ReturnsNull_MeaningTheNormalListingApplies()
    {
        string? reason = TileSetBindAvailability.UnavailableReason(hasSession: true, hasPackageContext: true);
        Assert.That(reason, Is.Null);
    }

    static ResourceSummary TileSetEntry(string path, string displayName = "Tiles") => new ResourceSummary
    {
        Path = ResourcePath.Create(path),
        Kind = ResourceKind.TileSet,
        DisplayName = displayName,
    };

    static ResourceSummary NonTileSetEntry(string path) => new ResourceSummary
    {
        Path = ResourcePath.Create(path),
        Kind = ResourceKind.Level,
        DisplayName = "A Level",
    };

    [Test]
    public void SelectBindableSiblings_SinglePackageTileSet_MatchingCurrent_ReturnsEmpty()
    {
        ResourcePath onlyPath = ResourcePath.Create("tilesets/shared.json");
        var contents = new[] { TileSetEntry("tilesets/shared.json") };
        ResourceReference current = ResourceReference.ToSelf(onlyPath);

        var siblings = TileSetBindAvailability.SelectBindableSiblings(contents, current);

        Assert.That(siblings, Is.Empty);
    }

    [Test]
    public void SelectBindableSiblings_MultipleTileSets_ExcludesOnlyTheCurrentOne()
    {
        ResourcePath currentPath = ResourcePath.Create("tilesets/a.json");
        var contents = new[]
        {
            TileSetEntry("tilesets/a.json", "A"),
            TileSetEntry("tilesets/b.json", "B"),
            TileSetEntry("tilesets/c.json", "C"),
        };
        ResourceReference current = ResourceReference.ToSelf(currentPath);

        var siblings = TileSetBindAvailability.SelectBindableSiblings(contents, current);

        Assert.Multiple(() =>
        {
            Assert.That(siblings, Has.Count.EqualTo(2));
            Assert.That(siblings.Select(s => s.DisplayName), Is.EquivalentTo(new[] { "B", "C" }));
        });
    }

    [Test]
    public void SelectBindableSiblings_IgnoresNonTileSetResources()
    {
        var contents = new[]
        {
            TileSetEntry("tilesets/a.json", "A"),
            NonTileSetEntry("levels/demo.json"),
        };
        ResourceReference current = ResourceReference.ToSelf(ResourcePath.Create("tilesets/zzz-not-present.json"));

        var siblings = TileSetBindAvailability.SelectBindableSiblings(contents, current);

        Assert.Multiple(() =>
        {
            Assert.That(siblings, Has.Count.EqualTo(1));
            Assert.That(siblings[0].DisplayName, Is.EqualTo("A"));
        });
    }

    [Test]
    public void SelectBindableSiblings_EmptyPackage_ReturnsEmpty()
    {
        var siblings = TileSetBindAvailability.SelectBindableSiblings(
            Array.Empty<ResourceSummary>(), ResourceReference.ToSelf(ResourcePath.Create("tilesets/a.json")));

        Assert.That(siblings, Is.Empty);
    }

    [Test]
    public void SelectBindableSiblings_CurrentReferenceFromAForeignPackage_NeverExcludesBySelfPathMatch()
    {
        var contents = new[] { TileSetEntry("tilesets/a.json", "A") };
        ResourceReference foreignCurrent = new ResourceReference(PackageId.New(), ResourcePath.Create("tilesets/a.json"));

        var siblings = TileSetBindAvailability.SelectBindableSiblings(contents, foreignCurrent);

        Assert.That(siblings, Has.Count.EqualTo(1));
    }

    [Test]
    public void SelectBindableSiblings_NullContents_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TileSetBindAvailability.SelectBindableSiblings(null!, default));
    }
}
