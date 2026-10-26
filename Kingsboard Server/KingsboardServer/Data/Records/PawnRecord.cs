namespace KingsboardServer.Data.Records
{
    /// <summary>
    /// The stored form of a pawn. Its cards and planned path are stored as <see cref="CardRecord"/>s and
    /// <see cref="PawnPathStepRecord"/>s. Storage only: <c>PawnService</c> turns it into a <c>Pawn</c> and back.
    /// </summary>
    public class PawnRecord : BaseModel
    {
        /// <summary>
        /// The pawn's own id
        /// </summary>
        public Guid PawnId { get; set; }

        /// <summary>
        /// Where the pawn comes in the list of pawns, from 0. Keeps the pawns in spawn order when they are read back.
        /// </summary>
        public int Order { get; set; }

        /// <summary>
        /// Display name
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Display colour as "#RRGGBB"
        /// </summary>
        public string Colour { get; set; } = string.Empty;

        /// <summary>
        /// Column the pawn stands in
        /// </summary>
        public int PositionX { get; set; }

        /// <summary>
        /// Row the pawn stands in
        /// </summary>
        public int PositionY { get; set; }

        /// <summary>
        /// Level the pawn stands at
        /// </summary>
        public int PositionZ { get; set; }

        /// <summary>
        /// Strength attribute
        /// </summary>
        public int Strength { get; set; }

        /// <summary>
        /// Dexterity attribute
        /// </summary>
        public int Dexterity { get; set; }

        /// <summary>
        /// Intelligence attribute
        /// </summary>
        public int Intelligence { get; set; }

        /// <summary>
        /// Wisdom attribute
        /// </summary>
        public int Wisdom { get; set; }

        /// <summary>
        /// Constitution attribute
        /// </summary>
        public int Constitution { get; set; }

        /// <summary>
        /// Physique stat, as worked out when the attributes were rolled
        /// </summary>
        public int Physique { get; set; }

        /// <summary>
        /// Current health
        /// </summary>
        public int Health { get; set; }

        /// <summary>
        /// The most health the pawn can have
        /// </summary>
        public int MaxHealth { get; set; }

        /// <summary>
        /// Id of the card in the pawn's hand that it will play, or null when it has none chosen
        /// </summary>
        public Guid? SelectedCardId { get; set; }

        /// <summary>
        /// Column the chosen card is aimed at, or null when it has no target
        /// </summary>
        public int? SelectedTargetX { get; set; }

        /// <summary>
        /// Row the chosen card is aimed at, or null when it has no target
        /// </summary>
        public int? SelectedTargetY { get; set; }

        /// <summary>
        /// Level the chosen card is aimed at, or null when it has no target
        /// </summary>
        public int? SelectedTargetZ { get; set; }

        /// <summary>
        /// Public id of the player who controls the pawn, or null when no player does
        /// </summary>
        public Guid? OwnerId { get; set; }

        /// <summary>
        /// Display name of the player who controls the pawn. Empty when no player does.
        /// </summary>
        public string OwnerName { get; set; } = string.Empty;

        /// <summary>
        /// Colour ("#RRGGBB") of the player who controls the pawn. Empty when no player does.
        /// </summary>
        public string OwnerColour { get; set; } = string.Empty;

        /// <summary>
        /// Every card the pawn holds, in its deck or its hand
        /// </summary>
        public List<CardRecord> Cards { get; set; } = new();

        /// <summary>
        /// The steps of the path the pawn plans to walk this turn. Empty when it has no plan.
        /// </summary>
        public List<PawnPathStepRecord> PlannedPath { get; set; } = new();
    }
}
