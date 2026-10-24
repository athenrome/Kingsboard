using Kingsboard.Shared.Models.Board;
using KingsboardServer.Models.Cards;
using KingsboardServer.Models.Enumerations;
using KingsboardServer.Services.Interfaces;

namespace KingsboardServer.Services.CardActions
{
    /// <summary>
    /// A test card: the pawn introduces itself by name
    /// </summary>
    public class SayHelloCardAction : ICardAction
    {
        /// <summary>
        /// <see cref="CardActionType.SayHello"/>
        /// </summary>
        public CardActionType Action => CardActionType.SayHello;

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
        /// Says "Hello, my name is {name}"
        /// </summary>
        public CardActionResult Resolve(CardActionContext context)
        {
            return new CardActionResult(Array.Empty<BoardPosition>(), context.Pawn.Position, false, $"Hello, my name is {context.Pawn.Name}");
        }
    }
}
