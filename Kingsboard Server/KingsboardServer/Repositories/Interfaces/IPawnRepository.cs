using KingsboardServer.Models.Pawns;

namespace KingsboardServer.Repositories.Interfaces
{
    /// <summary>
    /// Stores the pawns. All access to stored pawn data goes through here.
    /// This is plain storage: pawn rules, copying and locking are done by the PawnService, its only user.
    /// </summary>
    public interface IPawnRepository
    {
        /// <summary>
        /// Returns the stored pawns (empty if none have been saved yet)
        /// </summary>
        IReadOnlyList<Pawn> GetPawns();

        /// <summary>
        /// Stores pawns in place of all the current ones
        /// </summary>
        void SavePawns(IReadOnlyList<Pawn> pawns);
    }
}
