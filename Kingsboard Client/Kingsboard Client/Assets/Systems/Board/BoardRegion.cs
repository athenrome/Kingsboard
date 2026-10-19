namespace Kingsboard.Board
{
    /// <summary>
    /// A rectangle of columns on the board, covering every level. Used to pick the section of the board to view.
    /// </summary>
    public struct BoardRegion
    {
        /// <summary>
        /// Front-left column of the region (its Z is ignored)
        /// </summary>
        public BoardPosition Origin { get; set; }

        /// <summary>
        /// Number of columns (x) in the region
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// Number of rows (y) in the region
        /// </summary>
        public int Depth { get; set; }
    }
}
