using KingsboardServer.Data.Records;

namespace KingsboardServer.Repositories.Interfaces
{
    /// <summary>
    /// Stores the pawns and, separately, the cards they hold. All access to stored pawn data goes through here.
    /// This is plain storage: pawn rules, copying, locking and combining pawns with their cards are done by the
    /// PawnService, its only user.
    /// </summary>
    public interface IPawnRepository
    {
        /// <summary>
        /// Reads the stored pawns, without their cards, in <see cref="PawnRecord.Order"/> (empty if none have been saved yet)
        /// </summary>
        IReadOnlyList<PawnRecord> GetPawns();

        /// <summary>
        /// Reads every card the stored pawns hold, in no particular order
        /// </summary>
        IReadOnlyList<PawnCardRecord> GetPawnCards();

        /// <summary>
        /// Stores pawns and their cards in place of all the current ones, in a single transaction. A pawn or card already
        /// stored (matched by <see cref="Kingsboard.Shared.Models.PublicBaseModel.PublicId"/>) keeps its id; ones no longer
        /// listed are deleted. <paramref name="cardsByPawn"/> holds each pawn's cards, keyed by the pawn's PublicId; the
        /// cards are linked to their pawn here. Sets the ids of every pawn and card.
        /// </summary>
        void SavePawns(IReadOnlyList<PawnRecord> pawns, IReadOnlyDictionary<Guid, IReadOnlyList<PawnCardRecord>> cardsByPawn);
    }
}
