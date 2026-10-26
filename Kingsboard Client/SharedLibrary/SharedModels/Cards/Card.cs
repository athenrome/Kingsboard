using Kingsboard.Shared.Models.Enumerations;

namespace Kingsboard.Shared.Models.Cards
{
    /// <summary>
    /// One action card in a pawn's deck or hand. A pawn plays a card from its hand to choose what it does in the
    /// Resolution phase. Each card is its own copy with its own <see cref="PublicBaseModel.PublicId"/>, so a deck can hold
    /// several copies of the same card. The server decides what a card does; clients only show it.
    /// </summary>
    public class Card : PublicBaseModel
    {
        /// <summary>
        /// Which card this is a copy of (e.g. "move"). Every copy of the same card shares it.
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
        /// Id of the picture shown on the card. Empty until card art exists.
        /// </summary>
        public string ImageId { get; set; } = string.Empty;

        /// <summary>
        /// What the card has to be pointed at when it is chosen
        /// </summary>
        public CardTargetType TargetType { get; set; } = CardTargetType.None;
    }
}
