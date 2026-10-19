namespace KingsboardServer.Models.Configuration
{
    /// <summary>
    /// Settings for pawns. Bound from the "PawnSettings" section of appsettings.json.
    /// Count and spawn area are picked up the next time pawns are spawned (when a game starts); movement rules apply from the next turn.
    /// </summary>
    public class PawnSettings
    {
        /// <summary>
        /// The appsettings.json section these settings are read from
        /// </summary>
        public const string SectionName = "PawnSettings";

        /// <summary>
        /// How many pawns are placed on the board when a game starts
        /// </summary>
        public int Count { get; set; } = 8;

        /// <summary>
        /// Pawns start somewhere inside this many columns and rows from the board's front-left corner
        /// (matching the default section, so they're on screen when a client first joins)
        /// </summary>
        public int SpawnAreaSize { get; set; } = 24;

        /// <summary>
        /// Most steps a pawn can take in one turn (each step is to a neighbouring column)
        /// </summary>
        public int MoveRange { get; set; } = 3;

        /// <summary>
        /// Most levels a pawn can step up or down between neighbouring columns
        /// </summary>
        public int MaxClimb { get; set; } = 1;

        /// <summary>
        /// Seed for pawn names, colours, starting places and choices, so a game can be reproduced. Leave empty for different pawns every game.
        /// </summary>
        public int? Seed { get; set; }
    }
}
