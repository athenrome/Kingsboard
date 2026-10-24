using Kingsboard.Shared.Models.Board;
using Kingsboard.Shared.Models.Enumerations;
using KingsboardServer.Events.Board;
using KingsboardServer.Models.Board;

namespace KingsboardServer.Services.Interfaces
{
    /// <summary>
    /// Owns the game board: a 3D grid of columns where tiles stack on top of each other.
    /// The server is the only thing that changes it; clients are sent snapshots and changes.
    /// Every position is a <see cref="BoardPosition"/>, which may hold a tile or be empty space.
    /// Every stack is kept solid from the bottom up: a change that would leave a tile floating is rejected.
    /// </summary>
    public interface IBoardService
    {
        /// <summary>
        /// Raised when a new board is created (replacing any previous one). The <see cref="GameBoard"/> is a snapshot.
        /// Handlers must be quick and non-blocking.
        /// </summary>
        event EventHandler<GameBoard>? BoardCreated;

        /// <summary>
        /// Raised when tiles are placed, replaced or removed. Raised in version order.
        /// Handlers must be quick and non-blocking.
        /// </summary>
        event EventHandler<TilesChangeEvent>? TilesChanged;

        /// <summary>
        /// Generates a new board from BoardSettings, replacing the current one
        /// </summary>
        GameBoard CreateBoard();

        /// <summary>
        /// Gets a snapshot of the current board, or null if no board has been created yet
        /// </summary>
        GameBoard? GetBoard();

        /// <summary>
        /// Gets a snapshot of one section of the board, or null if no board has been created yet.
        /// A region hanging over the edge of the board is moved back onto it, keeping its size where the board is big enough;
        /// the section's <see cref="BoardSection.Region"/> is the region actually used.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The region is smaller than 1 x 1</exception>
        BoardSection? GetSection(BoardRegion region);

        /// <summary>
        /// Runs <paramref name="read"/> with a snapshot of a section (as <see cref="GetSection"/>) while no board change can happen.
        /// <see cref="BoardCreated"/> and <see cref="TilesChanged"/> are raised under the same guarantee, so a caller that
        /// starts listening for changes inside <paramref name="read"/> gets every change after the snapshot, and none before it.
        /// <paramref name="read"/> must be quick and non-blocking.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The region is smaller than 1 x 1</exception>
        void ReadSection(BoardRegion region, Action<BoardSection?> read);

        /// <summary>
        /// Whether the position is inside the board's bounds (false if no board has been created yet)
        /// </summary>
        bool IsOnBoard(BoardPosition position);

        /// <summary>
        /// Whether the position is on the board and is empty space (holds no tile)
        /// </summary>
        bool IsEmpty(BoardPosition position);

        /// <summary>
        /// Gets the tile at the position, or null if it's empty space, off the board, or there is no board yet
        /// </summary>
        MapTile? GetTile(BoardPosition position);

        /// <summary>
        /// Gets the top tile of the stack in the position's column (its Z is ignored), or null if the stack is empty,
        /// the column is off the board, or there is no board yet
        /// </summary>
        MapTile? GetTopTile(BoardPosition column);

        /// <summary>
        /// Gets the stack in the position's column (its Z is ignored), bottom tile first.
        /// Empty if the stack is empty, the column is off the board, or there is no board yet.
        /// </summary>
        IReadOnlyList<MapTile> GetStack(BoardPosition column);

        /// <summary>
        /// Gets the empty position on top of the stack in the position's column (its Z is ignored), where the next tile
        /// can be placed. Null if the stack is full, the column is off the board, or there is no board yet.
        /// </summary>
        BoardPosition? GetPlacementPosition(BoardPosition column);

        /// <summary>
        /// Whether a tile can be placed at the position: it is on the board, empty, and resting on a tile (or on the ground at Z = 0)
        /// </summary>
        bool CanPlaceTile(BoardPosition position);

        /// <summary>
        /// Whether the tile at the position can be removed: there is a tile there and nothing on top of it
        /// </summary>
        bool CanRemoveTile(BoardPosition position);

        /// <summary>
        /// Places a new tile at the position and returns it
        /// </summary>
        /// <exception cref="InvalidOperationException">No board has been created yet, the position isn't empty, or it has nothing beneath it</exception>
        /// <exception cref="ArgumentOutOfRangeException">The position is off the board</exception>
        MapTile PlaceTile(BoardPosition position, TileType type);

        /// <summary>
        /// Removes the tile at the position, leaving empty space, and returns the removed tile
        /// </summary>
        /// <exception cref="InvalidOperationException">No board has been created yet, or there is a tile on top of it</exception>
        /// <exception cref="ArgumentOutOfRangeException">The position is off the board</exception>
        /// <exception cref="ArgumentException">The position is already empty space</exception>
        MapTile RemoveTile(BoardPosition position);

        /// <summary>
        /// Places or replaces tiles at their own positions, as one change
        /// </summary>
        /// <exception cref="InvalidOperationException">No board has been created yet, or a tile would be left floating (nothing is changed)</exception>
        /// <exception cref="ArgumentOutOfRangeException">A tile is off the board (nothing is changed)</exception>
        void SetTiles(IReadOnlyCollection<MapTile> tiles);

        /// <summary>
        /// Removes the tiles at these positions, leaving empty space, as one change
        /// </summary>
        /// <exception cref="InvalidOperationException">No board has been created yet, or a tile would be left floating (nothing is changed)</exception>
        /// <exception cref="ArgumentOutOfRangeException">A position is off the board (nothing is changed)</exception>
        /// <exception cref="ArgumentException">A position is already empty space (nothing is changed)</exception>
        void RemoveTiles(IReadOnlyCollection<BoardPosition> positions);
    }
}
