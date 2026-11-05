using System;

namespace Kingsboard.Players
{
    /// <summary>
    /// Things the local player can be in the middle of doing. Several can be going on at once, so this is a set of flags.
    /// </summary>
    [Flags]
    public enum PlayerAction
    {
        /// <summary>
        /// Not doing anything in particular
        /// </summary>
        None = 0,

        /// <summary>
        /// Typing in the chat box, which has the keyboard
        /// </summary>
        TypingInChat = 1 << 0,

        /// <summary>
        /// Aiming a card that needs a tile; the next click on the board picks the target
        /// </summary>
        TargetingCard = 1 << 1,

        /// <summary>
        /// Seated at the table, using the board and our pawns. The avatar stays put and the camera looks at the board
        /// until we stand up; the board and pawns can only be used while this is going on.
        /// </summary>
        AtTable = 1 << 2,
    }
}
