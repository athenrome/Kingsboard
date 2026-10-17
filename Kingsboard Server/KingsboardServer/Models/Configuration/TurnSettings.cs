namespace KingsboardServer.Models.Configuration
{
    /// <summary>
    /// Settings for the global turn timer. Bound from the "TurnSettings" section of appsettings.json.
    /// Changes are picked up at the start of the next turn (no restart needed).
    /// </summary>
    public class TurnSettings
    {
        /// <summary>
        /// The appsettings.json section these settings are read from
        /// </summary>
        public const string SectionName = "TurnSettings";

        /// <summary>
        /// Turn length used when none is configured, or the configured one is invalid
        /// </summary>
        public const double DefaultTurnDurationSeconds = 4;

        /// <summary>
        /// How long each turn lasts, in seconds. A new turn starts as soon as the previous one ends.
        /// </summary>
        public double TurnDurationSeconds { get; set; } = DefaultTurnDurationSeconds;
    }
}
