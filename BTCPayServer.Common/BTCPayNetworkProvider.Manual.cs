using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using NBXplorer;

namespace BTCPayServer
{
    public class StubBTCPayNetwork : BTCPayNetworkBase
    {  
    }
    
    public partial class BTCPayNetworkProvider
    {
        //TODO: Remove ManualCryptoCode, move CryptoCode to a new interface (IHasCryptCode) and make BTCPayNetwork implement it. Then move all cryptocode method utilites to extension methods to the new interface
        public const string ManualCryptoCode = "";
        private void InitStubNetwork()
        {
            Add(new StubBTCPayNetwork()
            {
                DisplayName = "Stub BTCPay Network",
                CryptoCode = ManualCryptoCode
            });
        }
    }
}
