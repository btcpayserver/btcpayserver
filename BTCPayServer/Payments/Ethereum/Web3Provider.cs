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
        private static ConcurrentDictionary<string, Web3> savedWeb3s = new ConcurrentDictionary<string, Web3>();

        public Web3Provider(BTCPayServerOptions btcPayServerOptions)
        {
            _btcPayServerOptions = btcPayServerOptions;
        }

        public Web3 GetWeb3(BTCPayNetwork btcPayNetwork)
        {
            if (btcPayNetwork.UsesWeb3)
            {
                return CreateorGetWeb3FromCryptoCode(btcPayNetwork.CryptoCode);
            };
            return null;
        }

        public Web3 CreateorGetWeb3FromCryptoCode(string cryptoCode)
        {
            return savedWeb3s.GetOrAdd(cryptoCode, new Web3(_btcPayServerOptions.Web3ConnectionSettings.Single(setting => setting.CryptoCode == cryptoCode).NodeUri.ToString()));
        }


        public bool IsAvailable(BTCPayNetwork network)
        {
            return IsAvailable(network.CryptoCode);
        }

        public bool IsAvailable(string cryptoCode)
        {
            return savedWeb3s.ContainsKey(cryptoCode);
        }
    }
}
