using Uberkarl.Content;
using Uberkarl.Content.Json;
using Uberkarl.Packages;
using Uberkarl.Tools.SampleContent;

const int TileSize = 16;
const int Width = 20;
const int Height = 12;

var outputPath = args.Length > 0 ? args[0] : Path.Combine("content", "sample.pkg");

// Id, file, RGB, and whether the tile is solid (collision is a property of the tile).
var palette = new (int Id, string File, byte R, byte G, byte B, bool Collides)[]
{
    (1, "tiles/grass.png", 78, 168, 66, true),
    (2, "tiles/dirt.png", 138, 92, 52, true),
    (3, "tiles/stone.png", 128, 130, 138, true),
    (4, "tiles/brick.png", 190, 74, 60, true),
    (5, "tiles/water.png", 64, 122, 210, false),
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
    tiles.Add(new TileDefinition { Id = entry.Id, Graphic = ResourceReference.ToSelf(path), Collides = entry.Collides });
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
    Spawns = new Dictionary<string, GridPosition> { ["start"] = new GridPosition(2, 7) },
    DefaultSpawn = "start",
    Layers = new[]
    {
        // Drawn back to front (array order). A stone pillar behind the play field on a
        // non-collision layer: stone is flagged collides=true, but collision=false means the
        // player passes straight through it — proving collision is per-layer, not per-tile.
        new LayerDefinition { Name = "backdrop", Collision = false, Cells = BuildBackdropLayer() },
        // The collision layer: solid ground, side walls, and a brick platform.
        new LayerDefinition { Name = "terrain", Collision = true, Cells = BuildTerrainLayer() },
        // Decorative water splashes drawn in front on a non-collision layer.
        new LayerDefinition { Name = "decor", Collision = false, Cells = BuildDecorLayer() },
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

// A stone pillar behind the play field (rows 5-7, cols 9-10). Demonstrates that a
// collides=true tile on a non-collision layer does NOT collide.
static int[] BuildBackdropLayer()
{
    var cells = Filled(LayerDefinition.EmptyCell);
    for (var y = 5; y <= 7; y++)
    {
        Set(cells, 9, y, 3);
        Set(cells, 10, y, 3);
    }

    return cells;
}

// The collision layer: stone side walls, a grass surface at row 9, dirt fill below,
// and a floating brick platform (row 6, cols 13-16) to jump onto.
static int[] BuildTerrainLayer()
{
    var cells = Filled(LayerDefinition.EmptyCell);
    for (var y = 0; y < Height; y++)
    {
        for (var x = 0; x < Width; x++)
        {
            if (x == 0 || x == Width - 1)
                Set(cells, x, y, 3);       // stone side walls
            else if (y == 9)
                Set(cells, x, y, 1);       // grass surface
            else if (y > 9)
                Set(cells, x, y, 2);       // dirt fill
        }
    }

    for (var x = 13; x <= 16; x++)
        Set(cells, x, 6, 4);               // brick platform to jump onto

    return cells;
}

// Decorative water splashes drawn in front of the play field on a non-collision layer.
static int[] BuildDecorLayer()
{
    var cells = Filled(LayerDefinition.EmptyCell);
    Set(cells, 4, 8, 5);
    Set(cells, 5, 8, 5);
    return cells;
}

static int[] Filled(int value)
{
    var cells = new int[Width * Height];
    Array.Fill(cells, value);
    return cells;
}

static void Set(int[] cells, int x, int y, int value) => cells[y * Width + x] = value;
