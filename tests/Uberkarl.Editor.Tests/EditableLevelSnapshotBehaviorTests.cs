using System.Linq;
using System.Text;
using NUnit.Framework;
using Uberkarl.Behavior;
using Uberkarl.Content;
using Uberkarl.Content.Json;
using Uberkarl.Packages;

namespace Uberkarl.Editor.Tests;

/// <summary>
/// Regression coverage for DiVoid bug #7747: touching a scripted tile did nothing when playtested from the
/// LEVEL EDITOR's playtest overlay, even though the identical package played correctly stand-alone (via
/// <c>LevelPlay</c>/<see cref="LevelLoader"/>) and every P1/player-state unit test passed. Root cause was
/// NOT the behavior-runtime dispatch loop (that path is exercised end-to-end by
/// <c>Uberkarl.Behavior.Tests.PredefinedBehaviorsTests</c> and works correctly) — it was that
/// <see cref="EditableLevel"/>/<see cref="EditableTile"/> had NO fields at all for
/// <see cref="TileDefinition.Behavior"/>/<see cref="LevelDefinition.TileBehaviorOverrides"/>/
/// <see cref="LevelDefinition.Triggers"/>/<see cref="LevelDefinition.LevelScript"/>, so
/// <c>EditableLevelSnapshot.ToResolvedLevel</c> — the ONLY <see cref="ResolvedLevel"/> <c>PlaytestOverlay</c>
/// ever plays — always produced empty <c>TileBehaviors</c>/<c>Triggers</c>/a null <c>LevelScript</c>,
/// regardless of what the package actually authored. The HUD (which reads only <c>Player.Health</c>, wired
/// independently) still showed and worked; a scripted tile's contact behavior simply never had anything to
/// dispatch. Nothing in the existing P1/player-state suites caught this because they all exercise
/// <see cref="LevelLoader"/> directly — none of them go through <see cref="EditableLevelReader"/>/
/// <c>EditableLevelSnapshot</c>, the actual seam <c>PlaytestOverlay</c> depends on.
///
/// <para>
/// This test builds one small sample package (mirroring <c>tools/SampleContent/Program.cs</c>'s shape: a
/// tileset-default hurt-on-contact tile, a level-level tile-behavior override, an on-enter trigger, and a
/// level script) and loads it BOTH ways — the stand-alone <see cref="LevelLoader.Load"/> path and the editor
/// playtest path (<see cref="EditableLevelReader.FromPackage(Package)"/> →
/// <c>EditableLevelSnapshot.ToResolvedLevel</c>) — asserting the two agree. Before the fix this test failed:
/// the editor path's <c>EffectiveTileBehaviors()</c>/<c>Triggers</c>/<c>LevelScript</c> were all empty/null.
/// </para>
/// </summary>
[TestFixture]
public sealed class EditableLevelSnapshotBehaviorTests
{
    private static readonly ResourcePath LevelPath = ResourcePath.Create("levels/demo.json");
    private static readonly ResourcePath TileSetPath = ResourcePath.Create("tileset.json");
    private static readonly ResourcePath GrassPath = ResourcePath.Create("tiles/grass.png");
    private static readonly ResourcePath SpikePath = ResourcePath.Create("tiles/spike.png");
    private static readonly ResourcePath LevelScriptPath = ResourcePath.Create("scripts/level.poo");

    private const int TileSize = 16;
    private const int Width = 4;
    private const int Height = 2;
    private const int GrassTileId = 1;
    private const int SpikeTileId = 2;

    private const string LevelScriptSource =
        "$onLevelStart = [] => { self.setState(\"started\", true); }\n{ \"onLevelStart\": onLevelStart }";

