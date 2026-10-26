namespace KingsboardServer.Data.Records
{
    /// <summary>
    /// The stored form of the game board: its size and version. Its tiles are stored as <see cref="BoardTileRecord"/>s.
    /// Storage only: <c>BoardService</c> turns it into a <c>GameBoard</c> and back.
    /// </summary>
    public class BoardRecord
    {
        /// <summary>
        /// The board's id, which changes whenever the board is recreated
        /// </summary>
        public Guid Id { get; set; }

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

        /// <summary>
        /// Every tile on the board. Positions without a record are empty space.
        /// </summary>
        public List<BoardTileRecord> Tiles { get; set; } = new();
    }
}
