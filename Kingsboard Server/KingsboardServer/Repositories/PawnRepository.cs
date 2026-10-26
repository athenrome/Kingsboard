using KingsboardServer.Data;
using KingsboardServer.Data.Records;
using KingsboardServer.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace KingsboardServer.Repositories
{
    /// <summary>
    /// Stores the pawns and the cards they hold in the SQLite database through Entity Framework, so they outlive a server
    /// restart. Thread-safe: each call uses its own short-lived <see cref="KingsboardDbContext"/>.
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
        /// Reads the stored pawns, without their cards, in <see cref="PawnRecord.Order"/> (empty if none have been saved yet)
        /// </summary>
        public IReadOnlyList<PawnRecord> GetPawns()
        {
            using KingsboardDbContext context = _contextFactory.CreateDbContext();
            return context.Pawns
                .AsNoTracking()
                .OrderBy(p => p.Order)
                .ToList();
        }

        /// <summary>
        /// Reads every card the stored pawns hold, in no particular order
        /// </summary>
        public IReadOnlyList<PawnCardRecord> GetPawnCards()
        {
            using KingsboardDbContext context = _contextFactory.CreateDbContext();
            return context.PawnCards.AsNoTracking().ToList();
        }

        /// <summary>
        /// Stores pawns and their cards in place of all the current ones, in a single transaction. A pawn or card already
        /// stored (matched by PublicId) keeps its id; ones no longer listed are deleted. <paramref name="cardsByPawn"/> holds
        /// each pawn's cards, keyed by the pawn's PublicId; the cards are linked to their pawn here. Sets the ids of every
        /// pawn and card.
        /// </summary>
        public void SavePawns(IReadOnlyList<PawnRecord> pawns, IReadOnlyDictionary<Guid, IReadOnlyList<PawnCardRecord>> cardsByPawn)
        {
            using KingsboardDbContext context = _contextFactory.CreateDbContext();
            using var transaction = context.Database.BeginTransaction();

            List<Guid> pawnIds = pawns.Select(p => p.PublicId).ToList();
            List<Guid> cardIds = cardsByPawn.Values.SelectMany(cards => cards).Select(c => c.PublicId).ToList();

            // Cards first, since they belong to pawns that may be about to go
            context.PawnCards.Where(c => !cardIds.Contains(c.PublicId)).ExecuteDelete();
            context.Pawns.Where(p => !pawnIds.Contains(p.PublicId)).ExecuteDelete();

            Dictionary<Guid, int> storedPawns = context.Pawns.AsNoTracking().ToDictionary(p => p.PublicId, p => p.Id);
            foreach (PawnRecord pawn in pawns)
            {
                if (storedPawns.TryGetValue(pawn.PublicId, out int id))
                {
                    pawn.Id = id;
                    context.Pawns.Update(pawn);
                }
                else
                {
                    pawn.Id = 0;
                    context.Pawns.Add(pawn);
                }
            }
            // Save the pawns first, so the database numbers new ones and their cards can be linked to them
            context.SaveChanges();

            Dictionary<Guid, int> storedCards = context.PawnCards.AsNoTracking().ToDictionary(c => c.PublicId, c => c.Id);
            foreach (PawnRecord pawn in pawns)
            {
                if (!cardsByPawn.TryGetValue(pawn.PublicId, out IReadOnlyList<PawnCardRecord>? cards))
                {
                    continue;
                }

                foreach (PawnCardRecord card in cards)
                {
                    card.PawnRecordId = pawn.Id;
                    if (storedCards.TryGetValue(card.PublicId, out int id))
                    {
                        card.Id = id;
                        context.PawnCards.Update(card);
                    }
                    else
                    {
                        card.Id = 0;
                        context.PawnCards.Add(card);
                    }
                }
            }
            context.SaveChanges();

            transaction.Commit();
        }
    }
}
