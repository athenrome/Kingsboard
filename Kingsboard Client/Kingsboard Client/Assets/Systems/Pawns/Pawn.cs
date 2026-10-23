using System;
using System.Collections.Generic;
using Kingsboard.Board;
using UnityEngine;

namespace Kingsboard.Pawns
{
    /// <summary>
    /// A character that moves around the board on its own, as last sent by the server.
    /// The server decides everything a pawn does; the client only displays it.
    /// </summary>
    public class Pawn
    {
        /// <summary>
        /// Unique id for the pawn
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Display name
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Display colour
        /// </summary>
        public Color Colour { get; set; } = Color.white;

        /// <summary>
        /// Where the pawn stands: the empty space directly above the top tile of a stack
        /// </summary>
        public BoardPosition Position { get; set; }

        /// <summary>
        /// The path the pawn plans to walk this turn, each step in order and ending at its intended destination.
        /// Empty when it has no plan.
        /// </summary>
        public IReadOnlyList<BoardPosition> PlannedPath { get; set; } = Array.Empty<BoardPosition>();

        /// <summary>
        /// The pawn's core attributes (each a whole number from 1 to 10) and its Physique
        /// </summary>
        public PawnAttributes Attributes { get; set; } = new PawnAttributes();

        /// <summary>
        /// Current health
        /// </summary>
        public int Health { get; set; }

        /// <summary>
        /// Most health the pawn can have (equal to its Physique)
        /// </summary>
        public int MaxHealth { get; set; }
    }

    /// <summary>
    /// A pawn's core attributes, rolled by the server when the pawn is spawned, and the Physique stat derived from them
    /// </summary>
    public class PawnAttributes
    {
        /// <summary>
        /// Physique stat (Dexterity + Strength × 2), as worked out by the server. Also the pawn's max health.
        /// </summary>
        public int Physique { get; set; }

        /// <summary>
        /// Physical power
        /// </summary>
        public int Strength { get; set; }

        /// <summary>
        /// Agility and coordination
        /// </summary>
        public int Dexterity { get; set; }

        /// <summary>
        /// Reasoning and learning
        /// </summary>
        public int Intelligence { get; set; }

        /// <summary>
        /// Awareness and judgement
        /// </summary>
        public int Wisdom { get; set; }

        /// <summary>
        /// Toughness and endurance
        /// </summary>
        public int Constitution { get; set; }
    }

    /// <summary>
    /// How one pawn moved during the Resolution phase
    /// </summary>
    public class PawnMovement
    {
        /// <summary>
        /// The pawn that moved
        /// </summary>
        public string PawnId { get; set; }

        /// <summary>
        /// Each position it moved through, in order (empty if it didn't move)
        /// </summary>
        public IReadOnlyList<BoardPosition> Path { get; set; } = Array.Empty<BoardPosition>();

        /// <summary>
        /// Where it ended up
        /// </summary>
        public BoardPosition Position { get; set; }

        /// <summary>
        /// True if it stopped short of its planned destination
        /// </summary>
        public bool Blocked { get; set; }
    }
}
