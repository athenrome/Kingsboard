using Kingsboard.Shared.Models.Enumerations;
using KingsboardServer.Models.Enumerations;

namespace KingsboardServer.Models.Cards
{
    /// <summary>
    /// One kind of card. Every card in a deck is a copy of a definition, made by the CardService.
    /// </summary>
    public class CardDefinition
    {
        /// <summary>
        /// Unique id of the definition (e.g. "move"), used to list the cards in a starting deck
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// The card's name, as shown to players
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// What the card does, as shown to players
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Id of the picture shown on the card. Empty until card art exists.
        /// </summary>
        public string ImageId { get; set; } = string.Empty;

        /// <summary>
        /// What the card has to be pointed at when it is chosen
        /// </summary>
        public CardTargetType TargetType { get; set; } = CardTargetType.None;

        /// <summary>
        /// What the card does when it is played
        /// </summary>
        public CardActionType Action { get; set; }
    }
}
