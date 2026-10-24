namespace KingsboardServer.Models.Enumerations
{
    /// <summary>
    /// The outcome of a player choosing a card for a pawn
    /// </summary>
    public enum CardSelectStatus
    {
        /// <summary>
        /// The card was chosen
        /// </summary>
        Selected,

        /// <summary>
        /// Cards can only be chosen in the Player phase
        /// </summary>
        NotPlayerPhase,

        /// <summary>
        /// No pawn has that id
        /// </summary>
        PawnNotFound,

        /// <summary>
        /// The player doesn't control that pawn
        /// </summary>
        NotOwner,

        /// <summary>
        /// The card isn't in the pawn's hand
        /// </summary>
        CardNotInHand,

        /// <summary>
        /// The card needs a target and none was given
        /// </summary>
        TargetRequired,

        /// <summary>
        /// The target isn't on the board
        /// </summary>
        TargetOffBoard
    }
}
