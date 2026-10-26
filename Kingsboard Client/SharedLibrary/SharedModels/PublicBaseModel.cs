using System;

namespace Kingsboard.Shared.Models
{
    /// <summary>
    /// The base of models that the game and its clients identify by a GUID (pawns, cards, boards, players).
    /// Adds a <see cref="PublicId"/> to the whole-number <see cref="BaseModel.Id"/>.
    /// </summary>
    public abstract class PublicBaseModel : BaseModel
    {
        /// <summary>
        /// The GUID the game, its messages and its clients know the model by. Assigned by the server, never by the database.
        /// </summary>
        public Guid PublicId { get; set; }
    }
}
