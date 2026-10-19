namespace KingsboardServer.Models.Configuration
{
    /// <summary>
    /// Settings for the game board. Bound from the "BoardSettings" section of appsettings.json.
    /// Board size and seed are picked up the next time a board is created (when a game starts);
    /// section sizes apply to the next section a client asks for.
    /// </summary>
    public class BoardSettings
    {
        /// <summary>
        /// The appsettings.json section these settings are read from
        /// </summary>
        public const string SectionName = "BoardSettings";

        /// <summary>
        /// Number of columns (x) on the whole board
        /// </summary>
        public int Width { get; set; } = 128;

        /// <summary>
        /// Number of rows (y) on the whole board
        /// </summary>
        public int Depth { get; set; } = 128;

        /// <summary>
        /// Most tiles a stack can hold (z). Generated terrain uses up to this many levels.
        /// </summary>
        public int Height { get; set; } = 8;

        /// <summary>
        /// Width and depth of the section a client views when it doesn't ask for a particular one
        /// </summary>
        public int DefaultSectionSize { get; set; } = 24;

        /// <summary>
        /// Largest width or depth of a section a client may view at once
        /// </summary>
        public int MaxSectionSize { get; set; } = 64;

        /// <summary>
        /// Seed for map generation, so a map can be reproduced. Leave empty for a different map every game.
        /// </summary>
        public int? Seed { get; set; }
    }
}
