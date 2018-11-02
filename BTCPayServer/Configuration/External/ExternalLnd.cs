using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Lightning;

namespace BTCPayServer.Configuration.External
{
    public abstract class ExternalLnd : ExternalService
    {
        public ExternalLnd(LightningConnectionString connectionString, LndTypes type)
        {
            ConnectionString = connectionString;
            Type = type;
        }

        public LndTypes Type { get; set; }
        public LightningConnectionString ConnectionString { get; set; }
    }

    public enum LndTypes
    {
        gRPC, Rest
    }

    public class ExternalLndGrpc : ExternalLnd
    {
        public ExternalLndGrpc(LightningConnectionString connectionString) : base(connectionString, LndTypes.gRPC) { }
    }

    public class ExternalLndRest : ExternalLnd
    {
        public ExternalLndRest(LightningConnectionString connectionString) : base(connectionString, LndTypes.Rest) { }
    }
}
