namespace KingsboardServer.Models.Enumerations
{
    /// <summary>
    /// Who a chat message came from
    /// </summary>
    public enum ChatEntryType
    {
        /// <summary>
        /// Sent by a player
        /// </summary>
        Player,
        /// <summary>
        /// Announced by the server (joins, leaves, etc.)
        /// </summary>
        System
    }
}
