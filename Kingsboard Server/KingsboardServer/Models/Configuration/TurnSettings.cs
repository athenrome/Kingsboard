namespace KingsboardServer.Models.Configuration
{
    /// <summary>
    /// Settings for the global turn timer. Bound from the "TurnSettings" section of appsettings.json.
    /// Changes are picked up at the start of the next turn (no restart needed).
    /// </summary>
    public class TurnSettings
    {
        public const string SectionName = "TurnSettings";

        public const double DefaultTurnDurationSeconds = 4;

        /// <summary>
        /// How long each turn lasts, in seconds. A new turn starts as soon as the previous one ends.
        /// </summary>
        public double TurnDurationSeconds { get; set; } = DefaultTurnDurationSeconds;
    }
}
