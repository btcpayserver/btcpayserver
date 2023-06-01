using System.Collections.Generic;
using BTCPayServer;
using NBitcoin;

namespace BTCPayServer.Models.WalletViewModels
{
    public class ListWalletsViewModel
    {
        public class WalletViewModel
        {
            public string StoreName { get; set; }
            public string StoreId { get; set; }
            public string CryptoCode { get; set; }
            public string Balance { get; set; }
            public WalletId Id { get; set; }
        }

        public Dictionary<BTCPayNetwork, IMoney> BalanceForCryptoCode { get; set; } = new Dictionary<BTCPayNetwork, IMoney>();
        public List<WalletViewModel> Wallets { get; set; } = new List<WalletViewModel>();
    }
}
