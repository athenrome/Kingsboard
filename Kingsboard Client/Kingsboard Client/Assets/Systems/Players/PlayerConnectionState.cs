namespace Kingsboard.Players
{
    /// <summary>
    /// Where the local player is in joining the game
    /// </summary>
    public enum PlayerConnectionState
    {
        /// <summary>
        /// Joining for the first time
        /// </summary>
        Connecting,

        /// <summary>
        /// Joined to the game, with a player id
        /// </summary>
        Joined,

        /// <summary>
        /// The connection dropped and we're joining again
        /// </summary>
        Rejoining,
    }
}
