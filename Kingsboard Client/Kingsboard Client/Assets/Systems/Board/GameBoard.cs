using System;

namespace Kingsboard.Board
{
    /// <summary>
    /// The client's copy of the game board: a <see cref="Width"/> x <see cref="Height"/> grid of <see cref="MapTile"/>s.
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
