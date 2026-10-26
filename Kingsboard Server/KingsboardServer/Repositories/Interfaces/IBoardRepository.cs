using Kingsboard.Shared.Models.Board;
using KingsboardServer.Data.Records;

namespace KingsboardServer.Repositories.Interfaces
{
    /// <summary>
    /// Stores the game board in the database. All access to stored board data goes through here.
    /// This is plain storage: board rules, copying, locking and turning records into a board are done by the
    /// BoardService, its only user.
    /// </summary>
    public interface IBoardRepository
    {
        /// <summary>
        /// Reads the stored board with all its tiles, or null if no board has been saved yet
        /// </summary>
        BoardRecord? GetBoard();

        /// <summary>
        /// Stores a board, with its tiles, in place of the current one
        /// </summary>
        void ReplaceBoard(BoardRecord board);

        /// <summary>
        /// Saves one change to the stored board in a single transaction: deletes the tiles at <paramref name="removed"/>,
        /// stores <paramref name="placed"/> (in place of any tile already at the same position) and sets the board's version.
        /// Throws if the board isn't stored.
        /// </summary>
        void SaveTileChanges(Guid boardId, long version, IReadOnlyCollection<BoardTileRecord> placed, IReadOnlyCollection<BoardPosition> removed);
    }
}
