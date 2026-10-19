namespace KingsboardServer.Models.Board
{
    /// <summary>
    /// A change to tiles on the board
    /// </summary>
    /// <param name="BoardId">The board that changed</param>
    /// <param name="Version">The board version after the change</param>
    /// <param name="Placed">Tiles placed or replaced</param>
    /// <param name="Removed">Positions whose tiles were taken off the board (now empty space)</param>
    public record TilesChange(Guid BoardId, long Version, IReadOnlyList<MapTile> Placed, IReadOnlyList<BoardPosition> Removed);
}
