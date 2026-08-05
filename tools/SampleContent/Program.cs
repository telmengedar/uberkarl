using Uberkarl.Content;
using Uberkarl.Content.Json;
using Uberkarl.Packages;
using Uberkarl.Tools.SampleContent;

const int TileSize = 16;
const int Width = 60;   // ~60 tiles wide: clearly larger than the scrolling viewport, so the camera must follow.
const int Height = 16;

var outputPath = args.Length > 0 ? args[0] : Path.Combine("content", "sample.pkg");

// Id, file, RGB, and whether the tile is solid (collision is a property of the tile — a full-tile shape
// when solid, none otherwise; DiVoid #7551 Phase 4's richer shapes are an authoring-time concern, not
// needed for this generated sample).
var palette = new (int Id, string File, byte R, byte G, byte B, bool Solid)[]
{
    (1, "tiles/grass.png", 78, 168, 66, true),
    (2, "tiles/dirt.png", 138, 92, 52, true),
    (3, "tiles/stone.png", 128, 130, 138, true),
    (4, "tiles/brick.png", 190, 74, 60, true),
    (5, "tiles/water.png", 64, 122, 210, false),
    (6, "tiles/hill.png", 86, 110, 120, false),   // distant background hills (non-colliding, parallax layer)
    (7, "tiles/cloud.png", 210, 215, 225, false), // distant background clouds (non-colliding, parallax layer)
};

var builder = new PackageBuilder()
    .WithName("Uberkarl Demo Level")
    .WithVersion("0.1.0")
    .WithAttribution(new Attribution { Author = "Uberkarl", License = "CC0-1.0" });

var tiles = new List<TileDefinition>();
foreach (var entry in palette)
{
    var path = ResourcePath.Create(entry.File);
    var png = PngWriter.Encode(TileSize, TileSize, SolidTile(TileSize, entry.R, entry.G, entry.B));
    builder.AddResource(ResourceKind.TileGraphic, path, png, "image/png");
    var collisionShape = entry.Solid ? CollisionShapeDefinition.Full : CollisionShapeDefinition.None;
    tiles.Add(new TileDefinition { Id = entry.Id, Graphic = ResourceReference.ToSelf(path), CollisionShape = collisionShape });
}

var tileSet = new TileSetDefinition { Tiles = tiles };
var tileSetPath = ResourcePath.Create("tileset.json");
builder.AddResource(ResourceKind.TileSet, tileSetPath, LevelContentSerializer.WriteTileSet(tileSet));

var level = new LevelDefinition
{
    TileSize = TileSize,
    Width = Width,
    Height = Height,
    TileSet = ResourceReference.ToSelf(tileSetPath),
    // A dusk-blue sky fill behind every layer. It always covers the viewport regardless of camera
    // position, so scrolling to an edge never hard-cuts to the viewport clear colour behind the
    // finite parallax hills.
    BackgroundColor = "#3A5A8C",
    Spawns = new Dictionary<string, GridPosition> { ["start"] = new GridPosition(2, 10) },
    DefaultSpawn = "start",
    Layers = new[]
    {
        // Drawn back to front (array order). A non-collision PARALLAX layer at scrollSpeed 0.5:
        // distant hills and clouds that scroll at half the camera's speed, so they visibly lag
        // behind the world-locked terrain as the player moves — proving the depth differential.
        // repeat:true tiles the backdrop across the full scroll extent so it never runs out at an edge.
        new LayerDefinition { Name = "backdrop", Collision = false, ScrollSpeed = 0.5f, Repeat = true, Cells = BuildBackdropLayer() },
        // The world-locked collision layer (scrollSpeed 1.0): ground, side walls, and platforms.
        new LayerDefinition { Name = "terrain", Collision = true, ScrollSpeed = 1.0f, Cells = BuildTerrainLayer() },
    },
};
builder.AddResource(ResourceKind.Level, ResourcePath.Create("levels/demo.json"), LevelContentSerializer.WriteLevel(level));

var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
if (!string.IsNullOrEmpty(directory))
    Directory.CreateDirectory(directory);
builder.Write(outputPath);

Console.WriteLine($"Wrote {outputPath} ({new FileInfo(outputPath).Length} bytes, {palette.Length} tiles, {Width}x{Height} grid).");

static byte[] SolidTile(int size, byte r, byte g, byte b)
{
    var rgba = new byte[size * size * 4];
    for (var y = 0; y < size; y++)
    {
        for (var x = 0; x < size; x++)
        {
            var edge = x == 0 || y == 0 || x == size - 1 || y == size - 1;
            var offset = (y * size + x) * 4;
            rgba[offset] = edge ? Darken(r) : r;
            rgba[offset + 1] = edge ? Darken(g) : g;
            rgba[offset + 2] = edge ? Darken(b) : b;
            rgba[offset + 3] = 255;
        }
    }

    return rgba;
}

static byte Darken(byte channel) => (byte)(channel * 6 / 10);

// The parallax background (scrollSpeed 0.5): a run of distant hills near the ground line plus a
// band of clouds in the sky. Placed at distinct x-positions across the full width so the reduced
// scroll speed is obvious as the camera pans. All tiles are non-colliding.
static int[] BuildBackdropLayer()
{
    var cells = Filled(LayerDefinition.EmptyCell);

    // Pyramid hills sitting just behind the ground line, peaks reaching into the sky.
    foreach (var centre in new[] { 8, 22, 36, 50 })
    {
        for (var k = 0; k <= 4; k++)                 // k = 0 is the widest base row (row 11)
        {
            var row = 11 - k;
            for (var x = centre - (4 - k); x <= centre + (4 - k); x++)
                if (x > 0 && x < Width - 1)
                    Set(cells, x, row, 6);
        }
    }

    // A band of clouds high in the sky, offset from the hills.
    foreach (var cx in new[] { 5, 18, 30, 44, 56 })
    {
        Set(cells, cx, 3, 7);
        if (cx + 1 < Width - 1)
            Set(cells, cx + 1, 3, 7);
    }

    return cells;
}

// The world-locked collision layer: stone side walls, a grass surface at row 12, dirt fill below,
// and a few floating brick platforms to jump onto.
static int[] BuildTerrainLayer()
{
    var cells = Filled(LayerDefinition.EmptyCell);
    for (var y = 0; y < Height; y++)
    {
        for (var x = 0; x < Width; x++)
        {
            if (x == 0 || x == Width - 1)
                Set(cells, x, y, 3);       // stone side walls (full height, stop the camera-follow player at the edges)
            else if (y == 12)
                Set(cells, x, y, 1);       // grass surface
            else if (y > 12)
                Set(cells, x, y, 2);       // dirt fill
        }
    }

    // Floating brick platforms across the level (foreground reference points for the parallax).
    // Kept at row 9 or higher so they clear the walking player — the ground path stays traversable
    // end to end while the platforms remain jumpable-onto.
    PlatformRun(cells, 12, 15, 9);
    PlatformRun(cells, 26, 29, 8);
    PlatformRun(cells, 42, 45, 9);

    return cells;
}

static void PlatformRun(int[] cells, int fromX, int toX, int row)
{
    for (var x = fromX; x <= toX; x++)
        Set(cells, x, row, 4);
}

static int[] Filled(int value)
{
    var cells = new int[Width * Height];
    Array.Fill(cells, value);
    return cells;
}

static void Set(int[] cells, int x, int y, int value) => cells[y * Width + x] = value;
