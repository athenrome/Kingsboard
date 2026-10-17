using KingsboardServer.Models.Enumerations;

namespace KingsboardServer.Models
{
    /// <summary>
    /// One round of the game, run on the global turn timer
    /// </summary>
    public class Turn
    {
        /// <summary>
        /// Turn number, starting at 1 and going up by one each turn
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// The phase the turn is currently in
        /// </summary>
        public TurnPhase CurrentPhase { get; set; }

        /// <summary>
        /// When the turn started (UTC, server clock)
        /// </summary>
        public DateTimeOffset StartedAt { get; set; }

        /// <summary>
        /// When the turn is scheduled to end (UTC, server clock)
        /// </summary>
        public DateTimeOffset EndsAt { get; set; }
    }
}
