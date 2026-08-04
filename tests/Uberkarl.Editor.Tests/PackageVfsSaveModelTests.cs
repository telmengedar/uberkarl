using System.Text;
using NUnit.Framework;
using Uberkarl.Content;
using Uberkarl.Content.Json;
using Uberkarl.Packages;

namespace Uberkarl.Editor.Tests;

/// <summary>
/// Covers the package-as-VFS save model correction (DiVoid #7571, design #7572): the fix for the "1
/// package = 1 level" conceptual flaw where <c>EditableLevelWriter</c> fabricated a whole package around
/// one level, dropping any sibling resources on save (the #7570 §16.7 boundary). This file exercises the
/// new pieces directly — <see cref="LevelResourcePaths"/> (per-resource namespacing),
/// <see cref="PackageContext"/> (de-conflated archive identity), <see cref="EditableLevel.Attach"/>
/// (establishing/re-establishing a level's namespaced resource slot), <see cref="LevelMergeWriter"/>
/// (contribution-build + compose + build-fresh), and the <see cref="LevelEditSession"/> attach/save
/// surface the Godot glue drives — plus the end-to-end acceptance scenario: saving a level into a
/// package that already holds other resources must preserve them, and two distinctly-named levels must
/// coexist in one package without colliding.
/// </summary>
[TestFixture]
public sealed class PackageVfsSaveModelTests
{
    private const int TileSize = 16;
    private const int Width = 2;
    private const int Height = 2;

    private static readonly ResourcePath GrassPath = ResourcePath.Create("tiles/grass.png");

    // A pre-existing "village" tile set's path — a plain, arbitrary fixture path (not derived from the
    // now-removed LevelResourcePaths.TileSetPath; a tile set's namespace is TileSetResourcePaths' own,
    // DiVoid #7551 Phase 1a) standing in for content that already existed in a package before this PR.
    private static readonly ResourcePath VillageTileSetPath = ResourcePath.Create("tilesets/village.json");

    // ----- LevelResourcePaths -----

