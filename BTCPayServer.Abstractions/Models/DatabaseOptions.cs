namespace BTCPayServer.Abstractions.Models
{
    public class DatabaseOptions
    {
        public DatabaseOptions(DatabaseType type, string connString)
        {
            DatabaseType = type;
            ConnectionString = connString;
        }

        public DatabaseType DatabaseType { get; set; }
        public string ConnectionString { get; set; }
    }
}
