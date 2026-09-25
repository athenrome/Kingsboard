using KingsboardServer.Models.Enumerations;

namespace KingsboardServer.Models
{
    public class Turn
    {
        public int Id { get; set; }

        public TurnPhase CurrentPhase { get; set; }

        /// <summary>
        /// When the turn started (UTC, server clock)
        /// </summary>
        public DateTimeOffset StartedAt { get; set; }

        /// <summary>
        /// When the turn is scheduled to end (UTC, server clock)
        /// </summary>
        public DateTimeOffset EndsAt { get; set; }

        public TimeSpan Duration => EndsAt - StartedAt;

        /// <summary>
        /// Time left in the turn relative to <paramref name="now"/>, never negative
        /// </summary>
        public TimeSpan GetTimeRemaining(DateTimeOffset now)
        {
            TimeSpan remaining = EndsAt - now;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        /// <summary>
        /// Creates a copy so callers can't mutate the live turn
        /// </summary>
        public Turn Clone() => (Turn)MemberwiseClone();
    }
}
