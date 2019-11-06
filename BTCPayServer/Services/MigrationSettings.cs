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
        public bool ConvertNetworkFeeProperty { get; set; }
        public bool ConvertCrowdfundOldSettings { get; set; }
        public bool ConvertWalletKeyPathRoots { get; set; }
        public bool CheckedFirstRun { get; set; }
        public override string ToString()
        {
            return string.Empty;
        }
    }
}
