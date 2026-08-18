using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Uberkarl.Behavior;
using Uberkarl.Content;
using Uberkarl.Content.Json;
using Uberkarl.Packages;

namespace Uberkarl.Editor.Tests;

/// <summary>
/// Covers <see cref="TileSetMigration"/> (DiVoid #7551 Phase 1a, design #7580 §12): the mechanical pass
/// that dedups byte-identical per-level tile set resources within a package onto one shared, surviving
/// resource — the exact per-level redundancy Toni flagged, and the shape it took organically once the
/// editor's per-save fabrication (the pre-#7551 behaviour) accumulated duplicate content across two
/// "New" levels saved into the same package.
/// </summary>
[TestFixture]
public sealed class TileSetMigrationTests
{
    private const int TileSize = 16;

    private static readonly ResourcePath GrassPath = ResourcePath.Create("tiles/grass.png");

    [Test]
    public void Migrate_DedupsIdenticalTileSets_RewritesTheSecondLevelsReference_RemovesTheDuplicate()
    {
        var (packageBytes, tileSetA, tileSetB, graphicA, graphicB) = BuildTwoLevelsWithIdenticalTileSets();
        using var package = PackageReader.Open(new MemoryStream(packageBytes));

        var result = TileSetMigration.Migrate(package);
        using var migrated = PackageReader.Open(new MemoryStream(result.Bytes));

        Assert.Multiple(() =>
        {
            Assert.That(result.LevelsRewritten, Is.EqualTo(1));
            Assert.That(result.DuplicateTileSetsRemoved, Is.EqualTo(1));
            Assert.That(result.OrphanedGraphicsRemoved, Is.EqualTo(1));

            // Exactly one tileset resource survives — the redundancy is gone.
            Assert.That(migrated.Manifest.Resources.Count(e => e.Kind == ResourceKind.TileSet), Is.EqualTo(1));
            Assert.That(migrated.Contains(tileSetA), Is.True, "the first-sighted (canonical) tile set survives verbatim.");
            Assert.That(migrated.Contains(tileSetB), Is.False, "the duplicate is removed.");

            // Both levels are still present and BOTH now reference the surviving canonical resource.
            var firstLevel = LevelContentSerializer.ReadLevel(migrated.ReadBytes(ResourcePath.Create("levels/first.json")));
            var secondLevel = LevelContentSerializer.ReadLevel(migrated.ReadBytes(ResourcePath.Create("levels/second.json")));
            Assert.That(firstLevel.TileSet, Is.EqualTo(ResourceReference.ToSelf(tileSetA)));
            Assert.That(secondLevel.TileSet, Is.EqualTo(ResourceReference.ToSelf(tileSetA)), "the second level's reference is rewritten onto the survivor.");

            // The duplicate's own exclusive graphic is gone too; the surviving tileset's own graphic is untouched.
            Assert.That(migrated.Contains(graphicA), Is.True);
            Assert.That(migrated.Contains(graphicB), Is.False);

            // Render-identical: both levels' cells (the observable content) are byte-for-byte unchanged by migration.
            Assert.That(firstLevel.Layers[0].Cells, Is.EqualTo(new[] { 1 }));
            Assert.That(secondLevel.Layers[0].Cells, Is.EqualTo(new[] { 1 }));
        });
    }

    [Test]
    [Description("DiVoid #8397: two animated tile sets whose frame-0 graphic and every other field match, but whose animation frames differ, must not be treated as the same tileset — the losing level would otherwise silently acquire the survivor's frames.")]
    public void Migrate_DoesNotDedupeTileSets_DifferingOnlyInAnimationFrames()
    {
        var (packageBytes, tileSetA, tileSetB) = BuildTwoLevelsWithAnimatedTileSets(sameFrameBytes: false);
        using var package = PackageReader.Open(new MemoryStream(packageBytes));

        var result = TileSetMigration.Migrate(package);
        using var migrated = PackageReader.Open(new MemoryStream(result.Bytes));

        Assert.Multiple(() =>
        {
            Assert.That(result.LevelsRewritten, Is.EqualTo(0), "frames differ, so these are not the same tileset");
            Assert.That(result.DuplicateTileSetsRemoved, Is.EqualTo(0));
            Assert.That(result.OrphanedGraphicsRemoved, Is.EqualTo(0));
            Assert.That(migrated.Contains(tileSetA), Is.True);
            Assert.That(migrated.Contains(tileSetB), Is.True);
        });
    }

