using System;
using System.Collections.Generic;
using Kingsboard.Shared.Models.Board;

namespace Kingsboard.Board
{
    /// <summary>
    /// All client-side board logic: positions, regions and the loaded <see cref="BoardSection"/> (models hold data only).
    /// Every position on the board is checked and evaluated as a <see cref="BoardPosition"/>, using positions on the whole board.
    /// The server owns the real board and its rules; this only reads and updates the client's copy of a section.
    /// </summary>
    public static class BoardService
    {
        /// <summary>
        /// The same column as <paramref name="position"/>, at level <paramref name="z"/>
        /// </summary>
        public static BoardPosition AtLevel(BoardPosition position, int z)
        {
            return position with { Z = z };
        }

        /// <summary>
        /// The same region moved by <paramref name="dx"/> columns and <paramref name="dy"/> rows
        /// </summary>
        public static BoardRegion Move(BoardRegion region, int dx, int dy)
        {
            return region with { Origin = new BoardPosition(region.Origin.X + dx, region.Origin.Y + dy, 0) };
        }

        /// <summary>
        /// Whether the position's column is inside the region (its Z is ignored)
        /// </summary>
        public static bool Contains(BoardRegion region, BoardPosition position)
        {
            return position.X >= region.Origin.X && position.X < region.Origin.X + region.Width
                && position.Y >= region.Origin.Y && position.Y < region.Origin.Y + region.Depth;
        }

        /// <summary>
        /// Whether the position is inside the section: in its region and within the board's height
        /// </summary>
        public static bool Contains(BoardSection section, BoardPosition position)
        {
            return Contains(section.Region, position) && position.Z >= 0 && position.Z < section.BoardHeight;
        }

        /// <summary>
        /// Index of the position in <see cref="BoardSection.Tiles"/> (only meaningful if the section contains it)
        /// </summary>
        public static int IndexOf(BoardSection section, BoardPosition position)
        {
            BoardRegion region = section.Region;
            return (position.Z * region.Depth + (position.Y - region.Origin.Y)) * region.Width + (position.X - region.Origin.X);
        }

        /// <summary>
        /// Whether the position is in the section and is empty space (holds no tile)
        /// </summary>
        public static bool IsEmpty(BoardSection section, BoardPosition position)
        {
            return Contains(section, position) && section.Tiles[IndexOf(section, position)] == null;
        }

        /// <summary>
        /// The tile at the position, or null if it's empty space or outside the section
        /// </summary>
        public static MapTile GetTile(BoardSection section, BoardPosition position)
        {
            return Contains(section, position) ? section.Tiles[IndexOf(section, position)] : null;
        }

        /// <summary>
        /// Number of tiles in the stack in the position's column (its Z is ignored), or 0 if the column is outside the section.
        /// Stacks are solid from the bottom up, so this is also the Z of the empty space on top of the stack.
        /// </summary>
        public static int GetStackHeight(BoardSection section, BoardPosition column)
        {
            if (!Contains(section.Region, column))
            {
                return 0;
            }

            int z = 0;
            while (z < section.BoardHeight && !IsEmpty(section, AtLevel(column, z)))
            {
                z++;
            }
            return z;
        }

        /// <summary>
        /// The top tile of the stack in the position's column (its Z is ignored), or null if the stack is empty or outside the section
        /// </summary>
        public static MapTile GetTopTile(BoardSection section, BoardPosition column)
        {
            int stackHeight = GetStackHeight(section, column);
            return stackHeight == 0 ? null : GetTile(section, AtLevel(column, stackHeight - 1));
        }

        /// <summary>
        /// The empty position on top of the stack in the position's column (its Z is ignored), where the next tile would go.
        /// Null if the stack is full or the column is outside the section.
        /// </summary>
        public static BoardPosition GetPlacementPosition(BoardSection section, BoardPosition column)
        {
            if (!Contains(section.Region, column))
            {
                return null;
            }

            int stackHeight = GetStackHeight(section, column);
            return stackHeight < section.BoardHeight ? AtLevel(column, stackHeight) : null;
        }

        /// <summary>
        /// Puts tiles into the section at their own positions, replacing anything there. Tiles outside the section are ignored.
        /// </summary>
        public static void PlaceTiles(BoardSection section, IEnumerable<MapTile> tiles)
        {
            int size = section.Region.Width * section.Region.Depth * section.BoardHeight;
            if (section.Tiles.Length != size)
            {
                section.Tiles = new MapTile[size];
            }

            foreach (MapTile tile in tiles)
            {
                if (Contains(section, tile.Position))
                {
                    section.Tiles[IndexOf(section, tile.Position)] = tile;
                }
            }
        }

        /// <summary>
        /// Turns these positions into empty space. Positions outside the section are ignored.
        /// </summary>
        public static void RemoveTiles(BoardSection section, IEnumerable<BoardPosition> positions)
        {
            foreach (BoardPosition position in positions)
            {
                if (Contains(section, position))
                {
                    section.Tiles[IndexOf(section, position)] = null;
                }
            }
        }

        /// <summary>
        /// Applies a change from the server: removals first, then placements (the same order the server uses)
        /// </summary>
        public static void ApplyChanges(BoardSection section, long version, IEnumerable<MapTile> placed, IEnumerable<BoardPosition> removed)
        {
            RemoveTiles(section, removed);
            PlaceTiles(section, placed);
            section.Version = Math.Max(section.Version, version);
        }
    }
}
