using KingsboardServer.Data.Records;

namespace KingsboardServer.Repositories.Interfaces
{
    /// <summary>
    /// Stores the pawns in the database. All access to stored pawn data goes through here.
    /// This is plain storage: pawn rules, copying, locking and turning records into pawns are done by the
    /// PawnService, its only user.
    /// </summary>
    public interface IPawnRepository
    {
        /// <summary>
        /// Reads the stored pawns with their cards and planned paths (empty if none have been saved yet)
        /// </summary>
        IReadOnlyList<PawnRecord> GetPawns();

        /// <summary>
        /// Stores pawns, with their cards and planned paths, in place of all the current ones, in a single transaction
        /// </summary>
        void SavePawns(IReadOnlyList<PawnRecord> pawns);
    }
}
