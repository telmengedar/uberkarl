using System.Text;
using NUnit.Framework;
using Uberkarl.Behavior;
using Uberkarl.Content.Json;
using Uberkarl.Packages;

namespace Uberkarl.Content.Tests;

/// <summary>
/// Covers the DiVoid #7738 (behavior system Phase 1) content-pipeline additions: a tile's default
/// contact/contact-leave binding, the level's sparse tile-behavior override/remove map, area triggers, and
/// the level script -- resolved (design #7704 §6, C-2's Definition → Resolved shape) and validated by
/// <see cref="LevelLoader"/>, and round-tripped through <see cref="LevelContentSerializer"/> (which now
/// registers <c>Uberkarl.Behavior.Json.BehaviorBindingJsonConverter</c>).
/// </summary>
[TestFixture]
public sealed class BehaviorSchemaTests
{
    private static readonly ResourcePath LevelPath = ResourcePath.Create("levels/demo.json");
    private static readonly ResourcePath TileSetPath = ResourcePath.Create("tileset.json");
    private static readonly ResourcePath GrassPath = ResourcePath.Create("tiles/grass.png");
    private static readonly ResourcePath SpikePath = ResourcePath.Create("tiles/spike.png");
    private static readonly ResourcePath SpikeScriptPath = ResourcePath.Create("scripts/spike.poo");
    private static readonly ResourcePath TriggerScriptPath = ResourcePath.Create("scripts/trigger.poo");
    private static readonly ResourcePath LevelScriptPath = ResourcePath.Create("scripts/level.poo");

    private const string SpikeScriptSource = "$onContact = $other => { player.hurt(10); }\n{ \"onContact\": onContact }";
    private const string TriggerScriptSource = "$onEnter = $who => { level.setState(\"seen\", true); }\n{ \"onEnter\": onEnter }";
    private const string LevelScriptSource = "$onLevelStart = [] => { self.setState(\"started\", true); }\n{ \"onLevelStart\": onLevelStart }";

