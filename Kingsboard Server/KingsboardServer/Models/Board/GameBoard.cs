namespace KingsboardServer.Models.Board
{
    /// <summary>
    /// The game board: a <see cref="Width"/> x <see cref="Height"/> grid of <see cref="MapTile"/>s.
    /// x is the column (0 = left), y is the row (0 = bottom).
    /// </summary>
    public class GameBoard
    {
        /// <summary>
        /// Changes whenever the board is recreated (e.g. a new game)
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Goes up by one with every change to the board
        /// </summary>
        public long Version { get; set; } = 1;

        /// <summary>
        /// Number of columns
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// Number of rows
        /// </summary>
        public int Height { get; set; }

        /// <summary>
        /// Every tile, row by row (index = y * Width + x)
        /// </summary>
        public MapTile[] Tiles { get; set; } = Array.Empty<MapTile>();
    }
}