    [Test]
    [Description("DiVoid #8397: the orphan sweep only walked tile.Graphic, so a removed duplicate's frame graphic (referenced only via TileDefinition.Frames) survived in the archive forever.")]
    public void Migrate_RemovesOrphanedFrameGraphic_WhenDuplicateAnimatedTileSetIsRemoved()
    {
        var (packageBytes, tileSetA, tileSetB) = BuildTwoLevelsWithAnimatedTileSets(sameFrameBytes: true);
        using var package = PackageReader.Open(new MemoryStream(packageBytes));

        var result = TileSetMigration.Migrate(package);
        using var migrated = PackageReader.Open(new MemoryStream(result.Bytes));

        Assert.Multiple(() =>
        {
            Assert.That(result.DuplicateTileSetsRemoved, Is.EqualTo(1));
            Assert.That(result.OrphanedGraphicsRemoved, Is.EqualTo(2), "both the duplicate's base graphic and its frame graphic are orphaned");
            Assert.That(migrated.Contains(tileSetA), Is.True);
            Assert.That(migrated.Contains(tileSetB), Is.False);
            Assert.That(migrated.Contains(ResourcePath.Create("graphics/second/1.png")), Is.False);
            Assert.That(migrated.Contains(ResourcePath.Create("graphics/second/2.png")), Is.False, "the removed duplicate's frame graphic must not survive");
            Assert.That(migrated.Contains(ResourcePath.Create("graphics/first/1.png")), Is.True);
            Assert.That(migrated.Contains(ResourcePath.Create("graphics/first/2.png")), Is.True, "the surviving tileset's own frame graphic is untouched");
        });
    }

