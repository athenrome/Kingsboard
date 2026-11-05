using UnityEngine;

namespace Kingsboard.Room
{
    /// <summary>
    /// The room's own walking and placement grid: a flat Width × Depth grid of square cells laid on the room's floor.
    /// It belongs to the client's room only and has nothing to do with the Kingsboard board. A cell is a
    /// <see cref="Vector2Int"/>: x counts cells along the room's x axis and y counts them along its z axis, with (0, 0)
    /// the cell at <see cref="Origin"/>. Positions are in the room's local space. Logic lives in <see cref="RoomGridService"/>.
    /// </summary>
    public class RoomGrid
    {
        /// <summary>
        /// The length of one side of a cell, in room units
        /// </summary>
        public float CellSize { get; set; } = 8f;

        /// <summary>
        /// How many cells the grid has along the room's x axis
        /// </summary>
        public int Width { get; set; } = 26;

        /// <summary>
        /// How many cells the grid has along the room's z axis
        /// </summary>
        public int Depth { get; set; } = 26;

        /// <summary>
        /// The corner of cell (0, 0) with the lowest x and z, on the floor, in the room's local space
        /// </summary>
        public Vector3 Origin { get; set; }
    }
}
