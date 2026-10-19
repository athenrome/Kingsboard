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
        /// Pawns chose where they intend to move this turn (Planning phase)
        /// </summary>
        Planned,

        /// <summary>
        /// Pawns walked their planned paths (Resolution phase)
        /// </summary>
        Moved
    }
}