    [Test]
    public void Load_ScriptedTile_ResolvesTypeDefaultBehavior_ForEveryPlacedCell()
    {
        var level = new LevelDefinition
        {
            TileSize = 16,
            Width = 2,
            Height = 1,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Layers = new[] { new LayerDefinition { Name = "main", Cells = new[] { 2, 2 } } },
        };

        using var registry = BuildRegistry(level, SpikeTileSet(), out _);
        var resolved = LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath));

        Assert.That(resolved.TileBehaviors[2].Script, Is.EqualTo(SpikeScriptSource));

        var scripted = resolved.EffectiveTileBehaviors().ToList();
        Assert.That(scripted, Has.Count.EqualTo(2));
        Assert.That(scripted.All(s => s.Layer == 0 && s.Binding.Script == SpikeScriptSource), Is.True);
        Assert.That(scripted.Select(s => s.Cell), Is.EquivalentTo(new[] { new GridPosition(0, 0), new GridPosition(1, 0) }));
    }

    [Test]
    public void Load_TileBehaviorOverride_ReplacesTypeDefault_ForOneInstanceOnly()
    {
        var level = new LevelDefinition
        {
            TileSize = 16,
            Width = 2,
            Height = 1,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Layers = new[] { new LayerDefinition { Name = "main", Cells = new[] { 2, 2 } } },
            TileBehaviorOverrides = new[]
            {
                new TileBehaviorOverride { Layer = 0, Cell = new GridPosition(1, 0), Binding = BehaviorBinding.FromPredefined(PredefinedBehaviors.HealOnEnter) },
            },
        };

        using var registry = BuildRegistry(level, SpikeTileSet(), out _);
        var resolved = LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath));

        var scripted = resolved.EffectiveTileBehaviors().ToDictionary(s => s.Cell);
        Assert.That(scripted[new GridPosition(0, 0)].Binding.Script, Is.EqualTo(SpikeScriptSource));
        Assert.That(scripted[new GridPosition(1, 0)].Binding.PredefinedId, Is.EqualTo(PredefinedBehaviors.HealOnEnter));
    }

    [Test]
    public void Load_TileBehaviorOverride_Removed_SuppressesTypeDefault_ForOneInstanceOnly()
    {
        var level = new LevelDefinition
        {
            TileSize = 16,
            Width = 2,
            Height = 1,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Layers = new[] { new LayerDefinition { Name = "main", Cells = new[] { 2, 2 } } },
            TileBehaviorOverrides = new[]
            {
                new TileBehaviorOverride { Layer = 0, Cell = new GridPosition(1, 0), Removed = true },
            },
        };

        using var registry = BuildRegistry(level, SpikeTileSet(), out _);
        var resolved = LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath));

        var scripted = resolved.EffectiveTileBehaviors().ToList();
        Assert.That(scripted, Has.Count.EqualTo(1));
        Assert.That(scripted[0].Cell, Is.EqualTo(new GridPosition(0, 0)));
    }

    [Test]
    public void Load_TileBehaviorOverride_OutOfBoundsLayer_Throws()
    {
        var level = MinimalLevel();
        var withOverride = new LevelDefinition
        {
            TileSize = level.TileSize,
            Width = level.Width,
            Height = level.Height,
            TileSet = level.TileSet,
            Layers = level.Layers,
            TileBehaviorOverrides = new[]
            {
                new TileBehaviorOverride { Layer = 5, Cell = new GridPosition(0, 0), Removed = true },
            },
        };

        using var registry = BuildRegistry(withOverride, SpikeTileSet(), out _);
        var exception = Assert.Throws<LevelContentException>(() => LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath)));
        Assert.That(exception!.Message, Does.Contain("layer 5"));
    }

    [Test]
    public void Load_TileBehaviorOverride_OutOfBoundsCell_Throws()
    {
        var level = MinimalLevel();
        var withOverride = new LevelDefinition
        {
            TileSize = level.TileSize,
            Width = level.Width,
            Height = level.Height,
            TileSet = level.TileSet,
            Layers = level.Layers,
            TileBehaviorOverrides = new[]
            {
                new TileBehaviorOverride { Layer = 0, Cell = new GridPosition(99, 0), Removed = true },
            },
        };

        using var registry = BuildRegistry(withOverride, SpikeTileSet(), out _);
        var exception = Assert.Throws<LevelContentException>(() => LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath)));
        Assert.That(exception!.Message, Does.Contain("outside"));
    }

    [Test]
    public void Load_TileBehaviorOverride_BothBindingAndRemoved_Throws()
    {
        var level = MinimalLevel();
        var withOverride = new LevelDefinition
        {
            TileSize = level.TileSize,
            Width = level.Width,
            Height = level.Height,
            TileSet = level.TileSet,
            Layers = level.Layers,
            TileBehaviorOverrides = new[]
            {
                new TileBehaviorOverride
                {
                    Layer = 0,
                    Cell = new GridPosition(0, 0),
                    Binding = BehaviorBinding.FromPredefined(PredefinedBehaviors.HurtOnContact),
                    Removed = true,
                },
            },
        };

        using var registry = BuildRegistry(withOverride, SpikeTileSet(), out _);
        var exception = Assert.Throws<LevelContentException>(() => LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath)));
        Assert.That(exception!.Message, Does.Contain("both"));
    }

    [Test]
    public void Load_TileBehaviorOverride_NeitherBindingNorRemoved_Throws()
    {
        var level = MinimalLevel();
        var withOverride = new LevelDefinition
        {
            TileSize = level.TileSize,
            Width = level.Width,
            Height = level.Height,
            TileSet = level.TileSet,
            Layers = level.Layers,
            TileBehaviorOverrides = new[] { new TileBehaviorOverride { Layer = 0, Cell = new GridPosition(0, 0) } },
        };

        using var registry = BuildRegistry(withOverride, SpikeTileSet(), out _);
        var exception = Assert.Throws<LevelContentException>(() => LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath)));
        Assert.That(exception!.Message, Does.Contain("neither"));
    }

    [Test]
    public void Load_TileBehaviorOverride_Duplicate_Throws()
    {
        var level = MinimalLevel();
        var withOverride = new LevelDefinition
        {
            TileSize = level.TileSize,
            Width = level.Width,
            Height = level.Height,
            TileSet = level.TileSet,
            Layers = level.Layers,
            TileBehaviorOverrides = new[]
            {
                new TileBehaviorOverride { Layer = 0, Cell = new GridPosition(0, 0), Removed = true },
                new TileBehaviorOverride { Layer = 0, Cell = new GridPosition(0, 0), Removed = true },
            },
        };

        using var registry = BuildRegistry(withOverride, SpikeTileSet(), out _);
        var exception = Assert.Throws<LevelContentException>(() => LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath)));
        Assert.That(exception!.Message, Does.Contain("more than once"));
    }

    [Test]
    public void Load_Trigger_ResolvesRectAndBinding()
    {
        var level = MinimalLevel();
        var withTrigger = new LevelDefinition
        {
            TileSize = level.TileSize,
            Width = 4,
            Height = 4,
            TileSet = level.TileSet,
            Layers = new[] { new LayerDefinition { Name = "main", Cells = Enumerable.Repeat(LayerDefinition.EmptyCell, 16).ToArray() } },
            Triggers = new[]
            {
                new AreaTriggerDefinition { Name = "heal-zone", X = 1, Y = 1, Width = 2, Height = 2, Binding = BehaviorBinding.FromScript(ResourceReference.ToSelf(TriggerScriptPath)) },
            },
        };

        using var registry = BuildRegistry(withTrigger, SpikeTileSet(), out _, includeTriggerScript: true);
        var resolved = LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath));

        Assert.That(resolved.Triggers, Has.Count.EqualTo(1));
        var trigger = resolved.Triggers[0];
        Assert.That(trigger.Name, Is.EqualTo("heal-zone"));
        Assert.That((trigger.X, trigger.Y, trigger.Width, trigger.Height), Is.EqualTo((1, 1, 2, 2)));
        Assert.That(trigger.Binding.Script, Is.EqualTo(TriggerScriptSource));
    }

    [TestCase(0, 0, 0, 2, "size")]
    [TestCase(0, 0, 2, 0, "size")]
    [TestCase(3, 0, 2, 2, "fit")]
    [TestCase(0, 3, 2, 2, "fit")]
    public void Load_Trigger_InvalidRect_Throws(int x, int y, int width, int height, string expectedFragment)
    {
        var level = MinimalLevel();
        var withTrigger = new LevelDefinition
        {
            TileSize = level.TileSize,
            Width = 4,
            Height = 4,
            TileSet = level.TileSet,
            Layers = new[] { new LayerDefinition { Name = "main", Cells = Enumerable.Repeat(LayerDefinition.EmptyCell, 16).ToArray() } },
            Triggers = new[]
            {
                new AreaTriggerDefinition { Name = "bad", X = x, Y = y, Width = width, Height = height, Binding = BehaviorBinding.FromPredefined(PredefinedBehaviors.HealOnEnter) },
            },
        };

        using var registry = BuildRegistry(withTrigger, SpikeTileSet(), out _);
        var exception = Assert.Throws<LevelContentException>(() => LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath)));
        Assert.That(exception!.Message, Does.Contain(expectedFragment));
    }

    [Test]
    public void Load_LevelScript_ResolvesWhenPresent_AndIsNullWhenAbsent()
    {
        var level = MinimalLevel();
        var withScript = new LevelDefinition
        {
            TileSize = level.TileSize,
            Width = level.Width,
            Height = level.Height,
            TileSet = level.TileSet,
            Layers = level.Layers,
            LevelScript = BehaviorBinding.FromScript(ResourceReference.ToSelf(LevelScriptPath)),
        };

        using var registryWith = BuildRegistry(withScript, SpikeTileSet(), out _, includeLevelScript: true);
        var resolvedWith = LevelLoader.Load(registryWith, ResourceReference.ToSelf(LevelPath));
        Assert.That(resolvedWith.LevelScript!.Script, Is.EqualTo(LevelScriptSource));

        using var registryWithout = BuildRegistry(level, SpikeTileSet(), out _);
        var resolvedWithout = LevelLoader.Load(registryWithout, ResourceReference.ToSelf(LevelPath));
        Assert.That(resolvedWithout.LevelScript, Is.Null);
    }

    [Test]
    public void JsonRoundTrip_PreservesTileBehavior_Triggers_Overrides_AndLevelScript()
    {
        var level = new LevelDefinition
        {
            TileSize = 16,
            Width = 2,
            Height = 1,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Layers = new[] { new LayerDefinition { Name = "main", Cells = new[] { 2, 2 } } },
            TileBehaviorOverrides = new[]
            {
                new TileBehaviorOverride { Layer = 0, Cell = new GridPosition(1, 0), Removed = true },
            },
            Triggers = new[]
            {
                new AreaTriggerDefinition
                {
                    Name = "zone",
                    X = 0, Y = 0, Width = 1, Height = 1,
                    Binding = BehaviorBinding.FromPredefined(PredefinedBehaviors.HealOnEnter, new Dictionary<string, object?> { ["amount"] = 25 }),
                },
            },
            LevelScript = BehaviorBinding.FromScript(ResourceReference.ToSelf(LevelScriptPath)),
        };
        var tileSet = new TileSetDefinition
        {
            Tiles = new[]
            {
                new TileDefinition { Id = 2, Graphic = ResourceReference.ToSelf(SpikePath), Behavior = BehaviorBinding.FromScript(ResourceReference.ToSelf(SpikeScriptPath)) },
            },
        };

        var roundTrippedLevel = LevelContentSerializer.ReadLevel(LevelContentSerializer.WriteLevel(level));
        var roundTrippedTileSet = LevelContentSerializer.ReadTileSet(LevelContentSerializer.WriteTileSet(tileSet));

        Assert.That(roundTrippedLevel.TileBehaviorOverrides, Has.Count.EqualTo(1));
        Assert.That(roundTrippedLevel.TileBehaviorOverrides[0].Removed, Is.True);
        Assert.That(roundTrippedLevel.TileBehaviorOverrides[0].Cell, Is.EqualTo(new GridPosition(1, 0)));

        Assert.That(roundTrippedLevel.Triggers, Has.Count.EqualTo(1));
        var trigger = roundTrippedLevel.Triggers[0];
        Assert.That(trigger.Binding.PredefinedId, Is.EqualTo(PredefinedBehaviors.HealOnEnter));
        Assert.That(trigger.Binding.Parameters["amount"], Is.EqualTo(25L));

        Assert.That(roundTrippedLevel.LevelScript!.Script, Is.EqualTo(ResourceReference.ToSelf(LevelScriptPath)));

        Assert.That(roundTrippedTileSet.Tiles[0].Behavior!.Script, Is.EqualTo(ResourceReference.ToSelf(SpikeScriptPath)));
    }

    private static LevelDefinition MinimalLevel() => new()
    {
        TileSize = 16,
        Width = 2,
        Height = 1,
        TileSet = ResourceReference.ToSelf(TileSetPath),
        Layers = new[] { new LayerDefinition { Name = "main", Cells = new[] { 2, 2 } } },
    };

    private static TileSetDefinition SpikeTileSet() => new()
    {
        Tiles = new[]
        {
            new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath) },
            new TileDefinition
            {
                Id = 2,
                Graphic = ResourceReference.ToSelf(SpikePath),
                Behavior = BehaviorBinding.FromScript(ResourceReference.ToSelf(SpikeScriptPath)),
            },
        },
    };

    private static PackageRegistry BuildRegistry(
        LevelDefinition level, TileSetDefinition tileSet, out PackageBuilder usedBuilder,
        bool includeTriggerScript = false, bool includeLevelScript = false)
    {
        var builder = new PackageBuilder().WithName("Behavior Demo Pack");
        builder.AddResource(ResourceKind.Level, LevelPath, LevelContentSerializer.WriteLevel(level));
        builder.AddResource(ResourceKind.TileSet, TileSetPath, LevelContentSerializer.WriteTileSet(tileSet));
        builder.AddResource(ResourceKind.TileGraphic, GrassPath, Encoding.UTF8.GetBytes("GRASS-PNG"), "image/png");
        builder.AddResource(ResourceKind.TileGraphic, SpikePath, Encoding.UTF8.GetBytes("SPIKE-PNG"), "image/png");
        builder.AddResource(ResourceKind.Script, SpikeScriptPath, Encoding.UTF8.GetBytes(SpikeScriptSource), "text/x-pooscript");
        if (includeTriggerScript)
            builder.AddResource(ResourceKind.Script, TriggerScriptPath, Encoding.UTF8.GetBytes(TriggerScriptSource), "text/x-pooscript");
        if (includeLevelScript)
            builder.AddResource(ResourceKind.Script, LevelScriptPath, Encoding.UTF8.GetBytes(LevelScriptSource), "text/x-pooscript");

        usedBuilder = builder;
        var buffer = new MemoryStream();
        builder.Write(buffer);
        buffer.Position = 0;
        return new PackageRegistry(PackageReader.Open(buffer));
    }
}
