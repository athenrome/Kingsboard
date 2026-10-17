namespace KingsboardServer.Models.Enumerations
{
    /// <summary>
    /// The terrain on a map tile
    /// </summary>
    public enum TileType
    {
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
}
