using System.Text;
using NUnit.Framework;
using Uberkarl.Content;
using Uberkarl.Content.Json;
using Uberkarl.Packages;

namespace Uberkarl.Editor.Tests;

/// <summary>Save/reload round-trip coverage for <see cref="ObjectSetMergeWriter"/>.</summary>
[TestFixture]
public sealed class ObjectSetRoundTripTests
{
    private const int TileSize = 16;
    private const int Width = 4;
    private const int Height = 2;

    private static readonly ResourcePath LevelPath = ResourcePath.Create("levels/demo.json");
    private static readonly ResourcePath TileSetPath = ResourcePath.Create("tileset.json");
    private static readonly ResourcePath GrassPath = ResourcePath.Create("tiles/grass.png");
    private static readonly ResourcePath ObjectSetPath = ResourcePath.Create("objectsets/demo.json");
    private static readonly ResourcePath PlatformGraphicPath = ResourcePath.Create("objects/platform.png");
    private static readonly ResourcePath JumpBlockGraphicPath = ResourcePath.Create("objects/jump-block.png");

    private static readonly byte[] PlatformGraphicBytes = Encoding.UTF8.GetBytes("PLATFORM-PNG");
    private static readonly byte[] JumpBlockGraphicBytes = Encoding.UTF8.GetBytes("JUMP-BLOCK-PNG");

    [Test]
    public void SaveAsNewPackage_WithPlacedObjects_RoundTripsTheObjectSetAndEveryObjectGraphic()
    {
        var packageBytes = BuildPackageBytes();
        var freshBytes = BuildFreshPackageBytes(packageBytes);
        var reloaded = EditableLevelReader.FromPackageBytes(freshBytes);

        using var freshPackage = PackageReader.Open(new MemoryStream(freshBytes));

        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Objects, Has.Count.EqualTo(2),
                "both placed objects must resolve after a fresh-package save -- BuildFresh must emit the object set, not rely on an existing archive already carrying it.");

            Assert.That(reloaded.Objects[0].Graphic, Is.EqualTo(PlatformGraphicBytes),
                "the platform's graphic bytes did not survive the fresh-package save.");
            Assert.That(reloaded.Objects[1].Graphic, Is.EqualTo(JumpBlockGraphicBytes),
                "the jump-block's graphic bytes did not survive the fresh-package save.");

            Assert.That(reloaded.Objects[0].CollisionRole, Is.EqualTo(ObjectCollisionRole.Solid),
                "the platform's collision role did not survive the fresh-package save.");
            Assert.That(reloaded.Objects[1].CollisionRole, Is.EqualTo(ObjectCollisionRole.Passthrough),
                "the jump-block's collision role did not survive the fresh-package save.");

            Assert.That(freshPackage.GetEntry(ObjectSetPath).Kind, Is.EqualTo(ResourceKind.ObjectSet),
                "the object set resource must be stamped with the objectset kind so package browsers can find it.");
            Assert.That(freshPackage.GetEntry(PlatformGraphicPath).Kind, Is.EqualTo(ResourceKind.Sprite),
                "an object graphic resource must be stamped with the sprite kind so package browsers can find it.");
        });
    }

    [Test]
    public void BuildContributions_WithNoObjectTypes_StillEmitsAnEmptyObjectSetDefinition()
    {
        var contributions = ObjectSetMergeWriter.BuildContributions(ObjectSetPath, Array.Empty<EditableObjectType>());

        Assert.Multiple(() =>
        {
            Assert.That(contributions, Has.Count.EqualTo(1),
                "an empty object type list must still contribute the (empty) object set definition -- the caller decides whether to call this at all, not this method.");
            Assert.That(contributions[0].Path, Is.EqualTo(ObjectSetPath));
            Assert.That(LevelContentSerializer.ReadObjectSet(contributions[0].Payload).Objects, Is.Empty);
        });
    }

    private static byte[] BuildFreshPackageBytes(byte[] packageBytes)
    {
        using var package = PackageReader.Open(new MemoryStream(packageBytes));
        var level = EditableLevelReader.FromPackage(package, LevelPath);
        var tileSet = EditableTileSetReader.FromPackage(package, ResourceReference.ToSelf(TileSetPath));
        var objectSetReference = level.Objects[0].Placement.ObjectSet;
        var objectTypes = EditableObjectSetReader.FromPackage(package, objectSetReference);

        var combined = LevelMergeWriter.BuildContributions(level)
            .Concat(TileSetMergeWriter.BuildContributions(tileSet))
            .Concat(ObjectSetMergeWriter.BuildContributions(objectSetReference.Path, objectTypes))
            .ToList();

        return PackageMergeWriter.BuildFresh("Fresh Object Pack", combined);
    }

    private static byte[] BuildPackageBytes()
    {
        var cells = new int[Width * Height];
        Array.Fill(cells, LayerDefinition.EmptyCell);

        var objectSet = new ObjectSetDefinition
        {
            Objects = new[]
            {
                new ObjectDefinition
                {
                    Id = "platform", Name = "Moving Platform",
                    Graphic = ResourceReference.ToSelf(PlatformGraphicPath), CollisionRole = ObjectCollisionRole.Solid,
                },
                new ObjectDefinition
                {
                    Id = "jump-block", Name = "Jump Block",
                    Graphic = ResourceReference.ToSelf(JumpBlockGraphicPath), CollisionRole = ObjectCollisionRole.Passthrough,
                },
            },
        };

        var level = new LevelDefinition
        {
            TileSize = TileSize,
            Width = Width,
            Height = Height,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Layers = new[] { new LayerDefinition { Name = "terrain", Collision = true, Cells = cells } },
            Objects = new[]
            {
                new ObjectPlacement { ObjectSet = ResourceReference.ToSelf(ObjectSetPath), ObjectId = "platform", Cell = new GridPosition(1, 0), Name = "platform-1" },
                new ObjectPlacement { ObjectSet = ResourceReference.ToSelf(ObjectSetPath), ObjectId = "jump-block", Cell = new GridPosition(2, 0), Name = "jump-block-1" },
            },
        };

        var tileSet = new TileSetDefinition
        {
            Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath), CollisionShape = CollisionShapeDefinition.Full } },
        };

        var builder = new PackageBuilder().WithName("Object Round Trip Pack").WithVersion("0.1.0");
        builder.AddResource(ResourceKind.TileGraphic, GrassPath, Encoding.UTF8.GetBytes("GRASS-PNG"), "image/png");
        builder.AddResource(ResourceKind.Sprite, PlatformGraphicPath, PlatformGraphicBytes, "image/png");
        builder.AddResource(ResourceKind.Sprite, JumpBlockGraphicPath, JumpBlockGraphicBytes, "image/png");
        builder.AddResource(ResourceKind.TileSet, TileSetPath, LevelContentSerializer.WriteTileSet(tileSet));
        builder.AddResource(ResourceKind.ObjectSet, ObjectSetPath, LevelContentSerializer.WriteObjectSet(objectSet));
        builder.AddResource(ResourceKind.Level, LevelPath, LevelContentSerializer.WriteLevel(level));

        using var buffer = new MemoryStream();
        builder.Write(buffer);
        return buffer.ToArray();
    }
}
