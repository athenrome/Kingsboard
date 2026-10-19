using KingsboardServer.Events.Pawns;
using KingsboardServer.Models.Pawns;

namespace KingsboardServer.Services.Interfaces
{
    /// <summary>
    /// Owns the pawns: characters that move around the board on their own. All decisions about where pawns are and
    /// what they do are made here; clients only display them. Pawns choose where to move in the Planning phase and
    /// walk there in the Resolution phase (this service is also the <see cref="ITurnPhaseHandler"/> for those phases).
    /// </summary>
    public interface IPawnService
    {
        /// <summary>
        /// Raised when pawns are spawned, plan their moves, or move. Raised in order, under the same guarantee as
        /// <see cref="ReadPawns"/>. Handlers must be quick and non-blocking.
        /// </summary>
        event EventHandler<PawnsChangeEvent>? PawnsChanged;

        /// <summary>
        /// Gets snapshots of every pawn (empty before any are spawned)
        /// </summary>
        IReadOnlyList<Pawn> GetPawns();

        /// <summary>
        /// Runs <paramref name="read"/> with snapshots of every pawn while no pawn can change. <see cref="PawnsChanged"/>
        /// is raised under the same guarantee, so a caller that starts listening inside <paramref name="read"/> gets every
        /// change after the snapshot, and none before it. <paramref name="read"/> must be quick and non-blocking.
        /// </summary>
        void ReadPawns(Action<IReadOnlyList<Pawn>> read);

        /// <summary>
        /// Places a new set of pawns (PawnSettings:Count) on passable ground near the board's front-left corner,
        /// replacing any earlier pawns, and returns them. Fewer are placed if there isn't enough room.
        /// </summary>
        /// <exception cref="InvalidOperationException">No board has been created yet</exception>
        IReadOnlyList<Pawn> SpawnPawns();
    }
}
