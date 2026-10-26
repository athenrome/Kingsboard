using KingsboardServer.Data.Records;

namespace KingsboardServer.Repositories.Interfaces
{
    /// <summary>
    /// Stores the card definitions in the database. All access to stored card definitions goes through here.
    /// This is plain storage: the definitions themselves are written in code and kept in step by the CardService.
    /// </summary>
    public interface ICardRepository
    {
        /// <summary>
        /// Stores each definition, matched by <see cref="CardRecord.DefinitionId"/>: one already stored is updated and keeps
        /// its id, and a new one is added. Stored definitions not listed are left alone. Returns every stored definition,
        /// with its id.
        /// </summary>
        IReadOnlyList<CardRecord> SaveDefinitions(IReadOnlyList<CardRecord> definitions);
    }
}
