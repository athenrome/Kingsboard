using KingsboardServer.Models.Chat;

namespace KingsboardServer.Repositories.Interfaces
{
    /// <summary>
    /// Stores chat messages. All access to stored chat data goes through here.
    /// </summary>
    public interface IChatRepository
    {
        /// <summary>
        /// Stores a new message and returns it with its id assigned
        /// </summary>
        ChatEntry Add(ChatEntryType type, string username, string text, DateTimeOffset sentAt);

        /// <summary>
        /// Returns the most recent messages, oldest first
        /// </summary>
        IReadOnlyList<ChatEntry> GetRecent(int count);
    }
}
