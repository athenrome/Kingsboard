using KingsboardServer.Models.Board;
using KingsboardServer.Models.Enumerations;

namespace KingsboardServer.Extensions
{
    /// <summary>
    /// Logic for <see cref="GameBoard"/> and <see cref="MapTile"/> (models hold data only)
    /// </summary>
    public static class BoardExtensions
    {
        /// <summary>
        /// Whether pawns can move onto this tile
        /// </summary>
        public static bool IsPassable(this MapTile tile) => tile.Type is not (TileType.Water or TileType.Mountain);

        /// <summary>
        /// Whether (x, y) is a square on the board
        /// </summary>
        public static bool Contains(this GameBoard board, int x, int y) => x >= 0 && x < board.Width && y >= 0 && y < board.Height;

        /// <summary>
        /// Index of (x, y) in <see cref="GameBoard.Tiles"/>
        /// </summary>
        public static int IndexOf(this GameBoard board, int x, int y) => y * board.Width + x;

        /// <summary>
        /// The tile at (x, y). Throws <see cref="ArgumentOutOfRangeException"/> if that's outside the board.
        /// </summary>
        public static MapTile GetTile(this GameBoard board, int x, int y)
        {
            if (!board.Contains(x, y))
            {
                throw new ArgumentOutOfRangeException(nameof(x), $"({x}, {y}) is outside the {board.Width} x {board.Height} board");
            }
            return board.Tiles[board.IndexOf(x, y)];
        }

        /// <summary>
        /// Checks the board has a tile for every square and each tile is stored at its own coordinates
        /// </summary>
        /// <exception cref="ArgumentException">The board is malformed</exception>
        public static void Validate(this GameBoard board)
        {
            if (board.Width <= 0 || board.Height <= 0)
            {
                throw new ArgumentException("A board must be at least 1 x 1", nameof(board));
            }
            if (board.Tiles.Length != board.Width * board.Height)
            {
                throw new ArgumentException($"A {board.Width} x {board.Height} board needs {board.Width * board.Height} tiles, not {board.Tiles.Length}", nameof(board));
            }

            for (int i = 0; i < board.Tiles.Length; i++)
            {
                MapTile? tile = board.Tiles[i];
                if (tile is null || !board.Contains(tile.X, tile.Y) || board.IndexOf(tile.X, tile.Y) != i)
                {
                    throw new ArgumentException($"No tile was given for ({i % board.Width}, {i / board.Width})", nameof(board));
                }
            }
        }

        /// <summary>
        /// Replaces tiles (matched by their coordinates) and bumps <see cref="GameBoard.Version"/> once for the whole change
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">A tile is outside the board (nothing is changed)</exception>
        public static void SetTiles(this GameBoard board, IReadOnlyCollection<MapTile> tiles)
        {
            foreach (MapTile tile in tiles)
            {
                if (!board.Contains(tile.X, tile.Y))
                {
                    throw new ArgumentOutOfRangeException(nameof(tiles), $"Tile ({tile.X}, {tile.Y}) is outside the {board.Width} x {board.Height} board");
                }
            }

            foreach (MapTile tile in tiles)
            {
                board.Tiles[board.IndexOf(tile.X, tile.Y)] = tile;
            }
            board.Version++;
        }

        /// <summary>
        /// Creates a copy so callers can't change the live board
        /// </summary>
        public static GameBoard Clone(this GameBoard board)
        {
            return new GameBoard()
            {
                Id = board.Id,
                Version = board.Version,
                Width = board.Width,
                Height = board.Height,
                // Tiles are immutable records, so copying the array is enough
                Tiles = (MapTile[])board.Tiles.Clone()
            };
        }
    }
}
