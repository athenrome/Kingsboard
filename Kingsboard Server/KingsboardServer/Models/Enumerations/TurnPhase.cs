namespace KingsboardServer.Models.Enumerations
{
    /// <summary>
    /// The stages a turn moves through, in order
    /// </summary>
    public enum TurnPhase
    {
        /// <summary>
        /// The turn has just begun
        /// </summary>
        Start,

        /// <summary>
        /// Pawns are prepared for the turn (e.g. dealt cards)
        /// </summary>
        Upkeep,

        /// <summary>
        /// Pawns choose their actions
        /// </summary>
        Planning,

        /// <summary>
        /// Players choose cards for their pawns until the timer runs out
        /// </summary>
        Player,

        /// <summary>
        /// The chosen actions are carried out
        /// </summary>
        Resolution,

        /// <summary>
        /// The turn is over
        /// </summary>
        End
    }
}
