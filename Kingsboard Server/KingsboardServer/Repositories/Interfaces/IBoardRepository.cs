using KingsboardServer.Models.Board;

namespace KingsboardServer.Repositories.Interfaces
{
    /// <summary>
    /// Stores the game board. All access to stored board data goes through here.
    /// </summary>
    public interface IBoardRepository
    {
        /// <summary>
        /// Returns a copy of the current board, or null if no board has been created yet
        /// </summary>
        GameBoard? GetBoard();

        /// <summary>
        /// Stores a new board, replacing the current one. Returns a copy of what was stored.
        /// </summary>
        GameBoard ReplaceBoard(GameBoard board);

        /// <summary>
        /// Replaces tiles on the current board (matched by coordinates) and returns the change, with the new board version
        /// </summary>
        /// <exception cref="InvalidOperationException">No board has been created yet</exception>
        /// <exception cref="ArgumentOutOfRangeException">A tile is outside the board (nothing is changed)</exception>
        TilesChange UpdateTiles(IReadOnlyCollection<MapTile> tiles);
    }
}