    [TestCase("Forest Level", "forest-level")]
    [TestCase("  Trim Me  ", "trim-me")]
    [TestCase("Multiple   Spaces", "multiple-spaces")]
    [TestCase("Punctuation!!! Galore???", "punctuation-galore")]
    [TestCase("CAPS Lock", "caps-lock")]
    [TestCase("123 Numeric", "123-numeric")]
    public void Slugify_ProducesTheExpectedSlug(string name, string expected)
    {
        Assert.That(LevelResourcePaths.Slugify(name), Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("!!!")]
    [TestCase(null)]
    public void Slugify_WithNoAlphanumericContent_FallsBackToLevel(string? name)
    {
        Assert.That(LevelResourcePaths.Slugify(name!), Is.EqualTo("level"));
    }

    [Test]
    public void LevelPath_FollowsTheNamespacedConvention()
    {
        // Shared-tileset correction (DiVoid #7551 Phase 1a): LevelResourcePaths no longer addresses tile
        // set/graphic paths at all — those are TileSetResourcePaths' own namespace now (see the
        // TileSetResourcePaths tests below), since a tile set is no longer a level-owned resource.
        Assert.That(LevelResourcePaths.LevelPath("forest"), Is.EqualTo(ResourcePath.Create("levels/forest.json")));
    }

    [Test]
    public void TileSetResourcePaths_TileSetPath_GraphicPath_FollowTheNamespacedConvention()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TileSetResourcePaths.TileSetPath("forest-tiles"), Is.EqualTo(ResourcePath.Create("tilesets/forest-tiles.json")));
            Assert.That(TileSetResourcePaths.GraphicPath("forest-tiles", 3), Is.EqualTo(ResourcePath.Create("graphics/forest-tiles/3.png")));
        });
    }

    [Test]
    public void TileSetResourcePaths_SlugFromTileSetPath_ExtractsTheSlug()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TileSetResourcePaths.SlugFromTileSetPath(ResourcePath.Create("tilesets/forest-tiles.json")), Is.EqualTo("forest-tiles"));
            Assert.That(TileSetResourcePaths.SlugFromTileSetPath(ResourcePath.Create("tileset.json")), Is.Null, "a legacy non-namespaced path does not follow the convention.");
        });
    }

    [Test]
    public void SlugFromLevelPath_ExtractsTheSlug_ForANamespacedPath()
    {
        Assert.That(LevelResourcePaths.SlugFromLevelPath(ResourcePath.Create("levels/forest.json")), Is.EqualTo("forest"));
    }

    [Test]
    public void SlugFromLevelPath_ReturnsNull_ForALegacyNonNamespacedPath()
    {
        // A path that predates this correction (the old fixed constant) does not fit the convention.
        Assert.That(LevelResourcePaths.SlugFromLevelPath(ResourcePath.Create("level.json")), Is.Null);
    }

    [Test]
    public void UniqueSlug_ReturnsTheBaseSlug_WhenItIsFree()
    {
        Assert.That(LevelResourcePaths.UniqueSlug("forest", _ => false), Is.EqualTo("forest"));
    }

    [Test]
    public void UniqueSlug_AppendsAnIncrementingSuffix_UntilFree()
    {
        var taken = new HashSet<string> { "forest", "forest-2", "forest-3" };
        Assert.That(LevelResourcePaths.UniqueSlug("forest", candidate => taken.Contains(candidate)), Is.EqualTo("forest-4"));
    }

    // ----- PackageContext -----

    [Test]
    public void PackageContext_FromPackage_CarriesIdentityAndInventory_IndependentOfAnyLevel()
    {
        var builder = new PackageBuilder()
            .WithName("Adventure Pack")
            .WithVersion("3.0.0")
            .WithAttribution(new Attribution { Author = "Toni" });
        builder.AddResource(ResourceKind.Level, ResourcePath.Create("levels/a.json"), Encoding.UTF8.GetBytes("A"));
        builder.AddResource(ResourceKind.Level, ResourcePath.Create("levels/b.json"), Encoding.UTF8.GetBytes("B"));
        using var package = ToPackage(builder);
        var handle = FakeHandle();

        var context = PackageContext.FromPackage(package, handle);

        Assert.Multiple(() =>
        {
            Assert.That(context.Name, Is.EqualTo("Adventure Pack"));
            Assert.That(context.Version, Is.EqualTo("3.0.0"));
            Assert.That(context.Attribution!.Author, Is.EqualTo("Toni"));
            Assert.That(context.Resources, Has.Count.EqualTo(2));
            Assert.That(context.Contains(ResourcePath.Create("levels/a.json")), Is.True);
            Assert.That(context.Contains(ResourcePath.Create("levels/missing.json")), Is.False);
        });
    }

    // ----- EditableLevel.Attach -----

    [Test]
    public void Attach_NamespacesTheLevelPath_FromTheGivenSlug_MarksAttached_NeverTouchesTileGraphicPaths()
    {
        // Shared-tileset correction (DiVoid #7551 Phase 1a): a level's own Attach only ever namespaces
        // ITS level path — tile graphic paths belong to whichever tile set is bound (a separate resource
        // with its own independent attach lifecycle, EditableTileSet.Attach), so they must be untouched by
        // a level-side attach/rename.
        var level = EditableLevel.CreateBlank("Untitled", TileSize, Width, Height, ResourceReference.ToSelf(GrassPath), Palette());
        var graphicPathBefore = level.Tiles[0].GraphicPath;
        Assert.That(level.IsAttached, Is.False);

        level.Attach("forest", overwriteLevelPath: null);

        Assert.Multiple(() =>
        {
            Assert.That(level.IsAttached, Is.True);
            Assert.That(level.LevelPath, Is.EqualTo(LevelResourcePaths.LevelPath("forest")));
            Assert.That(level.Tiles[0].GraphicPath, Is.EqualTo(graphicPathBefore));
        });
    }

    [Test]
    public void Attach_WithAnExplicitOverwritePath_UsesItVerbatimForTheLevelPath()
    {
        var level = EditableLevel.CreateBlank("Untitled", TileSize, Width, Height, ResourceReference.ToSelf(GrassPath), Palette());
        var existingPath = ResourcePath.Create("levels/legacy.json");

        level.Attach("legacy", existingPath);

        Assert.That(level.LevelPath, Is.EqualTo(existingPath));
    }

    // ----- LevelMergeWriter: BuildContributions / Compose / BuildFresh -----

    [Test]
    public void BuildContributions_YieldsOnlyTheLevelDefinition_CarryingTheBoundTileSetAsAReference()
    {
        // The shared-tileset correction's central contract (DiVoid #7551 Phase 1a, design #7580): a level
        // no longer contributes a tileset.json or any tile graphics on save — those belong to
        // TileSetMergeWriter (see the tests below). The level's ONE contribution carries whatever
        // TileSetReference it is bound to, unchanged.
        var reference = new ResourceReference(PackageId.New(), ResourcePath.Create("tilesets/shared.json"));
        var level = EditableLevel.CreateBlank("Untitled", TileSize, Width, Height, reference, Palette());
        level.Attach("forest", null);

        var contributions = LevelMergeWriter.BuildContributions(level);

        Assert.Multiple(() =>
        {
            Assert.That(contributions, Has.Count.EqualTo(1));
            Assert.That(contributions[0].Kind, Is.EqualTo(ResourceKind.Level));
            Assert.That(contributions[0].Path, Is.EqualTo(level.LevelPath));

            var written = LevelContentSerializer.ReadLevel(contributions[0].Payload);
            Assert.That(written.TileSet, Is.EqualTo(reference));
        });
    }

    // ----- TileSetMergeWriter: BuildContributions (DiVoid #7551 Phase 1a counterpart to LevelMergeWriter) -----

    [Test]
    public void TileSetMergeWriter_BuildContributions_YieldsTheDefinitionAndEveryTileGraphic_AtTheTileSetsOwnPaths()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled", Palette());
        tileSet.Attach("forest-tiles", null);

        var contributions = TileSetMergeWriter.BuildContributions(tileSet);

        Assert.Multiple(() =>
        {
            Assert.That(contributions.Count(c => c.Kind == ResourceKind.TileSet), Is.EqualTo(1));
            Assert.That(contributions.Count(c => c.Kind == ResourceKind.TileGraphic), Is.EqualTo(tileSet.Tiles.Count));
            Assert.That(contributions.Select(c => c.Path), Contains.Item(tileSet.TileSetPath));
            Assert.That(contributions.Select(c => c.Path), Contains.Item(tileSet.Tiles[0].GraphicPath));
        });
    }

    // ----- TileSetMergeWriter + EditableTileSetReader: animated-tile round trip (DiVoid #7551 Phase 2) -----

    [Test]
    public void TileSetMergeWriter_BuildContributions_IncludesEveryAnimationFrameGraphic()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled", Palette());
        var id = tileSet.Tiles[0].Id;
        tileSet.AddFrame(id, Encoding.UTF8.GetBytes("FRAME-2"));
        tileSet.AddFrame(id, Encoding.UTF8.GetBytes("FRAME-3"));
        tileSet.Attach("forest-tiles", null);

        var contributions = TileSetMergeWriter.BuildContributions(tileSet);
        var tile = tileSet.Tiles[0];

        Assert.Multiple(() =>
        {
            // The primary graphic plus both extra frames — three TileGraphic contributions for one tile.
            Assert.That(contributions.Count(c => c.Kind == ResourceKind.TileGraphic), Is.EqualTo(3));
            Assert.That(contributions.Select(c => c.Path), Contains.Item(tile.Frames[0].GraphicPath));
            Assert.That(contributions.Select(c => c.Path), Contains.Item(tile.Frames[1].GraphicPath));
        });
    }

    [Test]
    public void SavedAnimatedTileSet_ReloadsThroughEditableTileSetReader_WithFramesAndSpeedIntact()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled", Palette());
        var id = tileSet.Tiles[0].Id;
        var frame2Bytes = Encoding.UTF8.GetBytes("FRAME-2");
        var frame3Bytes = Encoding.UTF8.GetBytes("FRAME-3");
        tileSet.AddFrame(id, frame2Bytes);
        tileSet.AddFrame(id, frame3Bytes);
        tileSet.SetAnimationSpeed(id, 12.0);
        tileSet.Attach("forest-tiles", null);

        var contributions = TileSetMergeWriter.BuildContributions(tileSet);
        var packageBytes = TileSetMergeWriter.BuildFresh("Forest Pack", contributions);
        using var package = PackageReader.Open(new MemoryStream(packageBytes));

        var reloaded = EditableTileSetReader.FromPackage(package);
        var reloadedTile = reloaded.Tiles.First(t => t.Id == id);

        Assert.Multiple(() =>
        {
            Assert.That(reloadedTile.IsAnimated, Is.True);
            Assert.That(reloadedTile.Frames, Has.Count.EqualTo(2));
            Assert.That(reloadedTile.Frames[0].Graphic, Is.EqualTo(frame2Bytes));
            Assert.That(reloadedTile.Frames[1].Graphic, Is.EqualTo(frame3Bytes));
            Assert.That(reloadedTile.AnimationSpeed, Is.EqualTo(12.0));
        });
    }

    [Test]
    public void SavedSimpleTileSet_ReloadsThroughEditableTileSetReader_AsNotAnimated()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled", Palette());
        tileSet.Attach("forest-tiles", null);

        var contributions = TileSetMergeWriter.BuildContributions(tileSet);
        var packageBytes = TileSetMergeWriter.BuildFresh("Forest Pack", contributions);
        using var package = PackageReader.Open(new MemoryStream(packageBytes));

        var reloaded = EditableTileSetReader.FromPackage(package);

        Assert.That(reloaded.Tiles[0].IsAnimated, Is.False);
    }

    [Test]
    public void Compose_PreservesSiblingResourcesAndPackageIdentity_TheSevenFiveSeventyPointSixteenSevenScenario()
    {
        // The exact scenario DiVoid #7570 §16.7 flagged as a known boundary — now expected to PASS:
        // saving a level into a package that already holds OTHER resources must not clobber them, and the
        // package must keep its own identity/name (independent of the level).
        var existingBuilder = new PackageBuilder().WithName("Campaign Pack").WithVersion("1.0.0");
        existingBuilder.AddResource(ResourceKind.Script, ResourcePath.Create("scripts/intro.lua"), Encoding.UTF8.GetBytes("intro-script"));
        existingBuilder.AddResource(ResourceKind.Track, ResourcePath.Create("audio/theme.ogg"), Encoding.UTF8.GetBytes("theme-bytes"));
        existingBuilder.AddResource(ResourceKind.Level, LevelResourcePaths.LevelPath("village"),
            LevelContentSerializer.WriteLevel(MinimalLevelDefinition()));
        existingBuilder.AddResource(ResourceKind.TileSet, VillageTileSetPath,
            LevelContentSerializer.WriteTileSet(MinimalTileSetDefinition()));
        existingBuilder.AddResource(ResourceKind.TileGraphic, GrassPath, Encoding.UTF8.GetBytes("GRASS-PNG"), "image/png");
        using var existingPackage = ToPackage(existingBuilder);
        var originalId = existingPackage.Id;

        // Author loads and edits a SECOND, distinctly-named level bound to its OWN fresh tile set, then
        // saves both into the SAME package in one merge (mirrors LevelEditor.SaveLevelAndTileSet — a
        // level's own contribution is just its level.json under the shared-tileset correction, so the
        // bound tile set's contributions are folded in alongside it).
        var forestTileSet = EditableTileSet.CreateBlank("Forest Tiles", Palette());
        forestTileSet.Attach("forest-tiles", overwriteTileSetPath: null);
        var forestLevel = EditableLevel.CreateBlank("Forest", TileSize, Width, Height, ResourceReference.ToSelf(forestTileSet.TileSetPath), Palette());
        forestLevel.Attach("forest", overwriteLevelPath: null);
        var contributions = LevelMergeWriter.BuildContributions(forestLevel)
            .Concat(TileSetMergeWriter.BuildContributions(forestTileSet))
            .ToList();

        var mergedBytes = LevelMergeWriter.Compose(existingPackage, contributions);
        using var merged = PackageReader.Open(new MemoryStream(mergedBytes));

        Assert.Multiple(() =>
        {
            // Package identity is untouched — independent of the level's name.
            Assert.That(merged.Id, Is.EqualTo(originalId));
            Assert.That(merged.Manifest.Name, Is.EqualTo("Campaign Pack"));

            // The pre-existing siblings survive, byte-for-byte.
            Assert.That(merged.ReadBytes(ResourcePath.Create("scripts/intro.lua")), Is.EqualTo(Encoding.UTF8.GetBytes("intro-script")));
            Assert.That(merged.ReadBytes(ResourcePath.Create("audio/theme.ogg")), Is.EqualTo(Encoding.UTF8.GetBytes("theme-bytes")));
            Assert.That(merged.Contains(LevelResourcePaths.LevelPath("village")), Is.True);

            // The new level is ALSO present, at its own distinct path — two levels, one package, no collision.
            Assert.That(merged.Contains(LevelResourcePaths.LevelPath("forest")), Is.True);
            Assert.That(merged.Contains(LevelResourcePaths.LevelPath("village")), Is.True);

            // Its bound tile set landed too, at ITS own distinct (non-level-namespaced) path — village's own
            // tileset resource is untouched, still there, no collision between the two tile sets either.
            Assert.That(merged.Contains(TileSetResourcePaths.TileSetPath("forest-tiles")), Is.True);
            Assert.That(merged.Contains(VillageTileSetPath), Is.True);

            var villageAndForest = merged.Manifest.Resources.Count(e => e.Kind == ResourceKind.Level);
            Assert.That(villageAndForest, Is.EqualTo(2));
        });
    }

    [Test]
    public void BuildFresh_MintsANewPackageId_AndContainsOnlyTheContributions()
    {
        var level = EditableLevel.CreateBlank("Untitled", TileSize, Width, Height, ResourceReference.ToSelf(GrassPath), Palette());
        level.Attach("solo", null);
        var contributions = LevelMergeWriter.BuildContributions(level);

        var bytes = LevelMergeWriter.BuildFresh("Solo Pack", contributions);
        using var package = PackageReader.Open(new MemoryStream(bytes));

        Assert.Multiple(() =>
        {
            Assert.That(package.Id.IsSelf, Is.False);
            Assert.That(package.Manifest.Name, Is.EqualTo("Solo Pack"));
            Assert.That(package.Manifest.Resources, Has.Count.EqualTo(contributions.Count));
        });
    }

    [Test]
    public void BuildFresh_CalledTwice_MintsDistinctPackageIds()
    {
        var level = EditableLevel.CreateBlank("Untitled", TileSize, Width, Height, ResourceReference.ToSelf(GrassPath), Palette());
        level.Attach("solo", null);
        var contributions = LevelMergeWriter.BuildContributions(level);

        using var first = PackageReader.Open(new MemoryStream(LevelMergeWriter.BuildFresh("Pack", contributions)));
        using var second = PackageReader.Open(new MemoryStream(LevelMergeWriter.BuildFresh("Pack", contributions)));

        Assert.That(first.Id, Is.Not.EqualTo(second.Id));
    }

    // ----- LevelEditSession: AttachAsNewResource / AttachToExistingResource -----

    [Test]
    public void Session_AttachAsNewResource_DerivesSlugFromTheLevelsCurrentName()
    {
        var session = new LevelEditSession(EditableLevel.CreateBlank("Untitled", TileSize, Width, Height, ResourceReference.ToSelf(GrassPath), Palette()));
        session.RenameLevel("Forest Level");

        session.AttachAsNewResource(Array.Empty<ResourceEntry>());

        Assert.Multiple(() =>
        {
            Assert.That(session.Level.IsAttached, Is.True);
            Assert.That(session.Level.LevelPath, Is.EqualTo(LevelResourcePaths.LevelPath("forest-level")));
        });
    }

    [Test]
    public void Session_AttachAsNewResource_MovesAnAlreadyAttachedLevel_ToItsOwnNewSlug_RegardlessOfPriorAttachment()
    {
        // Regression lock for a live-harness-caught bug (not reachable from unit tests alone, since the
        // wrong guard lived in the Godot glue's LevelEditor.OnBrowserSaveRequested, not here): loading an
        // existing level (e.g. "demo") and Save-As'ing it under a brand-new name ("veriforest") must
        // attach the level to demo's/veriforest's OWN new slot — it must NOT be treated as "already
        // attached, nothing to do" just because the level came from a real resource already. The glue
        // must call AttachAsNewResource unconditionally for a "＋ New level…" outcome; this test pins the
        // session-level contract that makes that call always move the level, regardless of IsAttached.
        var loadedLevel = new EditableLevel(
            "demo", ResourcePath.Create("levels/demo.json"), ResourceReference.ToSelf(ResourcePath.Create("tilesets/demo.json")),
            TileSize, Width, Height, backgroundColor: null,
            new Dictionary<string, GridPosition>(), defaultSpawn: null, Palette(),
            new[] { new EditableLayer("terrain", collision: true, scrollSpeed: 1f, repeat: false, new int[Width * Height]) },
            isAttached: true);
        var session = new LevelEditSession(loadedLevel);
        session.RenameLevel("Veriforest");

        session.AttachAsNewResource(Array.Empty<ResourceEntry>());

        Assert.Multiple(() =>
        {
            Assert.That(session.Level.LevelPath, Is.EqualTo(LevelResourcePaths.LevelPath("veriforest")));
            Assert.That(session.Level.LevelPath, Is.Not.EqualTo(ResourcePath.Create("levels/demo.json")), "must move to a NEW slot, not silently overwrite the origin resource.");
        });
    }

    [Test]
    public void Session_AttachAsNewResource_UniquifiesAgainstACollidingSibling()
    {
        var session = new LevelEditSession(EditableLevel.CreateBlank("Untitled", TileSize, Width, Height, ResourceReference.ToSelf(GrassPath), Palette()));
        session.RenameLevel("Forest Level");
        var existingResources = new[]
        {
            new ResourceEntry { Path = LevelResourcePaths.LevelPath("forest-level"), Kind = ResourceKind.Level },
        };

        session.AttachAsNewResource(existingResources);

        Assert.That(session.Level.LevelPath, Is.EqualTo(LevelResourcePaths.LevelPath("forest-level-2")));
    }

    [Test]
    public void Session_AttachToExistingResource_ReusesThePathVerbatim_AndStaysIdempotentAcrossResaves()
    {
        var overwritePath = ResourcePath.Create("levels/village.json");
        var session = new LevelEditSession(EditableLevel.CreateBlank("Untitled", TileSize, Width, Height, ResourceReference.ToSelf(GrassPath), Palette()));

        session.AttachToExistingResource(overwritePath);
        var firstLevelPath = session.Level.LevelPath;
        session.AttachToExistingResource(overwritePath); // re-save into the same slot

        Assert.Multiple(() =>
        {
            Assert.That(session.Level.LevelPath, Is.EqualTo(overwritePath));
            Assert.That(session.Level.LevelPath, Is.EqualTo(firstLevelPath), "re-attaching to the same slot must be idempotent, not drift to a fresh slug.");
        });
    }

    // ----- TileSetEditSession: AttachAsNewResource / AttachToExistingResource (DiVoid #7551 Phase 1a counterpart) -----

    [Test]
    public void TileSetEditSession_AttachAsNewResource_DerivesSlugFromTheTileSetsCurrentName()
    {
        var session = new TileSetEditSession(EditableTileSet.CreateBlank("Grassland Tiles", Palette()));

        session.AttachAsNewResource(Array.Empty<ResourceEntry>());

        Assert.Multiple(() =>
        {
            Assert.That(session.TileSet.IsAttached, Is.True);
            Assert.That(session.TileSet.TileSetPath, Is.EqualTo(TileSetResourcePaths.TileSetPath("grassland-tiles")));
        });
    }

    [Test]
    public void TileSetEditSession_AttachAsNewResource_UniquifiesAgainstACollidingSibling()
    {
        var session = new TileSetEditSession(EditableTileSet.CreateBlank("Grassland Tiles", Palette()));
        var existingResources = new[]
        {
            new ResourceEntry { Path = TileSetResourcePaths.TileSetPath("grassland-tiles"), Kind = ResourceKind.TileSet },
        };

        session.AttachAsNewResource(existingResources);

        Assert.That(session.TileSet.TileSetPath, Is.EqualTo(TileSetResourcePaths.TileSetPath("grassland-tiles-2")));
    }

    [Test]
    public void TileSetEditSession_AttachToExistingResource_ReusesThePathVerbatim_AndStaysIdempotentAcrossResaves()
    {
        var overwritePath = ResourcePath.Create("tilesets/village.json");
        var session = new TileSetEditSession(EditableTileSet.CreateBlank("Untitled", Palette()));

        session.AttachToExistingResource(overwritePath);
        var firstGraphicPath = session.TileSet.Tiles[0].GraphicPath;
        session.AttachToExistingResource(overwritePath); // re-save into the same slot

        Assert.Multiple(() =>
        {
            Assert.That(session.TileSet.TileSetPath, Is.EqualTo(overwritePath));
            Assert.That(session.TileSet.Tiles[0].GraphicPath, Is.EqualTo(firstGraphicPath), "re-attaching to the same slot must be idempotent, not drift to a fresh slug.");
        });
    }

    [Test]
    public void TileSetEditSession_EnsureAttached_OnlyAttachesOnce()
    {
        var session = new TileSetEditSession(EditableTileSet.CreateBlank("Untitled", Palette()));

        session.EnsureAttached(Array.Empty<ResourceEntry>());
        var pathAfterFirst = session.TileSet.TileSetPath;
        session.EnsureAttached(new[] { new ResourceEntry { Path = pathAfterFirst, Kind = ResourceKind.TileSet } });

        Assert.That(session.TileSet.TileSetPath, Is.EqualTo(pathAfterFirst), "a tile set that already has a home must never be moved by a later save.");
    }

    [Test]
    public void Session_BuildContributions_ThenSave_RoundTripsThroughTheMergeWriter()
    {
        var existing = new PackageBuilder().WithName("Host Pack");
        existing.AddResource(ResourceKind.Script, ResourcePath.Create("scripts/keep.lua"), Encoding.UTF8.GetBytes("keep-me"));
        using var existingPackage = ToPackage(existing);

        var session = new LevelEditSession(EditableLevel.CreateBlank("Untitled", TileSize, Width, Height, ResourceReference.ToSelf(GrassPath), Palette()));
        session.AttachAsNewResource(existingPackage.Manifest.Resources);

        var bytes = session.Save(existingPackage);
        using var merged = PackageReader.Open(new MemoryStream(bytes));

        Assert.Multiple(() =>
        {
            Assert.That(session.IsDirty, Is.False);
            Assert.That(merged.ReadBytes(ResourcePath.Create("scripts/keep.lua")), Is.EqualTo(Encoding.UTF8.GetBytes("keep-me")));
            Assert.That(merged.Contains(session.Level.LevelPath), Is.True);
        });
    }

    // ----- helpers -----

    private static IReadOnlyList<EditableTile> Palette() => new[]
    {
        new EditableTile(1, GrassPath, Encoding.UTF8.GetBytes("GRASS-PNG"), collides: true),
    };

    private static LevelDefinition MinimalLevelDefinition() => new()
    {
        TileSize = TileSize,
        Width = 1,
        Height = 1,
        TileSet = ResourceReference.ToSelf(VillageTileSetPath),
        Layers = new[] { new LayerDefinition { Name = "terrain", Cells = new[] { LayerDefinition.EmptyCell } } },
    };

    private static TileSetDefinition MinimalTileSetDefinition() => new()
    {
        Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath), Collides = true } },
    };

    private static Package ToPackage(PackageBuilder builder)
    {
        using var buffer = new MemoryStream();
        builder.Write(buffer);
        return PackageReader.Open(new MemoryStream(buffer.ToArray()));
    }

    private static PackageHandle FakeHandle()
    {
        // FolderPackageSource is the only public minter of PackageHandle; a throwaway temp file is the
        // simplest way to obtain one for a test that only needs a handle to carry through, never resolve.
        var directory = Path.Combine(Path.GetTempPath(), $"uberkarl-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var source = new FolderPackageSource(directory);
        var builder = new PackageBuilder().WithName("Placeholder");
        builder.AddResource(ResourceKind.Script, ResourcePath.Create("scripts/x.lua"), Encoding.UTF8.GetBytes("x"));
        using var buffer = new MemoryStream();
        builder.Write(buffer);
        return ((IWritablePackageSource)source).Create("placeholder", buffer.ToArray());
    }
}
