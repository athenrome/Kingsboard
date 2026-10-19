using KingsboardServer.Models.Enumerations;
using KingsboardServer.Models.Pawns;

namespace KingsboardServer.Events.Pawns
{
    /// <summary>
    /// A change to the pawns, sent to every client
    /// </summary>
    /// <param name="Reason">What happened</param>
    /// <param name="TurnId">The turn it happened in (0 for spawning, which happens before the first turn)</param>
    /// <param name="Pawns">Snapshots of every pawn after the change</param>
    /// <param name="Movements">How each pawn moved (only for <see cref="PawnChangeReason.Moved"/>; otherwise empty)</param>
    public record PawnsChangeEvent(PawnChangeReason Reason, int TurnId, IReadOnlyList<Pawn> Pawns, IReadOnlyList<PawnMovement> Movements);
}
