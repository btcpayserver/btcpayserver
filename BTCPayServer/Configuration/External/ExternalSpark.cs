using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Configuration.External
{
    public class ExternalSpark : ExternalService
    {
        public SparkConnectionString ConnectionString { get; }

        public ExternalSpark(SparkConnectionString connectionString)
        {
            if (connectionString == null)
                throw new ArgumentNullException(nameof(connectionString));
            ConnectionString = connectionString;
        }
    }
}
