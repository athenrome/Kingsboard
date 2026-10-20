using KingsboardServer.Models.Chat;

namespace KingsboardServer.Repositories.Interfaces
{
    /// <summary>
    /// Stores chat messages. All access to stored chat data goes through here.
    /// </summary>
    public interface IChatRepository
    {
        /// <summary>
        /// Stores a message after the ones already stored
        /// </summary>
        void Add(ChatEntry entry);

        /// <summary>
        /// Removes the oldest messages until at most <paramref name="maxCount"/> remain
        /// </summary>
        void TrimTo(int maxCount);

        /// <summary>
        /// Returns the most recent messages, oldest first
        /// </summary>
        IReadOnlyList<ChatEntry> GetRecent(int count);
    }
}
