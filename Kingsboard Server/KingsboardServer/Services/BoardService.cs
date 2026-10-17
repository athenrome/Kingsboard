using KingsboardServer.Extensions;
using KingsboardServer.Models.Board;
using KingsboardServer.Models.Configuration;
using KingsboardServer.Models.Enumerations;
using KingsboardServer.Repositories.Interfaces;
using KingsboardServer.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace KingsboardServer.Services
{
    /// <summary>
    /// Generates, stores and changes the game board, and tells listeners about every change
    /// </summary>
    public class BoardService : IBoardService
    {
        // Passes of neighbour-averaging applied to the random heightmap. More passes = larger, smoother regions.
        private const int SmoothingPasses = 3;

        private readonly Serilog.ILogger _logger;
        private readonly IOptionsMonitor<BoardSettings> _settings;
        private readonly IBoardRepository _repository;

        // Keeps changes and their events in the same order, so clients can apply them as they arrive.
        // Everything inside it is non-blocking.
        private readonly object _lock = new();

        /// <summary>
        /// Raised with a snapshot when a new board is created. Handlers must be quick and non-blocking.
        /// </summary>
        public event EventHandler<GameBoard>? BoardCreated;
        /// <summary>
        /// Raised, in version order, when tiles change. Handlers must be quick and non-blocking.
        /// </summary>
        public event EventHandler<TilesChange>? TilesChanged;

        /// <summary>
        /// Creates the service. No board exists until <see cref="CreateBoard"/> is called.
        /// </summary>
        public BoardService(Serilog.ILogger logger, IOptionsMonitor<BoardSettings> settings, IBoardRepository repository)
        {
            _logger = logger;
            _settings = settings;
            _repository = repository;
        }

        /// <summary>
        /// Generates a new board from BoardSettings, stores it in place of the current one and raises <see cref="BoardCreated"/>
        /// </summary>
        public GameBoard CreateBoard()
        {
            BoardSettings settings = _settings.CurrentValue;
            int seed = settings.Seed ?? Random.Shared.Next();

            GameBoard board = new GameBoard()
            {
                Id = Guid.NewGuid(),
                Width = settings.Width,
                Height = settings.Height,
                Tiles = GenerateTiles(settings.Width, settings.Height, seed).ToArray()
            };
            board.Validate();

            GameBoard stored;
            lock (_lock)
            {
                stored = _repository.ReplaceBoard(board);
                BoardCreated?.Invoke(this, stored.Clone());
            }

            _logger.Information("Created a {Width} x {Height} board (seed {Seed})", stored.Width, stored.Height, seed);
            return stored;
        }

        /// <summary>
        /// Returns a snapshot of the current board, or null if no board has been created yet
        /// </summary>
        public GameBoard? GetBoard()
        {
            return _repository.GetBoard();
        }

        /// <summary>
        /// Replaces tiles on the board and raises <see cref="TilesChanged"/>. Throws if no board exists or a tile is off the board.
        /// </summary>
        public void SetTiles(IReadOnlyCollection<MapTile> tiles)
        {
            if (tiles.Count == 0)
            {
                return;
            }

            lock (_lock)
            {
                TilesChange change = _repository.UpdateTiles(tiles);
                TilesChanged?.Invoke(this, change);
            }
        }

        /// <summary>
        /// Builds terrain from a smoothed random heightmap: low ground is water, high ground is mountains
        /// </summary>
        private static IEnumerable<MapTile> GenerateTiles(int width, int height, int seed)
        {
            Random random = new Random(seed);
            double[,] heights = new double[width, height];
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    heights[x, y] = random.NextDouble();
                }
            }

            for (int pass = 0; pass < SmoothingPasses; pass++)
            {
                heights = Smooth(heights, width, height);
            }

            // Smoothing squashes values towards the middle, so stretch them back out to 0..1
            double min = double.MaxValue;
            double max = double.MinValue;
            foreach (double h in heights)
            {
                min = Math.Min(min, h);
                max = Math.Max(max, h);
            }
            double range = max - min;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    double normalised = range > 0 ? (heights[x, y] - min) / range : 0.5;
                    yield return new MapTile(x, y, ToTileType(normalised));
                }
            }
        }

        private static double[,] Smooth(double[,] heights, int width, int height)
        {
            double[,] result = new double[width, height];
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    double total = 0;
                    int count = 0;
                    for (int nx = Math.Max(0, x - 1); nx <= Math.Min(width - 1, x + 1); nx++)
                    {
                        for (int ny = Math.Max(0, y - 1); ny <= Math.Min(height - 1, y + 1); ny++)
                        {
                            total += heights[nx, ny];
                            count++;
                        }
                    }
                    result[x, y] = total / count;
                }
            }
            return result;
        }

        private static TileType ToTileType(double height) => height switch
        {
            < 0.25 => TileType.Water,
            < 0.55 => TileType.Plains,
            < 0.72 => TileType.Forest,
            < 0.88 => TileType.Hills,
            _ => TileType.Mountain
        };
    }
}
