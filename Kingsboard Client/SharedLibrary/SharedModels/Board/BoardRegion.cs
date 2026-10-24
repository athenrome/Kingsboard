namespace Kingsboard.Shared.Models.Board
{
    /// <summary>
    /// A rectangle of columns on the board, covering every level. Used to pick the section of the board a client views.
    /// Compared by value.
    /// </summary>
    /// <param name="Origin">Front-left column of the region (its Z is ignored)</param>
    /// <param name="Width">Number of columns (x) in the region</param>
    /// <param name="Depth">Number of rows (y) in the region</param>
    public record BoardRegion(BoardPosition Origin, int Width, int Depth);
}
