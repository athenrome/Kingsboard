using Kingsboard.Shared.Models.Board;

namespace KingsboardServer.Services.Interfaces
{
    /// <summary>
    /// The rules for where pawns can stand and how they walk: a step goes to a neighbouring column (4-way) whose top tile
    /// is passable, at most PawnSettings:MaxClimb levels up or down. Holds no pawn state, so the pawn service and card
    /// actions can both use it. It only reads the board (lock order: pawn, then board).
    /// </summary>
    public interface IPawnMovementService
    {
        /// <summary>
        /// Where a pawn would stand in <paramref name="column"/> (its Z is ignored): the empty space directly above the
        /// top tile. Null if the column is off the board, empty, or its top tile isn't passable.
        /// </summary>
        BoardPosition? GetStandPosition(BoardPosition column);

        /// <summary>
        /// Every column (Z = 0) a pawn standing at <paramref name="from"/> can reach within <paramref name="range"/> steps
        /// without entering a <paramref name="blocked"/> column, including its own, each with the shortest path there
        /// (the stand positions walked through, in order, ending at the destination).
        /// </summary>
        IReadOnlyDictionary<BoardPosition, IReadOnlyList<BoardPosition>> FindReachable(BoardPosition from, int range, IReadOnlySet<BoardPosition> blocked);

        /// <summary>
        /// The path a pawn standing at <paramref name="from"/> walks towards <paramref name="target"/>'s column within
        /// <paramref name="range"/> steps, avoiding <paramref name="blocked"/> columns: to the target if it can reach it,
        /// otherwise to the reachable column nearest it (fewest steps on a tie). Empty if no move gets it closer.
        /// </summary>
        IReadOnlyList<BoardPosition> FindPathTowards(BoardPosition from, BoardPosition target, int range, IReadOnlySet<BoardPosition> blocked);

        /// <summary>
        /// The column <paramref name="position"/> is in: the same position at Z = 0
        /// </summary>
        BoardPosition GetColumn(BoardPosition position);
    }
}
