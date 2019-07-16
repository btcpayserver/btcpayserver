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
        private void InitStubNetwork()
        {
            Add(new StubBTCPayNetwork()
            {
                DisplayName = "Stub BTCPay Network",
                CryptoCode = string.Empty
            });
        }
    }
}
