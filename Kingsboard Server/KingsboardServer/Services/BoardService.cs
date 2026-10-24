using Kingsboard.Shared.Models.Board;
using Kingsboard.Shared.Models.Enumerations;
using KingsboardServer.Events.Board;
using KingsboardServer.Models.Board;
using KingsboardServer.Models.Configuration;
using KingsboardServer.Repositories.Interfaces;
using KingsboardServer.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace KingsboardServer.Services
{
    /// <summary>
    /// Generates, stores and changes the 3D game board, and tells listeners about every change.
    /// All board rules (bounds, stacking, validation, versioning, copying) live here, and every position
    /// is checked and evaluated as a <see cref="BoardPosition"/>.
    /// </summary>
    public class BoardService : IBoardService
    {
        // Terrain noise: the largest features (continents, mountain ranges) span about this many tiles,
        // and each further octave adds detail at half the size and half the strength
        private const double LargestFeatureSize = 32;
        private const int NoiseOctaves = 4;

        // Heightmap values below this are water; everything above is land that rises with the heightmap
        private const double SeaLevel = 0.25;

        private readonly Serilog.ILogger _logger;
        private readonly IOptionsMonitor<BoardSettings> _settings;
        private readonly IBoardRepository _repository;

        // Guards every read and change of the stored board, and keeps changes and their events
        // in the same order so clients can apply them as they arrive. Everything inside it is non-blocking.
        private readonly object _lock = new();

        /// <summary>
        /// Raised with a snapshot when a new board is created. Handlers must be quick and non-blocking.
        /// </summary>
        public event EventHandler<GameBoard>? BoardCreated;

        /// <summary>
        /// Raised, in version order, when tiles are placed, replaced or removed. Handlers must be quick and non-blocking.
        /// </summary>
        public event EventHandler<TilesChangeEvent>? TilesChanged;

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
                Depth = settings.Depth,
                Height = settings.Height,
                Tiles = new MapTile?[settings.Width * settings.Depth * settings.Height]
            };
            foreach (MapTile tile in GenerateTiles(settings.Width, settings.Depth, settings.Height, seed))
            {
                board.Tiles[IndexOf(board, tile.Position)] = tile;
            }
            Validate(board);

            lock (_lock)
            {
                _repository.SaveBoard(board);
                BoardCreated?.Invoke(this, Clone(board));
            }

            _logger.Information("Created a {Width} x {Depth} x {Height} board (seed {Seed})", board.Width, board.Depth, board.Height, seed);
            return Clone(board);
        }

        /// <summary>
        /// Returns a snapshot of the current board, or null if no board has been created yet
        /// </summary>
        public GameBoard? GetBoard()
        {
            lock (_lock)
            {
                GameBoard? board = _repository.GetBoard();
                return board is null ? null : Clone(board);
            }
        }

        /// <summary>
        /// Returns a snapshot of one section of the board, or null if no board has been created yet.
        /// A region hanging over the edge is moved back onto the board, keeping its size where the board is big enough.
        /// Throws if the region is smaller than 1 x 1.
        /// </summary>
        public BoardSection? GetSection(BoardRegion region)
        {
            RequireValidRegion(region);

            lock (_lock)
            {
                GameBoard? board = _repository.GetBoard();
                return board is null ? null : CutSection(board, region);
            }
        }

        /// <summary>
        /// Runs <paramref name="read"/> with a snapshot of a section while no board change can happen.
        /// Changes and their events happen under the same lock, so a caller that starts listening inside
        /// <paramref name="read"/> gets every change after the snapshot and none before it.
        /// </summary>
        public void ReadSection(BoardRegion region, Action<BoardSection?> read)
        {
            RequireValidRegion(region);

            lock (_lock)
            {
                GameBoard? board = _repository.GetBoard();
                read(board is null ? null : CutSection(board, region));
            }
        }

        /// <summary>
        /// Whether the position is inside the board's bounds (false if no board has been created yet)
        /// </summary>
        public bool IsOnBoard(BoardPosition position)
        {
            lock (_lock)
            {
                GameBoard? board = _repository.GetBoard();
                return board is not null && Contains(board, position);
            }
        }

        /// <summary>
        /// Whether the position is on the board and is empty space (holds no tile)
        /// </summary>
        public bool IsEmpty(BoardPosition position)
        {
            lock (_lock)
            {
                GameBoard? board = _repository.GetBoard();
                return board is not null && Contains(board, position) && board.Tiles[IndexOf(board, position)] is null;
            }
        }

        /// <summary>
        /// Returns the tile at the position, or null if it's empty space, off the board, or there is no board yet
        /// </summary>
        public MapTile? GetTile(BoardPosition position)
        {
            lock (_lock)
            {
                GameBoard? board = _repository.GetBoard();
                return board is not null && Contains(board, position) ? board.Tiles[IndexOf(board, position)] : null;
            }
        }

        /// <summary>
        /// Returns the top tile of the stack in the position's column (its Z is ignored), or null if the stack is empty,
        /// the column is off the board, or there is no board yet
        /// </summary>
        public MapTile? GetTopTile(BoardPosition column)
        {
            lock (_lock)
            {
                GameBoard? board = _repository.GetBoard();
                if (board is null || !ContainsColumn(board, column))
                {
                    return null;
                }

                int stackHeight = GetStackHeight(board, column);
                return stackHeight == 0 ? null : board.Tiles[IndexOf(board, column with { Z = stackHeight - 1 })];
            }
        }

        /// <summary>
        /// Returns the stack in the position's column (its Z is ignored), bottom tile first.
        /// Empty if the stack is empty, the column is off the board, or there is no board yet.
        /// </summary>
        public IReadOnlyList<MapTile> GetStack(BoardPosition column)
        {
            lock (_lock)
            {
                GameBoard? board = _repository.GetBoard();
                if (board is null || !ContainsColumn(board, column))
                {
                    return Array.Empty<MapTile>();
                }

                int stackHeight = GetStackHeight(board, column);
                MapTile[] stack = new MapTile[stackHeight];
                for (int z = 0; z < stackHeight; z++)
                {
                    stack[z] = board.Tiles[IndexOf(board, column with { Z = z })]!;
                }
                return stack;
            }
        }

        /// <summary>
        /// Returns the empty position on top of the stack in the position's column (its Z is ignored), where the next
        /// tile can be placed. Null if the stack is full, the column is off the board, or there is no board yet.
        /// </summary>
        public BoardPosition? GetPlacementPosition(BoardPosition column)
        {
            lock (_lock)
            {
                GameBoard? board = _repository.GetBoard();
                if (board is null || !ContainsColumn(board, column))
                {
                    return null;
                }

                int stackHeight = GetStackHeight(board, column);
                return stackHeight < board.Height ? column with { Z = stackHeight } : null;
            }
        }

        /// <summary>
        /// Whether a tile can be placed at the position: it is on the board, empty, and resting on a tile (or on the ground at Z = 0)
        /// </summary>
        public bool CanPlaceTile(BoardPosition position)
        {
            lock (_lock)
            {
                GameBoard? board = _repository.GetBoard();
                return board is not null && CanPlace(board, position);
            }
        }

        /// <summary>
        /// Whether the tile at the position can be removed: there is a tile there and nothing on top of it
        /// </summary>
        public bool CanRemoveTile(BoardPosition position)
        {
            lock (_lock)
            {
                GameBoard? board = _repository.GetBoard();
                return board is not null && CanRemove(board, position);
            }
        }

        /// <summary>
        /// Places a new tile at the position, raises <see cref="TilesChanged"/> and returns the tile.
        /// Throws if there is no board, the position is off the board, isn't empty, or has nothing beneath it.
        /// </summary>
        public MapTile PlaceTile(BoardPosition position, TileType type)
        {
            lock (_lock)
            {
                GameBoard board = GetStoredBoard();
                RequireOnBoard(board, position);

                if (!IsEmpty(board, position))
                {
                    throw new InvalidOperationException($"{Describe(position)} already holds a tile");
                }

                // ApplyChanges rejects the tile if it would float, so there's no separate support check here
                MapTile tile = new MapTile(position, type);
                ApplyChanges(board, new[] { tile }, Array.Empty<BoardPosition>());
                return tile;
            }
        }

        /// <summary>
        /// Removes the tile at the position (leaving empty space), raises <see cref="TilesChanged"/> and returns the removed tile.
        /// Throws if there is no board, the position is off the board or already empty, or another tile rests on it.
        /// </summary>
        public MapTile RemoveTile(BoardPosition position)
        {
            lock (_lock)
            {
                GameBoard board = GetStoredBoard();
                RequireOnBoard(board, position);

                MapTile tile = board.Tiles[IndexOf(board, position)]
                    ?? throw new ArgumentException($"{Describe(position)} is already empty space", nameof(position));

                ApplyChanges(board, Array.Empty<MapTile>(), new[] { position });
                return tile;
            }
        }

        /// <summary>
        /// Places or replaces tiles at their own positions as one change, and raises <see cref="TilesChanged"/>.
        /// Throws if there is no board, a tile is off the board, or a tile would be left floating (in which case nothing is changed).
        /// </summary>
        public void SetTiles(IReadOnlyCollection<MapTile> tiles)
        {
            if (tiles.Count == 0)
            {
                return;
            }

            lock (_lock)
            {
                ApplyChanges(GetStoredBoard(), tiles, Array.Empty<BoardPosition>());
            }
        }

        /// <summary>
        /// Removes the tiles at these positions (leaving empty space) as one change, and raises <see cref="TilesChanged"/>.
        /// Throws if there is no board, a position is off the board or already empty, or a tile would be left floating (in which case nothing is changed).
        /// </summary>
        public void RemoveTiles(IReadOnlyCollection<BoardPosition> positions)
        {
            if (positions.Count == 0)
            {
                return;
            }

            lock (_lock)
            {
                ApplyChanges(GetStoredBoard(), Array.Empty<MapTile>(), positions);
            }
        }

        /// <summary>
        /// Applies removals then placements to a copy of the board, checks every stack is still solid,
        /// then saves it, bumps the version once and raises <see cref="TilesChanged"/>. Caller must hold <see cref="_lock"/>.
        /// </summary>
        private void ApplyChanges(GameBoard board, IReadOnlyCollection<MapTile> placed, IReadOnlyCollection<BoardPosition> removed)
        {
            foreach (BoardPosition position in removed)
            {
                RequireOnBoard(board, position);
                if (IsEmpty(board, position))
                {
                    throw new ArgumentException($"{Describe(position)} is already empty space", nameof(removed));
                }
            }
            foreach (MapTile tile in placed)
            {
                RequireOnBoard(board, tile.Position);
            }

            // Change a copy, so the stored board is never left half-updated or breaking the stacking rule
            GameBoard updated = Clone(board);
            foreach (BoardPosition position in removed)
            {
                updated.Tiles[IndexOf(updated, position)] = null;
            }
            foreach (MapTile tile in placed)
            {
                updated.Tiles[IndexOf(updated, tile.Position)] = tile;
            }

            IEnumerable<BoardPosition> touchedColumns = placed.Select(t => t.Position).Concat(removed).Select(p => p with { Z = 0 }).Distinct();
            foreach (BoardPosition column in touchedColumns)
            {
                RequireSolidStack(updated, column);
            }

            updated.Version++;
            _repository.SaveBoard(updated);

            TilesChanged?.Invoke(this, new TilesChangeEvent(updated.Id, updated.Version, placed.ToArray(), removed.ToArray()));
        }

        private GameBoard GetStoredBoard()
        {
            return _repository.GetBoard() ?? throw new InvalidOperationException("No board has been created yet");
        }

        private static bool Contains(GameBoard board, BoardPosition position)
        {
            return position.X >= 0 && position.X < board.Width
                && position.Y >= 0 && position.Y < board.Depth
                && position.Z >= 0 && position.Z < board.Height;
        }

        /// <summary>
        /// Whether the position's column is on the board, whatever its Z
        /// </summary>
        private static bool ContainsColumn(GameBoard board, BoardPosition column) => Contains(board, column with { Z = 0 });

        private static int IndexOf(GameBoard board, BoardPosition position)
        {
            return (position.Z * board.Depth + position.Y) * board.Width + position.X;
        }

        private static bool IsEmpty(GameBoard board, BoardPosition position) => board.Tiles[IndexOf(board, position)] is null;

        private static bool CanPlace(GameBoard board, BoardPosition position)
        {
            return Contains(board, position)
                && IsEmpty(board, position)
                && (position.Z == 0 || !IsEmpty(board, position with { Z = position.Z - 1 }));
        }

        private static bool CanRemove(GameBoard board, BoardPosition position)
        {
            if (!Contains(board, position) || IsEmpty(board, position))
            {
                return false;
            }

            BoardPosition above = position with { Z = position.Z + 1 };
            return !Contains(board, above) || IsEmpty(board, above);
        }

        private static void RequireValidRegion(BoardRegion region)
        {
            if (region.Width < 1 || region.Depth < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(region), $"A region must be at least 1 x 1, not {region.Width} x {region.Depth}");
            }
        }

        /// <summary>
        /// Moves a region that hangs over the edge back onto the board, keeping its size where the board is big enough
        /// </summary>
        private static BoardRegion FitToBoard(GameBoard board, BoardRegion region)
        {
            int width = Math.Min(region.Width, board.Width);
            int depth = Math.Min(region.Depth, board.Depth);
            int x = Math.Clamp(region.Origin.X, 0, board.Width - width);
            int y = Math.Clamp(region.Origin.Y, 0, board.Depth - depth);
            return new BoardRegion(new BoardPosition(x, y, 0), width, depth);
        }

        /// <summary>
        /// Copies every tile in a region (fitted onto the board) into a section snapshot
        /// </summary>
        private static BoardSection CutSection(GameBoard board, BoardRegion region)
        {
            BoardRegion fitted = FitToBoard(board, region);

            List<MapTile> tiles = new List<MapTile>();
            for (int z = 0; z < board.Height; z++)
            {
                for (int y = fitted.Origin.Y; y < fitted.Origin.Y + fitted.Depth; y++)
                {
                    for (int x = fitted.Origin.X; x < fitted.Origin.X + fitted.Width; x++)
                    {
                        if (board.Tiles[IndexOf(board, new BoardPosition(x, y, z))] is MapTile tile)
                        {
                            tiles.Add(tile);
                        }
                    }
                }
            }

            return new BoardSection(board.Id, board.Version, board.Width, board.Depth, board.Height, fitted, tiles);
        }

        private static string Describe(BoardPosition position) => $"({position.X}, {position.Y}, {position.Z})";

        private static string Describe(GameBoard board) => $"{board.Width} x {board.Depth} x {board.Height}";

        private static void RequireOnBoard(GameBoard board, BoardPosition position)
        {
            if (!Contains(board, position))
            {
                throw new ArgumentOutOfRangeException(nameof(position), $"{Describe(position)} is outside the {Describe(board)} board");
            }
        }

        /// <summary>
        /// Number of tiles in the stack in the position's column (its Z is ignored).
        /// Stacks are solid, so this is also the Z of the empty space on top of the stack.
        /// </summary>
        private static int GetStackHeight(GameBoard board, BoardPosition column)
        {
            int z = 0;
            while (z < board.Height && !IsEmpty(board, column with { Z = z }))
            {
                z++;
            }
            return z;
        }

        /// <summary>
        /// Throws if the stack in the position's column has empty space with a tile above it (a floating tile)
        /// </summary>
        private static void RequireSolidStack(GameBoard board, BoardPosition column)
        {
            int stackHeight = GetStackHeight(board, column);
            for (int z = stackHeight + 1; z < board.Height; z++)
            {
                BoardPosition position = column with { Z = z };
                if (!IsEmpty(board, position))
                {
                    throw new InvalidOperationException($"The tile at {Describe(position)} would be left floating: {Describe(column with { Z = stackHeight })} beneath it is empty space");
                }
            }
        }

        /// <summary>
        /// Creates a copy so callers can't change the stored board
        /// </summary>
        private static GameBoard Clone(GameBoard board)
        {
            return new GameBoard()
            {
                Id = board.Id,
                Version = board.Version,
                Width = board.Width,
                Depth = board.Depth,
                Height = board.Height,
                // Tiles are immutable records, so copying the array is enough
                Tiles = (MapTile?[])board.Tiles.Clone()
            };
        }

        /// <summary>
        /// Checks the board's size, that each tile is stored at its own position, and that every stack is solid
        /// </summary>
        /// <exception cref="ArgumentException">The board is malformed</exception>
        private static void Validate(GameBoard board)
        {
            if (board.Width <= 0 || board.Depth <= 0 || board.Height <= 0)
            {
                throw new ArgumentException("A board must be at least 1 x 1 x 1", nameof(board));
            }
            if (board.Tiles.Length != board.Width * board.Depth * board.Height)
            {
                throw new ArgumentException($"A {Describe(board)} board needs {board.Width * board.Depth * board.Height} positions, not {board.Tiles.Length}", nameof(board));
            }

            for (int i = 0; i < board.Tiles.Length; i++)
            {
                MapTile? tile = board.Tiles[i];
                if (tile is not null && (!Contains(board, tile.Position) || IndexOf(board, tile.Position) != i))
                {
                    throw new ArgumentException($"Tile {Describe(tile.Position)} is stored at the wrong position", nameof(board));
                }
            }

            for (int y = 0; y < board.Depth; y++)
            {
                for (int x = 0; x < board.Width; x++)
                {
                    RequireSolidStack(board, new BoardPosition(x, y, 0));
                }
            }
        }

        /// <summary>
        /// Builds terrain from a value-noise heightmap. Each column becomes a stack: water at sea level,
        /// otherwise stone topped with a surface tile, taller and rockier the higher the ground.
        /// </summary>
        private static IEnumerable<MapTile> GenerateTiles(int width, int depth, int height, int seed)
        {
            Random random = new Random(seed);
            double[,] heights = new double[width, depth];

            // Layer octaves of smooth noise: large features (continents, ranges) plus finer detail on top
            for (int octave = 0; octave < NoiseOctaves; octave++)
            {
                double cellSize = Math.Max(2, LargestFeatureSize / Math.Pow(2, octave));
                double weight = Math.Pow(0.5, octave);
                AddNoise(heights, width, depth, cellSize, weight, random);
            }

            // Stretch the heights out to 0..1, so every board has some sea and some peaks
            double min = double.MaxValue;
            double max = double.MinValue;
            foreach (double h in heights)
            {
                min = Math.Min(min, h);
                max = Math.Max(max, h);
            }
            double range = max - min;

            for (int y = 0; y < depth; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    double normalised = range > 0 ? (heights[x, y] - min) / range : 0.5;
                    TileType surface = ToTileType(normalised);

                    // Water sits at level 0; land rises from 1 tile at sea level to the full height at the highest peaks
                    int stackHeight = normalised < SeaLevel
                        ? 1
                        : Math.Clamp(1 + (int)Math.Round((normalised - SeaLevel) / (1 - SeaLevel) * (height - 1)), 1, height);

                    for (int z = 0; z < stackHeight; z++)
                    {
                        yield return new MapTile(new BoardPosition(x, y, z), z == stackHeight - 1 ? surface : TileType.Stone);
                    }
                }
            }
        }

        /// <summary>
        /// Adds one octave of value noise: random values on a grid of <paramref name="cellSize"/> squares,
        /// smoothly blended between grid points
        /// </summary>
        private static void AddNoise(double[,] heights, int width, int depth, double cellSize, double weight, Random random)
        {
            int cellsX = (int)Math.Ceiling(width / cellSize) + 1;
            int cellsY = (int)Math.Ceiling(depth / cellSize) + 1;
            double[,] lattice = new double[cellsX + 1, cellsY + 1];
            for (int cx = 0; cx <= cellsX; cx++)
            {
                for (int cy = 0; cy <= cellsY; cy++)
                {
                    lattice[cx, cy] = random.NextDouble();
                }
            }

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < depth; y++)
                {
                    double gx = x / cellSize;
                    double gy = y / cellSize;
                    int cx = (int)gx;
                    int cy = (int)gy;
                    double tx = SmoothStep(gx - cx);
                    double ty = SmoothStep(gy - cy);

                    double front = Lerp(lattice[cx, cy], lattice[cx + 1, cy], tx);
                    double back = Lerp(lattice[cx, cy + 1], lattice[cx + 1, cy + 1], tx);
                    heights[x, y] += Lerp(front, back, ty) * weight;
                }
            }
        }

        private static double SmoothStep(double t) => t * t * (3 - 2 * t);

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        private static TileType ToTileType(double height) => height switch
        {
            < SeaLevel => TileType.Water,
            < 0.55 => TileType.Plains,
            < 0.72 => TileType.Forest,
            < 0.88 => TileType.Hills,
            _ => TileType.Mountain
        };
    }
}
