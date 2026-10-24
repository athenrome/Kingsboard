namespace Kingsboard.Shared.Models.Enumerations
{
    /// <summary>
    /// The stages a turn moves through, in order. Each phase finishes all its work before the next begins.
    /// </summary>
    public enum TurnPhase
    {
        /// <summary>
        /// A phase this side doesn't recognise (e.g. a client older than the server). The server never runs it.
        /// </summary>
        Unknown,

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
