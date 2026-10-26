using KingsboardServer.Data.Records;

namespace KingsboardServer.Repositories.Interfaces
{
    /// <summary>
    /// Stores chat messages in the database. All access to stored chat data goes through here.
    /// </summary>
    public interface IChatRepository
    {
        /// <summary>
        /// Stores a message
        /// </summary>
        void Add(ChatMessageRecord message);

        /// <summary>
        /// Deletes the oldest messages (lowest <see cref="ChatMessageRecord.Sequence"/>) until at most
        /// <paramref name="maxCount"/> remain
        /// </summary>
        void TrimTo(int maxCount);

        /// <summary>
        /// Reads the most recent messages, oldest first
        /// </summary>
        IReadOnlyList<ChatMessageRecord> GetRecent(int count);

        /// <summary>
        /// The highest stored message number, or 0 if no messages are stored
        /// </summary>
        long GetLatestSequence();
    }
}
