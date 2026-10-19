using System.Threading.Channels;

namespace KingsboardServer.Models.Chat
{
    /// <summary>
    /// A player's membership in the chat, returned when they join
    /// </summary>
    /// <param name="SessionId">Secret id the player uses to send messages</param>
    /// <param name="Username">The player's (unique) display name</param>
    /// <param name="Messages">Messages for this player: recent history first, then live messages. Completes when they leave.</param>
    public record ChatSubscription(Guid SessionId, string Username, ChannelReader<ChatEntry> Messages);

    /// <summary>
    /// The outcome of trying to send a chat message
    /// </summary>
    public enum ChatSendStatus
    {
        /// <summary>
        /// The message was sent to everyone
        /// </summary>
        Sent,
        /// <summary>
        /// The session id isn't in the chat (never joined, or already left)
        /// </summary>
        NotJoined,
        /// <summary>
        /// The message was blank after cleaning
        /// </summary>
        EmptyMessage,
        /// <summary>
        /// The message is longer than ChatSettings:MaxMessageLength
        /// </summary>
        MessageTooLong,
        /// <summary>
        /// The player sent another message too soon (ChatSettings:MinSecondsBetweenMessages)
        /// </summary>
        RateLimited
    }

    /// <summary>
    /// The result of trying to send a chat message
    /// </summary>
    /// <param name="Status">Whether the message was sent, and if not, why</param>
    /// <param name="Error">A reason the player can be shown when the message wasn't sent, otherwise null</param>
    public record ChatSendResult(ChatSendStatus Status, string? Error = null);
}
