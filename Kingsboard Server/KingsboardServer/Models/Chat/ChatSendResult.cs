using KingsboardServer.Models.Enumerations;

namespace KingsboardServer.Models.Chat
{
    /// <summary>
    /// The result of trying to send a chat message
    /// </summary>
    /// <param name="Status">Whether the message was sent, and if not, why</param>
    /// <param name="Error">A reason the player can be shown when the message wasn't sent, otherwise null</param>
    public record ChatSendResult(ChatSendStatus Status, string? Error = null);
}
