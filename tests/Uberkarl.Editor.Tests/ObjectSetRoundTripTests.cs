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
    private static readonly ResourcePath TargetOwnResourcePath = ResourcePath.Create("targets/own-resource.png");
    private static readonly ResourcePath CollidingObjectSetPath = ResourcePath.Create("tilesets/probe-c-set.json");

    private static readonly byte[] PlatformGraphicBytes = Encoding.UTF8.GetBytes("PLATFORM-PNG");
    private static readonly byte[] JumpBlockGraphicBytes = Encoding.UTF8.GetBytes("JUMP-BLOCK-PNG");
    private static readonly byte[] TargetOwnResourceBytes = Encoding.UTF8.GetBytes("TARGET-OWN-PNG");

    [Test]
    public void SaveAsNewPackage_WithPlacedObjects_RoundTripsTheObjectSetAndEveryObjectGraphic()
    {
        byte[] packageBytes = BuildPackageBytes();
        byte[] freshBytes = BuildFreshPackageBytes(packageBytes);
        EditableLevel reloaded = EditableLevelReader.FromPackageBytes(freshBytes);

        using Package freshPackage = PackageReader.Open(new MemoryStream(freshBytes));

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
        byte[] packageBytes = BuildTwoObjectSetPackageBytes();
        byte[] freshBytes = BuildFreshPackageBytes(packageBytes);
        EditableLevel reloaded = EditableLevelReader.FromPackageBytes(freshBytes);

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
        byte[] packageBytes = BuildSharedGraphicPackageBytes();
        byte[] freshBytes = BuildFreshPackageBytes(packageBytes);
        EditableLevel reloaded = EditableLevelReader.FromPackageBytes(freshBytes);

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
        byte[] originBytes = BuildPackageBytes();
        byte[] targetBytes = BuildOtherExistingPackageBytes();

        using Package originPackage = PackageReader.Open(new MemoryStream(originBytes));
        using Package targetPackage = PackageReader.Open(new MemoryStream(targetBytes));

        EditableLevel level = EditableLevelReader.FromPackage(originPackage, LevelPath);
        EditableTileSet tileSet = EditableTileSetReader.FromPackage(originPackage, ResourceReference.ToSelf(TileSetPath));
        TileSetEditSession tileSetSession = new TileSetEditSession(tileSet);
        LevelEditSession session = new LevelEditSession(level);
        session.AttachAsNewResource(targetPackage.Manifest.Resources);

        IReadOnlyList<PendingResource> extra = LevelSaveOrchestration.BuildExtraContributions(level, tileSetSession, targetPackage.Manifest.Resources, originPackage);
        byte[] mergedBytes = session.Save(targetPackage, extra);

        EditableLevel reloaded = EditableLevelReader.FromPackageBytes(mergedBytes);
        using Package mergedPackage = PackageReader.Open(new MemoryStream(mergedBytes));

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
            Assert.That(mergedPackage.GetEntry(TargetOwnResourcePath).Kind, Is.EqualTo(ResourceKind.Sprite),
                "the target package's own pre-existing resource must survive the merge.");
            Assert.That(mergedPackage.ReadBytes(TargetOwnResourcePath), Is.EqualTo(TargetOwnResourceBytes),
                "the target package's own pre-existing resource bytes must survive the merge unchanged.");
        });
    }

    [Test]
    public void ResaveIntoSamePackage_WithPlacedObjects_RoundTripsTheObjectSetAndEveryObjectGraphic()
    {
        byte[] packageBytes = BuildPackageBytes();

        using Package package = PackageReader.Open(new MemoryStream(packageBytes));
        EditableLevel level = EditableLevelReader.FromPackage(package, LevelPath);
        EditableTileSet tileSet = EditableTileSetReader.FromPackage(package, ResourceReference.ToSelf(TileSetPath));
        TileSetEditSession tileSetSession = new TileSetEditSession(tileSet);
        LevelEditSession session = new LevelEditSession(level);

        IReadOnlyList<PendingResource> extra = LevelSaveOrchestration.BuildExtraContributions(level, tileSetSession, package.Manifest.Resources, package);
        byte[] mergedBytes = session.Save(package, extra);

        EditableLevel reloaded = EditableLevelReader.FromPackageBytes(mergedBytes);

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
        byte[] packageBytes = BuildTwoObjectSetsSharingOneGraphicPackageBytes();
        byte[] freshBytes = BuildFreshPackageBytes(packageBytes);
        EditableLevel reloaded = EditableLevelReader.FromPackageBytes(freshBytes);

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
    public void SaveAsNewPackage_WithObjectGraphicSharingTheTileSetGraphic_DoesNotThrowAndAssembledPathsStayUnique()
    {
        byte[] packageBytes = BuildObjectGraphicSharesTileGraphicPackageBytes();
        byte[] freshBytes = BuildFreshPackageBytes(packageBytes);
        EditableLevel reloaded = EditableLevelReader.FromPackageBytes(freshBytes);

        using Package freshPackage = PackageReader.Open(new MemoryStream(freshBytes));

        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Objects, Has.Count.EqualTo(1),
                "the placement must resolve when its object type's graphic path equals the tile set's own graphic path.");
            Assert.That(reloaded.Objects[0].Graphic, Is.EqualTo(Encoding.UTF8.GetBytes("GRASS-PNG")),
                "the shared path's single assembled entry must carry the shared graphic bytes.");
            Assert.That(freshPackage.Manifest.Resources.Count(entry => entry.Path == GrassPath), Is.EqualTo(1),
                "the assembled manifest must not carry the shared path twice.");
        });
    }

    [Test]
    public void BuildContributionsForLevel_WithPlacementsAndNoSourcePackage_ThrowsArgumentNullException()
    {
        byte[] packageBytes = BuildPackageBytes();
        using Package package = PackageReader.Open(new MemoryStream(packageBytes));
        EditableLevel level = EditableLevelReader.FromPackage(package, LevelPath);

        Assert.That(() => ObjectSetMergeWriter.BuildContributionsForLevel(null, level),
            Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("package"),
            "a level with object placements must not silently lose them when no source package is available.");
    }

    [Test]
    public void BuildContributionsForLevel_WithNoPlacementsAndNoSourcePackage_ReturnsEmpty()
    {
        byte[] packageBytes = BuildEmptyLevelPackageBytes();
        using Package package = PackageReader.Open(new MemoryStream(packageBytes));
        EditableLevel level = EditableLevelReader.FromPackage(package, LevelPath);

        IReadOnlyList<PendingResource> withoutPackage = ObjectSetMergeWriter.BuildContributionsForLevel(null, level);
        IReadOnlyList<PendingResource> withPackage = ObjectSetMergeWriter.BuildContributionsForLevel(package, level);

        Assert.That(withoutPackage, Is.EqualTo(withPackage),
            "a level with no object placements must return the same (empty) contribution list whether or not a source package is supplied.");
        Assert.That(withoutPackage, Is.Empty,
            "a level with no object placements must return an empty contribution list.");
    }

    [Test]
    public void SaveAsNewPackage_WithLevelPathCollidingWithReferencedObjectSet_ThrowsLevelContentException()
    {
        byte[] packageBytes = BuildPackageBytes();
        using Package package = PackageReader.Open(new MemoryStream(packageBytes));
        EditableLevel level = EditableLevelReader.FromPackage(package, LevelPath);
        EditableTileSet tileSet = EditableTileSetReader.FromPackage(package, ResourceReference.ToSelf(TileSetPath));
        TileSetEditSession tileSetSession = new TileSetEditSession(tileSet);
        LevelEditSession session = new LevelEditSession(level);
        session.AttachToExistingResource(ObjectSetPath);

        IReadOnlyList<PendingResource> extra = LevelSaveOrchestration.BuildExtraContributions(level, tileSetSession, Array.Empty<ResourceEntry>(), package);

        Assert.That(() => session.SaveFresh("Collision Pack", extra),
            Throws.TypeOf<LevelContentException>().With.Message.Contains(ObjectSetPath.Value),
            "the level's own resource path colliding with a referenced object set's path must surface as a named conflict, not silently drop one side.");
    }

    [Test]
    public void SaveAsNewPackage_WithReferencedObjectSetPathCollidingWithAFreshTileSetsDerivedPath_ThrowsLevelContentException()
    {
        byte[] packageBytes = BuildObjectSetPathCollidesWithFreshTileSetPackageBytes();
        using Package package = PackageReader.Open(new MemoryStream(packageBytes));
        EditableLevel level = EditableLevelReader.FromPackage(package, LevelPath);
        EditableTileSet freshTileSet = EditableTileSet.CreateBlank("Probe C Set");
        TileSetEditSession tileSetSession = new TileSetEditSession(freshTileSet);
        LevelEditSession session = new LevelEditSession(level);

        IReadOnlyList<PendingResource> extra = LevelSaveOrchestration.BuildExtraContributions(level, tileSetSession, Array.Empty<ResourceEntry>(), package);

        Assert.That(() => session.SaveFresh("Collision Pack", extra),
            Throws.TypeOf<LevelContentException>().With.Message.Contains(CollidingObjectSetPath.Value),
            "an object set's own path colliding with a freshly-attached tile set's derived path must surface as a named conflict, not silently drop the object set.");
    }

    [Test]
    public void BuildFresh_WithIdenticalBytesAndMediaTypeButDifferingAttributionAtSharedPath_ThrowsLevelContentException()
    {
        PendingResource first = new PendingResource(GrassPath, ResourceKind.Sprite, "image/png",
            Encoding.UTF8.GetBytes("GRASS-PNG"), new Attribution { Author = "FIRST", License = "CC0-1.0" });
        PendingResource second = new PendingResource(GrassPath, ResourceKind.Sprite, "image/png",
            Encoding.UTF8.GetBytes("GRASS-PNG"), new Attribution { Author = "SECOND", License = "CC-BY-4.0" });

        Assert.That(() => PackageMergeWriter.BuildFresh("Attribution Collision Pack", new[] { first, second }),
            Throws.TypeOf<LevelContentException>().With.Message.Contains(GrassPath.Value),
            "identical bytes and media type must not silently discard a differing attribution (e.g. a licence) at a shared path.");
    }

    [Test]
    public void BuildFresh_WithIdenticalContentAndIdenticalAttributionAtSharedPath_CollapsesToOne()
    {
        PendingResource first = new PendingResource(GrassPath, ResourceKind.TileGraphic, "image/png",
            Encoding.UTF8.GetBytes("GRASS-PNG"), new Attribution { Author = "SHARED", License = "CC0-1.0" });
        PendingResource second = new PendingResource(GrassPath, ResourceKind.Sprite, "image/png",
            Encoding.UTF8.GetBytes("GRASS-PNG"), new Attribution { Author = "SHARED", License = "CC0-1.0" });

        byte[] freshBytes = PackageMergeWriter.BuildFresh("Attribution Match Pack", new[] { first, second });
        using Package freshPackage = PackageReader.Open(new MemoryStream(freshBytes));

        Assert.That(freshPackage.Manifest.Resources.Count(entry => entry.Path == GrassPath), Is.EqualTo(1),
            "content and attribution that are field-wise identical on separate instances must still collapse to one entry.");
    }

    private static byte[] BuildFreshPackageBytes(byte[] packageBytes)
    {
        using Package package = PackageReader.Open(new MemoryStream(packageBytes));
        EditableLevel level = EditableLevelReader.FromPackage(package, LevelPath);
        EditableTileSet tileSet = EditableTileSetReader.FromPackage(package, ResourceReference.ToSelf(TileSetPath));
        TileSetEditSession tileSetSession = new TileSetEditSession(tileSet);
        LevelEditSession session = new LevelEditSession(level);

        IReadOnlyList<PendingResource> extra = LevelSaveOrchestration.BuildExtraContributions(level, tileSetSession, Array.Empty<ResourceEntry>(), package);
        return session.SaveFresh("Fresh Object Pack", extra);
    }

    private static byte[] BuildPackageBytes()
    {
        ObjectSetDefinition objectSet = new ObjectSetDefinition
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

        LevelDefinition level = BuildLevel(new[]
        {
            new ObjectPlacement { ObjectSet = ResourceReference.ToSelf(ObjectSetPath), ObjectId = "platform", Cell = new GridPosition(1, 0), Name = "platform-1" },
            new ObjectPlacement { ObjectSet = ResourceReference.ToSelf(ObjectSetPath), ObjectId = "jump-block", Cell = new GridPosition(2, 0), Name = "jump-block-1" },
        });

        PackageBuilder builder = StartPackage();
        builder.AddResource(ResourceKind.Sprite, PlatformGraphicPath, PlatformGraphicBytes, "image/png");
        builder.AddResource(ResourceKind.Sprite, JumpBlockGraphicPath, JumpBlockGraphicBytes, "image/png");
        builder.AddResource(ResourceKind.ObjectSet, ObjectSetPath, LevelContentSerializer.WriteObjectSet(objectSet));
        builder.AddResource(ResourceKind.Level, LevelPath, LevelContentSerializer.WriteLevel(level));

        return FinishPackage(builder);
    }

    private static byte[] BuildTwoObjectSetPackageBytes()
    {
        ResourcePath objectSetPathA = ResourcePath.Create("objectsets/a.json");
        ResourcePath objectSetPathB = ResourcePath.Create("objectsets/b.json");

        ObjectSetDefinition objectSetA = new ObjectSetDefinition
        {
            Objects = new[]
            {
                new ObjectDefinition { Id = "platform", Name = "Moving Platform", Graphic = ResourceReference.ToSelf(PlatformGraphicPath), CollisionRole = ObjectCollisionRole.Solid },
            },
        };
        ObjectSetDefinition objectSetB = new ObjectSetDefinition
        {
            Objects = new[]
            {
                new ObjectDefinition { Id = "jump-block", Name = "Jump Block", Graphic = ResourceReference.ToSelf(JumpBlockGraphicPath), CollisionRole = ObjectCollisionRole.Passthrough },
            },
        };

        LevelDefinition level = BuildLevel(new[]
        {
            new ObjectPlacement { ObjectSet = ResourceReference.ToSelf(objectSetPathA), ObjectId = "platform", Cell = new GridPosition(1, 0), Name = "platform-1" },
            new ObjectPlacement { ObjectSet = ResourceReference.ToSelf(objectSetPathB), ObjectId = "jump-block", Cell = new GridPosition(2, 0), Name = "jump-block-1" },
        });

        PackageBuilder builder = StartPackage();
        builder.AddResource(ResourceKind.Sprite, PlatformGraphicPath, PlatformGraphicBytes, "image/png");
        builder.AddResource(ResourceKind.Sprite, JumpBlockGraphicPath, JumpBlockGraphicBytes, "image/png");
        builder.AddResource(ResourceKind.ObjectSet, objectSetPathA, LevelContentSerializer.WriteObjectSet(objectSetA));
        builder.AddResource(ResourceKind.ObjectSet, objectSetPathB, LevelContentSerializer.WriteObjectSet(objectSetB));
        builder.AddResource(ResourceKind.Level, LevelPath, LevelContentSerializer.WriteLevel(level));

        return FinishPackage(builder);
    }

    private static byte[] BuildSharedGraphicPackageBytes()
    {
        ObjectSetDefinition objectSet = new ObjectSetDefinition
        {
            Objects = new[]
            {
                new ObjectDefinition { Id = "solid-crate", Name = "Solid Crate", Graphic = ResourceReference.ToSelf(PlatformGraphicPath), CollisionRole = ObjectCollisionRole.Solid },
                new ObjectDefinition { Id = "passthrough-crate", Name = "Passthrough Crate", Graphic = ResourceReference.ToSelf(PlatformGraphicPath), CollisionRole = ObjectCollisionRole.Passthrough },
            },
        };

        LevelDefinition level = BuildLevel(new[]
        {
            new ObjectPlacement { ObjectSet = ResourceReference.ToSelf(ObjectSetPath), ObjectId = "solid-crate", Cell = new GridPosition(1, 0), Name = "solid-crate-1" },
            new ObjectPlacement { ObjectSet = ResourceReference.ToSelf(ObjectSetPath), ObjectId = "passthrough-crate", Cell = new GridPosition(2, 0), Name = "passthrough-crate-1" },
        });

        PackageBuilder builder = StartPackage();
        builder.AddResource(ResourceKind.Sprite, PlatformGraphicPath, PlatformGraphicBytes, "image/png");
        builder.AddResource(ResourceKind.ObjectSet, ObjectSetPath, LevelContentSerializer.WriteObjectSet(objectSet));
        builder.AddResource(ResourceKind.Level, LevelPath, LevelContentSerializer.WriteLevel(level));

        return FinishPackage(builder);
    }

    private static byte[] BuildOtherExistingPackageBytes()
    {
        PackageBuilder builder = new PackageBuilder().WithName("Other Existing Pack").WithVersion("0.1.0");
        builder.AddResource(ResourceKind.Sprite, TargetOwnResourcePath, TargetOwnResourceBytes, "image/png");
        return FinishPackage(builder);
    }

    private static byte[] BuildTwoObjectSetsSharingOneGraphicPackageBytes()
    {
        ResourcePath objectSetPathA = ResourcePath.Create("objectsets/a.json");
        ResourcePath objectSetPathB = ResourcePath.Create("objectsets/b.json");

        ObjectSetDefinition objectSetA = new ObjectSetDefinition
        {
            Objects = new[]
            {
                new ObjectDefinition { Id = "crate-a", Name = "Crate A", Graphic = ResourceReference.ToSelf(PlatformGraphicPath), CollisionRole = ObjectCollisionRole.Solid },
            },
        };
        ObjectSetDefinition objectSetB = new ObjectSetDefinition
        {
            Objects = new[]
            {
                new ObjectDefinition { Id = "crate-b", Name = "Crate B", Graphic = ResourceReference.ToSelf(PlatformGraphicPath), CollisionRole = ObjectCollisionRole.Passthrough },
            },
        };

        LevelDefinition level = BuildLevel(new[]
        {
            new ObjectPlacement { ObjectSet = ResourceReference.ToSelf(objectSetPathA), ObjectId = "crate-a", Cell = new GridPosition(1, 0), Name = "crate-a-1" },
            new ObjectPlacement { ObjectSet = ResourceReference.ToSelf(objectSetPathB), ObjectId = "crate-b", Cell = new GridPosition(2, 0), Name = "crate-b-1" },
        });

        PackageBuilder builder = StartPackage();
        builder.AddResource(ResourceKind.Sprite, PlatformGraphicPath, PlatformGraphicBytes, "image/png");
        builder.AddResource(ResourceKind.ObjectSet, objectSetPathA, LevelContentSerializer.WriteObjectSet(objectSetA));
        builder.AddResource(ResourceKind.ObjectSet, objectSetPathB, LevelContentSerializer.WriteObjectSet(objectSetB));
        builder.AddResource(ResourceKind.Level, LevelPath, LevelContentSerializer.WriteLevel(level));

        return FinishPackage(builder);
    }

    private static byte[] BuildObjectGraphicSharesTileGraphicPackageBytes()
    {
        ObjectSetDefinition objectSet = new ObjectSetDefinition
        {
            Objects = new[]
            {
                new ObjectDefinition { Id = "grass-crate", Name = "Grass Crate", Graphic = ResourceReference.ToSelf(GrassPath), CollisionRole = ObjectCollisionRole.Solid },
            },
        };

        LevelDefinition level = BuildLevel(new[]
        {
            new ObjectPlacement { ObjectSet = ResourceReference.ToSelf(ObjectSetPath), ObjectId = "grass-crate", Cell = new GridPosition(1, 0), Name = "grass-crate-1" },
        });

        PackageBuilder builder = StartPackage();
        builder.AddResource(ResourceKind.ObjectSet, ObjectSetPath, LevelContentSerializer.WriteObjectSet(objectSet));
        builder.AddResource(ResourceKind.Level, LevelPath, LevelContentSerializer.WriteLevel(level));

        return FinishPackage(builder);
    }

    private static byte[] BuildObjectSetPathCollidesWithFreshTileSetPackageBytes()
    {
        ObjectSetDefinition objectSet = new ObjectSetDefinition
        {
            Objects = new[]
            {
                new ObjectDefinition { Id = "crate", Name = "Crate", Graphic = ResourceReference.ToSelf(PlatformGraphicPath), CollisionRole = ObjectCollisionRole.Solid },
            },
        };

        LevelDefinition level = BuildLevel(new[]
        {
            new ObjectPlacement { ObjectSet = ResourceReference.ToSelf(CollidingObjectSetPath), ObjectId = "crate", Cell = new GridPosition(1, 0), Name = "crate-1" },
        });

        PackageBuilder builder = StartPackage();
        builder.AddResource(ResourceKind.Sprite, PlatformGraphicPath, PlatformGraphicBytes, "image/png");
        builder.AddResource(ResourceKind.ObjectSet, CollidingObjectSetPath, LevelContentSerializer.WriteObjectSet(objectSet));
        builder.AddResource(ResourceKind.Level, LevelPath, LevelContentSerializer.WriteLevel(level));

        return FinishPackage(builder);
    }

    private static byte[] BuildEmptyLevelPackageBytes()
    {
        LevelDefinition level = BuildLevel(Array.Empty<ObjectPlacement>());
        PackageBuilder builder = StartPackage();
        builder.AddResource(ResourceKind.Level, LevelPath, LevelContentSerializer.WriteLevel(level));
        return FinishPackage(builder);
    }

    private static LevelDefinition BuildLevel(ObjectPlacement[] placements)
    {
        int[] cells = new int[Width * Height];
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
        TileSetDefinition tileSet = new TileSetDefinition
        {
            Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath), CollisionShape = CollisionShapeDefinition.Full } },
        };

        PackageBuilder builder = new PackageBuilder().WithName("Object Round Trip Pack").WithVersion("0.1.0");
        builder.AddResource(ResourceKind.TileGraphic, GrassPath, Encoding.UTF8.GetBytes("GRASS-PNG"), "image/png");
        builder.AddResource(ResourceKind.TileSet, TileSetPath, LevelContentSerializer.WriteTileSet(tileSet));
        return builder;
    }

    private static byte[] FinishPackage(PackageBuilder builder)
    {
        using MemoryStream buffer = new MemoryStream();
        builder.Write(buffer);
        return buffer.ToArray();
    }
}
