namespace Kingsboard.TurnTimer
{
    /// <summary>
    /// The stages every turn moves through, in order. Each phase finishes all its work before the next begins.
    /// </summary>
    public enum TurnPhase
    {
        /// <summary>
        /// A phase this client doesn't recognise (e.g. the server is newer)
        /// </summary>
        Unknown,
        /// <summary>
        /// The turn has just begun
        /// </summary>
        Start,
        /// <summary>
        /// Pawns are prepared for the turn
        /// </summary>
        Upkeep,
        /// <summary>
        /// Pawns choose their actions
        /// </summary>
        Planning,
        /// <summary>
        /// Players choose cards for their pawns until the timer runs out
        /// </summary>
        Player,
        /// <summary>
        /// The chosen actions are carried out
        /// </summary>
        Resolution,
        /// <summary>
        /// The turn is over
        /// </summary>
        End
    }

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
