using Kingsboard.Shared.Models.Pawns;
using UnityEngine;

namespace Kingsboard.Pawns
{
    /// <summary>
    /// Client-side pawn logic for the shared <see cref="Pawn"/> model (models hold data only).
    /// The server owns every pawn rule; this only turns pawn data into things Unity can show.
    /// </summary>
    public static class PawnService
    {
        /// <summary>
        /// The pawn's display colour as a Unity colour, parsed from its "#RRGGBB" string. White if the string isn't a valid colour.
        /// </summary>
        public static Color GetColour(Pawn pawn)
        {
            return ColorUtility.TryParseHtmlString(pawn.Colour, out Color colour) ? colour : Color.white;
        }
    }
}
