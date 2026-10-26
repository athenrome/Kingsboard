using Kingsboard.Shared.Models;
using Kingsboard.Shared.Models.Enumerations;
using KingsboardServer.Models.Enumerations;

namespace KingsboardServer.Data.Records
{
    /// <summary>
    /// The stored definition of one kind of card (e.g. Move). Which pawn holds a copy, and where, is stored separately
    /// as a <see cref="PawnCardRecord"/>. Storage only: <c>CardService</c> keeps these in step with the definitions in code.
    /// </summary>
    public class CardRecord : BaseModel
    {
        /// <summary>
        /// The definition's id in code (e.g. "move"). Unique.
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

        /// <summary>
        /// What the card does when it is played
        /// </summary>
        public CardActionType Action { get; set; }
    }
}
