using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Configuration.External
{
    public interface IAccessKeyService
    {
        SparkConnectionString ConnectionString { get; }
        Task<string> ExtractAccessKey();
    }
    public class ExternalSpark : ExternalService, IAccessKeyService
    {
        public SparkConnectionString ConnectionString { get; }

        public ExternalSpark(SparkConnectionString connectionString)
        {
            if (connectionString == null)
                throw new ArgumentNullException(nameof(connectionString));
            ConnectionString = connectionString;
        }

        public async Task<string> ExtractAccessKey()
        {
            if (ConnectionString?.CookeFile == null)
                throw new FormatException("Invalid connection string");
            var cookie = (ConnectionString.CookeFile == "fake"
                        ? "fake:fake:fake" // Hacks for testing
                        : await System.IO.File.ReadAllTextAsync(ConnectionString.CookeFile)).Split(':');
            if (cookie.Length >= 3)
            {
                return cookie[2];
            }
            throw new FormatException("Invalid cookiefile format");
        }
    }
}
