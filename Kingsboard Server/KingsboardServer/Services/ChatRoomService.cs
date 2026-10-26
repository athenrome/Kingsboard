using System.Text;
using System.Threading.Channels;
using KingsboardServer.Data.Records;
using KingsboardServer.Models.Enumerations;
using KingsboardServer.Subscriptions.Chat;
using KingsboardServer.Models.Chat;
using KingsboardServer.Models.Configuration;
using KingsboardServer.Repositories.Interfaces;
using KingsboardServer.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace KingsboardServer.Services
{
    /// <summary>
    /// The global chat room: tracks who is in the chat, enforces chat rules and broadcasts messages
    /// </summary>
    public class ChatRoomService : IChatRoomService
    {
        /// <summary>
        /// The name shown on server announcements
        /// </summary>
        public const string SystemUsername = "Server";

        private const string DefaultUsername = "Player";

        // Extra room in each player's queue on top of the replayed history. If a player falls this far
        // behind, their oldest messages are dropped rather than holding up everyone else.
        private const int OutboxHeadroom = 64;

        private readonly Serilog.ILogger _logger;
        private readonly IOptionsMonitor<ChatSettings> _settings;
        private readonly TimeProvider _timeProvider;
        private readonly IChatRepository _repository;

        // One lock keeps joins, leaves, storing and broadcasts in a consistent order,
        // so a joining player never misses or duplicates a message. Everything inside it is non-blocking.
        private readonly object _lock = new();
        private readonly Dictionary<Guid, Participant> _participants = new();
        // Null until the first message is sent, when it's read from the stored history
        private long? _nextMessageId;

        /// <summary>
        /// Creates an empty chat room that stores its messages in <paramref name="repository"/>
        /// </summary>
        public ChatRoomService(Serilog.ILogger logger, IOptionsMonitor<ChatSettings> settings, TimeProvider timeProvider, IChatRepository repository)
        {
            _logger = logger;
            _settings = settings;
            _timeProvider = timeProvider;
            _repository = repository;
        }

        /// <summary>
        /// Adds a player with a cleaned, unique name, queues recent history for them and announces that they joined
        /// </summary>
        public ChatSubscription Join(string requestedUsername)
        {
            ChatSettings settings = _settings.CurrentValue;
            string baseName = CleanUsername(requestedUsername, settings.MaxUsernameLength);

            Channel<ChatEntry> outbox = Channel.CreateBounded<ChatEntry>(new BoundedChannelOptions(Math.Max(0, settings.HistorySize) + OutboxHeadroom)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });

            Participant participant;
            lock (_lock)
            {
                string username = MakeUnique(baseName, settings.MaxUsernameLength);
                participant = new Participant(Guid.NewGuid(), username, outbox);

                foreach (ChatMessageRecord record in _repository.GetRecent(settings.HistorySize))
                {
                    outbox.Writer.TryWrite(FromRecord(record) with { IsHistory = true });
                }

                _participants.Add(participant.SessionId, participant);
                BroadcastLocked(ChatEntryType.System, SystemUsername, $"{username} joined the chat");
            }

            _logger.Information("{Username} joined the chat", participant.Username);
            return new ChatSubscription(participant.SessionId, participant.Username, outbox.Reader);
        }

        /// <summary>
        /// Removes a player, completes their message stream and announces that they left. Safe to call more than once.
        /// </summary>
        public void Leave(Guid sessionId)
        {
            Participant? participant;
            lock (_lock)
            {
                if (!_participants.Remove(sessionId, out participant))
                {
                    return;
                }

                participant.Outbox.Writer.TryComplete();
                BroadcastLocked(ChatEntryType.System, SystemUsername, $"{participant.Username} left the chat");
            }

            _logger.Information("{Username} left the chat", participant.Username);
        }

        /// <summary>
        /// Validates a player's message (length, membership, rate limit) and broadcasts it if it passes
        /// </summary>
        public ChatSendResult SendMessage(Guid sessionId, string text)
        {
            ChatSettings settings = _settings.CurrentValue;
            string cleaned = CleanText(text);

            if (cleaned.Length == 0)
            {
                return new ChatSendResult(ChatSendStatus.EmptyMessage, "Message is empty");
            }

            if (cleaned.Length > settings.MaxMessageLength)
            {
                return new ChatSendResult(ChatSendStatus.MessageTooLong, $"Message is too long (max {settings.MaxMessageLength} characters)");
            }

            lock (_lock)
            {
                if (!_participants.TryGetValue(sessionId, out Participant? participant))
                {
                    return new ChatSendResult(ChatSendStatus.NotJoined, "You are not in the chat. Join before sending messages.");
                }

                DateTimeOffset now = _timeProvider.GetUtcNow();
                TimeSpan minInterval = TimeSpan.FromSeconds(Math.Max(0, settings.MinSecondsBetweenMessages));
                if (participant.LastMessageAt is DateTimeOffset last && now - last < minInterval)
                {
                    return new ChatSendResult(ChatSendStatus.RateLimited, "You're sending messages too quickly");
                }

                participant.LastMessageAt = now;
                BroadcastLocked(ChatEntryType.Player, participant.Username, cleaned);
            }

            return new ChatSendResult(ChatSendStatus.Sent);
        }

        /// <summary>
        /// Broadcasts a server announcement to everyone. Blank announcements are ignored.
        /// </summary>
        public void SendSystemMessage(string text)
        {
            string cleaned = CleanText(text);
            if (cleaned.Length == 0)
            {
                return;
            }

            lock (_lock)
            {
                BroadcastLocked(ChatEntryType.System, SystemUsername, cleaned);
            }
        }

        /// <summary>
        /// Stores a message with the next id, drops the oldest stored messages beyond ChatSettings:MaxStoredMessages,
        /// and queues it for every participant. Caller must hold <see cref="_lock"/>.
        /// </summary>
        private void BroadcastLocked(ChatEntryType type, string username, string text)
        {
            // Message numbers carry on from the stored history, so they keep going up across restarts
            _nextMessageId ??= _repository.GetLatestSequence() + 1;

            ChatEntry entry = new ChatEntry(_nextMessageId.Value, type, username, text, _timeProvider.GetUtcNow());
            _repository.Add(ToRecord(entry));
            _nextMessageId++;
            _repository.TrimTo(Math.Max(1, _settings.CurrentValue.MaxStoredMessages));

            foreach (Participant participant in _participants.Values)
            {
                participant.Outbox.Writer.TryWrite(entry);
            }

            _logger.Debug("[Chat] {Username}: {Text}", username, text);
        }

        /// <summary>
        /// Turns a message into the record it's stored as, with a new record id
        /// </summary>
        private static ChatMessageRecord ToRecord(ChatEntry entry)
        {
            return new ChatMessageRecord()
            {
                Id = Guid.NewGuid(),
                Sequence = entry.Id,
                Type = entry.Type,
                Username = entry.Username,
                Text = entry.Text,
                SentAt = entry.SentAt
            };
        }

        /// <summary>
        /// Turns a stored message back into a message
        /// </summary>
        private static ChatEntry FromRecord(ChatMessageRecord record)
        {
            return new ChatEntry(record.Sequence, record.Type, record.Username, record.Text, record.SentAt);
        }

        /// <summary>
        /// Adds " (2)", " (3)"... if the name is already in use. Caller must hold <see cref="_lock"/>.
        /// </summary>
        private string MakeUnique(string baseName, int maxLength)
        {
            string candidate = baseName;
            for (int suffix = 2; IsNameTaken(candidate); suffix++)
            {
                string tag = $" ({suffix})";
                string trimmedBase = baseName.Length + tag.Length > maxLength
                    ? baseName.Substring(0, Math.Max(1, maxLength - tag.Length))
                    : baseName;
                candidate = trimmedBase + tag;
            }
            return candidate;
        }

        private bool IsNameTaken(string name)
        {
            if (string.Equals(name, SystemUsername, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (Participant participant in _participants.Values)
            {
                if (string.Equals(participant.Username, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static string CleanUsername(string? username, int maxLength)
        {
            string cleaned = CleanText(username);
            if (maxLength > 0 && cleaned.Length > maxLength)
            {
                cleaned = cleaned.Substring(0, maxLength).TrimEnd();
            }
            return cleaned.Length == 0 ? DefaultUsername : cleaned;
        }

        /// <summary>
        /// Makes text single-line: control characters (including newlines) become spaces, runs of whitespace collapse, ends are trimmed
        /// </summary>
        private static string CleanText(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(text.Length);
            bool lastWasSpace = false;
            foreach (char c in text)
            {
                bool isSpace = char.IsWhiteSpace(c) || char.IsControl(c);
                if (isSpace)
                {
                    if (!lastWasSpace)
                    {
                        builder.Append(' ');
                    }
                }
                else
                {
                    builder.Append(c);
                }
                lastWasSpace = isSpace;
            }
            return builder.ToString().Trim();
        }

        private class Participant
        {
            public Guid SessionId { get; }
            public string Username { get; }
            public Channel<ChatEntry> Outbox { get; }
            public DateTimeOffset? LastMessageAt { get; set; }

            public Participant(Guid sessionId, string username, Channel<ChatEntry> outbox)
            {
                SessionId = sessionId;
                Username = username;
                Outbox = outbox;
            }
        }
    }
}
