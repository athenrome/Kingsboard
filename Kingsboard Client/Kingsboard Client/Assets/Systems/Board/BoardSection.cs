using System;
using Kingsboard.Shared.Models.Board;

namespace Kingsboard.Board
{
    /// <summary>
    /// The client's copy of the section of the board it is viewing: every tile in <see cref="Region"/> (a rectangle of
    /// columns, full height). The whole board is <see cref="BoardWidth"/> x <see cref="BoardDepth"/> x <see cref="BoardHeight"/>
    /// and lives on the server; this is only ever updated from what the server sends.
    /// Tiles keep their positions on the whole board, so a tile at (40, 12, 0) is at (40, 12, 0) whatever section it's in.
    /// </summary>
    public class BoardSection
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
        /// Number of columns (x) on the whole board
        /// </summary>
        public int BoardWidth { get; set; }

        /// <summary>
        /// Number of rows (y) on the whole board
        /// </summary>
        public int BoardDepth { get; set; }

        /// <summary>
        /// Most tiles a stack can hold (z)
        /// </summary>
        public int BoardHeight { get; set; }

        /// <summary>
        /// The columns this section covers
        /// </summary>
        public BoardRegion Region { get; set; } = new BoardRegion(new BoardPosition(0, 0, 0), 0, 0);

        /// <summary>
        /// Every position in the section, null where there is no tile. Indexed relative to the region's origin:
        /// index = (z * Region.Depth + (y - Region.Origin.Y)) * Region.Width + (x - Region.Origin.X).
        /// </summary>
        public MapTile[] Tiles { get; set; } = Array.Empty<MapTile>();
    }
}
