namespace BTCPayServer.Abstractions.Models
{
    public class DatabaseOptions
    {
        public DatabaseType DatabaseType { get; set; }
        public string ConnectionString { get; set; }
    }
}
