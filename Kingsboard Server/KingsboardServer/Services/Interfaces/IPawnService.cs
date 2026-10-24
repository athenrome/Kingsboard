using Kingsboard.Shared.Models.Board;
using Kingsboard.Shared.Models.Pawns;
using KingsboardServer.Events.Pawns;
using KingsboardServer.Models.Cards;

namespace KingsboardServer.Services.Interfaces
{
    /// <summary>
    /// Owns the pawns: characters on the board that act by playing action cards. Each turn a pawn draws a hand
    /// (Upkeep), chooses a card (Planning), may have that choice changed by the player who controls it (Player), plays
    /// it (Resolution) and returns its hand to its deck (End). This service is also the <see cref="ITurnPhaseHandler"/>
    /// for those phases. All decisions about pawns are made here; clients only display them.
    /// </summary>
    public interface IPawnService
    {
        /// <summary>
        /// Raised when pawns change (see <see cref="Models.Enumerations.PawnChangeReason"/>). Raised in order, under the
        /// same guarantee as <see cref="ReadPawns"/>. Handlers must be quick and non-blocking.
        /// </summary>
        event EventHandler<PawnsChangeEvent>? PawnsChanged;

        /// <summary>
        /// Gets snapshots of every pawn (empty before any are spawned)
        /// </summary>
        IReadOnlyList<Pawn> GetPawns();

        /// <summary>
        /// Gets a snapshot of one pawn with all its details, or null if no pawn has that id
        /// </summary>
        Pawn? GetPawnById(Guid id);

        /// <summary>
        /// Runs <paramref name="read"/> with snapshots of every pawn while no pawn can change. <see cref="PawnsChanged"/>
        /// is raised under the same guarantee, so a caller that starts listening inside <paramref name="read"/> gets every
        /// change after the snapshot, and none before it. <paramref name="read"/> must be quick and non-blocking.
        /// </summary>
        void ReadPawns(Action<IReadOnlyList<Pawn>> read);

        /// <summary>
        /// Places a new set of pawns (PawnSettings:Count) on passable ground near the board's front-left corner, each
        /// with a freshly shuffled starting deck, replacing any earlier pawns, and gives them to the joined players.
        /// Fewer are placed if there isn't enough room.
        /// </summary>
        /// <exception cref="InvalidOperationException">No board has been created yet</exception>
        IReadOnlyList<Pawn> SpawnPawns();

        /// <summary>
        /// The most health the pawn can have, which is its Physique (<see cref="PawnAttributes.Physique"/>).
        /// Pawns are spawned with this much health.
        /// </summary>
        int GetMaxHealth(Pawn pawn);

        /// <summary>
        /// Chooses the card from its hand that a pawn will play this turn, on behalf of the player with public id
        /// <paramref name="playerId"/>, replacing the pawn's current choice. Only allowed in the Player phase, for a pawn
        /// that player controls. <paramref name="target"/> is required for cards that need a tile, and ignored otherwise.
        /// Returns why the card couldn't be chosen, if it couldn't; nothing changes in that case.
        /// </summary>
        CardSelectResult SelectCard(Guid playerId, Guid pawnId, Guid cardId, BoardPosition? target);
    }
}
