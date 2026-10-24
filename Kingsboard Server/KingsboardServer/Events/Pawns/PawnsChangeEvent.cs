using Kingsboard.Shared.Models.Pawns;
using KingsboardServer.Models.Enumerations;

namespace KingsboardServer.Events.Pawns
{
    /// <summary>
    /// A change to the pawns, sent to every client
    /// </summary>
    /// <param name="Reason">What happened</param>
    /// <param name="TurnId">The turn it happened in (0 when it didn't happen in a turn, e.g. spawning or owners changing)</param>
    /// <param name="Pawns">Snapshots of the pawns after the change: every pawn, except for <see cref="PawnChangeReason.CardSelected"/>, which has only the pawn that changed</param>
    /// <param name="Movements">How each pawn that moved got there, in the order they moved (only for <see cref="PawnChangeReason.Resolved"/>; otherwise empty)</param>
    /// <param name="Speeches">What pawns said, in order (only for <see cref="PawnChangeReason.Resolved"/>; otherwise empty)</param>
    public record PawnsChangeEvent(PawnChangeReason Reason, int TurnId, IReadOnlyList<Pawn> Pawns, IReadOnlyList<PawnMovement> Movements, IReadOnlyList<PawnSpeech> Speeches);
}
