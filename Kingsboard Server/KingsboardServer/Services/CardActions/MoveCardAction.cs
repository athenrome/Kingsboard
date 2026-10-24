using Kingsboard.Shared.Models.Board;
using KingsboardServer.Models.Cards;
using KingsboardServer.Models.Enumerations;
using KingsboardServer.Services.Interfaces;

namespace KingsboardServer.Services.CardActions
{
    /// <summary>
    /// The Move card: the pawn walks towards a target tile, up to as many steps as its Dexterity. If it can't reach the
    /// target it stops at the reachable spot nearest to it. The path is worked out again when the card is played, against
    /// the board and the other pawns as they are then.
    /// </summary>
    public class MoveCardAction : ICardAction
    {
        private readonly IPawnMovementService _movement;

        /// <summary>
        /// Creates the action, which walks pawns using <paramref name="movement"/>'s rules
        /// </summary>
        public MoveCardAction(IPawnMovementService movement)
        {
            _movement = movement;
        }

        /// <summary>
        /// <see cref="CardActionType.Move"/>
        /// </summary>
        public CardActionType Action => CardActionType.Move;

        /// <summary>
        /// Picks a random column the pawn can reach this turn that no other pawn stands on or has claimed. Returns false
        /// if there is none. The target is the stand position there.
        /// </summary>
        public bool TryChooseTarget(CardActionContext context, out BoardPosition? target)
        {
            BoardPosition start = _movement.GetColumn(context.Pawn.Position);
            IReadOnlyDictionary<BoardPosition, IReadOnlyList<BoardPosition>> reachable = _movement.FindReachable(context.Pawn.Position, GetRange(context), context.Occupied);
            List<BoardPosition> destinations = reachable.Keys.Where(c => c != start && !context.Claimed.Contains(c)).ToList();

            if (destinations.Count == 0)
            {
                target = null;
                return false;
            }

            BoardPosition destination = destinations[context.Random.Next(destinations.Count)];
            target = reachable[destination][^1];
            return true;
        }

        /// <summary>
        /// The path towards the target as things stand now; empty without a target
        /// </summary>
        public IReadOnlyList<BoardPosition> PlanPath(CardActionContext context)
        {
            if (context.Target is not BoardPosition target)
            {
                return Array.Empty<BoardPosition>();
            }
            return _movement.FindPathTowards(context.Pawn.Position, target, GetRange(context), context.Occupied);
        }

        /// <summary>
        /// Walks towards the target. The ground under the pawn may have changed since it last moved, so it starts from
        /// wherever it would stand in its column now. Blocked if it ends anywhere but the target's column.
        /// </summary>
        public CardActionResult Resolve(CardActionContext context)
        {
            BoardPosition start = _movement.GetStandPosition(context.Pawn.Position) ?? context.Pawn.Position;
            if (context.Target is not BoardPosition target)
            {
                return new CardActionResult(Array.Empty<BoardPosition>(), start, false, null);
            }

            IReadOnlyList<BoardPosition> path = _movement.FindPathTowards(start, target, GetRange(context), context.Occupied);
            BoardPosition end = path.Count > 0 ? path[^1] : start;
            bool blocked = _movement.GetColumn(end) != _movement.GetColumn(target);
            return new CardActionResult(path, end, blocked, null);
        }

        /// <summary>
        /// Steps the pawn can take: its Dexterity
        /// </summary>
        private static int GetRange(CardActionContext context)
        {
            return Math.Max(0, context.Pawn.Attributes.Dexterity);
        }
    }
}
