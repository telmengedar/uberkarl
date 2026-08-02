using System.Text;
using NUnit.Framework;
using Uberkarl.Content;
using Uberkarl.Content.Json;
using Uberkarl.Editor;
using Uberkarl.Packages;

namespace Uberkarl.Editor.Tests;

/// <summary>
/// Covers <see cref="EditableLevelReader.FromPackage(Package, ResourcePath)"/> — the targeted-load
/// addition that lets the package browser open the resource the user actually chose in a multi-level
/// package, instead of always the first level found.
/// </summary>
[TestFixture]
public sealed class TargetedLoadTests
{
    private static readonly ResourcePath TileSetPath = ResourcePath.Create("tileset.json");
    private static readonly ResourcePath GrassPath = ResourcePath.Create("tiles/grass.png");
    private static readonly ResourcePath FirstLevelPath = ResourcePath.Create("levels/first.json");
    private static readonly ResourcePath SecondLevelPath = ResourcePath.Create("levels/second.json");

    [Test]
    public void FromPackage_WithExplicitPath_LoadsTheChosenLevel_NotTheFirst()
    {
        using var package = PackageReader.Open(new MemoryStream(BuildTwoLevelPackageBytes()));

        var chosen = EditableLevelReader.FromPackage(package, SecondLevelPath);

        Assert.Multiple(() =>
        {
            Assert.That(chosen.Width, Is.EqualTo(3));
            Assert.That(chosen.LevelPath, Is.EqualTo(SecondLevelPath));
        });
    }

    [Test]
    public void FromPackage_WithoutPath_StillLoadsTheFirstLevel()
    {
        using var package = PackageReader.Open(new MemoryStream(BuildTwoLevelPackageBytes()));

        var first = EditableLevelReader.FromPackage(package);

        Assert.Multiple(() =>
        {
            Assert.That(first.Width, Is.EqualTo(2));
            Assert.That(first.LevelPath, Is.EqualTo(FirstLevelPath));
        });
    }

    [Test]
    public void FromPackage_WithPathNotInPackage_Throws()
    {
        using var package = PackageReader.Open(new MemoryStream(BuildTwoLevelPackageBytes()));

        Assert.Throws<LevelContentException>(
            () => EditableLevelReader.FromPackage(package, ResourcePath.Create("levels/missing.json")));
    }

    [Test]
    public void FromPackage_WithPathToANonLevelResource_Throws()
    {
        using var package = PackageReader.Open(new MemoryStream(BuildTwoLevelPackageBytes()));

        var exception = Assert.Throws<LevelContentException>(() => EditableLevelReader.FromPackage(package, TileSetPath));
        Assert.That(exception!.Message, Does.Contain("not a level resource"));
    }

    private static byte[] BuildTwoLevelPackageBytes()
    {
        var tileSet = new TileSetDefinition
        {
            Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath), Collides = true } },
        };

        var first = new LevelDefinition
        {
            TileSize = 16,
            Width = 2,
            Height = 1,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Layers = new[] { new LayerDefinition { Name = "terrain", Cells = new[] { 1, 1 } } },
        };
        var second = new LevelDefinition
        {
            TileSize = 16,
            Width = 3,
            Height = 1,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Layers = new[] { new LayerDefinition { Name = "terrain", Cells = new[] { 1, 1, 1 } } },
        };

        var builder = new PackageBuilder().WithName("Two Levels").WithVersion("0.1.0");
        builder.AddResource(ResourceKind.TileGraphic, GrassPath, Encoding.UTF8.GetBytes("GRASS-PNG"), "image/png");
        builder.AddResource(ResourceKind.TileSet, TileSetPath, LevelContentSerializer.WriteTileSet(tileSet));
        builder.AddResource(ResourceKind.Level, FirstLevelPath, LevelContentSerializer.WriteLevel(first));
        builder.AddResource(ResourceKind.Level, SecondLevelPath, LevelContentSerializer.WriteLevel(second));

        using var buffer = new MemoryStream();
        builder.Write(buffer);
        return buffer.ToArray();
    }
}
