using Kingsboard.Shared.Models.Board;

namespace KingsboardServer.Models.Cards
{
    /// <summary>
    /// What happened when a pawn played a card
    /// </summary>
    /// <param name="Path">Each position the pawn moved through, in order (empty if it didn't move)</param>
    /// <param name="Position">Where the pawn stands afterwards</param>
    /// <param name="Blocked">True if the card was meant to move the pawn to its target and it stopped short</param>
    /// <param name="Speech">What the pawn said, or null if it said nothing</param>
    public record CardActionResult(IReadOnlyList<BoardPosition> Path, BoardPosition Position, bool Blocked, string? Speech);
}
