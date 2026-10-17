using System;

namespace Kingsboard.Chat
{
    /// <summary>
    /// One line shown in the chat window
    /// </summary>
    public class ChatLine
    {
        /// <summary>True for server announcements and local notices, false for messages from players</summary>
        public bool IsSystem { get; set; }

        /// <summary>True when the message was sent before we joined</summary>
        public bool IsHistory { get; set; }

        /// <summary>Name of the player who sent the message (empty for system lines)</summary>
        public string Username { get; set; }

        /// <summary>The message text</summary>
        public string Text { get; set; }

        /// <summary>When the message was sent, in local time</summary>
        public DateTime SentAt { get; set; }
    }
}
