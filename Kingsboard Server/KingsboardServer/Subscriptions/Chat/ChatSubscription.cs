using System.Threading.Channels;
using KingsboardServer.Models.Chat;

namespace KingsboardServer.Subscriptions.Chat
{
    /// <summary>
    /// A player's membership in the chat, returned when they join
    /// </summary>
    /// <param name="SessionId">Secret id the player uses to send messages</param>
    /// <param name="Username">The player's (unique) display name</param>
    /// <param name="Messages">Messages for this player: recent history first, then live messages. Completes when they leave.</param>
    public record ChatSubscription(Guid SessionId, string Username, ChannelReader<ChatEntry> Messages);
}
