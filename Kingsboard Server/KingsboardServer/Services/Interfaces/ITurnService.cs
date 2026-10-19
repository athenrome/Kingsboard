using KingsboardServer.Models;

namespace KingsboardServer.Services.Interfaces
{
    /// <summary>
    /// Drives the global turn timer. A new turn starts every TurnSettings:TurnDurationSeconds.
    /// </summary>
    public interface ITurnService
    {
        /// <summary>
        /// Raised when a new turn begins. The <see cref="Turn"/> is a snapshot.
        /// Handlers run on the turn loop, so they must be quick and non-blocking.
        /// </summary>
        event EventHandler<Turn>? TurnStarted;

        /// <summary>
        /// Raised when the turn moves into a new phase, before that phase's work runs. The <see cref="Turn"/> is a snapshot.
        /// Handlers run on the turn loop, so they must be quick and non-blocking.
        /// </summary>
        event EventHandler<Turn>? PhaseStarted;

        /// <summary>
        /// Raised when a turn ends (after resolution). The <see cref="Turn"/> is a snapshot.
        /// Handlers run on the turn loop, so they must be quick and non-blocking.
        /// </summary>
        event EventHandler<Turn>? TurnEnded;

        /// <summary>
        /// Gets a snapshot of the turn currently in progress, or null if no turn has started yet.
        /// </summary>
        Turn? GetCurrentTurn();

        /// <summary>
        /// How long the turn lasts in total
        /// </summary>
        TimeSpan GetDuration(Turn turn);

        /// <summary>
        /// Time left in the turn right now, by the server clock; never negative
        /// </summary>
        TimeSpan GetTimeRemaining(Turn turn);

        /// <summary>
        /// Runs turns back-to-back until <paramref name="cancellationToken"/> is cancelled.
        /// </summary>
        Task RunTurnLoopAsync(CancellationToken cancellationToken);
    }
}
