using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Configuration.External
{
    public class ExternalRTL : ExternalService, IAccessKeyService
    {
        public SparkConnectionString ConnectionString { get; }

        public ExternalRTL(SparkConnectionString connectionString)
        {
            if (connectionString == null)
                throw new ArgumentNullException(nameof(connectionString));
            ConnectionString = connectionString;
        }

        public async Task<string> ExtractAccessKey()
        {
            if (ConnectionString?.CookeFile == null)
                throw new FormatException("Invalid connection string");
            return await System.IO.File.ReadAllTextAsync(ConnectionString.CookeFile);
        }
    }
}
