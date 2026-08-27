using System.Text;
using NUnit.Framework;
using Uberkarl.Content;
using Uberkarl.Content.Json;
using Uberkarl.Packages;

namespace Uberkarl.Editor.Tests;

/// <summary>Save/reload round-trip coverage for <see cref="ObjectSetMergeWriter"/> and <see cref="LevelSaveOrchestration"/>.</summary>
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
                "both placed objects must resolve after a fresh-package save.");

            Assert.That(reloaded.Objects[0].Graphic, Is.EqualTo(PlatformGraphicBytes),
                "the platform's graphic bytes did not survive the fresh-package save.");
            Assert.That(reloaded.Objects[1].Graphic, Is.EqualTo(JumpBlockGraphicBytes),
                "the jump-block's graphic bytes did not survive the fresh-package save.");

            Assert.That(reloaded.Objects[0].CollisionRole, Is.EqualTo(ObjectCollisionRole.Solid),
                "the platform's collision role did not survive the fresh-package save.");
            Assert.That(reloaded.Objects[1].CollisionRole, Is.EqualTo(ObjectCollisionRole.Passthrough),
                "the jump-block's collision role did not survive the fresh-package save.");

            Assert.That(freshPackage.GetEntry(ObjectSetPath).Kind, Is.EqualTo(ResourceKind.ObjectSet),
                "the object set resource must be stamped with the objectset kind.");
            Assert.That(freshPackage.GetEntry(PlatformGraphicPath).Kind, Is.EqualTo(ResourceKind.Sprite),
                "an object graphic resource must be stamped with the sprite kind.");
        });
    }

    [Test]
    public void SaveAsNewPackage_WithPlacementsInTwoDistinctObjectSets_RoundTripsBoth()
    {
        var packageBytes = BuildTwoObjectSetPackageBytes();
        var freshBytes = BuildFreshPackageBytes(packageBytes);
        var reloaded = EditableLevelReader.FromPackageBytes(freshBytes);

        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Objects, Has.Count.EqualTo(2),
                "both placements must resolve when their placements reference two distinct object sets.");
            Assert.That(reloaded.Objects[0].Graphic, Is.EqualTo(PlatformGraphicBytes),
                "the object set A placement's graphic did not survive the fresh-package save.");
            Assert.That(reloaded.Objects[1].Graphic, Is.EqualTo(JumpBlockGraphicBytes),
                "the object set B placement's graphic did not survive the fresh-package save.");
        });
    }

    [Test]
    public void SaveAsNewPackage_WithTwoObjectTypesSharingOneGraphic_DoesNotThrowAndRoundTripsBoth()
    {
        var packageBytes = BuildSharedGraphicPackageBytes();
        var freshBytes = BuildFreshPackageBytes(packageBytes);
        var reloaded = EditableLevelReader.FromPackageBytes(freshBytes);

        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Objects, Has.Count.EqualTo(2),
                "both placements must resolve when their object types share one graphic path.");
            Assert.That(reloaded.Objects[0].Graphic, Is.EqualTo(PlatformGraphicBytes),
                "the shared graphic did not survive the fresh-package save for the first placement.");
            Assert.That(reloaded.Objects[1].Graphic, Is.EqualTo(PlatformGraphicBytes),
                "the shared graphic did not survive the fresh-package save for the second placement.");
        });
    }

    [Test]
    public void MergeIntoDifferentExistingPackage_WithPlacedObjects_RoundTripsTheObjectSetAndEveryObjectGraphic()
    {
        var originBytes = BuildPackageBytes();
        var targetBytes = BuildOtherExistingPackageBytes();

        using var originPackage = PackageReader.Open(new MemoryStream(originBytes));
        using var targetPackage = PackageReader.Open(new MemoryStream(targetBytes));

        var level = EditableLevelReader.FromPackage(originPackage, LevelPath);
        var tileSet = EditableTileSetReader.FromPackage(originPackage, ResourceReference.ToSelf(TileSetPath));
        var tileSetSession = new TileSetEditSession(tileSet);
        var session = new LevelEditSession(level);
        session.AttachAsNewResource(targetPackage.Manifest.Resources);

        var extra = LevelSaveOrchestration.BuildExtraContributions(level, tileSetSession, targetPackage.Manifest.Resources, originPackage);
        var mergedBytes = session.Save(targetPackage, extra);

        var reloaded = EditableLevelReader.FromPackageBytes(mergedBytes);
        using var mergedPackage = PackageReader.Open(new MemoryStream(mergedBytes));

        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Objects, Has.Count.EqualTo(2),
                "both placed objects must resolve after merging the level into a different existing package.");
            Assert.That(reloaded.Objects[0].Graphic, Is.EqualTo(PlatformGraphicBytes),
                "the platform's graphic bytes did not survive the merge-into-existing-package save.");
            Assert.That(reloaded.Objects[1].Graphic, Is.EqualTo(JumpBlockGraphicBytes),
                "the jump-block's graphic bytes did not survive the merge-into-existing-package save.");
            Assert.That(mergedPackage.GetEntry(ObjectSetPath).Kind, Is.EqualTo(ResourceKind.ObjectSet),
                "the object set resource must be stamped with the objectset kind after the merge.");
        });
    }

    [Test]
    public void ResaveIntoSamePackage_WithPlacedObjects_RoundTripsTheObjectSetAndEveryObjectGraphic()
    {
        var packageBytes = BuildPackageBytes();

        using var package = PackageReader.Open(new MemoryStream(packageBytes));
        var level = EditableLevelReader.FromPackage(package, LevelPath);
        var tileSet = EditableTileSetReader.FromPackage(package, ResourceReference.ToSelf(TileSetPath));
        var tileSetSession = new TileSetEditSession(tileSet);
        var session = new LevelEditSession(level);

        var extra = LevelSaveOrchestration.BuildExtraContributions(level, tileSetSession, package.Manifest.Resources, package);
        var mergedBytes = session.Save(package, extra);

        var reloaded = EditableLevelReader.FromPackageBytes(mergedBytes);

        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Objects, Has.Count.EqualTo(2),
                "both placed objects must resolve after an ordinary re-save into the same package.");
            Assert.That(reloaded.Objects[0].Graphic, Is.EqualTo(PlatformGraphicBytes),
                "the platform's graphic bytes did not survive the ordinary re-save.");
            Assert.That(reloaded.Objects[1].Graphic, Is.EqualTo(JumpBlockGraphicBytes),
                "the jump-block's graphic bytes did not survive the ordinary re-save.");
        });
    }

    [Test]
    public void SaveAsNewPackage_WithTwoDistinctObjectSetsSharingOneGraphic_DoesNotThrowAndRoundTripsBoth()
    {
        var packageBytes = BuildTwoObjectSetsSharingOneGraphicPackageBytes();
        var freshBytes = BuildFreshPackageBytes(packageBytes);
        var reloaded = EditableLevelReader.FromPackageBytes(freshBytes);

        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Objects, Has.Count.EqualTo(2),
                "both placements must resolve when their distinct object sets share one graphic path.");
            Assert.That(reloaded.Objects[0].Graphic, Is.EqualTo(PlatformGraphicBytes),
                "the shared graphic did not survive the fresh-package save for the first object set's placement.");
            Assert.That(reloaded.Objects[1].Graphic, Is.EqualTo(PlatformGraphicBytes),
                "the shared graphic did not survive the fresh-package save for the second object set's placement.");
        });
    }

    [Test]
    public void BuildContributionsForLevel_WithPlacementsAndNoSourcePackage_ThrowsArgumentNullException()
    {
        var packageBytes = BuildPackageBytes();
        using var package = PackageReader.Open(new MemoryStream(packageBytes));
        var level = EditableLevelReader.FromPackage(package, LevelPath);

        Assert.That(() => ObjectSetMergeWriter.BuildContributionsForLevel(null, level),
            Throws.ArgumentNullException,
            "a level with object placements must not silently lose them when no source package is available.");
    }

    [Test]
    public void BuildContributionsForLevel_WithNoPlacementsAndNoSourcePackage_ReturnsEmpty()
    {
        var packageBytes = BuildEmptyLevelPackageBytes();
        using var package = PackageReader.Open(new MemoryStream(packageBytes));
        var level = EditableLevelReader.FromPackage(package, LevelPath);

        IReadOnlyList<PendingResource> contributions = ObjectSetMergeWriter.BuildContributionsForLevel(null, level);

        Assert.That(contributions, Is.Empty,
            "a level with no object placements has nothing to lose when no source package is available.");
    }

    private static byte[] BuildFreshPackageBytes(byte[] packageBytes)
    {
        using var package = PackageReader.Open(new MemoryStream(packageBytes));
        var level = EditableLevelReader.FromPackage(package, LevelPath);
        var tileSet = EditableTileSetReader.FromPackage(package, ResourceReference.ToSelf(TileSetPath));
        var tileSetSession = new TileSetEditSession(tileSet);
        var session = new LevelEditSession(level);

        var extra = LevelSaveOrchestration.BuildExtraContributions(level, tileSetSession, Array.Empty<ResourceEntry>(), package);
        return session.SaveFresh("Fresh Object Pack", extra);
    }

    private static byte[] BuildPackageBytes()
    {
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

        var level = BuildLevel(new[]
        {
            new ObjectPlacement { ObjectSet = ResourceReference.ToSelf(ObjectSetPath), ObjectId = "platform", Cell = new GridPosition(1, 0), Name = "platform-1" },
            new ObjectPlacement { ObjectSet = ResourceReference.ToSelf(ObjectSetPath), ObjectId = "jump-block", Cell = new GridPosition(2, 0), Name = "jump-block-1" },
        });

        var builder = StartPackage();
        builder.AddResource(ResourceKind.Sprite, PlatformGraphicPath, PlatformGraphicBytes, "image/png");
        builder.AddResource(ResourceKind.Sprite, JumpBlockGraphicPath, JumpBlockGraphicBytes, "image/png");
        builder.AddResource(ResourceKind.ObjectSet, ObjectSetPath, LevelContentSerializer.WriteObjectSet(objectSet));
        builder.AddResource(ResourceKind.Level, LevelPath, LevelContentSerializer.WriteLevel(level));

        return FinishPackage(builder);
    }

    private static byte[] BuildTwoObjectSetPackageBytes()
    {
        var objectSetPathA = ResourcePath.Create("objectsets/a.json");
        var objectSetPathB = ResourcePath.Create("objectsets/b.json");

        var objectSetA = new ObjectSetDefinition
        {
            Objects = new[]
            {
                new ObjectDefinition { Id = "platform", Name = "Moving Platform", Graphic = ResourceReference.ToSelf(PlatformGraphicPath), CollisionRole = ObjectCollisionRole.Solid },
            },
        };
        var objectSetB = new ObjectSetDefinition
        {
            Objects = new[]
            {
                new ObjectDefinition { Id = "jump-block", Name = "Jump Block", Graphic = ResourceReference.ToSelf(JumpBlockGraphicPath), CollisionRole = ObjectCollisionRole.Passthrough },
            },
        };

        var level = BuildLevel(new[]
        {
            new ObjectPlacement { ObjectSet = ResourceReference.ToSelf(objectSetPathA), ObjectId = "platform", Cell = new GridPosition(1, 0), Name = "platform-1" },
            new ObjectPlacement { ObjectSet = ResourceReference.ToSelf(objectSetPathB), ObjectId = "jump-block", Cell = new GridPosition(2, 0), Name = "jump-block-1" },
        });

        var builder = StartPackage();
        builder.AddResource(ResourceKind.Sprite, PlatformGraphicPath, PlatformGraphicBytes, "image/png");
        builder.AddResource(ResourceKind.Sprite, JumpBlockGraphicPath, JumpBlockGraphicBytes, "image/png");
        builder.AddResource(ResourceKind.ObjectSet, objectSetPathA, LevelContentSerializer.WriteObjectSet(objectSetA));
        builder.AddResource(ResourceKind.ObjectSet, objectSetPathB, LevelContentSerializer.WriteObjectSet(objectSetB));
        builder.AddResource(ResourceKind.Level, LevelPath, LevelContentSerializer.WriteLevel(level));

        return FinishPackage(builder);
    }

    private static byte[] BuildSharedGraphicPackageBytes()
    {
        var objectSet = new ObjectSetDefinition
        {
            Objects = new[]
            {
                new ObjectDefinition { Id = "solid-crate", Name = "Solid Crate", Graphic = ResourceReference.ToSelf(PlatformGraphicPath), CollisionRole = ObjectCollisionRole.Solid },
                new ObjectDefinition { Id = "passthrough-crate", Name = "Passthrough Crate", Graphic = ResourceReference.ToSelf(PlatformGraphicPath), CollisionRole = ObjectCollisionRole.Passthrough },
            },
        };

        var level = BuildLevel(new[]
        {
            new ObjectPlacement { ObjectSet = ResourceReference.ToSelf(ObjectSetPath), ObjectId = "solid-crate", Cell = new GridPosition(1, 0), Name = "solid-crate-1" },
            new ObjectPlacement { ObjectSet = ResourceReference.ToSelf(ObjectSetPath), ObjectId = "passthrough-crate", Cell = new GridPosition(2, 0), Name = "passthrough-crate-1" },
        });

        var builder = StartPackage();
        builder.AddResource(ResourceKind.Sprite, PlatformGraphicPath, PlatformGraphicBytes, "image/png");
        builder.AddResource(ResourceKind.ObjectSet, ObjectSetPath, LevelContentSerializer.WriteObjectSet(objectSet));
        builder.AddResource(ResourceKind.Level, LevelPath, LevelContentSerializer.WriteLevel(level));

        return FinishPackage(builder);
    }

    private static byte[] BuildOtherExistingPackageBytes()
    {
        var builder = new PackageBuilder().WithName("Other Existing Pack").WithVersion("0.1.0");
        return FinishPackage(builder);
    }

    private static byte[] BuildTwoObjectSetsSharingOneGraphicPackageBytes()
    {
        var objectSetPathA = ResourcePath.Create("objectsets/a.json");
        var objectSetPathB = ResourcePath.Create("objectsets/b.json");

        var objectSetA = new ObjectSetDefinition
        {
            Objects = new[]
            {
                new ObjectDefinition { Id = "crate-a", Name = "Crate A", Graphic = ResourceReference.ToSelf(PlatformGraphicPath), CollisionRole = ObjectCollisionRole.Solid },
            },
        };
        var objectSetB = new ObjectSetDefinition
        {
            Objects = new[]
            {
                new ObjectDefinition { Id = "crate-b", Name = "Crate B", Graphic = ResourceReference.ToSelf(PlatformGraphicPath), CollisionRole = ObjectCollisionRole.Passthrough },
            },
        };

        var level = BuildLevel(new[]
        {
            new ObjectPlacement { ObjectSet = ResourceReference.ToSelf(objectSetPathA), ObjectId = "crate-a", Cell = new GridPosition(1, 0), Name = "crate-a-1" },
            new ObjectPlacement { ObjectSet = ResourceReference.ToSelf(objectSetPathB), ObjectId = "crate-b", Cell = new GridPosition(2, 0), Name = "crate-b-1" },
        });

        var builder = StartPackage();
        builder.AddResource(ResourceKind.Sprite, PlatformGraphicPath, PlatformGraphicBytes, "image/png");
        builder.AddResource(ResourceKind.ObjectSet, objectSetPathA, LevelContentSerializer.WriteObjectSet(objectSetA));
        builder.AddResource(ResourceKind.ObjectSet, objectSetPathB, LevelContentSerializer.WriteObjectSet(objectSetB));
        builder.AddResource(ResourceKind.Level, LevelPath, LevelContentSerializer.WriteLevel(level));

        return FinishPackage(builder);
    }

    private static byte[] BuildEmptyLevelPackageBytes()
    {
        var level = BuildLevel(Array.Empty<ObjectPlacement>());
        var builder = StartPackage();
        builder.AddResource(ResourceKind.Level, LevelPath, LevelContentSerializer.WriteLevel(level));
        return FinishPackage(builder);
    }

    private static LevelDefinition BuildLevel(ObjectPlacement[] placements)
    {
        var cells = new int[Width * Height];
        Array.Fill(cells, LayerDefinition.EmptyCell);

        return new LevelDefinition
        {
            TileSize = TileSize,
            Width = Width,
            Height = Height,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Layers = new[] { new LayerDefinition { Name = "terrain", Collision = true, Cells = cells } },
            Objects = placements,
        };
    }

    private static PackageBuilder StartPackage()
    {
        var tileSet = new TileSetDefinition
        {
            Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath), CollisionShape = CollisionShapeDefinition.Full } },
        };

        var builder = new PackageBuilder().WithName("Object Round Trip Pack").WithVersion("0.1.0");
        builder.AddResource(ResourceKind.TileGraphic, GrassPath, Encoding.UTF8.GetBytes("GRASS-PNG"), "image/png");
        builder.AddResource(ResourceKind.TileSet, TileSetPath, LevelContentSerializer.WriteTileSet(tileSet));
        return builder;
    }

    private static byte[] FinishPackage(PackageBuilder builder)
    {
        using var buffer = new MemoryStream();
        builder.Write(buffer);
        return buffer.ToArray();
    }
}
