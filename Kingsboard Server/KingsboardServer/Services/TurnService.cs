using KingsboardServer.Extensions;
using KingsboardServer.Models;
using KingsboardServer.Models.Configuration;
using KingsboardServer.Models.Enumerations;
using KingsboardServer.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace KingsboardServer.Services
{
    /// <summary>
    /// Runs the global turn timer: starts a turn every TurnSettings:TurnDurationSeconds and moves it through its phases
    /// </summary>
    public class TurnService : ITurnService
    {
        private readonly Serilog.ILogger _logger;
        private readonly IOptionsMonitor<TurnSettings> _settings;
        private readonly TimeProvider _timeProvider;

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
        /// Creates the service. No turns run until <see cref="RunTurnLoopAsync"/> is called.
        /// </summary>
        public TurnService(Serilog.ILogger logger, IOptionsMonitor<TurnSettings> settings, TimeProvider timeProvider)
        {
            _logger = logger;
            _settings = settings;
            _timeProvider = timeProvider;
        }

        /// <summary>
        /// Returns a snapshot of the turn in progress, or null if no turn has started yet
        /// </summary>
        public Turn? GetCurrentTurn()
        {
            lock (_turnLock)
            {
                return _currentTurn?.Clone();
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

                // Player phase: players choose cards for their pawns until the timer runs out
                TimeSpan wait = turn.EndsAt - _timeProvider.GetUtcNow();
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, _timeProvider, cancellationToken);
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
                snapshot = _currentTurn.Clone();
            }

            _logger.Information("Turn {TurnId} started ({DurationSeconds}s)", snapshot.Id, duration.TotalSeconds);
            RaiseTurnEvent(TurnStarted, snapshot);

            SetPhase(TurnPhase.Pawn);
            // TODO: Pawn phase logic (e.g. deal pawn cards) goes here

            SetPhase(TurnPhase.Player);

            return snapshot;
        }

        private void EndTurn()
        {
            SetPhase(TurnPhase.Resolution);
            // TODO: Resolve the actions for each pawn's selected card here

            Turn snapshot = SetPhase(TurnPhase.End);

            _logger.Debug("Turn {TurnId} ended", snapshot.Id);
            RaiseTurnEvent(TurnEnded, snapshot);
        }

        private Turn SetPhase(TurnPhase phase)
        {
            lock (_turnLock)
            {
                _currentTurn!.CurrentPhase = phase;
                return _currentTurn.Clone();
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
