namespace KingsboardServer.Models.Configuration
{
    /// <summary>
    /// Settings for the chat system. Bound from the "ChatSettings" section of appsettings.json.
    /// </summary>
    public class ChatSettings
    {
        public const string SectionName = "ChatSettings";

        /// <summary>Longest message a player can send, in characters</summary>
        public int MaxMessageLength { get; set; } = 256;

        /// <summary>Longest display name, in characters (longer names are cut)</summary>
        public int MaxUsernameLength { get; set; } = 24;

        /// <summary>How many recent messages are replayed to players when they join</summary>
        public int HistorySize { get; set; } = 50;

        /// <summary>Minimum time between messages from the same player, in seconds (0 = no limit)</summary>
        public double MinSecondsBetweenMessages { get; set; } = 0.25;
    }
}
