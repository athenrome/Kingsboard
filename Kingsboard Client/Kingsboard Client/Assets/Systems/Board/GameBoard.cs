using System;

namespace Kingsboard.Board
{
    /// <summary>
    /// The client's copy of the game board: a 3D grid of <see cref="Width"/> x <see cref="Depth"/> columns, each a stack of
    /// up to <see cref="Height"/> <see cref="MapTile"/>s. x is the column, y the row and z the level (0 = bottom).
    /// The server owns the real board; this is only ever updated from what the server sends.
    /// </summary>
    public class GameBoard
    {
        /// <summary>
        /// Changes whenever the server recreates the board
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// The server's board version this copy is up to date with
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
        /// Every position on the board, null where there is no tile.
        /// Index = (z * Depth + y) * Width + x.
        /// </summary>
        public MapTile[] Tiles { get; set; } = Array.Empty<MapTile>();
    }
}
