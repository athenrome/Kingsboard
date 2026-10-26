using Kingsboard.Shared.Models.Board;
using KingsboardServer.Data.Records;

namespace KingsboardServer.Repositories.Interfaces
{
    /// <summary>
    /// Stores the game board in the database: its definition and, separately, its tiles. All access to stored board data
    /// goes through here. This is plain storage: board rules, copying, locking and combining the board with its tiles are
    /// done by the BoardService, its only user.
    /// </summary>
    public interface IBoardRepository
    {
        /// <summary>
        /// Reads the stored board's definition, without its tiles, or null if no board has been saved yet
        /// </summary>
        BoardRecord? GetBoard();

        /// <summary>
        /// Reads every tile on the board whose <see cref="Kingsboard.Shared.Models.BaseModel.Id"/> is <paramref name="boardRecordId"/>
        /// </summary>
        IReadOnlyList<BoardTileRecord> GetTiles(int boardRecordId);

        /// <summary>
        /// Stores a board and its tiles in place of the current ones, in a single transaction. Sets the board's and tiles'
        /// ids, and links the tiles to the board.
        /// </summary>
        void ReplaceBoard(BoardRecord board, IReadOnlyCollection<BoardTileRecord> tiles);

        /// <summary>
        /// Saves one change to the stored board in a single transaction: deletes the tiles at <paramref name="removed"/>,
        /// stores <paramref name="placed"/> (in place of any tile already at the same position) and sets the board's version.
        /// <paramref name="boardId"/> is the board's <see cref="Kingsboard.Shared.Models.PublicBaseModel.PublicId"/>; the
        /// placed tiles are linked to it here. Throws if the board isn't stored.
        /// </summary>
        void SaveTileChanges(Guid boardId, long version, IReadOnlyCollection<BoardTileRecord> placed, IReadOnlyCollection<BoardPosition> removed);
    }
}
