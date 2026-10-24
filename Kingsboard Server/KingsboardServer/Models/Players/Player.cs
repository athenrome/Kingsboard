namespace KingsboardServer.Models.Players
{
    /// <summary>
    /// A person connected to the game. They stay joined while their player stream is open.
    /// </summary>
    public class Player
    {
        /// <summary>
        /// Public id. Pawns the player controls carry it as their owner id, so every client can see it.
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Secret id the player sends with requests that act as them. Only ever sent to that player.
        /// </summary>
        public Guid SessionId { get; set; }

        /// <summary>
        /// Display name, unique among joined players
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The player's colour as "#RRGGBB", unique among joined players while there are colours to go round.
        /// Pawns the player controls are shown in it.
        /// </summary>
        public string Colour { get; set; } = "#FFFFFF";

        /// <summary>
        /// When they joined. Players are given pawns in the order they joined.
        /// </summary>
        public DateTimeOffset JoinedAt { get; set; }
    }
}
