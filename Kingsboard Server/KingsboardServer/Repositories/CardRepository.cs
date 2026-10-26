using KingsboardServer.Data;
using KingsboardServer.Data.Records;
using KingsboardServer.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace KingsboardServer.Repositories
{
    /// <summary>
    /// Stores the card definitions in the SQLite database through Entity Framework.
    /// Thread-safe: each call uses its own short-lived <see cref="KingsboardDbContext"/>.
    /// </summary>
    public class CardRepository : ICardRepository
    {
        private readonly IDbContextFactory<KingsboardDbContext> _contextFactory;

        /// <summary>
        /// Creates a repository that opens database sessions from <paramref name="contextFactory"/>
        /// </summary>
        public CardRepository(IDbContextFactory<KingsboardDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        /// <summary>
        /// Stores each definition, matched by <see cref="CardRecord.DefinitionId"/>: one already stored is updated and keeps
        /// its id, and a new one is added. Stored definitions not listed are left alone, since pawns may still hold copies.
        /// Returns every stored definition, with its id.
        /// </summary>
        public IReadOnlyList<CardRecord> SaveDefinitions(IReadOnlyList<CardRecord> definitions)
        {
            using KingsboardDbContext context = _contextFactory.CreateDbContext();
            using var transaction = context.Database.BeginTransaction();

            Dictionary<string, int> stored = context.Cards.AsNoTracking().ToDictionary(c => c.DefinitionId, c => c.Id);
            foreach (CardRecord definition in definitions)
            {
                if (stored.TryGetValue(definition.DefinitionId, out int id))
                {
                    definition.Id = id;
                    context.Cards.Update(definition);
                }
                else
                {
                    definition.Id = 0;
                    context.Cards.Add(definition);
                }
            }
            context.SaveChanges();
            transaction.Commit();

            return context.Cards.AsNoTracking().ToList();
        }
    }
}
