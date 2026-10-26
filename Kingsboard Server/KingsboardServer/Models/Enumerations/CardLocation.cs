namespace KingsboardServer.Models.Enumerations
{
    /// <summary>
    /// Where a card a pawn holds is
    /// </summary>
    public enum CardLocation
    {
        /// <summary>
        /// In the pawn's deck, which it draws from
        /// </summary>
        Deck,

        /// <summary>
        /// In the pawn's hand, which it plays from this turn
        /// </summary>
        Hand
    }
}
