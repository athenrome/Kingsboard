using System;
using System.Collections.Generic;

namespace Kingsboard.Board
{
    /// <summary>
    /// Logic for <see cref="GameBoard"/> (models hold data only)
    /// </summary>
    public static class BoardExtensions
    {
        /// <summary>
        /// Whether (x, y) is a square on the board
        /// </summary>
        public static bool Contains(this GameBoard board, int x, int y) => x >= 0 && x < board.Width && y >= 0 && y < board.Height;

        /// <summary>
        /// Index of (x, y) in <see cref="GameBoard.Tiles"/>
        /// </summary>
        public static int IndexOf(this GameBoard board, int x, int y) => y * board.Width + x;

        /// <summary>
        /// The tile at (x, y), or null if that's outside the board
        /// </summary>
        public static MapTile GetTile(this GameBoard board, int x, int y)
        {
            return board.Contains(x, y) ? board.Tiles[board.IndexOf(x, y)] : null;
        }

        /// <summary>
        /// Puts tiles into the board's grid at their own coordinates. Tiles outside the board are ignored.
        /// </summary>
        public static void PlaceTiles(this GameBoard board, IEnumerable<MapTile> tiles)
        {
            if (board.Tiles.Length != board.Width * board.Height)
            {
                board.Tiles = new MapTile[board.Width * board.Height];
            }

            foreach (MapTile tile in tiles)
            {
                if (board.Contains(tile.X, tile.Y))
                {
                    board.Tiles[board.IndexOf(tile.X, tile.Y)] = tile;
                }
            }
        }

        /// <summary>
        /// Applies tile changes from the server. Tiles outside the board are ignored.
        /// </summary>
        public static void ApplyChanges(this GameBoard board, long version, IEnumerable<MapTile> tiles)
        {
            board.PlaceTiles(tiles);
            board.Version = Math.Max(board.Version, version);
        }
    }
}
