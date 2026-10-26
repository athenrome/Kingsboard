using Kingsboard.Shared.Models;
using Kingsboard.Shared.Models.Enumerations;

namespace KingsboardServer.Data.Records
{
    /// <summary>
    /// The stored form of one tile on the board. A board holds at most one tile per position.
    /// Storage only: <c>BoardService</c> combines a board's tiles with its <see cref="BoardRecord"/> into a <c>GameBoard</c>.
    /// </summary>
    public class BoardTileRecord : BaseModel
    {
        /// <summary>
        /// <see cref="BaseModel.Id"/> of the board the tile is on. Set by the repository when the tile is saved.
        /// </summary>
        public int BoardRecordId { get; set; }

        /// <summary>
        /// Column across the board (0 = left)
        /// </summary>
        public int X { get; set; }

        /// <summary>
        /// Row into the board (0 = front)
        /// </summary>
        public int Y { get; set; }

        /// <summary>
        /// Level in the stack (0 = bottom)
        /// </summary>
        public int Z { get; set; }

        /// <summary>
        /// The terrain the tile is made of
        /// </summary>
        public TileType Type { get; set; }
    }
}
