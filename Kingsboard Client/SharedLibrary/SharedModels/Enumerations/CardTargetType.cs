namespace Kingsboard.Shared.Models.Enumerations
{
    /// <summary>
    /// What a card has to be pointed at when it is chosen
    /// </summary>
    public enum CardTargetType
    {
        /// <summary>
        /// A target type this side doesn't recognise (e.g. a client older than the server). The server never uses it.
        /// </summary>
        Unknown,

        /// <summary>
        /// The card needs no target
        /// </summary>
        None,

        /// <summary>
        /// The card needs a board position to act on (only its column matters)
        /// </summary>
        Tile
    }
}
