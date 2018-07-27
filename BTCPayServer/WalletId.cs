using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BTCPayServer
{
    public class WalletId
    {
        static readonly Regex _WalletStoreRegex = new Regex("^S-([a-zA-Z0-9]{30,60})-([a-zA-Z]{2,5})$");
        public static bool TryParse(string str, out WalletId walletId)
        {
            walletId = null;
            WalletId w = new WalletId();
            var match = _WalletStoreRegex.Match(str);
            if (!match.Success)
                return false;
            w.StoreId = match.Groups[1].Value;
            w.CryptoCode = match.Groups[2].Value.ToUpperInvariant();
            walletId = w;
            return true;
        }
        public WalletId()
        {

        }
        public WalletId(string storeId, string cryptoCode)
        {
            StoreId = storeId;
            CryptoCode = cryptoCode;
        }
        public string StoreId { get; set; }
        public string CryptoCode { get; set; }
        public override string ToString()
        {
            return $"S-{StoreId}-{CryptoCode.ToUpperInvariant()}";
        }
    }
}
