using KingsboardServer.Models.Chat;
using KingsboardServer.Models.Configuration;
using KingsboardServer.Repositories.Interfaces;
using Microsoft.Extensions.Options;

namespace KingsboardServer.Repositories
{
    /// <summary>
    /// Keeps chat messages in memory. Messages are lost when the server restarts.
    /// </summary>
    public class ChatRepository : IChatRepository
    {
        private readonly IOptionsMonitor<ChatSettings> _settings;
        private readonly object _lock = new();
        private readonly List<ChatEntry> _messages = new();
        private long _nextId = 1;

        /// <summary>
        /// Creates an empty message store, capped by ChatSettings:MaxStoredMessages
        /// </summary>
        public ChatRepository(IOptionsMonitor<ChatSettings> settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// Stores a new message with the next id, dropping the oldest messages once the store is full
        /// </summary>
        public ChatEntry Add(ChatEntryType type, string username, string text, DateTimeOffset sentAt)
        {
            lock (_lock)
            {
                ChatEntry entry = new ChatEntry(_nextId++, type, username, text, sentAt);
                _messages.Add(entry);

                // Drop the oldest messages once the store is full
                int max = Math.Max(1, _settings.CurrentValue.MaxStoredMessages);
                if (_messages.Count > max)
                {
                    _messages.RemoveRange(0, _messages.Count - max);
                }

                return entry;
            }
        }

        /// <summary>
        /// Returns up to <paramref name="count"/> of the most recent messages, oldest first
        /// </summary>
        public IReadOnlyList<ChatEntry> GetRecent(int count)
        {
            lock (_lock)
            {
                int take = Math.Clamp(count, 0, _messages.Count);
                return _messages.GetRange(_messages.Count - take, take);
            }
        }
    }
}
