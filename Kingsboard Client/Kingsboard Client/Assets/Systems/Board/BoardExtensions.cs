using System;
using System.Collections.Generic;

namespace Kingsboard.Board
{
    /// <summary>
    /// Logic for <see cref="GameBoard"/> and <see cref="BoardPosition"/> (models hold data only).
    /// Every position on the board is checked and evaluated as a <see cref="BoardPosition"/>.
    /// </summary>
    public static class BoardExtensions
    {
        /// <summary>
        /// The same column as <paramref name="position"/>, at level <paramref name="z"/>
        /// </summary>
        public static BoardPosition AtLevel(this BoardPosition position, int z)
        {
            return new BoardPosition() { X = position.X, Y = position.Y, Z = z };
        }

        /// <summary>
        /// Whether the position is inside the board's bounds
        /// </summary>
        public static bool Contains(this GameBoard board, BoardPosition position)
        {
            return position.X >= 0 && position.X < board.Width
                && position.Y >= 0 && position.Y < board.Depth
                && position.Z >= 0 && position.Z < board.Height;
        }

        /// <summary>
        /// Index of the position in <see cref="GameBoard.Tiles"/>
        /// </summary>
        public static int IndexOf(this GameBoard board, BoardPosition position)
        {
            return (position.Z * board.Depth + position.Y) * board.Width + position.X;
        }

        /// <summary>
        /// Whether the position is on the board and is empty space (holds no tile)
        /// </summary>
        public static bool IsEmpty(this GameBoard board, BoardPosition position)
        {
            return board.Contains(position) && board.Tiles[board.IndexOf(position)] == null;
        }

        /// <summary>
        /// The tile at the position, or null if it's empty space or off the board
        /// </summary>
        public static MapTile GetTile(this GameBoard board, BoardPosition position)
        {
            return board.Contains(position) ? board.Tiles[board.IndexOf(position)] : null;
        }

        /// <summary>
        /// Number of tiles in the stack in the position's column (its Z is ignored), or 0 if the column is off the board.
        /// Stacks are solid from the bottom up, so this is also the Z of the empty space on top of the stack.
        /// </summary>
        public static int GetStackHeight(this GameBoard board, BoardPosition column)
        {
            if (!board.Contains(column.AtLevel(0)))
            {
                return 0;
            }

            int z = 0;
            while (z < board.Height && !board.IsEmpty(column.AtLevel(z)))
            {
                z++;
            }
            return z;
        }

        /// <summary>
        /// The top tile of the stack in the position's column (its Z is ignored), or null if the stack is empty or off the board
        /// </summary>
        public static MapTile GetTopTile(this GameBoard board, BoardPosition column)
        {
            int stackHeight = board.GetStackHeight(column);
            return stackHeight == 0 ? null : board.GetTile(column.AtLevel(stackHeight - 1));
        }

        /// <summary>
        /// The empty position on top of the stack in the position's column (its Z is ignored), where the next tile would go.
        /// Null if the stack is full or the column is off the board.
        /// </summary>
        public static BoardPosition? GetPlacementPosition(this GameBoard board, BoardPosition column)
        {
            if (!board.Contains(column.AtLevel(0)))
            {
                return null;
            }

            int stackHeight = board.GetStackHeight(column);
            return stackHeight < board.Height ? column.AtLevel(stackHeight) : (BoardPosition?)null;
        }

        /// <summary>
        /// Puts tiles onto the board at their own positions, replacing anything there. Tiles off the board are ignored.
        /// </summary>
        public static void PlaceTiles(this GameBoard board, IEnumerable<MapTile> tiles)
        {
            int size = board.Width * board.Depth * board.Height;
            if (board.Tiles.Length != size)
            {
                board.Tiles = new MapTile[size];
            }

            foreach (MapTile tile in tiles)
            {
                if (board.Contains(tile.Position))
                {
                    board.Tiles[board.IndexOf(tile.Position)] = tile;
                }
            }
        }

        /// <summary>
        /// Turns these positions into empty space. Positions off the board are ignored.
        /// </summary>
        public static void RemoveTiles(this GameBoard board, IEnumerable<BoardPosition> positions)
        {
            foreach (BoardPosition position in positions)
            {
                if (board.Contains(position))
                {
                    board.Tiles[board.IndexOf(position)] = null;
                }
            }
        }

        /// <summary>
        /// Applies a change from the server: removals first, then placements (the same order the server uses)
        /// </summary>
        public static void ApplyChanges(this GameBoard board, long version, IEnumerable<MapTile> placed, IEnumerable<BoardPosition> removed)
        {
            board.RemoveTiles(removed);
            board.PlaceTiles(placed);
            board.Version = Math.Max(board.Version, version);
        }
    }
}
