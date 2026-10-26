namespace KingsboardServer.Models.Enumerations
{
    /// <summary>
    /// Which of a pawn's piles a stored card is in
    /// </summary>
    public enum CardPile
    {
        /// <summary>
        /// The pawn's deck, which it draws from
        /// </summary>
        Deck,

        /// <summary>
        /// The pawn's hand, which it plays from this turn
        /// </summary>
        Hand
    }
}
