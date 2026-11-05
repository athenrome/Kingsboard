using System.Collections.Generic;
using UnityEngine;

namespace Kingsboard.Room
{
    /// <summary>
    /// Grid maths for the <see cref="RoomGrid"/> model (models hold data only): turning cells into room positions and
    /// back, and working out which cells a footprint covers. All positions are in the room's local space.
    /// </summary>
    public static class RoomGridService
    {
        /// <summary>
        /// The four directions a step on the grid can go: +z (north), +x (east), -z (south), -x (west)
        /// </summary>
        public static readonly Vector2Int[] Directions =
        {
            new Vector2Int(0, 1),
            new Vector2Int(1, 0),
            new Vector2Int(0, -1),
            new Vector2Int(-1, 0),
        };

        /// <summary>
        /// Builds a grid of <paramref name="width"/> × <paramref name="depth"/> cells of <paramref name="cellSize"/>,
        /// centred on <paramref name="centre"/> (a point on the floor). Sizes below 1 cell and cell sizes below 0.01 are
        /// raised to those limits.
        /// </summary>
        public static RoomGrid Create(float cellSize, int width, int depth, Vector3 centre)
        {
            cellSize = Mathf.Max(0.01f, cellSize);
            width = Mathf.Max(1, width);
            depth = Mathf.Max(1, depth);
            return new RoomGrid
            {
                CellSize = cellSize,
                Width = width,
                Depth = depth,
                Origin = centre - new Vector3(width * cellSize / 2f, 0f, depth * cellSize / 2f),
            };
        }

        /// <summary>
        /// True if <paramref name="cell"/> is on the grid
        /// </summary>
        public static bool Contains(RoomGrid grid, Vector2Int cell)
        {
            return cell.x >= 0 && cell.y >= 0 && cell.x < grid.Width && cell.y < grid.Depth;
        }

        /// <summary>
        /// The middle of <paramref name="cell"/> on the floor. Works for cells off the grid too.
        /// </summary>
        public static Vector3 CellToLocal(RoomGrid grid, Vector2Int cell)
        {
            return grid.Origin + new Vector3((cell.x + 0.5f) * grid.CellSize, 0f, (cell.y + 0.5f) * grid.CellSize);
        }

        /// <summary>
        /// The cell a point lies over, ignoring its height. The cell may be off the grid; check with <see cref="Contains"/>.
        /// </summary>
        public static Vector2Int LocalToCell(RoomGrid grid, Vector3 point)
        {
            Vector3 offset = point - grid.Origin;
            return new Vector2Int(Mathf.FloorToInt(offset.x / grid.CellSize), Mathf.FloorToInt(offset.z / grid.CellSize));
        }

        /// <summary>
        /// The nearest cell on the grid to a point, so a point beyond an edge gives the edge cell
        /// </summary>
        public static Vector2Int ClampToGrid(RoomGrid grid, Vector2Int cell)
        {
            return new Vector2Int(Mathf.Clamp(cell.x, 0, grid.Width - 1), Mathf.Clamp(cell.y, 0, grid.Depth - 1));
        }

        /// <summary>
        /// How many whole cells best cover an object of <paramref name="size"/> room units on this grid: each side rounded
        /// to the nearest number of cells, and never less than 1
        /// </summary>
        public static Vector2Int SizeToCells(RoomGrid grid, Vector2 size)
        {
            return new Vector2Int(
                Mathf.Max(1, Mathf.RoundToInt(size.x / grid.CellSize)),
                Mathf.Max(1, Mathf.RoundToInt(size.y / grid.CellSize)));
        }

        /// <summary>
        /// The size in cells of a footprint of <paramref name="size"/> once turned by <paramref name="quarterTurns"/>
        /// quarter turns: an odd number of turns swaps its width and depth
        /// </summary>
        public static Vector2Int RotateSize(Vector2Int size, int quarterTurns)
        {
            return NormaliseTurns(quarterTurns) % 2 == 0 ? size : new Vector2Int(size.y, size.x);
        }

        /// <summary>
        /// A number of quarter turns brought into the range 0 to 3
        /// </summary>
        public static int NormaliseTurns(int quarterTurns)
        {
            return ((quarterTurns % 4) + 4) % 4;
        }

        /// <summary>
        /// The quarter turns nearest to a yaw angle in degrees (0 faces +z, 1 faces +x, and so on)
        /// </summary>
        public static int YawToTurns(float yaw)
        {
            return NormaliseTurns(Mathf.RoundToInt(yaw / 90f));
        }

        /// <summary>
        /// The middle of a footprint of <paramref name="size"/> cells (already rotated) whose lowest cell is
        /// <paramref name="anchor"/>, on the floor
        /// </summary>
        public static Vector3 FootprintCentre(RoomGrid grid, Vector2Int anchor, Vector2Int size)
        {
            return grid.Origin + new Vector3((anchor.x + size.x / 2f) * grid.CellSize, 0f, (anchor.y + size.y / 2f) * grid.CellSize);
        }

        /// <summary>
        /// The lowest cell of the footprint of <paramref name="size"/> cells (already rotated) whose middle is nearest to
        /// <paramref name="centre"/>, i.e. where a footprint snaps to when dropped at that point
        /// </summary>
        public static Vector2Int SnapFootprint(RoomGrid grid, Vector3 centre, Vector2Int size)
        {
            Vector3 offset = centre - grid.Origin;
            return new Vector2Int(
                Mathf.RoundToInt(offset.x / grid.CellSize - size.x / 2f),
                Mathf.RoundToInt(offset.z / grid.CellSize - size.y / 2f));
        }

        /// <summary>
        /// Every cell covered by a footprint of <paramref name="size"/> cells (already rotated) whose lowest cell is
        /// <paramref name="anchor"/>, including any that fall off the grid
        /// </summary>
        public static IEnumerable<Vector2Int> FootprintCells(Vector2Int anchor, Vector2Int size)
        {
            for (int y = 0; y < size.y; y++)
            {
                for (int x = 0; x < size.x; x++)
                {
                    yield return new Vector2Int(anchor.x + x, anchor.y + y);
                }
            }
        }

        /// <summary>
        /// True if a footprint of <paramref name="size"/> cells (already rotated) at <paramref name="anchor"/> lies wholly
        /// on the grid
        /// </summary>
        public static bool ContainsFootprint(RoomGrid grid, Vector2Int anchor, Vector2Int size)
        {
            return Contains(grid, anchor) && Contains(grid, anchor + size - Vector2Int.one);
        }

        /// <summary>
        /// How many steps apart a cell is from a footprint, counting diagonal steps as one: 0 inside it, 1 for the ring
        /// of cells around it (corners included), and so on
        /// </summary>
        public static int DistanceToFootprint(Vector2Int cell, Vector2Int anchor, Vector2Int size)
        {
            Vector2Int max = anchor + size - Vector2Int.one;
            int dx = cell.x < anchor.x ? anchor.x - cell.x : cell.x > max.x ? cell.x - max.x : 0;
            int dy = cell.y < anchor.y ? anchor.y - cell.y : cell.y > max.y ? cell.y - max.y : 0;
            return Mathf.Max(dx, dy);
        }

        /// <summary>
        /// The grid direction nearest to a direction on the floor, ignoring its height (one of <see cref="Directions"/>)
        /// </summary>
        public static Vector2Int NearestDirection(Vector3 direction)
        {
            if (Mathf.Abs(direction.x) > Mathf.Abs(direction.z))
            {
                return direction.x >= 0f ? Vector2Int.right : Vector2Int.left;
            }
            return direction.z >= 0f ? Vector2Int.up : Vector2Int.down;
        }
    }
}
