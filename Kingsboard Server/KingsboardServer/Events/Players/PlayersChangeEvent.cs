using KingsboardServer.Models.Players;

namespace KingsboardServer.Events.Players
{
    /// <summary>
    /// A player joined or left the game
    /// </summary>
    /// <param name="Players">Snapshots of every joined player after the change, in the order they joined</param>
    public record PlayersChangeEvent(IReadOnlyList<Player> Players);
}
