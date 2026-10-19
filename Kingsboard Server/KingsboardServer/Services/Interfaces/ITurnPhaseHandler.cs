using KingsboardServer.Models;
using KingsboardServer.Models.Enumerations;

namespace KingsboardServer.Services.Interfaces
{
    /// <summary>
    /// A service that does work during particular turn phases (e.g. pawns choosing moves in Planning).
    /// The turn service runs every handler for a phase and waits for all of them to finish before the next phase begins.
    /// Register an implementation in Program.cs as an <see cref="ITurnPhaseHandler"/> for it to be run.
    /// </summary>
    public interface ITurnPhaseHandler
    {
        /// <summary>
        /// The phases this handler has work to do in
        /// </summary>
        IReadOnlyCollection<TurnPhase> Phases { get; }

        /// <summary>
        /// Does this handler's work for the phase the turn is in (<see cref="Turn.CurrentPhase"/>).
        /// The next phase doesn't begin until the returned task completes. If it throws, the error is logged and the turn carries on.
        /// </summary>
        Task RunPhaseAsync(Turn turn, CancellationToken cancellationToken);
    }
}
