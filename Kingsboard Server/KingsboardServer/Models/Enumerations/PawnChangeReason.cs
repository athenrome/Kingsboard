namespace KingsboardServer.Models.Enumerations
{
    /// <summary>
    /// Why the pawns changed
    /// </summary>
    public enum PawnChangeReason
    {
        /// <summary>
        /// A new set of pawns was placed on the board, replacing any earlier pawns
        /// </summary>
        Spawned,

        /// <summary>
        /// Pawns drew their hands for the turn (Upkeep phase)
        /// </summary>
        CardsDrawn,

        /// <summary>
        /// Pawns chose the card they'll play this turn (Planning phase)
        /// </summary>
        Planned,

        /// <summary>
        /// A player changed the card one of their pawns will play (Player phase). Only that pawn is included.
        /// </summary>
        CardSelected,

        /// <summary>
        /// Pawns played their chosen cards (Resolution phase)
        /// </summary>
        Resolved,

        /// <summary>
        /// Pawns shuffled their hands back into their decks (End phase)
        /// </summary>
        CardsReturned,

        /// <summary>
        /// Players joined or left, so which player controls which pawn changed
        /// </summary>
        OwnersChanged
    }
}
