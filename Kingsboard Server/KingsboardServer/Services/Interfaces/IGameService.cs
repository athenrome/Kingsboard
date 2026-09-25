namespace KingsboardServer.Services.Interfaces
{
    /// <summary>
    /// Interface for the service that controls the core game loop
    /// </summary>
    public interface IGameService
    {
        /// <summary>
        /// Starts the Game Loop on a background task
        /// </summary>
        public void StartGame();

        /// <summary>
        /// Stops the Game Loop
        /// </summary>
        public void StopGame();

        /// <summary>
        /// Runs the core server game loop, which will handle turn progression, player actions, and game state updates.
        /// Completes when <paramref name="cancellationToken"/> is cancelled.
        /// </summary>
        public Task RunGame(CancellationToken cancellationToken);
    }
}
