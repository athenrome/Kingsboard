using KingsboardServer.Models;
using KingsboardServer.Models.Configuration;
using KingsboardServer.Models.Enumerations;
using KingsboardServer.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace KingsboardServer.Services
{
    /// <summary>
    /// Runs the global turn timer: starts a turn every TurnSettings:TurnDurationSeconds and moves it through its phases
    /// (Start, Upkeep, Planning, Player, Resolution, End). Each phase runs every <see cref="ITurnPhaseHandler"/> registered
    /// for it and waits for them all to finish before the next phase begins.
    /// </summary>
    public class TurnService : ITurnService
    {
        // The order every turn moves through its phases
        private static readonly TurnPhase[] PhasesBeforePlayer = { TurnPhase.Upkeep, TurnPhase.Planning };
        private static readonly TurnPhase[] PhasesAfterPlayer = { TurnPhase.Resolution, TurnPhase.End };

        private readonly Serilog.ILogger _logger;
        private readonly IOptionsMonitor<TurnSettings> _settings;
        private readonly TimeProvider _timeProvider;
        private readonly IReadOnlyList<ITurnPhaseHandler> _phaseHandlers;

        private readonly object _turnLock = new();
        private Turn? _currentTurn;

        private TimeSpan _lastValidDuration = TimeSpan.FromSeconds(TurnSettings.DefaultTurnDurationSeconds);

        /// <summary>
        /// Raised with a snapshot when a new turn begins. Handlers must be quick and non-blocking.
        /// </summary>
        public event EventHandler<Turn>? TurnStarted;
        /// <summary>
        /// Raised with a snapshot when a turn ends. Handlers must be quick and non-blocking.
        /// </summary>
        public event EventHandler<Turn>? TurnEnded;

        /// <summary>
        /// Raised with a snapshot when the turn moves into a new phase, before that phase's work runs.
        /// Handlers must be quick and non-blocking.
        /// </summary>
        public event EventHandler<Turn>? PhaseStarted;

        /// <summary>
        /// Creates the service with the handlers that do each phase's work. No turns run until <see cref="RunTurnLoopAsync"/> is called.
        /// </summary>
        public TurnService(Serilog.ILogger logger, IOptionsMonitor<TurnSettings> settings, TimeProvider timeProvider, IEnumerable<ITurnPhaseHandler> phaseHandlers)
        {
            _logger = logger;
            _settings = settings;
            _timeProvider = timeProvider;
            _phaseHandlers = phaseHandlers.ToArray();
        }

        /// <summary>
        /// Returns a snapshot of the turn in progress, or null if no turn has started yet
        /// </summary>
        public Turn? GetCurrentTurn()
        {
            lock (_turnLock)
            {
                return _currentTurn is null ? null : Clone(_currentTurn);
            }
        }

        /// <summary>
        /// Runs turns back to back, scheduled from each turn's end time so they don't drift, until cancelled
        /// </summary>
        public async Task RunTurnLoopAsync(CancellationToken cancellationToken)
        {
            // Turns are scheduled back-to-back from the previous turn's end time (not "now"),
            // so small delays in resolution/broadcasting don't accumulate into drift.
            DateTimeOffset nextTurnStart = _timeProvider.GetUtcNow();

            while (!cancellationToken.IsCancellationRequested)
            {
                TimeSpan duration = GetTurnDuration();
                DateTimeOffset now = _timeProvider.GetUtcNow();

                if (now - nextTurnStart > duration)
                {
                    // We've fallen more than a full turn behind (e.g. the machine slept); resync instead of catching up.
                    _logger.Warning("Turn loop fell behind by {Lag}; resyncing the turn timer", now - nextTurnStart);
                    nextTurnStart = now;
                }

                Turn turn = BeginTurn(nextTurnStart, duration);
                await RunPhaseAsync(TurnPhase.Start, cancellationToken);

                foreach (TurnPhase phase in PhasesBeforePlayer)
                {
                    await RunPhaseAsync(phase, cancellationToken);
                }

                // Player phase: players choose cards for their pawns until the timer runs out
                await RunPhaseAsync(TurnPhase.Player, cancellationToken);
                TimeSpan wait = turn.EndsAt - _timeProvider.GetUtcNow();
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, _timeProvider, cancellationToken);
                }

                foreach (TurnPhase phase in PhasesAfterPlayer)
                {
                    await RunPhaseAsync(phase, cancellationToken);
                }

                EndTurn();

                nextTurnStart = turn.EndsAt;
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        private Turn BeginTurn(DateTimeOffset startedAt, TimeSpan duration)
        {
            Turn snapshot;
            lock (_turnLock)
            {
                _currentTurn = new Turn()
                {
                    Id = (_currentTurn?.Id ?? 0) + 1,
                    CurrentPhase = TurnPhase.Start,
                    StartedAt = startedAt,
                    EndsAt = startedAt + duration
                };
                snapshot = Clone(_currentTurn);
            }

            _logger.Information("Turn {TurnId} started ({DurationSeconds}s)", snapshot.Id, duration.TotalSeconds);
            RaiseTurnEvent(TurnStarted, snapshot);

            return snapshot;
        }

        /// <summary>
        /// Moves the turn into <paramref name="phase"/>, then runs every handler for it one after another,
        /// returning only once they have all finished. A handler that throws is logged and the turn carries on,
        /// so one broken system can't stop the game.
        /// </summary>
        private async Task RunPhaseAsync(TurnPhase phase, CancellationToken cancellationToken)
        {
            Turn snapshot = SetPhase(phase);
            RaiseTurnEvent(PhaseStarted, snapshot);

            foreach (ITurnPhaseHandler handler in _phaseHandlers)
            {
                if (!handler.Phases.Contains(phase))
                {
                    continue;
                }

                try
                {
                    await handler.RunPhaseAsync(Clone(snapshot), cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "{Handler} failed during the {Phase} phase of turn {TurnId}", handler.GetType().Name, phase, snapshot.Id);
                }
            }
        }

        private void EndTurn()
        {
            Turn snapshot = GetCurrentTurn()!;

            _logger.Debug("Turn {TurnId} ended", snapshot.Id);
            RaiseTurnEvent(TurnEnded, snapshot);
        }

        private Turn SetPhase(TurnPhase phase)
        {
            lock (_turnLock)
            {
                _currentTurn!.CurrentPhase = phase;
                return Clone(_currentTurn);
            }
        }

        /// <summary>
        /// Reads the configured turn duration. Re-read every turn so appsettings changes apply without a restart.
        /// Falls back to the last valid value if the config is invalid.
        /// </summary>
        private TimeSpan GetTurnDuration()
        {
            try
            {
                double seconds = _settings.CurrentValue.TurnDurationSeconds;
                if (seconds > 0)
                {
                    _lastValidDuration = TimeSpan.FromSeconds(seconds);
                }
                else
                {
                    _logger.Warning("TurnSettings:TurnDurationSeconds must be greater than 0 (was {Seconds}); using {Fallback}s",
                        seconds, _lastValidDuration.TotalSeconds);
                }
            }
            catch (OptionsValidationException ex)
            {
                _logger.Warning("Invalid TurnSettings ({Reason}); using {Fallback}s", ex.Message, _lastValidDuration.TotalSeconds);
            }

            return _lastValidDuration;
        }

        /// <summary>
        /// How long the turn lasts in total
        /// </summary>
        public TimeSpan GetDuration(Turn turn) => turn.EndsAt - turn.StartedAt;

        /// <summary>
        /// Time left in the turn right now, by the server clock; never negative
        /// </summary>
        public TimeSpan GetTimeRemaining(Turn turn)
        {
            TimeSpan remaining = turn.EndsAt - _timeProvider.GetUtcNow();
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        /// <summary>
        /// Creates a copy so callers can't change the live turn
        /// </summary>
        private static Turn Clone(Turn turn)
        {
            return new Turn()
            {
                Id = turn.Id,
                CurrentPhase = turn.CurrentPhase,
                StartedAt = turn.StartedAt,
                EndsAt = turn.EndsAt
            };
        }

        private void RaiseTurnEvent(EventHandler<Turn>? handler, Turn snapshot)
        {
            if (handler is null)
            {
                return;
            }

            // Invoke each subscriber separately so one failing client can't stop the others (or the turn loop)
            foreach (EventHandler<Turn> subscriber in handler.GetInvocationList().Cast<EventHandler<Turn>>())
            {
                try
                {
                    subscriber(this, snapshot);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "A turn event subscriber threw for turn {TurnId}", snapshot.Id);
                }
            }
        }
    }
}
