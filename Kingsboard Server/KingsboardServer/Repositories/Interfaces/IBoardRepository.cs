using KingsboardServer.Models.Board;

namespace KingsboardServer.Repositories.Interfaces
{
    /// <summary>
    /// Stores the game board. All access to stored board data goes through here.
    /// This is plain storage: board rules, copying and locking are done by the BoardService, its only user.
    /// </summary>
    public interface IBoardRepository
    {
        /// <summary>
        /// Returns the stored board, or null if no board has been saved yet
        /// </summary>
        GameBoard? GetBoard();

        /// <summary>
        /// Stores a board in place of the current one
        /// </summary>
        void SaveBoard(GameBoard board);
    }
}