    [Test]
    public void EditorPlaytestProjection_MatchesStandaloneLoad_ForScriptedTilesTriggersAndLevelScript()
    {
        var packageBytes = BuildSamplePackageBytes();

        // Path 1: stand-alone play (LevelPlay -> LevelLoader.Load), unaffected by DiVoid #7747.
        using var registry = new PackageRegistry(PackageReader.Open(new MemoryStream(packageBytes)));
        var standalone = LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath));

        // Path 2: editor playtest (PlaytestOverlay.Start receives EXACTLY this projection) -- the path that
        // was silently empty before the fix.
        using var package = PackageReader.Open(new MemoryStream(packageBytes));
        var editable = EditableLevelReader.FromPackage(package);
        var editorProjection = EditableLevelSnapshot.ToResolvedLevel(editable);

        var standaloneTiles = standalone.EffectiveTileBehaviors().ToList();
        var editorTiles = editorProjection.EffectiveTileBehaviors().ToList();

        Assert.That(editorTiles, Is.Not.Empty,
            "editor playtest projection lost every scripted tile cell (DiVoid #7747 regression)");
        Assert.That(editorTiles, Has.Count.EqualTo(standaloneTiles.Count));

        // The tileset-default hurt-on-contact spike is placed at BOTH (0,1) and (1,1); the level-level
        // override below removes the default from (1,1) specifically, so only (0,1) should survive into
        // EffectiveTileBehaviors() -- proves TileBehaviors (keyed by tile id) reaches every placed instance
        // of a scripted tile via the editor path, not just a single hard-coded cell.
        var editorSpikes = editorTiles.Where(t => t.Cell.Y == 1).OrderBy(t => t.Cell.X).ToList();
        Assert.That(editorSpikes, Has.Count.EqualTo(1));
        Assert.That(editorSpikes[0].Cell.X, Is.EqualTo(0));
        Assert.That(editorSpikes[0].Binding.IsPredefined, Is.True);
        Assert.That(editorSpikes[0].Binding.PredefinedId, Is.EqualTo(PredefinedBehaviors.HurtOnContact));

        // The level-level override REMOVING the default behavior from the second spike instance at (1,1)
        // must win over the tileset default (exactly like LevelLoader's own EffectiveTileBehaviors contract)
        // -- proves TileBehaviorOverrides threads through the editor path too, not just TileBehaviors.
        Assert.That(editorTiles.Any(t => t.Cell.X == 1 && t.Cell.Y == 1), Is.False,
            "the per-instance override removing this spike's behavior was not honored by the editor projection");

        Assert.That(editorProjection.Triggers, Has.Count.EqualTo(1));
        Assert.That(editorProjection.Triggers[0].Binding.IsPredefined, Is.True);
        Assert.That(editorProjection.Triggers[0].Binding.PredefinedId, Is.EqualTo(PredefinedBehaviors.HealOnEnter));

        Assert.That(editorProjection.LevelScript, Is.Not.Null);
        Assert.That(editorProjection.LevelScript!.IsScript, Is.True);
        Assert.That(editorProjection.LevelScript!.Script, Does.Contain("onLevelStart"));
    }

    private static byte[] BuildSamplePackageBytes()
    {
        var cells = new int[Width * Height];
        Array.Fill(cells, LayerDefinition.EmptyCell);
        cells[1 * Width + 0] = SpikeTileId; // (0,1): keeps the tileset-default hurtOnContact binding
        cells[1 * Width + 1] = SpikeTileId; // (1,1): overridden below to explicitly remove the behavior

        var spikeBehavior = BehaviorBinding.FromPredefined(PredefinedBehaviors.HurtOnContact,
            new Dictionary<string, object?> { ["amount"] = 10 });

        var tileSet = new TileSetDefinition
        {
            Tiles = new[]
            {
                new TileDefinition { Id = GrassTileId, Graphic = ResourceReference.ToSelf(GrassPath), CollisionShape = CollisionShapeDefinition.Full },
                new TileDefinition { Id = SpikeTileId, Graphic = ResourceReference.ToSelf(SpikePath), CollisionShape = CollisionShapeDefinition.Full, Behavior = spikeBehavior },
            },
        };

        var level = new LevelDefinition
        {
            TileSize = TileSize,
            Width = Width,
            Height = Height,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Layers = new[] { new LayerDefinition { Name = "terrain", Collision = true, Cells = cells } },
            TileBehaviorOverrides = new[]
            {
                new TileBehaviorOverride { Layer = 0, Cell = new GridPosition(1, 1), Removed = true },
            },
            Triggers = new[]
            {
                new AreaTriggerDefinition
                {
                    Name = "heal-zone", X = 2, Y = 0, Width = 1, Height = 1,
                    Binding = BehaviorBinding.FromPredefined(PredefinedBehaviors.HealOnEnter, new Dictionary<string, object?> { ["amount"] = 20 }),
                },
            },
            LevelScript = BehaviorBinding.FromScript(ResourceReference.ToSelf(LevelScriptPath)),
        };

        var builder = new PackageBuilder().WithName("Regression Pack").WithVersion("0.1.0");
        builder.AddResource(ResourceKind.TileGraphic, GrassPath, Encoding.UTF8.GetBytes("GRASS-PNG"), "image/png");
        builder.AddResource(ResourceKind.TileGraphic, SpikePath, Encoding.UTF8.GetBytes("SPIKE-PNG"), "image/png");
        builder.AddResource(ResourceKind.Script, LevelScriptPath, Encoding.UTF8.GetBytes(LevelScriptSource), "text/x-pooscript");
        builder.AddResource(ResourceKind.TileSet, TileSetPath, LevelContentSerializer.WriteTileSet(tileSet));
        builder.AddResource(ResourceKind.Level, LevelPath, LevelContentSerializer.WriteLevel(level));

        using var buffer = new MemoryStream();
        builder.Write(buffer);
        return buffer.ToArray();
    }
}
