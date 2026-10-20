using KingsboardServer.Models.Chat;
using KingsboardServer.Repositories.Interfaces;

namespace KingsboardServer.Repositories
{
    /// <summary>
    /// Keeps chat messages in memory. Messages are lost when the server restarts.
    /// Not thread-safe: <see cref="Services.ChatRoomService"/> calls it only under its own lock.
    /// </summary>
    public class ChatRepository : IChatRepository
    {
        private readonly List<ChatEntry> _messages = new();

        /// <summary>
        /// Stores a message after the ones already stored
        /// </summary>
        public void Add(ChatEntry entry)
        {
            _messages.Add(entry);
        }

        /// <summary>
        /// Removes the oldest messages until at most <paramref name="maxCount"/> remain. A negative count is treated as 0.
        /// </summary>
        public void TrimTo(int maxCount)
        {
            int excess = _messages.Count - Math.Max(0, maxCount);
            if (excess > 0)
            {
                _messages.RemoveRange(0, excess);
            }
        }

        /// <summary>
        /// Returns up to <paramref name="count"/> of the most recent messages, oldest first
        /// </summary>
        public IReadOnlyList<ChatEntry> GetRecent(int count)
        {
            int take = Math.Clamp(count, 0, _messages.Count);
            return _messages.GetRange(_messages.Count - take, take);
        }
    }
}
