namespace KingsboardServer.Models.Board
{
    /// <summary>
    /// Something a viewing client needs to be sent. Exactly one of the two is set.
    /// </summary>
    /// <param name="Section">A full section that replaces everything the client had</param>
    /// <param name="Change">Tiles that changed inside the client's current section</param>
    public record BoardViewEvent(BoardSection? Section, TilesChange? Change);
}
