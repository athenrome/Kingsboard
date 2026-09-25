using KingsboardServer.Models.Chat;

namespace KingsboardServer.Services.Interfaces
{
    /// <summary>
    /// The global chat room: tracks who is in the chat and broadcasts messages to them
    /// </summary>
    public interface IChatRoomService
    {
        /// <summary>
        /// Adds a player to the chat. Their name is cleaned up and made unique.
        /// The returned subscription's <see cref="ChatSubscription.Messages"/> starts with recent history.
        /// </summary>
        ChatSubscription Join(string requestedUsername);

        /// <summary>
        /// Removes a player from the chat and completes their message stream. Safe to call more than once.
        /// </summary>
        void Leave(Guid sessionId);

        /// <summary>
        /// Sends a message from a player to everyone in the chat
        /// </summary>
        ChatSendResult SendMessage(Guid sessionId, string text);

        /// <summary>
        /// Sends a server announcement to everyone in the chat
        /// </summary>
        void SendSystemMessage(string text);
    }
}
