using KingsboardServer.Models.Enumerations;

namespace KingsboardServer.Models.Chat
{
    /// <summary>
    /// A single chat message
    /// </summary>
    /// <param name="Id">Unique, increasing message number</param>
    /// <param name="Type">Whether a player or the server sent it</param>
    /// <param name="Username">Name of the sender (<see cref="Services.ChatRoomService.SystemUsername"/> for the server)</param>
    /// <param name="Text">The message text, already cleaned and length-checked</param>
    /// <param name="SentAt">When the server accepted the message (UTC)</param>
    public record ChatEntry(long Id, ChatEntryType Type, string Username, string Text, DateTimeOffset SentAt)
    {
        /// <summary>
        /// True when this is being replayed from history to a newly joined player
        /// </summary>
        public bool IsHistory { get; init; }
    }
}
