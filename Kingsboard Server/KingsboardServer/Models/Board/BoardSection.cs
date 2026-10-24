
using Kingsboard.Shared.Models.Board;

namespace KingsboardServer.Models.Board
{
    /// <summary>
    /// A snapshot of one section of the board: every tile in a region of columns, plus the size of the whole board
    /// </summary>
    /// <param name="BoardId">The board the section was taken from</param>
    /// <param name="Version">The board version the section was taken at</param>
    /// <param name="BoardWidth">Number of columns (x) on the whole board</param>
    /// <param name="BoardDepth">Number of rows (y) on the whole board</param>
    /// <param name="BoardHeight">Most tiles a stack can hold (z)</param>
    /// <param name="Region">The region the section covers, after being moved onto the board</param>
    /// <param name="Tiles">Every tile in the region; positions without a tile are empty space</param>
    public record BoardSection(Guid BoardId, long Version, int BoardWidth, int BoardDepth, int BoardHeight, BoardRegion Region, IReadOnlyList<MapTile> Tiles);
}
