using System.Linq;
using System.Text;
using NUnit.Framework;
using Uberkarl.Behavior;
using Uberkarl.Content;
using Uberkarl.Content.Json;
using Uberkarl.Packages;

namespace Uberkarl.Editor.Tests;

/// <summary>
/// Covers DiVoid #7863's object read-through: <see cref="EditableLevelReader"/>/<see cref="EditableLevelSnapshot"/>
/// must carry a level's placed objects into the editor's playtest projection exactly like the P1 read-through
/// for tile behaviors/triggers/level script (<see cref="EditableLevelSnapshotBehaviorTests"/>, DiVoid #7747) —
/// otherwise the editor's <c>PlaytestOverlay</c> would silently drop every object even though the identical
/// package plays correctly stand-alone via <c>LevelPlay</c>/<see cref="LevelLoader"/>.
/// </summary>
[TestFixture]
public sealed class EditableLevelSnapshotObjectTests
{
    private static readonly ResourcePath LevelPath = ResourcePath.Create("levels/demo.json");
    private static readonly ResourcePath TileSetPath = ResourcePath.Create("tileset.json");
    private static readonly ResourcePath ObjectSetPath = ResourcePath.Create("objectsets/demo.json");
    private static readonly ResourcePath GrassPath = ResourcePath.Create("tiles/grass.png");
    private static readonly ResourcePath PlatformGraphicPath = ResourcePath.Create("objects/platform.png");

    [Test]
    public void EditorPlaytestProjection_MatchesStandaloneLoad_ForPlacedObjects()
    {
        var packageBytes = BuildSamplePackageBytes();

        using var registry = new PackageRegistry(PackageReader.Open(new MemoryStream(packageBytes)));
        var standalone = LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath));

        using var package = PackageReader.Open(new MemoryStream(packageBytes));
        var editable = EditableLevelReader.FromPackage(package);
        var editorProjection = EditableLevelSnapshot.ToResolvedLevel(editable);

        Assert.That(editorProjection.Objects, Is.Not.Empty, "editor playtest projection lost every placed object");
        Assert.That(editorProjection.Objects, Has.Count.EqualTo(standalone.Objects.Count));

        var placed = editorProjection.Objects.Single();
        Assert.That(placed.Name, Is.EqualTo("platform-1"));
        Assert.That(placed.CollisionRole, Is.EqualTo(ObjectCollisionRole.Solid));
        Assert.That(placed.Binding!.PredefinedId, Is.EqualTo(PredefinedBehaviors.Patrol));
        Assert.That(placed.Graphic, Is.EqualTo(Encoding.UTF8.GetBytes("PLATFORM-PNG")));
    }

    private static byte[] BuildSamplePackageBytes()
    {
        var cells = new int[4 * 2];
        Array.Fill(cells, LayerDefinition.EmptyCell);

        var objectSet = new ObjectSetDefinition
        {
            Objects = new[]
            {
                new ObjectDefinition
                {
                    Id = "platform",
                    Graphic = ResourceReference.ToSelf(PlatformGraphicPath),
                    CollisionRole = ObjectCollisionRole.Solid,
                    Behavior = BehaviorBinding.FromPredefined(PredefinedBehaviors.Patrol),
                },
            },
        };

        var level = new LevelDefinition
        {
            TileSize = 16,
            Width = 4,
            Height = 2,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Layers = new[] { new LayerDefinition { Name = "terrain", Collision = true, Cells = cells } },
            Objects = new[]
            {
                new ObjectPlacement { ObjectSet = ResourceReference.ToSelf(ObjectSetPath), ObjectId = "platform", Cell = new GridPosition(1, 0), Name = "platform-1" },
            },
        };

        var tileSet = new TileSetDefinition
        {
            Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath), CollisionShape = CollisionShapeDefinition.Full } },
        };

        var builder = new PackageBuilder().WithName("Object Regression Pack").WithVersion("0.1.0");
        builder.AddResource(ResourceKind.TileGraphic, GrassPath, Encoding.UTF8.GetBytes("GRASS-PNG"), "image/png");
        builder.AddResource(ResourceKind.Sprite, PlatformGraphicPath, Encoding.UTF8.GetBytes("PLATFORM-PNG"), "image/png");
        builder.AddResource(ResourceKind.TileSet, TileSetPath, LevelContentSerializer.WriteTileSet(tileSet));
        builder.AddResource(ResourceKind.ObjectSet, ObjectSetPath, LevelContentSerializer.WriteObjectSet(objectSet));
        builder.AddResource(ResourceKind.Level, LevelPath, LevelContentSerializer.WriteLevel(level));

        using var buffer = new MemoryStream();
        builder.Write(buffer);
        return buffer.ToArray();
    }
}
