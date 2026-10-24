using Kingsboard.Shared.Models.Enumerations;

namespace Kingsboard.Shared.Models.Board
{
    /// <summary>
    /// One block of the game board. Immutable: change a tile by replacing it on the board.
    /// </summary>
    /// <param name="Position">Where the tile sits on the board</param>
    /// <param name="Type">The terrain this tile is made of</param>
    public record MapTile(BoardPosition Position, TileType Type);
}
