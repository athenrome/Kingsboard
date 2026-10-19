using KingsboardServer.Models.Pawns;
using KingsboardServer.Repositories.Interfaces;

namespace KingsboardServer.Repositories
{
    /// <summary>
    /// Keeps the pawns in memory. They are lost when the server restarts.
    /// </summary>
    public class PawnRepository : IPawnRepository
    {
        private IReadOnlyList<Pawn> _pawns = Array.Empty<Pawn>();

        /// <summary>
        /// Returns the stored pawns (empty if none have been saved yet)
        /// </summary>
        public IReadOnlyList<Pawn> GetPawns()
        {
            return Volatile.Read(ref _pawns);
        }

        /// <summary>
        /// Stores pawns in place of all the current ones
        /// </summary>
        public void SavePawns(IReadOnlyList<Pawn> pawns)
        {
            Volatile.Write(ref _pawns, pawns);
        }
    }
}