    [Test]
    [Description("DiVoid #8433 M3: the surviving-tile-set sweep must walk frame references too, or a frame graphic shared with a removed duplicate is deleted out from under the tile set that still uses it.")]
    public void Migrate_PreservesSharedFrameGraphic_WhenReferencedByBothSurvivorAndRemovedDuplicate()
    {
        var tileSetA = ResourcePath.Create("tilesets/first.json");
        var tileSetB = ResourcePath.Create("tilesets/second.json");
        var graphicA = ResourcePath.Create("graphics/first/1.png");
        var graphicB = ResourcePath.Create("graphics/second/1.png");
        var sharedFrame = ResourcePath.Create("graphics/shared-frame.png");

        var builder = new PackageBuilder().WithName("Shared Frame Pack").WithVersion("0.1.0");
        builder.AddResource(ResourceKind.TileGraphic, graphicA, Encoding.UTF8.GetBytes("SOLID-TILE-PNG"), "image/png");
        builder.AddResource(ResourceKind.TileGraphic, graphicB, Encoding.UTF8.GetBytes("SOLID-TILE-PNG"), "image/png");
        builder.AddResource(ResourceKind.TileGraphic, sharedFrame, Encoding.UTF8.GetBytes("SHARED-FRAME-PNG"), "image/png");

        var definitionA = new TileSetDefinition { Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(graphicA), Frames = new[] { ResourceReference.ToSelf(sharedFrame) } } } };
        var definitionB = new TileSetDefinition { Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(graphicB), Frames = new[] { ResourceReference.ToSelf(sharedFrame) } } } };
        builder.AddResource(ResourceKind.TileSet, tileSetA, LevelContentSerializer.WriteTileSet(definitionA));
        builder.AddResource(ResourceKind.TileSet, tileSetB, LevelContentSerializer.WriteTileSet(definitionB));

        builder.AddResource(ResourceKind.Level, ResourcePath.Create("levels/first.json"), LevelContentSerializer.WriteLevel(MinimalLevel(tileSetA)));
        builder.AddResource(ResourceKind.Level, ResourcePath.Create("levels/second.json"), LevelContentSerializer.WriteLevel(MinimalLevel(tileSetB)));

        using var package = ToPackage(builder);

        var result = TileSetMigration.Migrate(package);
        using var migrated = PackageReader.Open(new MemoryStream(result.Bytes));

        Assert.Multiple(() =>
        {
            Assert.That(result.DuplicateTileSetsRemoved, Is.EqualTo(1));
            Assert.That(migrated.Contains(tileSetA), Is.True);
            Assert.That(migrated.Contains(tileSetB), Is.False);
            Assert.That(migrated.Contains(graphicB), Is.False, "graphicB is exclusively used by the removed duplicate.");
            Assert.That(migrated.Contains(sharedFrame), Is.True, "the shared frame graphic is still used by the surviving tile set and must not be removed.");
        });
    }

    private const string ContentSignatureGuardScriptSource = "$onContact = $other => { player.hurt(1); }\n{ \"onContact\": onContact }";
    private const string ContentSignatureGuardAltScriptSource = "$onEnter = $who => { player.heal(1); }\n{ \"onEnter\": onEnter }";

    private static readonly ResourcePath ContentSignatureGuardGraphicPath = ResourcePath.Create("graphics/content-signature-guard/base.png");
    private static readonly ResourcePath ContentSignatureGuardAltGraphicPath = ResourcePath.Create("graphics/content-signature-guard/alt.png");
    private static readonly ResourcePath ContentSignatureGuardFramePath = ResourcePath.Create("graphics/content-signature-guard/frame.png");
    private static readonly ResourcePath ContentSignatureGuardScriptPath = ResourcePath.Create("scripts/content-signature-guard.poo");
    private static readonly ResourcePath ContentSignatureGuardAltScriptPath = ResourcePath.Create("scripts/content-signature-guard-alt.poo");
    private static readonly ResourcePath ContentSignatureGuardScriptPathA = ResourcePath.Create("scripts/content-signature-guard-route-a.poo");
    private static readonly ResourcePath ContentSignatureGuardScriptPathB = ResourcePath.Create("scripts/content-signature-guard-route-b.poo");

    private static TileDefinition ContentSignatureGuardTile(
        int id = 1,
        string? name = "grass",
        ResourceReference? graphic = null,
        CollisionShapeDefinition? collisionShape = null,
        IReadOnlyList<ResourceReference>? frames = null,
        double animationSpeed = TileDefinition.DefaultAnimationSpeed,
        int? terrain = null,
        TerrainPeering peeringBits = TerrainPeering.None,
        BehaviorBinding? behavior = null) => new()
    {
        Id = id,
        Name = name,
        Graphic = graphic ?? ResourceReference.ToSelf(ContentSignatureGuardGraphicPath),
        CollisionShape = collisionShape ?? CollisionShapeDefinition.Full,
        Frames = frames ?? Array.Empty<ResourceReference>(),
        AnimationSpeed = animationSpeed,
        Terrain = terrain,
        PeeringBits = peeringBits,
        Behavior = behavior,
    };

    private static IEnumerable<TestCaseData> ContentSignaturePropertyCases()
    {
        yield return new TestCaseData(
            nameof(TileDefinition.Id),
            ContentSignatureGuardTile(id: 1),
            ContentSignatureGuardTile(id: 2),
            (Action<PackageBuilder>)(_ => { }));

        yield return new TestCaseData(
            nameof(TileDefinition.Name),
            ContentSignatureGuardTile(name: "grass"),
            ContentSignatureGuardTile(name: "dirt"),
            (Action<PackageBuilder>)(_ => { }));

        yield return new TestCaseData(
            nameof(TileDefinition.Graphic),
            ContentSignatureGuardTile(graphic: ResourceReference.ToSelf(ContentSignatureGuardGraphicPath)),
            ContentSignatureGuardTile(graphic: ResourceReference.ToSelf(ContentSignatureGuardAltGraphicPath)),
            (Action<PackageBuilder>)(builder => builder.AddResource(ResourceKind.TileGraphic, ContentSignatureGuardAltGraphicPath, Encoding.UTF8.GetBytes("ALT-GRAPHIC-PNG"), "image/png")));

        yield return new TestCaseData(
            nameof(TileDefinition.CollisionShape),
            ContentSignatureGuardTile(collisionShape: CollisionShapeDefinition.Full),
            ContentSignatureGuardTile(collisionShape: CollisionShapeDefinition.None),
            (Action<PackageBuilder>)(_ => { }));

        yield return new TestCaseData(
            nameof(TileDefinition.Frames),
            ContentSignatureGuardTile(frames: Array.Empty<ResourceReference>()),
            ContentSignatureGuardTile(frames: new[] { ResourceReference.ToSelf(ContentSignatureGuardFramePath) }),
            (Action<PackageBuilder>)(builder => builder.AddResource(ResourceKind.TileGraphic, ContentSignatureGuardFramePath, Encoding.UTF8.GetBytes("FRAME-GRAPHIC-PNG"), "image/png")));

        yield return new TestCaseData(
            nameof(TileDefinition.AnimationSpeed),
            ContentSignatureGuardTile(animationSpeed: TileDefinition.DefaultAnimationSpeed),
            ContentSignatureGuardTile(animationSpeed: TileDefinition.DefaultAnimationSpeed + 3),
            (Action<PackageBuilder>)(_ => { }));

        yield return new TestCaseData(
            nameof(TileDefinition.Terrain),
            ContentSignatureGuardTile(terrain: null),
            ContentSignatureGuardTile(terrain: 3),
            (Action<PackageBuilder>)(_ => { }));

        yield return new TestCaseData(
            nameof(TileDefinition.PeeringBits),
            ContentSignatureGuardTile(peeringBits: TerrainPeering.None),
            ContentSignatureGuardTile(peeringBits: TerrainPeering.North),
            (Action<PackageBuilder>)(_ => { }));

        yield return new TestCaseData(
            nameof(TileDefinition.Behavior),
            ContentSignatureGuardTile(behavior: BehaviorBinding.FromScript(ResourceReference.ToSelf(ContentSignatureGuardScriptPath))),
            ContentSignatureGuardTile(behavior: BehaviorBinding.FromPredefined(PredefinedBehaviors.HealOnEnter, new Dictionary<string, object?> { ["amount"] = 5 })),
            (Action<PackageBuilder>)(builder => builder.AddResource(ResourceKind.Script, ContentSignatureGuardScriptPath, Encoding.UTF8.GetBytes(ContentSignatureGuardScriptSource))));
    }

    [Test]
    [Description("DiVoid #8433 CF-2: ContentSignaturePropertyCases must enumerate exactly TileDefinition's reflected settable properties, or a new field can join the type without ever gaining dedup-identity coverage.")]
    public void ContentSignaturePropertyCases_CoverEveryTileDefinitionSettableProperty()
    {
        var settable = typeof(TileDefinition)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.SetMethod is not null)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var covered = ContentSignaturePropertyCases()
            .Select(testCase => (string)testCase.Arguments[0]!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.That(covered, Is.EqualTo(settable));
    }

    [TestCaseSource(nameof(ContentSignaturePropertyCases))]
    [Description("DiVoid #8433 CF-2: two tile sets whose only difference is this one TileDefinition property must not be merged by TileSetMigration.Migrate.")]
    public void Migrate_DoesNotDedupeTileSets_DifferingOnlyInOneContentSignatureProperty(string propertyName, TileDefinition tileA, TileDefinition tileB, Action<PackageBuilder> registerResources)
        => AssertContentSignatureDistinguishes(propertyName, tileA, tileB, registerResources);

    private static IEnumerable<TestCaseData> ContentSignatureBranchCases()
    {
        yield return new TestCaseData(
            "Behavior: same predefined id, different parameter values",
            ContentSignatureGuardTile(behavior: BehaviorBinding.FromPredefined(PredefinedBehaviors.HealOnEnter, new Dictionary<string, object?> { ["amount"] = 5 })),
            ContentSignatureGuardTile(behavior: BehaviorBinding.FromPredefined(PredefinedBehaviors.HealOnEnter, new Dictionary<string, object?> { ["amount"] = 50 })),
            (Action<PackageBuilder>)(_ => { }));

        yield return new TestCaseData(
            "Behavior: two different predefined ids",
            ContentSignatureGuardTile(behavior: BehaviorBinding.FromPredefined(PredefinedBehaviors.HealOnEnter, new Dictionary<string, object?> { ["amount"] = 5 })),
            ContentSignatureGuardTile(behavior: BehaviorBinding.FromPredefined(PredefinedBehaviors.HurtOnContact, new Dictionary<string, object?> { ["amount"] = 5 })),
            (Action<PackageBuilder>)(_ => { }));

        yield return new TestCaseData(
            "Behavior: two different scripts",
            ContentSignatureGuardTile(behavior: BehaviorBinding.FromScript(ResourceReference.ToSelf(ContentSignatureGuardScriptPath))),
            ContentSignatureGuardTile(behavior: BehaviorBinding.FromScript(ResourceReference.ToSelf(ContentSignatureGuardAltScriptPath))),
            (Action<PackageBuilder>)(builder =>
            {
                builder.AddResource(ResourceKind.Script, ContentSignatureGuardScriptPath, Encoding.UTF8.GetBytes(ContentSignatureGuardScriptSource));
                builder.AddResource(ResourceKind.Script, ContentSignatureGuardAltScriptPath, Encoding.UTF8.GetBytes(ContentSignatureGuardAltScriptSource));
            }));

        yield return new TestCaseData(
            "Behavior: script vs none",
            ContentSignatureGuardTile(behavior: BehaviorBinding.FromScript(ResourceReference.ToSelf(ContentSignatureGuardScriptPath))),
            ContentSignatureGuardTile(behavior: null),
            (Action<PackageBuilder>)(builder => builder.AddResource(ResourceKind.Script, ContentSignatureGuardScriptPath, Encoding.UTF8.GetBytes(ContentSignatureGuardScriptSource))));

        yield return new TestCaseData(
            "Behavior: predefined vs none",
            ContentSignatureGuardTile(behavior: BehaviorBinding.FromPredefined(PredefinedBehaviors.HealOnEnter, new Dictionary<string, object?> { ["amount"] = 5 })),
            ContentSignatureGuardTile(behavior: null),
            (Action<PackageBuilder>)(_ => { }));

        yield return new TestCaseData(
            "CollisionShape: Rect branch, different rects same kind",
            ContentSignatureGuardTile(collisionShape: CollisionShapeDefinition.FromRect(0.1f, 0.1f, 0.5f, 0.5f)),
            ContentSignatureGuardTile(collisionShape: CollisionShapeDefinition.FromRect(0.2f, 0.2f, 0.6f, 0.6f)),
            (Action<PackageBuilder>)(_ => { }));

        yield return new TestCaseData(
            "CollisionShape: Polygon branch, different points same kind",
            ContentSignatureGuardTile(collisionShape: CollisionShapeDefinition.FromPolygon(new[] { new CollisionPointDefinition(0f, 0f), new CollisionPointDefinition(1f, 0f), new CollisionPointDefinition(0f, 1f) })),
            ContentSignatureGuardTile(collisionShape: CollisionShapeDefinition.FromPolygon(new[] { new CollisionPointDefinition(0f, 0f), new CollisionPointDefinition(1f, 0f), new CollisionPointDefinition(1f, 1f) })),
            (Action<PackageBuilder>)(_ => { }));

        yield return new TestCaseData(
            "CollisionShape: Preset branch, different presets same kind",
            ContentSignatureGuardTile(collisionShape: CollisionShapeDefinition.FromPreset(CollisionPreset.TopHalf)),
            ContentSignatureGuardTile(collisionShape: CollisionShapeDefinition.FromPreset(CollisionPreset.BottomHalf)),
            (Action<PackageBuilder>)(_ => { }));
    }

    [TestCaseSource(nameof(ContentSignatureBranchCases))]
    [Description("DiVoid #8443: BehaviorSignature and CollisionShapeSignature's branch bodies are entered by existing tests but not covered — each branch's OUTPUT, not just its entry, must distinguish these tile sets.")]
    public void Migrate_DoesNotDedupeTileSets_DifferingOnlyInAContentSignatureBranch(string caseLabel, TileDefinition tileA, TileDefinition tileB, Action<PackageBuilder> registerResources)
        => AssertContentSignatureDistinguishes(caseLabel, tileA, tileB, registerResources);

    private static IEnumerable<TestCaseData> ContentSignatureDualRouteCases()
    {
        yield return new TestCaseData(
            "Behavior script: identical content at different paths",
            ContentSignatureGuardTile(behavior: BehaviorBinding.FromScript(ResourceReference.ToSelf(ContentSignatureGuardScriptPathA))),
            ContentSignatureGuardTile(behavior: BehaviorBinding.FromScript(ResourceReference.ToSelf(ContentSignatureGuardScriptPathB))),
            (Action<PackageBuilder>)(builder =>
            {
                builder.AddResource(ResourceKind.Script, ContentSignatureGuardScriptPathA, Encoding.UTF8.GetBytes(ContentSignatureGuardScriptSource));
                builder.AddResource(ResourceKind.Script, ContentSignatureGuardScriptPathB, Encoding.UTF8.GetBytes(ContentSignatureGuardScriptSource));
            }));

        yield return new TestCaseData(
            "Behavior predefined parameters: same id and values, written in a different key order",
            ContentSignatureGuardTile(behavior: BehaviorBinding.FromPredefined(PredefinedBehaviors.HealOnEnter, new Dictionary<string, object?> { ["amount"] = 5, ["cooldown"] = 2 })),
            ContentSignatureGuardTile(behavior: BehaviorBinding.FromPredefined(PredefinedBehaviors.HealOnEnter, new Dictionary<string, object?> { ["cooldown"] = 2, ["amount"] = 5 })),
            (Action<PackageBuilder>)(_ => { }));
    }

    [TestCaseSource(nameof(ContentSignatureDualRouteCases))]
    [Description("DiVoid #8451: the dual of the branch-coverage guards — two tile sets reaching an identical field by a different route (content over path; sorted parameters) must still dedup.")]
    public void Migrate_DedupesTileSets_WhenAContentSignatureFieldIsReachedByADifferentRoute(string caseLabel, TileDefinition tileA, TileDefinition tileB, Action<PackageBuilder> registerResources)
        => AssertContentSignatureDedups(caseLabel, tileA, tileB, registerResources);

    private static Package BuildContentSignatureGuardPackage(TileDefinition tileA, TileDefinition tileB, Action<PackageBuilder> registerResources)
    {
        var tileSetAPath = ResourcePath.Create("tilesets/first.json");
        var tileSetBPath = ResourcePath.Create("tilesets/second.json");

        var builder = new PackageBuilder().WithName("Content Signature Guard Pack").WithVersion("0.1.0");
        builder.AddResource(ResourceKind.TileGraphic, ContentSignatureGuardGraphicPath, Encoding.UTF8.GetBytes("BASE-GRAPHIC-PNG"), "image/png");
        registerResources(builder);
        builder.AddResource(ResourceKind.TileSet, tileSetAPath, LevelContentSerializer.WriteTileSet(new TileSetDefinition { Tiles = new[] { tileA } }));
        builder.AddResource(ResourceKind.TileSet, tileSetBPath, LevelContentSerializer.WriteTileSet(new TileSetDefinition { Tiles = new[] { tileB } }));
        builder.AddResource(ResourceKind.Level, ResourcePath.Create("levels/first.json"), LevelContentSerializer.WriteLevel(ContentSignatureGuardLevel(tileSetAPath)));
        builder.AddResource(ResourceKind.Level, ResourcePath.Create("levels/second.json"), LevelContentSerializer.WriteLevel(ContentSignatureGuardLevel(tileSetBPath)));

        using var buffer = new MemoryStream();
        builder.Write(buffer);
        return PackageReader.Open(new MemoryStream(buffer.ToArray()));
    }

    private static void AssertContentSignatureDistinguishes(string caseLabel, TileDefinition tileA, TileDefinition tileB, Action<PackageBuilder> registerResources)
    {
        using var package = BuildContentSignatureGuardPackage(tileA, tileB, registerResources);

        var result = TileSetMigration.Migrate(package);

        Assert.That(result.LevelsRewritten, Is.EqualTo(0), $"tile sets differing only in {caseLabel} must not be deduped");
    }

    private static void AssertContentSignatureDedups(string caseLabel, TileDefinition tileA, TileDefinition tileB, Action<PackageBuilder> registerResources)
    {
        using var package = BuildContentSignatureGuardPackage(tileA, tileB, registerResources);

        var result = TileSetMigration.Migrate(package);

        Assert.That(result.LevelsRewritten, Is.EqualTo(1), $"tile sets reaching an identical {caseLabel} by a different route must still dedup");
    }

    private static LevelDefinition ContentSignatureGuardLevel(ResourcePath tileSetPath) => new()
    {
        TileSize = TileSize,
        Width = 1,
        Height = 1,
        TileSet = ResourceReference.ToSelf(tileSetPath),
        Layers = new[] { new LayerDefinition { Name = "terrain", Cells = new[] { 1 } } },
    };

    [Test]
    public void Migrate_LeavesDistinctTileSets_Untouched()
    {
        var builder = new PackageBuilder().WithName("Mixed Pack");
        var tileSetA = ResourcePath.Create("tilesets/a.json");
        var tileSetB = ResourcePath.Create("tilesets/b.json");
        builder.AddResource(ResourceKind.TileGraphic, GrassPath, Encoding.UTF8.GetBytes("GRASS-A"), "image/png");
        builder.AddResource(ResourceKind.TileGraphic, ResourcePath.Create("tiles/dirt.png"), Encoding.UTF8.GetBytes("DIRT-B"), "image/png");
        builder.AddResource(ResourceKind.TileSet, tileSetA, LevelContentSerializer.WriteTileSet(new TileSetDefinition
        {
            Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath), CollisionShape = Uberkarl.Content.CollisionShapeDefinition.Full } },
        }));
        builder.AddResource(ResourceKind.TileSet, tileSetB, LevelContentSerializer.WriteTileSet(new TileSetDefinition
        {
            Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(ResourcePath.Create("tiles/dirt.png")), CollisionShape = Uberkarl.Content.CollisionShapeDefinition.None } },
        }));
        builder.AddResource(ResourceKind.Level, ResourcePath.Create("levels/first.json"), LevelContentSerializer.WriteLevel(MinimalLevel(tileSetA)));
        builder.AddResource(ResourceKind.Level, ResourcePath.Create("levels/second.json"), LevelContentSerializer.WriteLevel(MinimalLevel(tileSetB)));
        using var package = ToPackage(builder);

        var result = TileSetMigration.Migrate(package);
        using var migrated = PackageReader.Open(new MemoryStream(result.Bytes));

        Assert.Multiple(() =>
        {
            Assert.That(result.LevelsRewritten, Is.EqualTo(0));
            Assert.That(result.DuplicateTileSetsRemoved, Is.EqualTo(0));
            Assert.That(migrated.Manifest.Resources.Count(e => e.Kind == ResourceKind.TileSet), Is.EqualTo(2), "two genuinely distinct tile sets must both survive.");
        });
    }

    [Test]
    public void Migrate_WithNoLevels_IsANoOp_StillRoundTrips()
    {
        var builder = new PackageBuilder().WithName("Empty");
        using var package = ToPackage(builder);

        var result = TileSetMigration.Migrate(package);
        using var migrated = PackageReader.Open(new MemoryStream(result.Bytes));

        Assert.Multiple(() =>
        {
            Assert.That(result.LevelsRewritten, Is.EqualTo(0));
            Assert.That(migrated.Id, Is.EqualTo(package.Id));
            Assert.That(migrated.Manifest.Name, Is.EqualTo("Empty"));
        });
    }

    // Simulates the exact organic redundancy this PR fixes prospectively: two "New" levels, each seeded
    // from the SAME default palette (so their generated tileset.json + graphic bytes are byte-identical),
    // saved into the same package under distinct namespaced slots — precisely what the pre-#7551
    // per-save-fabrication behaviour produced.
    private static (byte[] Bytes, ResourcePath TileSetA, ResourcePath TileSetB, ResourcePath GraphicA, ResourcePath GraphicB) BuildTwoLevelsWithIdenticalTileSets()
    {
        var tileSetA = ResourcePath.Create("tilesets/first.json");
        var tileSetB = ResourcePath.Create("tilesets/second.json");
        var graphicA = ResourcePath.Create("graphics/first/1.png");
        var graphicB = ResourcePath.Create("graphics/second/1.png");

        var builder = new PackageBuilder().WithName("Redundant Pack").WithVersion("0.1.0");
        builder.AddResource(ResourceKind.TileGraphic, graphicA, Encoding.UTF8.GetBytes("SOLID-TILE-PNG"), "image/png");
        builder.AddResource(ResourceKind.TileGraphic, graphicB, Encoding.UTF8.GetBytes("SOLID-TILE-PNG"), "image/png");

        var definitionA = new TileSetDefinition { Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(graphicA), CollisionShape = Uberkarl.Content.CollisionShapeDefinition.Full } } };
        var definitionB = new TileSetDefinition { Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(graphicB), CollisionShape = Uberkarl.Content.CollisionShapeDefinition.Full } } };
        // definitionA and definitionB reference DIFFERENT per-level-namespaced graphic paths (exactly the
        // real shape: graphics/first/1.png vs graphics/second/1.png), so their serialized JSON is NOT
        // byte-identical — but the graphic BYTES those paths resolve to ARE identical, which is what the
        // content-aware signature (not a raw byte comparison) is for.
        builder.AddResource(ResourceKind.TileSet, tileSetA, LevelContentSerializer.WriteTileSet(definitionA));
        builder.AddResource(ResourceKind.TileSet, tileSetB, LevelContentSerializer.WriteTileSet(definitionB));

        builder.AddResource(ResourceKind.Level, ResourcePath.Create("levels/first.json"), LevelContentSerializer.WriteLevel(MinimalLevel(tileSetA)));
        builder.AddResource(ResourceKind.Level, ResourcePath.Create("levels/second.json"), LevelContentSerializer.WriteLevel(MinimalLevel(tileSetB)));

        using var buffer = new MemoryStream();
        builder.Write(buffer);
        return (buffer.ToArray(), tileSetA, tileSetB, graphicA, graphicB);
    }

    private static (byte[] Bytes, ResourcePath TileSetA, ResourcePath TileSetB) BuildTwoLevelsWithAnimatedTileSets(bool sameFrameBytes)
    {
        var tileSetA = ResourcePath.Create("tilesets/first.json");
        var tileSetB = ResourcePath.Create("tilesets/second.json");
        var graphicA = ResourcePath.Create("graphics/first/1.png");
        var graphicB = ResourcePath.Create("graphics/second/1.png");
        var frameA = ResourcePath.Create("graphics/first/2.png");
        var frameB = ResourcePath.Create("graphics/second/2.png");

        var builder = new PackageBuilder().WithName("Animated Pack").WithVersion("0.1.0");
        builder.AddResource(ResourceKind.TileGraphic, graphicA, Encoding.UTF8.GetBytes("SOLID-TILE-PNG"), "image/png");
        builder.AddResource(ResourceKind.TileGraphic, graphicB, Encoding.UTF8.GetBytes("SOLID-TILE-PNG"), "image/png");
        builder.AddResource(ResourceKind.TileGraphic, frameA, Encoding.UTF8.GetBytes("SOLID-TILE-FRAME-PNG"), "image/png");
        builder.AddResource(ResourceKind.TileGraphic, frameB, Encoding.UTF8.GetBytes(sameFrameBytes ? "SOLID-TILE-FRAME-PNG" : "OTHER-TILE-FRAME-PNG"), "image/png");

        var definitionA = new TileSetDefinition { Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(graphicA), Frames = new[] { ResourceReference.ToSelf(frameA) } } } };
        var definitionB = new TileSetDefinition { Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(graphicB), Frames = new[] { ResourceReference.ToSelf(frameB) } } } };
        builder.AddResource(ResourceKind.TileSet, tileSetA, LevelContentSerializer.WriteTileSet(definitionA));
        builder.AddResource(ResourceKind.TileSet, tileSetB, LevelContentSerializer.WriteTileSet(definitionB));

        builder.AddResource(ResourceKind.Level, ResourcePath.Create("levels/first.json"), LevelContentSerializer.WriteLevel(MinimalLevel(tileSetA)));
        builder.AddResource(ResourceKind.Level, ResourcePath.Create("levels/second.json"), LevelContentSerializer.WriteLevel(MinimalLevel(tileSetB)));

        using var buffer = new MemoryStream();
        builder.Write(buffer);
        return (buffer.ToArray(), tileSetA, tileSetB);
    }

    private static LevelDefinition MinimalLevel(ResourcePath tileSetPath) => new()
    {
        TileSize = TileSize,
        Width = 1,
        Height = 1,
        TileSet = ResourceReference.ToSelf(tileSetPath),
        Layers = new[] { new LayerDefinition { Name = "terrain", Cells = new[] { 1 } } },
    };

    private static Package ToPackage(PackageBuilder builder)
    {
        using var buffer = new MemoryStream();
        builder.Write(buffer);
        return PackageReader.Open(new MemoryStream(buffer.ToArray()));
    }
}
