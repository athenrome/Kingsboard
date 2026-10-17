using KingsboardServer.Models.Board;

namespace KingsboardServer.Services.Interfaces
{
    /// <summary>
    /// Owns the game board. The server is the only thing that changes it; clients are sent snapshots and changes.
    /// </summary>
    public interface IBoardService
    {
        /// <summary>
        /// Raised when a new board is created (replacing any previous one). The <see cref="GameBoard"/> is a snapshot.
        /// Handlers must be quick and non-blocking.
        /// </summary>
        event EventHandler<GameBoard>? BoardCreated;

        /// <summary>
        /// Raised when tiles on the board change. Raised in version order.
        /// Handlers must be quick and non-blocking.
        /// </summary>
        event EventHandler<TilesChange>? TilesChanged;

        /// <summary>
        /// Generates a new board from BoardSettings, replacing the current one
        /// </summary>
        GameBoard CreateBoard();

        /// <summary>
        /// Gets a snapshot of the current board, or null if no board has been created yet
        /// </summary>
        GameBoard? GetBoard();

        /// <summary>
        /// Replaces tiles on the board (matched by coordinates) and tells every client
        /// </summary>
        /// <exception cref="InvalidOperationException">No board has been created yet</exception>
        /// <exception cref="ArgumentOutOfRangeException">A tile is outside the board (nothing is changed)</exception>
        void SetTiles(IReadOnlyCollection<MapTile> tiles);
    }
}
