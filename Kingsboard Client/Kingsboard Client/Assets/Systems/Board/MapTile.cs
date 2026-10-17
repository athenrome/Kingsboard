namespace Kingsboard.Board
{
    /// <summary>
    /// The terrain on a map tile
    /// </summary>
    public enum TileType
    {
        /// <summary>
        /// A tile type this client doesn't recognise (e.g. the server is newer)
        /// </summary>
        Unknown,
        /// <summary>
        /// Open water. Impassable.
        /// </summary>
        Water,
        /// <summary>
        /// Open grassland
        /// </summary>
        Plains,
        /// <summary>
        /// Woodland
        /// </summary>
        Forest,
        /// <summary>
        /// Raised, rough ground
        /// </summary>
        Hills,
        /// <summary>
        /// High peaks. Impassable.
        /// </summary>
        Mountain
    }

    /// <summary>
    /// One square of the game board, as last sent by the server
    /// </summary>
    public class MapTile
    {
        /// <summary>
        /// Column (0 = left)
        /// </summary>
        public int X { get; set; }

        /// <summary>
        /// Row (0 = bottom)
        /// </summary>
        public int Y { get; set; }

        /// <summary>
        /// The terrain on this tile
        /// </summary>
        public TileType Type { get; set; }

        /// <summary>
        /// Whether pawns can move onto this tile
        /// </summary>
        public bool IsPassable { get; set; }
    }
}
