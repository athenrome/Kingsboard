using KingsboardServer.Services.Interfaces;

namespace KingsboardServer.Services
{
    /// <summary>
    /// Runs the game: creates the board and spawns the pawns, then drives the turn loop on a background task
    /// </summary>
    public class GameService : IGameService
    {
        private readonly Serilog.ILogger _logger;
        private readonly ITurnService _turnService;
        private readonly IBoardService _boardService;
        private readonly IPawnService _pawnService;

        private readonly object _lifecycleLock = new();
        private CancellationTokenSource? _gameCts;
        private Task? _gameLoopTask;

        /// <summary>
        /// Creates the service. Nothing runs until <see cref="StartGame"/> is called.
        /// </summary>
        public GameService(Serilog.ILogger logger, ITurnService turnService, IBoardService boardService, IPawnService pawnService)
        {
            _logger = logger;
            _turnService = turnService;
            _boardService = boardService;
            _pawnService = pawnService;
        }

        /// <summary>
        /// Runs the core Game Loop
        /// </summary>
        public async Task RunGame(CancellationToken cancellationToken)
        {
            try
            {
                // Every game is played on a freshly generated board, with a fresh set of pawns
                _boardService.CreateBoard();
                _pawnService.SpawnPawns();

                await _turnService.RunTurnLoopAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.Information("Game loop stopped");
            }
            catch (Exception ex)
            {
                _logger.Fatal(ex, "Game loop crashed");
            }
        }

        /// <summary>
        /// Starts a new game on a background task. Does nothing if a game is already running.
        /// </summary>
        public void StartGame()
        {
            lock (_lifecycleLock)
            {
                if (_gameLoopTask is { IsCompleted: false })
                {
                    _logger.Warning("StartGame called while the game is already running");
                    return;
                }

                _gameCts?.Dispose();
                _gameCts = new CancellationTokenSource();
                CancellationToken token = _gameCts.Token;

                _gameLoopTask = Task.Run(() => RunGame(token), token);
            }

            _logger.Information("Game Started");
        }

        /// <summary>
        /// Asks the running game to stop. Returns straight away without waiting for it to finish.
        /// </summary>
        public void StopGame()
        {
            lock (_lifecycleLock)
            {
                _gameCts?.Cancel();
            }
        }
    }
}
