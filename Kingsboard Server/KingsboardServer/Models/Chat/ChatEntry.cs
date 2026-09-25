namespace KingsboardServer.Models.Chat
{
    public enum ChatEntryType
    {
        Player,
        System
    }

    /// <summary>
    /// A single chat message
    /// </summary>
    public record ChatEntry(long Id, ChatEntryType Type, string Username, string Text, DateTimeOffset SentAt)
    {
        /// <summary>True when this is being replayed from history to a newly joined player</summary>
        public bool IsHistory { get; init; }
    }
}
