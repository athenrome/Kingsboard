using Kingsboard.Shared.Models.Board;
using KingsboardServer.Services.Interfaces;

namespace KingsboardServer.Services
{
    /// <summary>
    /// Works out where pawns can stand and the paths they can walk, from the board as it is now
    /// </summary>
    public class PawnMovementService : IPawnMovementService
    {
        // The four neighbouring columns a pawn can step to
        private static readonly (int X, int Y)[] Steps = { (1, 0), (-1, 0), (0, 1), (0, -1) };

        private readonly IConfiguration _configuration;
        private readonly IBoardService _boardService;
        private readonly ITileService _tileService;

        /// <summary>
        /// Creates the service, which reads the board through <paramref name="boardService"/>
        /// </summary>
        public PawnMovementService(IConfiguration configuration, IBoardService boardService, ITileService tileService)
        {
            _configuration = configuration;
            _boardService = boardService;
            _tileService = tileService;
        }

        /// <summary>
        /// Where a pawn would stand in a column: the space directly above its top tile. Null if the column is off the board,
        /// empty, or its top tile isn't passable. On a full stack this is one level above the board's height limit.
        /// </summary>
        public BoardPosition? GetStandPosition(BoardPosition column)
        {
            MapTile? top = _boardService.GetTopTile(column);
            if (top is null || !_tileService.IsPassable(top))
            {
                return null;
            }
            return top.Position with { Z = top.Position.Z + 1 };
        }

        /// <summary>
        /// Breadth-first search out to <paramref name="range"/> steps, so each column's path is a shortest one
        /// </summary>
        public IReadOnlyDictionary<BoardPosition, IReadOnlyList<BoardPosition>> FindReachable(BoardPosition from, int range, IReadOnlySet<BoardPosition> blocked)
        {
            int maxClimb = Math.Max(0, _configuration.GetValue("PawnSettings:MaxClimb", 1));
            range = Math.Max(0, range);

            Dictionary<BoardPosition, IReadOnlyList<BoardPosition>> paths = new Dictionary<BoardPosition, IReadOnlyList<BoardPosition>>()
            {
                [GetColumn(from)] = Array.Empty<BoardPosition>()
            };
            Queue<BoardPosition> frontier = new Queue<BoardPosition>();
            frontier.Enqueue(from);

            while (frontier.Count > 0)
            {
                BoardPosition current = frontier.Dequeue();
                IReadOnlyList<BoardPosition> pathHere = paths[GetColumn(current)];
                if (pathHere.Count >= range)
                {
                    continue;
                }

                foreach ((int dx, int dy) in Steps)
                {
                    BoardPosition column = new BoardPosition(current.X + dx, current.Y + dy, 0);
                    if (paths.ContainsKey(column) || blocked.Contains(column) || CanStep(current, column, maxClimb) is not BoardPosition next)
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
        /// Picks the reachable column nearest the target (by steps across the board, ignoring height), preferring
        /// shorter paths on a tie, and returns the path there. Staying put wins if nothing is nearer.
        /// </summary>
        public IReadOnlyList<BoardPosition> FindPathTowards(BoardPosition from, BoardPosition target, int range, IReadOnlySet<BoardPosition> blocked)
        {
            IReadOnlyDictionary<BoardPosition, IReadOnlyList<BoardPosition>> reachable = FindReachable(from, range, blocked);

            IReadOnlyList<BoardPosition> best = Array.Empty<BoardPosition>();
            int bestDistance = Distance(from, target);
            foreach (KeyValuePair<BoardPosition, IReadOnlyList<BoardPosition>> entry in reachable)
            {
                int distance = Distance(entry.Key, target);
                if (distance < bestDistance || (distance == bestDistance && entry.Value.Count < best.Count))
                {
                    best = entry.Value;
                    bestDistance = distance;
                }
            }
            return best;
        }

        /// <summary>
        /// The same position at Z = 0
        /// </summary>
        public BoardPosition GetColumn(BoardPosition position)
        {
            return position with { Z = 0 };
        }

        /// <summary>
        /// Where a pawn at <paramref name="from"/> would stand after stepping onto <paramref name="column"/>, or null if it can't:
        /// the column must be a neighbour on the board, with a passable top tile, no more than <paramref name="maxClimb"/> levels up or down
        /// </summary>
        private BoardPosition? CanStep(BoardPosition from, BoardPosition column, int maxClimb)
        {
            if (Math.Abs(column.X - from.X) + Math.Abs(column.Y - from.Y) != 1)
            {
                return null;
            }
            if (GetStandPosition(column) is not BoardPosition stand)
            {
                return null;
            }
            return Math.Abs(stand.Z - from.Z) <= maxClimb ? stand : null;
        }

        /// <summary>
        /// Steps between two columns on a 4-way grid, ignoring height
        /// </summary>
        private static int Distance(BoardPosition a, BoardPosition b)
        {
            return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
        }
    }
}
