using KingsboardServer.Models.Board;

namespace KingsboardServer.Services.Interfaces
{
    /// <summary>
    /// Rules for individual map tiles
    /// </summary>
    public interface ITileService
    {
        /// <summary>
        /// Whether pawns can move onto the tile (water and mountains are impassable)
        /// </summary>
        bool IsPassable(MapTile tile);
    }
}
