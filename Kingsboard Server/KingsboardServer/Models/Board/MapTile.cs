using KingsboardServer.Models.Enumerations;

namespace KingsboardServer.Models.Board
{
    /// <summary>
    /// One square of the game board. Immutable: change a tile by replacing it on the board.
    /// </summary>
    public record MapTile(int X, int Y, TileType Type);
}
