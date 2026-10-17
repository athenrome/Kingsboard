using KingsboardServer.Models.Board;
using KingsboardServer.Repositories.Interfaces;

namespace KingsboardServer.Repositories
{
    /// <summary>
    /// Keeps the game board in memory. The board is lost when the server restarts.
    /// </summary>
    public class BoardRepository : IBoardRepository
    {
        private GameBoard? _board;

        /// <summary>
        /// Returns the stored board, or null if no board has been saved yet
        /// </summary>
        public GameBoard? GetBoard()
        {
            return Volatile.Read(ref _board);
        }

        /// <summary>
        /// Stores a board in place of the current one
        /// </summary>
        public void SaveBoard(GameBoard board)
        {
            Volatile.Write(ref _board, board);
        }
    }
}
