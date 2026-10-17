namespace Kingsboard.Board
{
    /// <summary>
    /// A position on the game board. It may hold a tile or be empty space, so it can name places
    /// a tile could go as well as tiles that exist. Compared by value (two positions with the same X, Y and Z are equal).
    /// </summary>
    public struct BoardPosition
    {
        /// <summary>
        /// Column across the board (0 = left)
        /// </summary>
        public int X { get; set; }

        /// <summary>
        /// Row into the board (0 = front)
        /// </summary>
        public int Y { get; set; }

        /// <summary>
        /// Level in the stack (0 = bottom)
        /// </summary>
        public int Z { get; set; }
    }
}
