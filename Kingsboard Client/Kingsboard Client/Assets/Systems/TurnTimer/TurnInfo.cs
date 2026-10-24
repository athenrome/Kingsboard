using Kingsboard.Shared.Models.Enumerations;

namespace Kingsboard.TurnTimer
{
    /// <summary>
    /// The state of the turn in progress, as last sent by the server
    /// </summary>
    public class TurnInfo
    {
        /// <summary>
        /// Turn number, starting at 1
        /// </summary>
        public int TurnId { get; set; }

        /// <summary>
        /// The phase the turn is in
        /// </summary>
        public TurnPhase Phase { get; set; }

        /// <summary>
        /// How long the whole turn lasts, in seconds
        /// </summary>
        public float DurationSeconds { get; set; }

        /// <summary>
        /// Seconds left in the turn when the server sent this. The Player phase lasts until the turn's time runs out,
        /// so during the Player phase this is also the time left to play.
        /// </summary>
        public float RemainingSeconds { get; set; }

        /// <summary>
        /// When this arrived, as Unity's <c>Time.realtimeSinceStartup</c>, so the countdown can carry on locally
        /// </summary>
        public float ReceivedAt { get; set; }
    }
}
