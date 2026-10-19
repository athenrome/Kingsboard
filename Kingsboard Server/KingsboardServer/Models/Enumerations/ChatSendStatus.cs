namespace KingsboardServer.Models.Enumerations
{
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
}
