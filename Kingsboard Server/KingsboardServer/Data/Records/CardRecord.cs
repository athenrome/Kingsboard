using Kingsboard.Shared.Models.Enumerations;
using KingsboardServer.Models.Enumerations;

namespace KingsboardServer.Data.Records
{
    /// <summary>
    /// The stored form of one card a pawn holds, in its deck or its hand.
    /// Storage only: <c>CardService</c> turns it into a <c>Card</c> and back.
    /// </summary>
    public class CardRecord
    {
        /// <summary>
        /// Unique id of this copy of the card (the same as the card's own id)
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// The pawn holding the card
        /// </summary>
        public Guid PawnId { get; set; }

        /// <summary>
        /// Whether the card is in the pawn's deck or its hand
        /// </summary>
        public CardPile Pile { get; set; }

        /// <summary>
        /// Where the card is in its pile, from 0 (the next card drawn, or the first in the hand)
        /// </summary>
        public int Order { get; set; }

        /// <summary>
        /// Which card this is a copy of (e.g. "move")
        /// </summary>
        public string DefinitionId { get; set; } = string.Empty;

        /// <summary>
        /// The card's name, as shown to players
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// What the card does, as shown to players
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Id of the picture shown on the card
        /// </summary>
        public string ImageId { get; set; } = string.Empty;

        /// <summary>
        /// What the card has to be pointed at when it is chosen
        /// </summary>
        public CardTargetType TargetType { get; set; }
    }
}
