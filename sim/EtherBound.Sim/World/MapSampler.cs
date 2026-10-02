using System.Globalization;
using EtherBound.Sim.World.Gen;

namespace EtherBound.Sim.World;

/// <summary>
/// Turns the ground into minimap colour (Dev-010): one cell per 2×2 tiles, the material's colour
/// shaded by the height step to the cell north-west of it. Pure: it reads only the surface it is given,
/// so the client paints bytes and never samples terrain itself.
/// </summary>
public sealed class MapSampler
{
    public const int CellsPerChunk = 16;
    public const int TilesPerCell = ChunkConst.Size / CellsPerChunk;
    public const int BytesPerCell = 3;

    private static readonly (byte R, byte G, byte B) Unknown = (16, 16, 24);

    private readonly Dictionary<int, (byte R, byte G, byte B)> _colours = new();

    public MapSampler(MaterialRegistry registry)
    {
        foreach (var material in registry.Materials)
        {
            var hex = material.Color.TrimStart('#');
            _colours[material.Id] = (byte.Parse(hex.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(hex.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(hex.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }
    }

    /// <summary>The RGB cells of one chunk, row by row; a tile the sample does not know is drawn dark.</summary>
    public byte[] Chunk(Func<int, int, TerrainCell?> sample, int cx, int cy)
    {
        // One extra row and column on the north and west gives every cell its shading neighbour.
        const int side = CellsPerChunk + 1;
        var cells = new TerrainCell?[side * side];
        int originX = cx * ChunkConst.Size, originY = cy * ChunkConst.Size;
        for (var j = 0; j < side; j++)
        for (var i = 0; i < side; i++)
            cells[j * side + i] = sample(originX + (i - 1) * TilesPerCell, originY + (j - 1) * TilesPerCell);

        var pixels = new byte[CellsPerChunk * CellsPerChunk * BytesPerCell];
        for (var j = 0; j < CellsPerChunk; j++)
        for (var i = 0; i < CellsPerChunk; i++)
        {
            var at = (j * CellsPerChunk + i) * BytesPerCell;
            if (cells[(j + 1) * side + i + 1] is not { } cell)
            {
                (pixels[at], pixels[at + 1], pixels[at + 2]) = Unknown;
                continue;
            }
            var (r, g, b) = _colours.GetValueOrDefault(cell.Material, Unknown);
            var shade = 1.0;
            if (cells[j * side + i] is { } northWest) shade = Math.Clamp(1.0 + (cell.Height - northWest.Height) * 0.035, 0.8, 1.2);
            pixels[at] = (byte)Math.Min(255, r * shade);
            pixels[at + 1] = (byte)Math.Min(255, g * shade);
            pixels[at + 2] = (byte)Math.Min(255, b * shade);
        }
        return pixels;
    }
}
