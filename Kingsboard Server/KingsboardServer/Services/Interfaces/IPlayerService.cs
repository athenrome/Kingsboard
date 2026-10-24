using KingsboardServer.Events.Players;
using KingsboardServer.Models.Players;

namespace KingsboardServer.Services.Interfaces
{
    /// <summary>
    /// Tracks who is playing: players join with a name, are identified by a secret session id, and leave when their
    /// connection closes. Other systems listen to <see cref="PlayersChanged"/> to react (e.g. pawns are given to players).
    /// </summary>
    public interface IPlayerService
    {
        /// <summary>
        /// Raised when a player joins or leaves, with every joined player. Raised in order, under the same guarantee
        /// as <see cref="ReadPlayers"/>. Handlers must be quick and non-blocking.
        /// </summary>
        event EventHandler<PlayersChangeEvent>? PlayersChanged;

        /// <summary>
        /// Adds a player with a cleaned, unique version of <paramref name="requestedName"/>, a colour no other joined player
        /// has (while there are colours to go round) and new ids, and returns a snapshot of them (including their secret session id)
        /// </summary>
        Player Join(string requestedName);

        /// <summary>
        /// Removes the player with this session id. Safe to call more than once.
        /// </summary>
        void Leave(Guid sessionId);

        /// <summary>
        /// Gets a snapshot of the joined player with this secret session id, or null if no joined player has it
        /// </summary>
        Player? GetPlayerBySession(Guid sessionId);

        /// <summary>
        /// Runs <paramref name="read"/> with snapshots of every joined player (in join order) while no player can join
        /// or leave. <see cref="PlayersChanged"/> is raised under the same guarantee, so work done inside
        /// <paramref name="read"/> can't be overtaken by a join or leave. <paramref name="read"/> must be quick and non-blocking.
        /// </summary>
        void ReadPlayers(Action<IReadOnlyList<Player>> read);
    }
}
