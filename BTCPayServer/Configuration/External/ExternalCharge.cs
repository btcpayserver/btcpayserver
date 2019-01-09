using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Lightning;

namespace BTCPayServer.Configuration.External
{
    public class ExternalCharge : ExternalService
    {
        public ExternalCharge(LightningConnectionString connectionString)
        {
            if (connectionString == null)
                throw new ArgumentNullException(nameof(connectionString));
            ConnectionString = connectionString;
        }
        public LightningConnectionString ConnectionString { get; }
    }
}
