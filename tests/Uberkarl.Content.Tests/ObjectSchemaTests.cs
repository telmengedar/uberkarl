using System.Text;
using NUnit.Framework;
using Uberkarl.Behavior;
using Uberkarl.Content.Json;
using Uberkarl.Packages;

namespace Uberkarl.Content.Tests;

/// <summary>
/// Covers the DiVoid #7863 (behavior system Phase 2) content-pipeline additions: the <c>objectset</c>
/// resource kind (<see cref="ObjectSetDefinition"/>/<see cref="ObjectDefinition"/>, mirroring
/// <see cref="TileSetDefinition"/>) and the level's placed objects (<see cref="ObjectPlacement"/>) — resolved
/// (design #7704 §6, C-2's Definition → Resolved shape) and validated by <see cref="LevelLoader"/>, and
/// round-tripped through <see cref="LevelContentSerializer"/>.
/// </summary>
[TestFixture]
public sealed class ObjectSchemaTests
{
    private static readonly ResourcePath LevelPath = ResourcePath.Create("levels/demo.json");
    private static readonly ResourcePath TileSetPath = ResourcePath.Create("tileset.json");
    private static readonly ResourcePath ObjectSetPath = ResourcePath.Create("objectsets/demo.json");
    private static readonly ResourcePath GrassPath = ResourcePath.Create("tiles/grass.png");
    private static readonly ResourcePath PlatformGraphicPath = ResourcePath.Create("objects/platform.png");
    private static readonly ResourcePath JumpBlockGraphicPath = ResourcePath.Create("objects/jump-block.png");

