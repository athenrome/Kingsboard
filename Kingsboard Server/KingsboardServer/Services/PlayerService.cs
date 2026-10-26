using System.Text;
using KingsboardServer.Events.Players;
using KingsboardServer.Models.Players;
using KingsboardServer.Services.Interfaces;

namespace KingsboardServer.Services
{
    /// <summary>
    /// Tracks the players connected to the game. Players exist only while connected, so they are kept here rather than
    /// in a repository, the same way the chat room keeps its participants.
    /// </summary>
    public class PlayerService : IPlayerService
    {
        private const string DefaultName = "Player";

        // Players are given the first of these no joined player has; once all are taken, they're reused in order
        private static readonly string[] Colours =
        {
            "#FF4136", "#0074D9", "#2ECC40", "#FFDC00", "#B10DC9", "#FF851B", "#39CCCC", "#F012BE"
        };

        private readonly Serilog.ILogger _logger;
        private readonly IConfiguration _configuration;
        private readonly TimeProvider _timeProvider;

        // Guards the players and keeps joins, leaves and their events in order.
        // PlayersChanged is raised under it, so listeners take their own locks after this one (player, then pawn).
        private readonly object _lock = new();
        private readonly List<Player> _players = new();

        /// <summary>
        /// Raised when a player joins or leaves, under the player lock, in order. Handlers must be quick and non-blocking.
        /// </summary>
        public event EventHandler<PlayersChangeEvent>? PlayersChanged;

        /// <summary>
        /// Creates the service with no players
        /// </summary>
        public PlayerService(Serilog.ILogger logger, IConfiguration configuration, TimeProvider timeProvider)
        {
            _logger = logger;
            _configuration = configuration;
            _timeProvider = timeProvider;
        }

        /// <summary>
        /// Adds a player with a cleaned, unique name, a colour, and new public and session ids, then raises <see cref="PlayersChanged"/>
        /// </summary>
        public Player Join(string requestedName)
        {
            int maxLength = Math.Max(1, _configuration.GetValue("PlayerSettings:MaxNameLength", 24));
            string baseName = CleanName(requestedName, maxLength);

            Player player;
            lock (_lock)
            {
                player = new Player()
                {
                    PublicId = Guid.NewGuid(),
                    SessionId = Guid.NewGuid(),
                    Name = MakeUnique(baseName, maxLength),
                    Colour = PickColour(),
                    JoinedAt = _timeProvider.GetUtcNow()
                };
                _players.Add(player);
                RaiseChanged();
            }

            _logger.Information("{Name} joined the game", player.Name);
            return Clone(player);
        }

        /// <summary>
        /// Removes the player with this session id and raises <see cref="PlayersChanged"/>. Does nothing if no joined player has it.
        /// </summary>
        public void Leave(Guid sessionId)
        {
            Player? player;
            lock (_lock)
            {
                player = _players.FirstOrDefault(p => p.SessionId == sessionId);
                if (player is null)
                {
                    return;
                }

                _players.Remove(player);
                RaiseChanged();
            }

            _logger.Information("{Name} left the game", player.Name);
        }

        /// <summary>
        /// Returns a snapshot of the joined player with this session id, or null if there isn't one
        /// </summary>
        public Player? GetPlayerBySession(Guid sessionId)
        {
            lock (_lock)
            {
                Player? player = _players.FirstOrDefault(p => p.SessionId == sessionId);
                return player is null ? null : Clone(player);
            }
        }

        /// <summary>
        /// Runs <paramref name="read"/> with snapshots of every joined player while no player can join or leave
        /// </summary>
        public void ReadPlayers(Action<IReadOnlyList<Player>> read)
        {
            lock (_lock)
            {
                read(_players.Select(Clone).ToArray());
            }
        }

        /// <summary>
        /// Raises <see cref="PlayersChanged"/> with snapshots. Caller must hold <see cref="_lock"/>.
        /// </summary>
        private void RaiseChanged()
        {
            PlayersChanged?.Invoke(this, new PlayersChangeEvent(_players.Select(Clone).ToArray()));
        }

        /// <summary>
        /// The first colour no joined player has, or (when every colour is taken) the next one in turn.
        /// Caller must hold <see cref="_lock"/>.
        /// </summary>
        private string PickColour()
        {
            string? free = Colours.FirstOrDefault(c => !_players.Any(p => string.Equals(p.Colour, c, StringComparison.OrdinalIgnoreCase)));
            return free ?? Colours[_players.Count % Colours.Length];
        }

        /// <summary>
        /// Adds " (2)", " (3)"... if the name is already in use, keeping it within <paramref name="maxLength"/>.
        /// Caller must hold <see cref="_lock"/>.
        /// </summary>
        private string MakeUnique(string baseName, int maxLength)
        {
            string candidate = baseName;
            for (int suffix = 2; _players.Any(p => string.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase)); suffix++)
            {
                string tag = $" ({suffix})";
                string trimmedBase = baseName.Length + tag.Length > maxLength
                    ? baseName.Substring(0, Math.Max(1, maxLength - tag.Length))
                    : baseName;
                candidate = trimmedBase + tag;
            }
            return candidate;
        }

        /// <summary>
        /// Makes a name single-line (control characters and runs of whitespace become one space), trims it, cuts it to
        /// <paramref name="maxLength"/>, and falls back to "Player" if nothing is left
        /// </summary>
        private static string CleanName(string? name, int maxLength)
        {
            StringBuilder builder = new StringBuilder(name?.Length ?? 0);
            bool lastWasSpace = false;
            foreach (char c in name ?? string.Empty)
            {
                bool isSpace = char.IsWhiteSpace(c) || char.IsControl(c);
                if (!isSpace || !lastWasSpace)
                {
                    builder.Append(isSpace ? ' ' : c);
                }
                lastWasSpace = isSpace;
            }

            string cleaned = builder.ToString().Trim();
            if (maxLength > 0 && cleaned.Length > maxLength)
            {
                cleaned = cleaned.Substring(0, maxLength).TrimEnd();
            }
            return cleaned.Length == 0 ? DefaultName : cleaned;
        }

        private static Player Clone(Player player)
        {
            return new Player()
            {
                Id = player.Id,
                PublicId = player.PublicId,
                SessionId = player.SessionId,
                Name = player.Name,
                Colour = player.Colour,
                JoinedAt = player.JoinedAt
            };
        }
    }
}
