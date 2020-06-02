namespace BTCPayServer.Contracts
{
    using System.Collections.Generic;
    using NBitcoin;

    namespace BTCPayServer
    {
        public interface IBTCPayNetworkProvider
        {
            IEnumerable<BTCPayNetworkBase> GetNetworks(NetworkType networkType);
        }
    } 
}
