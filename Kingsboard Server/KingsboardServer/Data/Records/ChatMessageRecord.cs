using KingsboardServer.Models.Enumerations;

namespace KingsboardServer.Data.Records
{
    /// <summary>
    /// The stored form of one chat message.
    /// Storage only: <c>ChatRoomService</c> turns it into a <c>ChatEntry</c> and back.
    /// </summary>
    public class ChatMessageRecord
    {
        /// <summary>
        /// Unique id of the stored message
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// The message number shown to clients. Goes up by one with each message, and carries on after a restart.
        /// </summary>
        public long Sequence { get; set; }

        /// <summary>
        /// Whether a player or the server sent it
        /// </summary>
        public ChatEntryType Type { get; set; }

        /// <summary>
        /// Name of the sender
        /// </summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// The message text
        /// </summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>
        /// When the server accepted the message (UTC)
        /// </summary>
        public DateTimeOffset SentAt { get; set; }
    }
}
