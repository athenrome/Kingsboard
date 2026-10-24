namespace KingsboardServer.Models.Configuration
{
    /// <summary>
    /// Settings for pawns. Bound from the "PawnSettings" section of appsettings.json.
    /// Count, spawn area and starting deck are picked up the next time pawns are spawned (when a game starts); movement
    /// rules and hand size apply from the next turn.
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
        /// Most levels a pawn can step up or down between neighbouring columns
        /// </summary>
        public int MaxClimb { get; set; } = 1;

        /// <summary>
        /// How many cards each pawn draws from its deck at the start of every turn (Upkeep phase)
        /// </summary>
        public int HandSize { get; set; } = 5;

        /// <summary>
        /// The card ids every pawn's deck starts with, one entry per copy (e.g. "move" three times for three Move cards).
        /// Leave empty for the default deck: three Move cards and one of each test card. Unknown ids are skipped.
        /// </summary>
        public string[] StartingDeck { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Seed for pawn names, colours, starting places and choices, so a game can be reproduced. Leave empty for different pawns every game.
        /// </summary>
        public int? Seed { get; set; }
    }
}
