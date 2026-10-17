using KingsboardServer.Models;

namespace KingsboardServer.Extensions
{
    /// <summary>
    /// Logic for <see cref="Turn"/> (models hold data only)
    /// </summary>
    public static class TurnExtensions
    {
        /// <summary>
        /// How long the turn lasts
        /// </summary>
        public static TimeSpan GetDuration(this Turn turn) => turn.EndsAt - turn.StartedAt;

        /// <summary>
        /// Time left in the turn relative to <paramref name="now"/>, never negative
        /// </summary>
        public static TimeSpan GetTimeRemaining(this Turn turn, DateTimeOffset now)
        {
            TimeSpan remaining = turn.EndsAt - now;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        /// <summary>
        /// Creates a copy so callers can't mutate the live turn
        /// </summary>
        public static Turn Clone(this Turn turn)
        {
            return new Turn()
            {
                Id = turn.Id,
                CurrentPhase = turn.CurrentPhase,
                StartedAt = turn.StartedAt,
                EndsAt = turn.EndsAt
            };
        }
    }
}
