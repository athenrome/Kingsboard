using System;
using Kingsboard.Shared.Models.Board;

namespace Kingsboard.Shared.Models.Cards
{
    /// <summary>
    /// The card a pawn will play in the Resolution phase, and what it is aimed at
    /// </summary>
    /// <param name="CardId">The id of the chosen card, which is in the pawn's hand</param>
    /// <param name="Target">Where the card is aimed, for cards that need a tile; null for cards that need no target</param>
    public record CardSelection(Guid CardId, BoardPosition? Target);
}
