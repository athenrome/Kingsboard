using Kingsboard.Shared.Models;

namespace KingsboardServer.Data.Records
{
    /// <summary>
    /// The stored definition of the game board: its size and version. Its <see cref="PublicBaseModel.PublicId"/> is the
    /// board's own id, which changes whenever the board is recreated. Its tiles are stored separately as
    /// <see cref="BoardTileRecord"/>s. Storage only: <c>BoardService</c> combines the two into a <c>GameBoard</c>.
    /// </summary>
    public class BoardRecord : PublicBaseModel
    {
        /// <summary>
        /// The board's version, which goes up by one with every change
        /// </summary>
        public long Version { get; set; }

        /// <summary>
        /// Number of columns (x)
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// Number of rows (y)
        /// </summary>
        public int Depth { get; set; }

        /// <summary>
        /// Most tiles a stack can hold (z)
        /// </summary>
        public int Height { get; set; }
    }
}
