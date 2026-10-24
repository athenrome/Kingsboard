using Kingsboard.Shared.Models.Cards;
using Kingsboard.Shared.Models.Pawns;
using KingsboardServer.Models.Cards;

namespace KingsboardServer.Services.Interfaces
{
    /// <summary>
    /// Owns the action cards: the list of every kind of card, making the copies that go in decks, finding the
    /// <see cref="ICardAction"/> that carries a card out, and moving cards between a pawn's deck and hand.
    /// Holds no pawn state; the pawn service calls it (with the pawn lock held) on its own working copies of pawns.
    /// </summary>
    public interface ICardService
    {
        /// <summary>
        /// Every kind of card in the game
        /// </summary>
        IReadOnlyList<CardDefinition> GetDefinitions();

        /// <summary>
        /// The kind of card with this id, or null if there isn't one
        /// </summary>
        CardDefinition? GetDefinition(string definitionId);

        /// <summary>
        /// Makes a new deck with a fresh copy of each listed card, in the order given. Unknown ids are logged and skipped.
        /// </summary>
        IReadOnlyList<Card> CreateDeck(IEnumerable<string> definitionIds);

        /// <summary>
        /// The action that carries out <paramref name="card"/>, or null if its definition is unknown or has no action registered
        /// </summary>
        ICardAction? GetAction(Card card);

        /// <summary>
        /// Moves up to <paramref name="count"/> cards from the front of the pawn's deck to the end of its hand
        /// (fewer if the deck runs out)
        /// </summary>
        void DrawCards(Pawn pawn, int count);

        /// <summary>
        /// Puts every card in the pawn's hand back into its deck, shuffles the deck with <paramref name="random"/>,
        /// and clears the pawn's chosen card
        /// </summary>
        void ReturnHand(Pawn pawn, Random random);

        /// <summary>
        /// Shuffles the cards with <paramref name="random"/> and returns them as a new list
        /// </summary>
        IReadOnlyList<Card> Shuffle(IReadOnlyList<Card> cards, Random random);

        /// <summary>
        /// A copy of the card, so callers can't change the original
        /// </summary>
        Card Clone(Card card);
    }
}
