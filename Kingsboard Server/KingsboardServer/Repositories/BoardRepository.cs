using KingsboardServer.Extensions;
using KingsboardServer.Models.Board;
using KingsboardServer.Repositories.Interfaces;

namespace KingsboardServer.Repositories
{
    /// <summary>
    /// Keeps the game board in memory. The board is lost when the server restarts.
    /// </summary>
    public class BoardRepository : IBoardRepository
    {
        private readonly object _lock = new();
        private GameBoard? _board;

        /// <summary>
        /// Returns a copy of the stored board, or null if no board has been created yet
        /// </summary>
        public GameBoard? GetBoard()
        {
            lock (_lock)
            {
                return _board?.Clone();
            }
        }

        /// <summary>
        /// Stores a copy of a new board in place of the current one, and returns another copy of it
        /// </summary>
        public GameBoard ReplaceBoard(GameBoard board)
        {
            lock (_lock)
            {
                _board = board.Clone();
                return _board.Clone();
            }
        }

        /// <summary>
        /// Replaces tiles on the stored board and returns the change with the new board version. Throws if no board exists or a tile is off the board.
        /// </summary>
        public TilesChange UpdateTiles(IReadOnlyCollection<MapTile> tiles)
        {
            lock (_lock)
            {
                if (_board is null)
                {
                    throw new InvalidOperationException("No board has been created yet");
                }

                _board.SetTiles(tiles);
                return new TilesChange(_board.Id, _board.Version, tiles.ToArray());
            }
        }
    }
}
