namespace KingsboardServer.Models.Pawns
{
    /// <summary>
    /// A pawn's core attributes, each a whole number from 1 to 10 rolled at random when the pawn is spawned,
    /// plus the Physique stat derived from them.
    /// </summary>
    public class PawnAttributes
    {
        /// <summary>
        /// Physical power. Counts double towards Physique.
        /// </summary>
        public int Strength { get; set; }

        /// <summary>
        /// Agility and coordination. Counts towards Physique.
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

        /// <summary>
        /// Physique stat: Dexterity + (Strength × 2). Set by PawnService when the attributes are rolled; it is also the pawn's max health.
        /// </summary>
        public int Physique { get; set; }
    }
}
