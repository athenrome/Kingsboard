namespace KingsboardServer.Models.Board
{
    /// <summary>
    /// Some tiles on the board changed. <paramref name="Version"/> is the board version after the change.
    /// </summary>
    public record TilesChange(Guid BoardId, long Version, IReadOnlyList<MapTile> Tiles);
}
