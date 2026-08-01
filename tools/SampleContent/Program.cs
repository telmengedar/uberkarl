using Uberkarl.Content;
using Uberkarl.Content.Json;
using Uberkarl.Packages;
using Uberkarl.Tools.SampleContent;

const int TileSize = 16;
const int Width = 20;
const int Height = 12;

var outputPath = args.Length > 0 ? args[0] : Path.Combine("content", "sample.pkg");

var palette = new (int Id, string File, byte R, byte G, byte B)[]
{
    (1, "tiles/grass.png", 78, 168, 66),
    (2, "tiles/dirt.png", 138, 92, 52),
    (3, "tiles/stone.png", 128, 130, 138),
    (4, "tiles/brick.png", 190, 74, 60),
    (5, "tiles/water.png", 64, 122, 210),
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
    tiles.Add(new TileDefinition { Id = entry.Id, Graphic = ResourceReference.ToSelf(path) });
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
    Layers = new[]
    {
        new LayerDefinition { Name = "ground", Cells = BuildGroundLayer() },
        new LayerDefinition { Name = "decoration", Cells = BuildDecorationLayer() },
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

static int[] BuildGroundLayer()
{
    var cells = Filled(LayerDefinition.EmptyCell);
    for (var y = 0; y < Height; y++)
    {
        for (var x = 0; x < Width; x++)
        {
            if (x == 0 || x == Width - 1)
                Set(cells, x, y, 3);
            else if (y == 9)
                Set(cells, x, y, 1);
            else if (y > 9)
                Set(cells, x, y, 2);
        }
    }

    return cells;
}

static int[] BuildDecorationLayer()
{
    var cells = Filled(LayerDefinition.EmptyCell);
    for (var x = 7; x <= 11; x++)
        Set(cells, x, 6, 4);
    Set(cells, 15, 8, 4);
    Set(cells, 16, 8, 4);
    Set(cells, 16, 7, 4);
    Set(cells, 4, 10, 5);
    Set(cells, 5, 10, 5);
    return cells;
}

static int[] Filled(int value)
{
    var cells = new int[Width * Height];
    Array.Fill(cells, value);
    return cells;
}

static void Set(int[] cells, int x, int y, int value) => cells[y * Width + x] = value;
