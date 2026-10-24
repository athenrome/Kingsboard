using System;

namespace Kingsboard.Players
{
    /// <summary>
    /// Who we are in the game, as given by the server when we joined
    /// </summary>
    public class PlayerInfo
    {
        /// <summary>
        /// Our public id. Pawns we control carry it as their owner id.
        /// </summary>
        public Guid PlayerId { get; set; }

        /// <summary>
        /// Our secret id, sent with requests that act as us (e.g. choosing a pawn's card). Never shown or shared.
        /// </summary>
        public Guid SessionId { get; set; }

        /// <summary>
        /// The name the server gave us (it may differ from the one we asked for, to keep names unique)
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Our colour as "#RRGGBB", given by the server. Pawns we control are shown in it.
        /// </summary>
        public string Colour { get; set; }
    }
}