    [Test]
    public void Load_Object_ResolvesTypeDefault_GraphicAndCollisionRole()
    {
        var level = LevelWith(new[]
        {
            new ObjectPlacement { ObjectSet = ResourceReference.ToSelf(ObjectSetPath), ObjectId = "platform", Cell = new GridPosition(1, 1), Name = "platform-1" },
        });

        using var registry = BuildRegistry(level, out _);
        var resolved = LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath));

        Assert.That(resolved.Objects, Has.Count.EqualTo(1));
        var placed = resolved.Objects[0];
        Assert.That(placed.Name, Is.EqualTo("platform-1"));
        Assert.That(placed.Cell, Is.EqualTo(new GridPosition(1, 1)));
        Assert.That(placed.CollisionRole, Is.EqualTo(ObjectCollisionRole.Solid));
        Assert.That(placed.Graphic, Is.EqualTo(Encoding.UTF8.GetBytes("PLATFORM-PNG")));
        Assert.That(placed.Binding!.PredefinedId, Is.EqualTo(PredefinedBehaviors.Patrol));
        Assert.That(placed.State["speed"], Is.EqualTo(20L));
    }

    [Test]
    public void Load_Object_InstanceBindingOverride_ReplacesTypeDefault()
    {
        var level = LevelWith(new[]
        {
            new ObjectPlacement
            {
                ObjectSet = ResourceReference.ToSelf(ObjectSetPath), ObjectId = "platform", Cell = new GridPosition(1, 1), Name = "platform-1",
                Behavior = BehaviorBinding.FromPredefined(PredefinedBehaviors.BumpOnHitFromBelow),
            },
        });

        using var registry = BuildRegistry(level, out _);
        var resolved = LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath));

        Assert.That(resolved.Objects[0].Binding!.PredefinedId, Is.EqualTo(PredefinedBehaviors.BumpOnHitFromBelow));
    }

    [Test]
    public void Load_Object_NoBinding_ResolvesNullBinding()
    {
        var level = LevelWith(new[]
        {
            new ObjectPlacement { ObjectSet = ResourceReference.ToSelf(ObjectSetPath), ObjectId = "decoration", Cell = new GridPosition(2, 2), Name = "deco" },
        });

        using var registry = BuildRegistry(level, out _);
        var resolved = LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath));

        Assert.That(resolved.Objects[0].Binding, Is.Null);
    }

    [Test]
    public void Load_Object_UndefinedObjectId_Throws()
    {
        var level = LevelWith(new[]
        {
            new ObjectPlacement { ObjectSet = ResourceReference.ToSelf(ObjectSetPath), ObjectId = "not-declared", Cell = new GridPosition(0, 0), Name = "x" },
        });

        using var registry = BuildRegistry(level, out _);
        var exception = Assert.Throws<LevelContentException>(() => LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath)));
        Assert.That(exception!.Message, Does.Contain("not-declared"));
    }

    [Test]
    public void Load_Object_OutOfBoundsCell_Throws()
    {
        var level = LevelWith(new[]
        {
            new ObjectPlacement { ObjectSet = ResourceReference.ToSelf(ObjectSetPath), ObjectId = "platform", Cell = new GridPosition(99, 0), Name = "x" },
        });

        using var registry = BuildRegistry(level, out _);
        var exception = Assert.Throws<LevelContentException>(() => LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath)));
        Assert.That(exception!.Message, Does.Contain("outside"));
    }

    [Test]
    public void JsonRoundTrip_PreservesObjectSetAndPlacement()
    {
        var objectSet = DemoObjectSet();
        var level = LevelWith(new[]
        {
            new ObjectPlacement { ObjectSet = ResourceReference.ToSelf(ObjectSetPath), ObjectId = "jump-block", Cell = new GridPosition(3, 4), Name = "jb-1" },
        });

        var roundTrippedObjectSet = LevelContentSerializer.ReadObjectSet(LevelContentSerializer.WriteObjectSet(objectSet));
        var roundTrippedLevel = LevelContentSerializer.ReadLevel(LevelContentSerializer.WriteLevel(level));

        Assert.That(roundTrippedObjectSet.Objects, Has.Count.EqualTo(3));
        var jumpBlock = roundTrippedObjectSet.Objects.Single(o => o.Id == "jump-block");
        Assert.That(jumpBlock.CollisionRole, Is.EqualTo(ObjectCollisionRole.Passthrough));
        Assert.That(jumpBlock.Behavior!.PredefinedId, Is.EqualTo(PredefinedBehaviors.BumpOnHitFromBelow));

        Assert.That(roundTrippedLevel.Objects, Has.Count.EqualTo(1));
        var placement = roundTrippedLevel.Objects[0];
        Assert.That(placement.ObjectId, Is.EqualTo("jump-block"));
        Assert.That(placement.Cell, Is.EqualTo(new GridPosition(3, 4)));
        Assert.That(placement.Name, Is.EqualTo("jb-1"));
    }

    private static LevelDefinition LevelWith(IReadOnlyList<ObjectPlacement> objects) => new()
    {
        TileSize = 16,
        Width = 4,
        Height = 4,
        TileSet = ResourceReference.ToSelf(TileSetPath),
        Layers = new[] { new LayerDefinition { Name = "main", Cells = Enumerable.Repeat(LayerDefinition.EmptyCell, 16).ToArray() } },
        Objects = objects,
    };

    private static ObjectSetDefinition DemoObjectSet() => new()
    {
        Objects = new[]
        {
            new ObjectDefinition
            {
                Id = "platform",
                Name = "Moving Platform",
                Graphic = ResourceReference.ToSelf(PlatformGraphicPath),
                CollisionRole = ObjectCollisionRole.Solid,
                Behavior = BehaviorBinding.FromPredefined(PredefinedBehaviors.Patrol, new Dictionary<string, object?> { ["speed"] = 20 }),
                State = new Dictionary<string, object?> { ["speed"] = 20 },
            },
            new ObjectDefinition
            {
                Id = "jump-block",
                Name = "Jump Block",
                Graphic = ResourceReference.ToSelf(JumpBlockGraphicPath),
                CollisionRole = ObjectCollisionRole.Passthrough,
                Behavior = BehaviorBinding.FromPredefined(PredefinedBehaviors.BumpOnHitFromBelow),
            },
            new ObjectDefinition
            {
                Id = "decoration",
                Graphic = ResourceReference.ToSelf(JumpBlockGraphicPath),
                CollisionRole = ObjectCollisionRole.Passthrough,
            },
        },
    };

    private static PackageRegistry BuildRegistry(LevelDefinition level, out PackageBuilder usedBuilder)
    {
        var builder = new PackageBuilder().WithName("Object Demo Pack");
        builder.AddResource(ResourceKind.Level, LevelPath, LevelContentSerializer.WriteLevel(level));
        builder.AddResource(ResourceKind.TileSet, TileSetPath, LevelContentSerializer.WriteTileSet(new TileSetDefinition
        {
            Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath) } },
        }));
        builder.AddResource(ResourceKind.TileGraphic, GrassPath, Encoding.UTF8.GetBytes("GRASS-PNG"), "image/png");
        builder.AddResource(ResourceKind.ObjectSet, ObjectSetPath, LevelContentSerializer.WriteObjectSet(DemoObjectSet()));
        builder.AddResource(ResourceKind.Sprite, PlatformGraphicPath, Encoding.UTF8.GetBytes("PLATFORM-PNG"), "image/png");
        builder.AddResource(ResourceKind.Sprite, JumpBlockGraphicPath, Encoding.UTF8.GetBytes("JUMPBLOCK-PNG"), "image/png");

        usedBuilder = builder;
        var buffer = new MemoryStream();
        builder.Write(buffer);
        buffer.Position = 0;
        return new PackageRegistry(PackageReader.Open(buffer));
    }
}
