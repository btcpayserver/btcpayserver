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
    }
}
