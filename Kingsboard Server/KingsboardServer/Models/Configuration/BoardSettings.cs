namespace KingsboardServer.Models.Configuration
{
    /// <summary>
    /// Settings for the game board. Bound from the "BoardSettings" section of appsettings.json.
    /// Changes are picked up the next time a board is created (when a game starts).
    /// </summary>
    public class BoardSettings
    {
        /// <summary>
        /// The appsettings.json section these settings are read from
        /// </summary>
        public const string SectionName = "BoardSettings";

        /// <summary>
        /// Number of columns (x)
        /// </summary>
        public int Width { get; set; } = 16;

        /// <summary>
        /// Number of rows (y)
        /// </summary>
        public int Depth { get; set; } = 16;

        /// <summary>
        /// Most tiles a stack can hold (z). Generated terrain uses up to this many levels.
        /// </summary>
        public int Height { get; set; } = 6;

        /// <summary>
        /// Seed for map generation, so a map can be reproduced. Leave empty for a different map every game.
        /// </summary>
        public int? Seed { get; set; }
    }
}
