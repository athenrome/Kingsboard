using System;
using System.Collections.Generic;
using Kingsboard.Shared.Models.Board;

namespace Kingsboard.Shared.Models.Pawns
{
    /// <summary>
    /// A character that moves around the board on its own. The server decides where it goes; clients only display it.
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
    }
}
