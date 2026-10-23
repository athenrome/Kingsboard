using KingsboardServer.Events.Pawns;
using KingsboardServer.Models;
using KingsboardServer.Models.Board;
using KingsboardServer.Models.Configuration;
using KingsboardServer.Models.Enumerations;
using KingsboardServer.Models.Pawns;
using KingsboardServer.Repositories.Interfaces;
using KingsboardServer.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace KingsboardServer.Services
{
    /// <summary>
    /// Owns the pawns and decides everything they do. Each turn, in the Planning phase every pawn picks a destination it
    /// can reach and the path there; in the Resolution phase every pawn walks its path as far as the board still allows.
    /// All pawn rules (where pawns can stand, how far and how high they can move, avoiding each other) live here.
    /// </summary>
    public class PawnService : IPawnService, ITurnPhaseHandler
    {
        private static readonly string[] Names =
        {
            "Alda", "Bram", "Cora", "Dain", "Elsa", "Finn", "Greta", "Hale",
            "Ilse", "Jory", "Kara", "Leif", "Mira", "Nils", "Oona", "Pell"
        };

        private static readonly string[] Colours =
        {
            "#E63946", "#F4A261", "#E9C46A", "#2A9D8F", "#457B9D", "#9B5DE5", "#F15BB5", "#00BBF9",
            "#FB8500", "#8AC926", "#FFFFFF", "#6D597A"
        };

        // The four neighbouring columns a pawn can step to
        private static readonly (int X, int Y)[] Steps = { (1, 0), (-1, 0), (0, 1), (0, -1) };

        private static readonly TurnPhase[] HandledPhases = { TurnPhase.Planning, TurnPhase.Resolution };

        // Every attribute is rolled as a whole number in this range, inclusive
        private const int MinAttribute = 1;
        private const int MaxAttribute = 10;

        private readonly Serilog.ILogger _logger;
        private readonly IOptionsMonitor<PawnSettings> _settings;
        private readonly IPawnRepository _repository;
        private readonly IBoardService _boardService;
        private readonly ITileService _tileService;

        // Guards every read and change of the pawns, and keeps changes and their events in order.
        // Board queries are made while holding it (pawn lock, then board lock); the board never calls back in.
        private readonly object _lock = new();
        private Random _random = new Random();

        /// <summary>
        /// Raised when pawns are spawned, plan their moves, or move. Raised under the pawn lock, in order.
        /// Handlers must be quick and non-blocking.
        /// </summary>
        public event EventHandler<PawnsChangeEvent>? PawnsChanged;

        /// <summary>
        /// Creates the service. There are no pawns until <see cref="SpawnPawns"/> is called.
        /// </summary>
        public PawnService(Serilog.ILogger logger, IOptionsMonitor<PawnSettings> settings, IPawnRepository repository, IBoardService boardService, ITileService tileService)
        {
            _logger = logger;
            _settings = settings;
            _repository = repository;
            _boardService = boardService;
            _tileService = tileService;
        }

        /// <summary>
        /// The phases pawns do work in: Planning (choose moves) and Resolution (carry them out)
        /// </summary>
        public IReadOnlyCollection<TurnPhase> Phases => HandledPhases;

        /// <summary>
        /// Returns snapshots of every pawn (empty before any are spawned)
        /// </summary>
        public IReadOnlyList<Pawn> GetPawns()
        {
            lock (_lock)
            {
                return CloneAll(_repository.GetPawns());
            }
        }

        /// <summary>
        /// Runs <paramref name="read"/> with snapshots of every pawn while no pawn can change, so a caller that starts
        /// listening inside it gets every change after the snapshot and none before it
        /// </summary>
        public void ReadPawns(Action<IReadOnlyList<Pawn>> read)
        {
            lock (_lock)
            {
                read(CloneAll(_repository.GetPawns()));
            }
        }

        /// <summary>
        /// Places PawnSettings:Count pawns on random passable columns near the board's front-left corner, replacing any
        /// earlier pawns, and raises <see cref="PawnsChanged"/>. Throws if no board has been created yet.
        /// </summary>
        public IReadOnlyList<Pawn> SpawnPawns()
        {
            PawnSettings settings = _settings.CurrentValue;
            GameBoard board = _boardService.GetBoard() ?? throw new InvalidOperationException("No board has been created yet");

            lock (_lock)
            {
                _random = settings.Seed is int seed ? new Random(seed) : new Random();

                int areaWidth = Math.Clamp(settings.SpawnAreaSize, 1, board.Width);
                int areaDepth = Math.Clamp(settings.SpawnAreaSize, 1, board.Depth);

                List<BoardPosition> candidates = new List<BoardPosition>();
                for (int y = 0; y < areaDepth; y++)
                {
                    for (int x = 0; x < areaWidth; x++)
                    {
                        if (GetStandPosition(new BoardPosition(x, y, 0)) is BoardPosition stand)
                        {
                            candidates.Add(stand);
                        }
                    }
                }

                Shuffle(candidates);
                int count = Math.Min(Math.Max(0, settings.Count), candidates.Count);
                List<Pawn> pawns = new List<Pawn>(count);
                for (int i = 0; i < count; i++)
                {
                    Pawn pawn = new Pawn()
                    {
                        Id = Guid.NewGuid(),
                        Name = i < Names.Length ? Names[i] : $"{Names[i % Names.Length]} {i / Names.Length + 1}",
                        Colour = Colours[i % Colours.Length],
                        Position = candidates[i],
                        Attributes = RollAttributes()
                    };
                    pawn.Health = GetMaxHealth(pawn);
                    pawns.Add(pawn);
                }

                if (count < settings.Count)
                {
                    _logger.Warning("Only room for {Count} of {Wanted} pawns in the spawn area", count, settings.Count);
                }

                _repository.SavePawns(pawns);
                RaiseChanged(PawnChangeReason.Spawned, 0, pawns, Array.Empty<PawnMovement>());

                _logger.Information("Spawned {Count} pawns", count);
                return CloneAll(pawns);
            }
        }

        /// <summary>
        /// The pawn's Physique stat: its Dexterity plus twice its Strength
        /// </summary>
        public int GetPhysique(Pawn pawn)
        {
            return pawn.Attributes.Dexterity + pawn.Attributes.Strength * 2;
        }

        /// <summary>
        /// The most health the pawn can have, which is its Physique
        /// </summary>
        public int GetMaxHealth(Pawn pawn)
        {
            return GetPhysique(pawn);
        }

        /// <summary>
        /// Does the pawns' work for the turn's phase: plan moves in Planning, carry them out in Resolution.
        /// Finishes all of it before returning, so the next phase only begins once every pawn has planned or moved.
        /// </summary>
        public Task RunPhaseAsync(Turn turn, CancellationToken cancellationToken)
        {
            switch (turn.CurrentPhase)
            {
                case TurnPhase.Planning:
                    PlanMoves(turn.Id);
                    break;
                case TurnPhase.Resolution:
                    ResolveMoves(turn.Id);
                    break;
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// Each pawn picks a random destination it can reach this turn (not one another pawn stands on or has already picked)
        /// and the shortest path there. A pawn with nowhere to go stays put with no plan.
        /// </summary>
        private void PlanMoves(int turnId)
        {
            PawnSettings settings = _settings.CurrentValue;

            lock (_lock)
            {
                List<Pawn> pawns = CloneAll(_repository.GetPawns()).ToList();
                if (pawns.Count == 0)
                {
                    return;
                }

                HashSet<BoardPosition> standing = pawns.Select(p => Column(p.Position)).ToHashSet();
                HashSet<BoardPosition> claimed = new HashSet<BoardPosition>();

                foreach (Pawn pawn in pawns)
                {
                    BoardPosition start = Column(pawn.Position);
                    HashSet<BoardPosition> blocked = standing.Where(c => c != start).ToHashSet();

                    Dictionary<BoardPosition, List<BoardPosition>> reachable = FindReachable(pawn.Position, settings, blocked);
                    List<BoardPosition> destinations = reachable.Keys.Where(c => c != start && !claimed.Contains(c)).ToList();

                    if (destinations.Count == 0)
                    {
                        pawn.PlannedPath = Array.Empty<BoardPosition>();
                        continue;
                    }

                    BoardPosition destination = destinations[_random.Next(destinations.Count)];
                    claimed.Add(destination);
                    pawn.PlannedPath = reachable[destination];
                }

                _repository.SavePawns(pawns);
                RaiseChanged(PawnChangeReason.Planned, turnId, pawns, Array.Empty<PawnMovement>());

                _logger.Debug("Turn {TurnId}: {Planned} of {Count} pawns planned a move", turnId, pawns.Count(p => p.PlannedPath.Count > 0), pawns.Count);
            }
        }

        /// <summary>
        /// Each pawn walks its planned path one step at a time, re-checking every step against the board as it is now
        /// (tiles may have changed since Planning) and against where the other pawns are. It stops at the first step it
        /// can no longer take. Plans are cleared afterwards.
        /// </summary>
        private void ResolveMoves(int turnId)
        {
            PawnSettings settings = _settings.CurrentValue;

            lock (_lock)
            {
                List<Pawn> pawns = CloneAll(_repository.GetPawns()).ToList();
                if (pawns.Count == 0)
                {
                    return;
                }

                Dictionary<Guid, BoardPosition> standing = pawns.ToDictionary(p => p.Id, p => Column(p.Position));
                List<PawnMovement> movements = new List<PawnMovement>(pawns.Count);

                foreach (Pawn pawn in pawns)
                {
                    // The ground under the pawn may have changed; stand on whatever is there now
                    BoardPosition current = GetStandPosition(Column(pawn.Position)) ?? pawn.Position;
                    List<BoardPosition> walked = new List<BoardPosition>();

                    foreach (BoardPosition step in pawn.PlannedPath)
                    {
                        BoardPosition column = Column(step);
                        bool occupied = standing.Any(s => s.Key != pawn.Id && s.Value == column);
                        if (occupied || CanStep(current, column, settings) is not BoardPosition next)
                        {
                            break;
                        }

                        current = next;
                        walked.Add(next);
                        standing[pawn.Id] = column;
                    }

                    bool blocked = walked.Count < pawn.PlannedPath.Count;
                    movements.Add(new PawnMovement(pawn.Id, walked, current, blocked));

                    pawn.Position = current;
                    pawn.PlannedPath = Array.Empty<BoardPosition>();
                }

                _repository.SavePawns(pawns);
                RaiseChanged(PawnChangeReason.Moved, turnId, pawns, movements);

                _logger.Debug("Turn {TurnId}: {Moved} pawns moved, {Blocked} were blocked", turnId, movements.Count(m => m.Path.Count > 0), movements.Count(m => m.Blocked));
            }
        }

        /// <summary>
        /// Every column the pawn can reach within PawnSettings:MoveRange steps (including where it stands), each with the
        /// shortest path to it: the stand positions it walks through, in order, ending at the destination.
        /// </summary>
        private Dictionary<BoardPosition, List<BoardPosition>> FindReachable(BoardPosition from, PawnSettings settings, HashSet<BoardPosition> blocked)
        {
            int range = Math.Max(0, settings.MoveRange);
            Dictionary<BoardPosition, List<BoardPosition>> paths = new Dictionary<BoardPosition, List<BoardPosition>>()
            {
                [Column(from)] = new List<BoardPosition>()
            };
            Queue<BoardPosition> frontier = new Queue<BoardPosition>();
            frontier.Enqueue(from);

            while (frontier.Count > 0)
            {
                BoardPosition current = frontier.Dequeue();
                List<BoardPosition> pathHere = paths[Column(current)];
                if (pathHere.Count >= range)
                {
                    continue;
                }

                foreach ((int dx, int dy) in Steps)
                {
                    BoardPosition column = new BoardPosition(current.X + dx, current.Y + dy, 0);
                    if (paths.ContainsKey(column) || blocked.Contains(column) || CanStep(current, column, settings) is not BoardPosition next)
                    {
                        continue;
                    }

                    paths[column] = new List<BoardPosition>(pathHere) { next };
                    frontier.Enqueue(next);
                }
            }

            return paths;
        }

        /// <summary>
        /// Where a pawn at <paramref name="from"/> would stand after stepping onto <paramref name="column"/>, or null if it can't:
        /// the column must be a neighbour on the board, with a passable top tile, no more than PawnSettings:MaxClimb levels up or down
        /// </summary>
        private BoardPosition? CanStep(BoardPosition from, BoardPosition column, PawnSettings settings)
        {
            if (Math.Abs(column.X - from.X) + Math.Abs(column.Y - from.Y) != 1)
            {
                return null;
            }
            if (GetStandPosition(column) is not BoardPosition stand)
            {
                return null;
            }
            return Math.Abs(stand.Z - from.Z) <= Math.Max(0, settings.MaxClimb) ? stand : null;
        }

        /// <summary>
        /// Where a pawn would stand in a column: the space directly above its top tile. Null if the column is off the board,
        /// empty, or its top tile isn't passable. On a full stack this is one level above the board's height limit.
        /// </summary>
        private BoardPosition? GetStandPosition(BoardPosition column)
        {
            MapTile? top = _boardService.GetTopTile(column);
            if (top is null || !_tileService.IsPassable(top))
            {
                return null;
            }
            return top.Position with { Z = top.Position.Z + 1 };
        }

        private static BoardPosition Column(BoardPosition position) => position with { Z = 0 };

        /// <summary>
        /// Rolls every attribute as a random whole number from <see cref="MinAttribute"/> to <see cref="MaxAttribute"/>.
        /// Caller must hold <see cref="_lock"/>, since it uses the shared random.
        /// </summary>
        private PawnAttributes RollAttributes()
        {
            return new PawnAttributes()
            {
                Strength = _random.Next(MinAttribute, MaxAttribute + 1),
                Dexterity = _random.Next(MinAttribute, MaxAttribute + 1),
                Intelligence = _random.Next(MinAttribute, MaxAttribute + 1),
                Wisdom = _random.Next(MinAttribute, MaxAttribute + 1),
                Constitution = _random.Next(MinAttribute, MaxAttribute + 1)
            };
        }

        private void Shuffle<T>(List<T> items)
        {
            for (int i = items.Count - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                (items[i], items[j]) = (items[j], items[i]);
            }
        }

        /// <summary>
        /// Raises <see cref="PawnsChanged"/> with snapshots, so listeners can't change the stored pawns. Caller must hold <see cref="_lock"/>.
        /// </summary>
        private void RaiseChanged(PawnChangeReason reason, int turnId, IReadOnlyList<Pawn> pawns, IReadOnlyList<PawnMovement> movements)
        {
            PawnsChanged?.Invoke(this, new PawnsChangeEvent(reason, turnId, CloneAll(pawns), movements));
        }

        /// <summary>
        /// Creates copies so callers can't change the stored pawns
        /// </summary>
        private static IReadOnlyList<Pawn> CloneAll(IReadOnlyList<Pawn> pawns)
        {
            return pawns.Select(p => new Pawn()
            {
                Id = p.Id,
                Name = p.Name,
                Colour = p.Colour,
                Position = p.Position,
                Attributes = new PawnAttributes()
                {
                    Strength = p.Attributes.Strength,
                    Dexterity = p.Attributes.Dexterity,
                    Intelligence = p.Attributes.Intelligence,
                    Wisdom = p.Attributes.Wisdom,
                    Constitution = p.Attributes.Constitution
                },
                Health = p.Health,
                PlannedPath = p.PlannedPath.ToArray()
            }).ToArray();
        }
    }
}
