using Kingsboard.Shared.Models.Board;
using KingsboardServer.Models.Cards;
using KingsboardServer.Models.Enumerations;
using KingsboardServer.Services.Interfaces;

namespace KingsboardServer.Services.CardActions
{
    /// <summary>
    /// A test card: the pawn says a random whole number from 1 to 100
    /// </summary>
    public class SayRandomNumberCardAction : ICardAction
    {
        private const int Min = 1;
        private const int Max = 100;

        /// <summary>
        /// <see cref="CardActionType.SayRandomNumber"/>
        /// </summary>
        public CardActionType Action => CardActionType.SayRandomNumber;

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
        /// Picks the number (when the card is played, not before) and says it
        /// </summary>
        public CardActionResult Resolve(CardActionContext context)
        {
            int number = context.Random.Next(Min, Max + 1);
            return new CardActionResult(Array.Empty<BoardPosition>(), context.Pawn.Position, false, number.ToString());
        }
    }
}
