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
        Mountain,
        /// <summary>
        /// Bedrock that fills a stack below its surface tile
        /// </summary>
        Stone
    }

    /// <summary>
    /// One block of the game board, as last sent by the server
    /// </summary>
    public class MapTile
    {
        /// <summary>
        /// Where the tile sits on the board
        /// </summary>
        public BoardPosition Position { get; set; }

        /// <summary>
        /// The terrain this tile is made of
        /// </summary>
        public TileType Type { get; set; }

        /// <summary>
        /// Whether pawns can stand on this tile when it's the top of its stack
        /// </summary>
        public bool IsPassable { get; set; }
    }
}
