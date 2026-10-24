using Kingsboard.Shared.Models.Board;
using KingsboardServer.Models.Cards;
using KingsboardServer.Models.Enumerations;
using KingsboardServer.Services.Interfaces;

namespace KingsboardServer.Services.CardActions
{
    /// <summary>
    /// A test card: the pawn says the name of a random colour
    /// </summary>
    public class SayRandomColourCardAction : ICardAction
    {
        private static readonly string[] Colours =
        {
            "Red", "Orange", "Yellow", "Green", "Blue", "Purple", "Pink", "Brown", "Black", "White", "Grey", "Teal"
        };

        /// <summary>
        /// <see cref="CardActionType.SayRandomColour"/>
        /// </summary>
        public CardActionType Action => CardActionType.SayRandomColour;

        /// <summary>
        /// Always usable, with no target
        /// </summary>
        public bool TryChooseTarget(CardActionContext context, out BoardPosition? target)
        {
            target = null;
            return true;
        }

        /// <summary>
        /// Empty: the pawn doesn't move
        /// </summary>
        public IReadOnlyList<BoardPosition> PlanPath(CardActionContext context)
        {
            return Array.Empty<BoardPosition>();
        }

        /// <summary>
        /// Picks the colour (when the card is played, not before) and says it
        /// </summary>
        public CardActionResult Resolve(CardActionContext context)
        {
            string colour = Colours[context.Random.Next(Colours.Length)];
            return new CardActionResult(Array.Empty<BoardPosition>(), context.Pawn.Position, false, colour);
        }
    }
}
