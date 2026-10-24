using Kingsboard.Shared.Models.Board;
using Kingsboard.Shared.Models.Pawns;

namespace KingsboardServer.Models.Cards
{
    /// <summary>
    /// Everything a card action needs to know to choose a target, plan, or play the card for one pawn
    /// </summary>
    public class CardActionContext
    {
        /// <summary>
        /// The pawn playing the card (a working copy; the action never changes it)
        /// </summary>
        public Pawn Pawn { get; set; } = new Pawn();

        /// <summary>
        /// Where the card is aimed, for cards that need a tile; null otherwise
        /// </summary>
        public BoardPosition? Target { get; set; }

        /// <summary>
        /// Columns (Z = 0) other pawns stand on, which this pawn can't move through or onto
        /// </summary>
        public IReadOnlySet<BoardPosition> Occupied { get; set; } = new HashSet<BoardPosition>();

        /// <summary>
        /// Columns (Z = 0) other pawns have already chosen to move to this turn. Only used when choosing a target,
        /// so pawns acting on their own don't all head for the same place.
        /// </summary>
        public IReadOnlySet<BoardPosition> Claimed { get; set; } = new HashSet<BoardPosition>();

        /// <summary>
        /// Random numbers for the action, shared with the pawn service so a seeded game plays out the same way.
        /// Only use it while the pawn lock is held.
        /// </summary>
        public Random Random { get; set; } = new Random();
    }
}
