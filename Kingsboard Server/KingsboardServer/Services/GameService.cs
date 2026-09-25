using KingsboardServer.Services.Interfaces;

namespace KingsboardServer.Services
{
    public class GameService : IGameService
    {
        private readonly Serilog.ILogger _logger;
        private readonly ITurnService _turnService;

        private readonly object _lifecycleLock = new();
        private CancellationTokenSource? _gameCts;
        private Task? _gameLoopTask;

        public GameService(Serilog.ILogger logger, ITurnService turnService) 
        {
            _logger = logger;
            _turnService = turnService;
        }

        /// <summary>
        /// Runs the core Game Loop
        /// </summary>
        public async Task RunGame(CancellationToken cancellationToken)
        {
            try
            {
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

        public void StopGame()
        {
            lock (_lifecycleLock)
            {
                _gameCts?.Cancel();
            }
        }
    }
}
