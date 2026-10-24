using System;

namespace Kingsboard.Shared.Models.Pawns
{
    /// <summary>
    /// Something a pawn said, because a card it played made it talk
    /// </summary>
    /// <param name="PawnId">The pawn that spoke</param>
    /// <param name="Text">What it said</param>
    public record PawnSpeech(Guid PawnId, string Text);
}
