namespace KingsboardServer.Data.Records
{
    /// <summary>
    /// The base of every database model. Gives each stored row its primary key.
    /// </summary>
    public abstract class BaseModel
    {
        /// <summary>
        /// Primary key of the stored row, numbered by the database when the row is first saved (0 until then).
        /// Only the database layer uses it; the game identifies things by their GUIDs.
        /// </summary>
        public int Id { get; set; }
    }
}
