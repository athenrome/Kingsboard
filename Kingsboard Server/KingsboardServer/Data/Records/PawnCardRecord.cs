using Kingsboard.Shared.Models;
using KingsboardServer.Models.Enumerations;

namespace KingsboardServer.Data.Records
{
    /// <summary>
    /// The stored ownership and location of one copy of a card: which pawn holds it, whether it's in the deck or the hand,
    /// and where. Its <see cref="PublicBaseModel.PublicId"/> is the card copy's own id. Storage only: <c>CardService</c>
    /// combines it with its <see cref="CardRecord"/> definition into a <c>Card</c>.
    /// </summary>
    public class PawnCardRecord : PublicBaseModel
    {
        /// <summary>
        /// <see cref="BaseModel.Id"/> of the pawn holding the card. Set by the repository when the pawn is saved.
        /// </summary>
        public int PawnRecordId { get; set; }

        /// <summary>
        /// <see cref="BaseModel.Id"/> of the <see cref="CardRecord"/> this is a copy of
        /// </summary>
        public int CardRecordId { get; set; }

        /// <summary>
        /// Whether the card is in the pawn's deck or its hand
        /// </summary>
        public CardLocation Location { get; set; }

        /// <summary>
        /// Where the card is in its location, from 0 (the next card drawn, or the first in the hand)
        /// </summary>
        public int Order { get; set; }
    }
}
