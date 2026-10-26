using KingsboardServer.Data;
using KingsboardServer.Data.Records;
using KingsboardServer.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace KingsboardServer.Repositories
{
    /// <summary>
    /// Stores chat messages in the SQLite database through Entity Framework, so chat history outlives a server restart.
    /// Thread-safe: each call uses its own short-lived <see cref="KingsboardDbContext"/>.
    /// </summary>
    public class ChatRepository : IChatRepository
    {
        private readonly IDbContextFactory<KingsboardDbContext> _contextFactory;

        /// <summary>
        /// Creates a repository that opens database sessions from <paramref name="contextFactory"/>
        /// </summary>
        public ChatRepository(IDbContextFactory<KingsboardDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        /// <summary>
        /// Stores a message
        /// </summary>
        public void Add(ChatMessageRecord message)
        {
            using KingsboardDbContext context = _contextFactory.CreateDbContext();
            context.ChatMessages.Add(message);
            context.SaveChanges();
        }

        /// <summary>
        /// Deletes the oldest messages until at most <paramref name="maxCount"/> remain. A negative count is treated as 0.
        /// </summary>
        public void TrimTo(int maxCount)
        {
            using KingsboardDbContext context = _contextFactory.CreateDbContext();

            // The newest message that has to go, if any; it and everything older are deleted
            long? cutoff = context.ChatMessages
                .OrderByDescending(m => m.Sequence)
                .Skip(Math.Max(0, maxCount))
                .Select(m => (long?)m.Sequence)
                .FirstOrDefault();

            if (cutoff is long sequence)
            {
                context.ChatMessages.Where(m => m.Sequence <= sequence).ExecuteDelete();
            }
        }

        /// <summary>
        /// Reads up to <paramref name="count"/> of the most recent messages, oldest first
        /// </summary>
        public IReadOnlyList<ChatMessageRecord> GetRecent(int count)
        {
            using KingsboardDbContext context = _contextFactory.CreateDbContext();
            List<ChatMessageRecord> newestFirst = context.ChatMessages
                .AsNoTracking()
                .OrderByDescending(m => m.Sequence)
                .Take(Math.Max(0, count))
                .ToList();
            newestFirst.Reverse();
            return newestFirst;
        }

        /// <summary>
        /// The highest stored message number, or 0 if no messages are stored
        /// </summary>
        public long GetLatestSequence()
        {
            using KingsboardDbContext context = _contextFactory.CreateDbContext();
            return context.ChatMessages.Max(m => (long?)m.Sequence) ?? 0;
        }
    }
}
