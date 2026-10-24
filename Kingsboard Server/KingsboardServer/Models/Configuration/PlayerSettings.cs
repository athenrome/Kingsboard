namespace KingsboardServer.Models.Configuration
{
    /// <summary>
    /// Settings for players. Bound from the "PlayerSettings" section of appsettings.json.
    /// </summary>
    public class PlayerSettings
    {
        /// <summary>
        /// The appsettings.json section these settings are read from
        /// </summary>
        public const string SectionName = "PlayerSettings";

        /// <summary>
        /// How many pawns each player controls. Players are given pawns that no one controls, in the order they joined;
        /// a player may get fewer if there aren't enough to go round. Applies the next time a player joins or leaves.
        /// </summary>
        public int PawnsPerPlayer { get; set; } = 2;

        /// <summary>
        /// Longest player name, in characters (longer names are cut)
        /// </summary>
        public int MaxNameLength { get; set; } = 24;
    }
}
