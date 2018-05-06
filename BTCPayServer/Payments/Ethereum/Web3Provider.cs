using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using NBitcoin;
using Nethereum.Web3;

namespace BTCPayServer.Payments.Ethereum
{
    public class Web3Provider
    {
        private readonly BTCPayServerOptions _btcPayServerOptions;
        private static ConcurrentDictionary<BTCPayNetwork, Web3> savedWeb3s = new ConcurrentDictionary<BTCPayNetwork, Web3>();

        public Web3Provider(BTCPayServerOptions btcPayServerOptions)
        {
            _btcPayServerOptions = btcPayServerOptions;
        }

        public Web3 GetWeb3(BTCPayNetwork btcPayNetwork)
        {
            return savedWeb3s.GetOrAdd(btcPayNetwork, CreateWeb3FromCryptoCode(btcPayNetwork.CryptoCode));
        }

        public Web3 CreateWeb3FromCryptoCode(string cryptoCode)
        {
            return new Web3(_btcPayServerOptions.Web3ConnectionSettings.Single(setting => setting.CryptoCode == cryptoCode).NodeUri.ToString());
        }
    }
}
