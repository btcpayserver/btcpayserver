using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Lightning;

namespace BTCPayServer.Configuration.External
{
    public abstract class ExternalLnd : ExternalService
    {
        public ExternalLnd(LightningConnectionString connectionString, string type)
        {
            ConnectionString = connectionString;
            Type = type;
        }

        public string Type { get; set; }
        public LightningConnectionString ConnectionString { get; set; }
    }

    public class ExternalLndGrpc : ExternalLnd
    {
        public ExternalLndGrpc(LightningConnectionString connectionString) : base(connectionString, "lnd-grpc") { }
    }

    public class ExternalLndRest : ExternalLnd
    {
        public ExternalLndRest(LightningConnectionString connectionString) : base(connectionString, "lnd-rest") { }
    }
}
