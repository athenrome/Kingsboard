using Kingsboard.Shared.Models.Board;
using KingsboardServer.Models.Cards;
using KingsboardServer.Models.Enumerations;

namespace KingsboardServer.Services.Interfaces
{
    /// <summary>
    /// Carries out one kind of card action (<see cref="Action"/>). Register each implementation in Program.cs as an
    /// <see cref="ICardAction"/>; the <see cref="ICardService"/> finds the one for a card by its definition's action.
    /// Actions only work things out and report them: the pawn service applies the results.
    /// Called with the pawn lock held, so they must be quick and must not call back into the pawn service.
    /// </summary>
    public interface ICardAction
    {
        /// <summary>
        /// The action this implementation carries out
        /// </summary>
        CardActionType Action { get; }

        /// <summary>
        /// Picks a target for a pawn acting on its own. Returns false if the card can't be used right now (e.g. a move
        /// with nowhere to go); otherwise <paramref name="target"/> is the chosen target, or null for cards that need none.
        /// </summary>
        bool TryChooseTarget(CardActionContext context, out BoardPosition? target);

        /// <summary>
        /// The path the pawn expects to walk if it plays the card at <see cref="CardActionContext.Target"/>, as things stand
        /// now (each step in order). Empty for cards that don't move the pawn.
        /// </summary>
        IReadOnlyList<BoardPosition> PlanPath(CardActionContext context);

        /// <summary>
        /// Plays the card against the board and pawns as they are now, and reports what happened
        /// </summary>
        CardActionResult Resolve(CardActionContext context);
    }
}
