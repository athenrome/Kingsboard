namespace KingsboardServer.Models.Board
{
    /// <summary>
    /// The game board: a 3D grid of <see cref="Width"/> x <see cref="Depth"/> columns, each a stack of up to
    /// <see cref="Height"/> <see cref="MapTile"/>s. x is the column, y the row and z the level (0 = bottom).
    /// Every stack is solid from z = 0 up to its top tile.
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
        /// Every position on the board, null where there is no tile.
        /// Index = (z * Depth + y) * Width + x.
        /// </summary>
        public MapTile?[] Tiles { get; set; } = Array.Empty<MapTile?>();
    }
}
