using System;
using System.Collections.Generic;
using Kingsboard.Shared.Models.Board;
using Kingsboard.Shared.Models.Cards;

namespace Kingsboard.Shared.Models.Pawns
{
    /// <summary>
    /// A character on the board that acts by playing action cards from its hand. The server decides everything it does;
    /// clients only display it.
    /// </summary>
    public class Pawn
    {
        /// <summary>
        /// Unique id for the pawn
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Display name
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Display colour as "#RRGGBB"
        /// </summary>
        public string Colour { get; set; } = "#FFFFFF";

        /// <summary>
        /// Where the pawn stands: the empty space directly above the top tile of a stack
        /// </summary>
        public BoardPosition Position { get; set; } = new BoardPosition(0, 0, 0);

        /// <summary>
        /// The pawn's core attributes (Strength, Dexterity and so on), rolled when it is spawned
        /// </summary>
        public PawnAttributes Attributes { get; set; } = new PawnAttributes();

        /// <summary>
        /// Current health, a whole number. Starts at <see cref="MaxHealth"/>.
        /// </summary>
        public int Health { get; set; }

        /// <summary>
        /// The most health the pawn can have. The server sets it from the pawn's Physique when the pawn is created.
        /// </summary>
        public int MaxHealth { get; set; }

        /// <summary>
        /// The path the pawn plans to walk this turn, each step in order and ending at its intended destination.
        /// Chosen in the Planning phase and walked (then cleared) in the Resolution phase. Empty when it has no plan.
        /// </summary>
        public IReadOnlyList<BoardPosition> PlannedPath { get; set; } = Array.Empty<BoardPosition>();

        /// <summary>
        /// The cards the pawn draws from, in draw order (the first card is drawn next). A card drawn into the
        /// <see cref="Hand"/> leaves the deck until it is returned at the end of the turn.
        /// </summary>
        public IReadOnlyList<Card> Deck { get; set; } = Array.Empty<Card>();

        /// <summary>
        /// The cards the pawn can play this turn. Drawn from the <see cref="Deck"/> in the Upkeep phase and shuffled back
        /// into it in the End phase.
        /// </summary>
        public IReadOnlyList<Card> Hand { get; set; } = Array.Empty<Card>();

        /// <summary>
        /// The card from the <see cref="Hand"/> the pawn will play in the Resolution phase, and its target.
        /// Chosen by the pawn in the Planning phase, can be changed by its owner in the Player phase, and cleared once played.
        /// Null when it has no card chosen.
        /// </summary>
        public CardSelection? SelectedCard { get; set; }

        /// <summary>
        /// Public id of the player who controls the pawn, or null when no player does and the pawn acts on its own
        /// </summary>
        public Guid? OwnerId { get; set; }

        /// <summary>
        /// Display name of the player who controls the pawn. Empty when no player does.
        /// </summary>
        public string OwnerName { get; set; } = string.Empty;
    }
}
