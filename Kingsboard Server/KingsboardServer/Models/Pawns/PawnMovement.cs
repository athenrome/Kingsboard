using KingsboardServer.Models.Board;

namespace KingsboardServer.Models.Pawns
{
    /// <summary>
    /// How one pawn moved during the Resolution phase
    /// </summary>
    /// <param name="PawnId">The pawn that moved</param>
    /// <param name="Path">Each position it moved through, in order (empty if it didn't move)</param>
    /// <param name="Position">Where it ended up</param>
    /// <param name="Blocked">True if it stopped short of its planned destination</param>
    public record PawnMovement(Guid PawnId, IReadOnlyList<BoardPosition> Path, BoardPosition Position, bool Blocked);
}
