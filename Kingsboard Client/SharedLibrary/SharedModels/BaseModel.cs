namespace Kingsboard.Shared.Models
{
    /// <summary>
    /// The base of Kingsboard's models. Gives each one a whole-number id. Stored models (the server's database records)
    /// use it as their primary key, which the database numbers when the row is first saved; until then it is 0.
    /// </summary>
    public abstract class BaseModel
    {
        /// <summary>
        /// Whole-number id of the model. For a stored model it's the database row's key (0 until first saved).
        /// </summary>
        public int Id { get; set; }
    }
}
