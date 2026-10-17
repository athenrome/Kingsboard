using KingsboardServer.Models.Board;
using KingsboardServer.Models.Enumerations;
using KingsboardServer.Services.Interfaces;

namespace KingsboardServer.Services
{
    /// <summary>
    /// Rules for individual map tiles. All tile rules (such as which terrain can be crossed) live here.
    /// </summary>
    public class TileService : ITileService
    {
        /// <summary>
        /// Whether pawns can move onto the tile (water and mountains are impassable)
        /// </summary>
        public bool IsPassable(MapTile tile) => tile.Type is not (TileType.Water or TileType.Mountain);
    }
}
