using System.Threading.Channels;
using KingsboardServer.Models.Board;
using KingsboardServer.Models.Configuration;
using KingsboardServer.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace KingsboardServer.Services
{
    /// <summary>
    /// Tracks which section of the board each client is viewing, and queues each one its section and the changes inside it.
    /// Every section snapshot is taken inside <see cref="IBoardService.ReadSection"/>, and board events arrive under the same
    /// board lock, so each client's queue is always in version order: a section, then exactly the changes after it.
    /// </summary>
    public class BoardViewService : IBoardViewService
    {
        // Per-client queue of board events. If a client falls this far behind, its queued events are thrown away
        // and it is sent a fresh section instead, so it never misses a change.
        private const int OutboxCapacity = 256;

        private readonly Serilog.ILogger _logger;
        private readonly IOptionsMonitor<BoardSettings> _settings;
        private readonly IBoardService _boardService;

        // Guards the viewers. Always taken inside the board lock (never the other way round), and everything inside it is non-blocking.
        private readonly object _lock = new();
        private readonly Dictionary<Guid, Viewer> _viewers = new();

        /// <summary>
        /// Creates the service and starts listening for board changes
        /// </summary>
        public BoardViewService(Serilog.ILogger logger, IOptionsMonitor<BoardSettings> settings, IBoardService boardService)
        {
            _logger = logger;
            _settings = settings;
            _boardService = boardService;

            _boardService.BoardCreated += OnBoardCreated;
            _boardService.TilesChanged += OnTilesChanged;
        }

        /// <summary>
        /// Largest width or depth of a section a client may view at once (BoardSettings:MaxSectionSize)
        /// </summary>
        public int MaxSectionSize => Math.Max(1, _settings.CurrentValue.MaxSectionSize);

        /// <summary>
        /// Width and depth of the default section (BoardSettings:DefaultSectionSize, capped at <see cref="MaxSectionSize"/>)
        /// </summary>
        public int DefaultSectionSize => Math.Clamp(_settings.CurrentValue.DefaultSectionSize, 1, MaxSectionSize);

        /// <summary>
        /// The section a client views when it doesn't ask for a particular one: the board's front-left corner
        /// </summary>
        public BoardRegion DefaultRegion => new BoardRegion(new BoardPosition(0, 0, 0), DefaultSectionSize, DefaultSectionSize);

        /// <summary>
        /// Adds a client viewing <paramref name="region"/> and queues its section (once a board exists).
        /// Throws if the region is smaller than 1 x 1 or bigger than <see cref="MaxSectionSize"/>.
        /// </summary>
        public BoardViewSubscription Join(BoardRegion region)
        {
            RequireAllowedRegion(region);

            Channel<BoardViewEvent> outbox = Channel.CreateBounded<BoardViewEvent>(new BoundedChannelOptions(OutboxCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait, // TryWrite fails when full, which triggers a resync
                SingleReader = false, // The resync drains the queue while the client's stream is reading it
                SingleWriter = false
            });
            Viewer viewer = new Viewer(Guid.NewGuid(), region, outbox);

            // Register inside ReadSection so no change can fall between the snapshot and the first change sent
            _boardService.ReadSection(region, section =>
            {
                lock (_lock)
                {
                    _viewers[viewer.SessionId] = viewer;
                    if (section is not null)
                    {
                        QueueSectionLocked(viewer, section);
                    }
                }
            });

            _logger.Information("Board viewer {SessionId} joined viewing {Region}", viewer.SessionId, Describe(region));
            return new BoardViewSubscription(viewer.SessionId, outbox.Reader);
        }

        /// <summary>
        /// Moves a client to a different section and queues the new section. Returns false if the session isn't joined.
        /// Throws if the region is smaller than 1 x 1 or bigger than <see cref="MaxSectionSize"/>.
        /// </summary>
        public bool SetRegion(Guid sessionId, BoardRegion region)
        {
            RequireAllowedRegion(region);

            bool found = false;
            _boardService.ReadSection(region, section =>
            {
                lock (_lock)
                {
                    if (!_viewers.TryGetValue(sessionId, out Viewer? viewer))
                    {
                        return;
                    }

                    found = true;
                    viewer.RequestedRegion = region;
                    if (section is not null)
                    {
                        QueueSectionLocked(viewer, section);
                    }
                }
            });

            if (found)
            {
                _logger.Debug("Board viewer {SessionId} moved to {Region}", sessionId, Describe(region));
            }
            return found;
        }

        /// <summary>
        /// Removes a client and completes its events. Safe to call more than once.
        /// </summary>
        public void Leave(Guid sessionId)
        {
            lock (_lock)
            {
                if (!_viewers.Remove(sessionId, out Viewer? viewer))
                {
                    return;
                }
                viewer.Outbox.Writer.TryComplete();
            }

            _logger.Information("Board viewer {SessionId} left", sessionId);
        }

        /// <summary>
        /// Returns a one-off snapshot of a section, or null if no board has been created yet.
        /// Throws if the region is smaller than 1 x 1 or bigger than <see cref="MaxSectionSize"/>.
        /// </summary>
        public BoardSection? GetSection(BoardRegion region)
        {
            RequireAllowedRegion(region);
            return _boardService.GetSection(region);
        }

        /// <summary>
        /// A new board replaces everything: send every viewer its section of the new board. Runs inside the board lock.
        /// </summary>
        private void OnBoardCreated(object? sender, GameBoard board)
        {
            lock (_lock)
            {
                foreach (Viewer viewer in _viewers.Values)
                {
                    BoardSection? section = _boardService.GetSection(viewer.RequestedRegion);
                    if (section is not null)
                    {
                        QueueSectionLocked(viewer, section);
                    }
                }
            }
        }

        /// <summary>
        /// Sends each viewer just the part of the change inside its current section. Runs inside the board lock.
        /// </summary>
        private void OnTilesChanged(object? sender, TilesChange change)
        {
            lock (_lock)
            {
                foreach (Viewer viewer in _viewers.Values)
                {
                    // Before a viewer has a section there's nothing for these changes to apply to
                    if (viewer.SentRegion is not BoardRegion region)
                    {
                        continue;
                    }

                    MapTile[] placed = change.Placed.Where(t => IsInRegion(region, t.Position)).ToArray();
                    BoardPosition[] removed = change.Removed.Where(p => IsInRegion(region, p)).ToArray();
                    if (placed.Length > 0 || removed.Length > 0)
                    {
                        Queue(viewer, new BoardViewEvent(null, change with { Placed = placed, Removed = removed }));
                    }
                }
            }
        }

        /// <summary>
        /// Queues a section for a viewer and remembers which region it covers. Caller must hold <see cref="_lock"/>.
        /// </summary>
        private void QueueSectionLocked(Viewer viewer, BoardSection section)
        {
            viewer.SentRegion = section.Region;
            Queue(viewer, new BoardViewEvent(section, null));
        }

        /// <summary>
        /// Queues an event for a viewer. If its queue is full it has fallen too far behind: throw the backlog away and
        /// queue a fresh section instead, so it never misses a change. Caller must hold <see cref="_lock"/> (inside the board lock).
        /// </summary>
        private void Queue(Viewer viewer, BoardViewEvent viewEvent)
        {
            if (viewer.Outbox.Writer.TryWrite(viewEvent))
            {
                return;
            }

            while (viewer.Outbox.Reader.TryRead(out _)) { }

            BoardSection? fresh = _boardService.GetSection(viewer.RequestedRegion);
            if (fresh is not null)
            {
                viewer.SentRegion = fresh.Region;
                viewer.Outbox.Writer.TryWrite(new BoardViewEvent(fresh, null));
            }

            _logger.Warning("Board viewer {SessionId} fell behind; resending its section", viewer.SessionId);
        }

        private void RequireAllowedRegion(BoardRegion region)
        {
            int max = MaxSectionSize;
            if (region.Width < 1 || region.Depth < 1 || region.Width > max || region.Depth > max)
            {
                throw new ArgumentOutOfRangeException(nameof(region), $"A section must be between 1 x 1 and {max} x {max}, not {region.Width} x {region.Depth}");
            }
        }

        private static bool IsInRegion(BoardRegion region, BoardPosition position)
        {
            return position.X >= region.Origin.X && position.X < region.Origin.X + region.Width
                && position.Y >= region.Origin.Y && position.Y < region.Origin.Y + region.Depth;
        }

        private static string Describe(BoardRegion region) => $"{region.Width} x {region.Depth} at ({region.Origin.X}, {region.Origin.Y})";

        /// <summary>
        /// One client viewing the board
        /// </summary>
        private sealed class Viewer
        {
            public Viewer(Guid sessionId, BoardRegion requestedRegion, Channel<BoardViewEvent> outbox)
            {
                SessionId = sessionId;
                RequestedRegion = requestedRegion;
                Outbox = outbox;
            }

            public Guid SessionId { get; }

            /// <summary>
            /// The region the client asked for (re-fitted to the board whenever a section is taken)
            /// </summary>
            public BoardRegion RequestedRegion { get; set; }

            /// <summary>
            /// The region of the last section queued for the client, or null before it has one. Changes are filtered to this.
            /// </summary>
            public BoardRegion? SentRegion { get; set; }

            public Channel<BoardViewEvent> Outbox { get; }
        }
    }
}
