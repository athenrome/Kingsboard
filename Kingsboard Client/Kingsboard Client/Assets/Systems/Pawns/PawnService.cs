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
        /// The colour the pawn is shown in, as a Unity colour: its owner's colour while a player controls it, otherwise its
        /// own. Parsed from the "#RRGGBB" string; white if that isn't a valid colour.
        /// </summary>
        public static Color GetColour(Pawn pawn)
        {
            string hex = pawn.OwnerId.HasValue && !string.IsNullOrEmpty(pawn.OwnerColour) ? pawn.OwnerColour : pawn.Colour;
            return ColorUtility.TryParseHtmlString(hex, out Color colour) ? colour : Color.white;
        }
    }
}
