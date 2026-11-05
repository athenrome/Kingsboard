namespace Kingsboard.Players
{
    /// <summary>
    /// What the local player is controlling right now: their avatar walking around the room, or the game at the table.
    /// Exactly one mode applies at a time; see <see cref="PlayerStateTracker.Mode"/>.
    /// </summary>
    public enum PlayerMode
    {
        /// <summary>
        /// Walking around the room as the avatar, seen in first person. The board and pawns can't be used.
        /// </summary>
        Avatar = 0,

        /// <summary>
        /// Seated at the table, interacting with the board and our pawns from the board view. The avatar stays put.
        /// </summary>
        InGame = 1,
    }
}
