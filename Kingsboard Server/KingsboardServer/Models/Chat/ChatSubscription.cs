using System.Threading.Channels;

namespace KingsboardServer.Models.Chat
{
    /// <summary>
    /// A player's membership in the chat, returned when they join
    /// </summary>
    public class ChatSubscription
    {
        /// <summary>Secret id the player uses to send messages</summary>
        public Guid SessionId { get; }

        /// <summary>The player's (unique) display name</summary>
        public string Username { get; }

        /// <summary>Messages for this player: recent history first, then live messages. Completes when they leave.</summary>
        public ChannelReader<ChatEntry> Messages { get; }

        public ChatSubscription(Guid sessionId, string username, ChannelReader<ChatEntry> messages)
        {
            SessionId = sessionId;
            Username = username;
            Messages = messages;
        }
    }

    public enum ChatSendStatus
    {
        Sent,
        NotJoined,
        EmptyMessage,
        MessageTooLong,
        RateLimited
    }

    public record ChatSendResult(ChatSendStatus Status, string? Error = null)
    {
        public static readonly ChatSendResult Success = new(ChatSendStatus.Sent);
    }
}
