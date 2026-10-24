namespace Kingsboard.Shared.Models.Board
{
    /// <summary>
    /// A position on the game board. It may hold a tile or be empty space, so it can name places
    /// a tile could go as well as tiles that exist. Compared by value, so it can be used as a dictionary key.
    /// Change a level with <c>position with { Z = ... }</c>.
    /// </summary>
    /// <param name="X">Column across the board (0 = left)</param>
    /// <param name="Y">Row into the board (0 = front)</param>
    /// <param name="Z">Level in the stack (0 = bottom)</param>
    public record BoardPosition(int X, int Y, int Z);
}
