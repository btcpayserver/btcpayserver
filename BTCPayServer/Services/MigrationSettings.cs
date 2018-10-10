using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Services
{
    public class MigrationSettings
    {
        public bool UnreachableStoreCheck { get; set; }
        public bool DeprecatedLightningConnectionStringCheck { get; set; }
        public bool ConvertMultiplierToSpread { get; set; }
    }
}
