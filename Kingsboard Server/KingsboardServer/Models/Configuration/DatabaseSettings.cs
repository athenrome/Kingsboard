namespace KingsboardServer.Models.Configuration
{
    /// <summary>
    /// Settings for the SQLite database the server stores its data in. Bound from the "DatabaseSettings" section of appsettings.json.
    /// </summary>
    public class DatabaseSettings
    {
        /// <summary>
        /// The appsettings.json section these settings are read from
        /// </summary>
        public const string SectionName = "DatabaseSettings";

        /// <summary>
        /// SQLite connection string. A relative "Data Source" file is placed in the server's content root folder.
        /// Read once, when the server starts.
        /// </summary>
        public string ConnectionString { get; set; } = "Data Source=kingsboard.db";
    }
}
