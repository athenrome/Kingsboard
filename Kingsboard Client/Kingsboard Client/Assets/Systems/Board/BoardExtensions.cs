using System;
using System.Collections.Generic;

namespace Kingsboard.Board
{
    /// <summary>
    /// Logic for <see cref="BoardSection"/>, <see cref="BoardRegion"/> and <see cref="BoardPosition"/> (models hold data only).
    /// Every position on the board is checked and evaluated as a <see cref="BoardPosition"/>, using positions on the whole board.
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
        /// The same region moved by <paramref name="dx"/> columns and <paramref name="dy"/> rows
        /// </summary>
        public static BoardRegion Moved(this BoardRegion region, int dx, int dy)
        {
            return new BoardRegion()
            {
                Origin = new BoardPosition() { X = region.Origin.X + dx, Y = region.Origin.Y + dy, Z = 0 },
                Width = region.Width,
                Depth = region.Depth
            };
        }

        /// <summary>
        /// Whether the position's column is inside the region (its Z is ignored)
        /// </summary>
        public static bool Contains(this BoardRegion region, BoardPosition position)
        {
            return position.X >= region.Origin.X && position.X < region.Origin.X + region.Width
                && position.Y >= region.Origin.Y && position.Y < region.Origin.Y + region.Depth;
        }

        /// <summary>
        /// Whether the position is inside this section: in its region and within the board's height
        /// </summary>
        public static bool Contains(this BoardSection section, BoardPosition position)
        {
            return section.Region.Contains(position) && position.Z >= 0 && position.Z < section.BoardHeight;
        }

        /// <summary>
        /// Index of the position in <see cref="BoardSection.Tiles"/> (only meaningful if the section contains it)
        /// </summary>
        public static int IndexOf(this BoardSection section, BoardPosition position)
        {
            BoardRegion region = section.Region;
            return (position.Z * region.Depth + (position.Y - region.Origin.Y)) * region.Width + (position.X - region.Origin.X);
        }

        /// <summary>
        /// Whether the position is in this section and is empty space (holds no tile)
        /// </summary>
        public static bool IsEmpty(this BoardSection section, BoardPosition position)
        {
            return section.Contains(position) && section.Tiles[section.IndexOf(position)] == null;
        }

        /// <summary>
        /// The tile at the position, or null if it's empty space or outside this section
        /// </summary>
        public static MapTile GetTile(this BoardSection section, BoardPosition position)
        {
            return section.Contains(position) ? section.Tiles[section.IndexOf(position)] : null;
        }

        /// <summary>
        /// Number of tiles in the stack in the position's column (its Z is ignored), or 0 if the column is outside this section.
        /// Stacks are solid from the bottom up, so this is also the Z of the empty space on top of the stack.
        /// </summary>
        public static int GetStackHeight(this BoardSection section, BoardPosition column)
        {
            if (!section.Region.Contains(column))
            {
                return 0;
            }

            int z = 0;
            while (z < section.BoardHeight && !section.IsEmpty(column.AtLevel(z)))
            {
                z++;
            }
            return z;
        }

        /// <summary>
        /// The top tile of the stack in the position's column (its Z is ignored), or null if the stack is empty or outside this section
        /// </summary>
        public static MapTile GetTopTile(this BoardSection section, BoardPosition column)
        {
            int stackHeight = section.GetStackHeight(column);
            return stackHeight == 0 ? null : section.GetTile(column.AtLevel(stackHeight - 1));
        }

        /// <summary>
        /// The empty position on top of the stack in the position's column (its Z is ignored), where the next tile would go.
        /// Null if the stack is full or the column is outside this section.
        /// </summary>
        public static BoardPosition? GetPlacementPosition(this BoardSection section, BoardPosition column)
        {
            if (!section.Region.Contains(column))
            {
                return null;
            }

            int stackHeight = section.GetStackHeight(column);
            return stackHeight < section.BoardHeight ? column.AtLevel(stackHeight) : (BoardPosition?)null;
        }

        /// <summary>
        /// Puts tiles into the section at their own positions, replacing anything there. Tiles outside the section are ignored.
        /// </summary>
        public static void PlaceTiles(this BoardSection section, IEnumerable<MapTile> tiles)
        {
            int size = section.Region.Width * section.Region.Depth * section.BoardHeight;
            if (section.Tiles.Length != size)
            {
                section.Tiles = new MapTile[size];
            }

            foreach (MapTile tile in tiles)
            {
                if (section.Contains(tile.Position))
                {
                    section.Tiles[section.IndexOf(tile.Position)] = tile;
                }
            }
        }

        /// <summary>
        /// Turns these positions into empty space. Positions outside the section are ignored.
        /// </summary>
        public static void RemoveTiles(this BoardSection section, IEnumerable<BoardPosition> positions)
        {
            foreach (BoardPosition position in positions)
            {
                if (section.Contains(position))
                {
                    section.Tiles[section.IndexOf(position)] = null;
                }
            }
        }

        /// <summary>
        /// Applies a change from the server: removals first, then placements (the same order the server uses)
        /// </summary>
        public static void ApplyChanges(this BoardSection section, long version, IEnumerable<MapTile> placed, IEnumerable<BoardPosition> removed)
        {
            section.RemoveTiles(removed);
            section.PlaceTiles(placed);
            section.Version = Math.Max(section.Version, version);
        }
    }
}
