namespace KingsboardServer.Models.Cards
{
    /// <summary>
    /// The outcome of a player choosing a card for a pawn
    /// </summary>
    /// <param name="Selected">True if the card was chosen; false if it was turned down, and nothing changed</param>
    /// <param name="Error">Why the card wasn't chosen, as a message for the player; null when it was</param>
    public record CardSelectResult(bool Selected, string? Error = null);
}
