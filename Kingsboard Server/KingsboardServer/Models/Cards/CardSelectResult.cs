using KingsboardServer.Models.Enumerations;

namespace KingsboardServer.Models.Cards
{
    /// <summary>
    /// The outcome of a player choosing a card for a pawn
    /// </summary>
    /// <param name="Status">Whether the card was chosen, or why not</param>
    /// <param name="Error">A message for the player when it wasn't chosen; null when it was</param>
    public record CardSelectResult(CardSelectStatus Status, string? Error = null);
}
