using KingsboardServer.Data;
using KingsboardServer.Data.Records;
using KingsboardServer.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace KingsboardServer.Repositories
{
    /// <summary>
    /// Stores the pawns in the SQLite database through Entity Framework, so they outlive a server restart.
    /// Thread-safe: each call uses its own short-lived <see cref="KingsboardDbContext"/>.
    /// </summary>
    public class PawnRepository : IPawnRepository
    {
        private readonly IDbContextFactory<KingsboardDbContext> _contextFactory;

        /// <summary>
        /// Creates a repository that opens database sessions from <paramref name="contextFactory"/>
        /// </summary>
        public PawnRepository(IDbContextFactory<KingsboardDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        /// <summary>
        /// Reads the stored pawns with their cards and planned paths, in <see cref="PawnRecord.Order"/>
        /// (empty if none have been saved yet). Cards and steps come back in no particular order.
        /// </summary>
        public IReadOnlyList<PawnRecord> GetPawns()
        {
            using KingsboardDbContext context = _contextFactory.CreateDbContext();
            return context.Pawns
                .AsNoTracking()
                .Include(p => p.Cards)
                .Include(p => p.PlannedPath)
                .AsSplitQuery()
                .OrderBy(p => p.Order)
                .ToList();
        }

        /// <summary>
        /// Stores pawns, with their cards and planned paths, in place of all the current ones, in a single transaction
        /// </summary>
        public void SavePawns(IReadOnlyList<PawnRecord> pawns)
        {
            using KingsboardDbContext context = _contextFactory.CreateDbContext();
            using var transaction = context.Database.BeginTransaction();

            context.PawnPathSteps.ExecuteDelete();
            context.Cards.ExecuteDelete();
            context.Pawns.ExecuteDelete();
            context.Pawns.AddRange(pawns);
            context.SaveChanges();

            transaction.Commit();
        }
    }
}
