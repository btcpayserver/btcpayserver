using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
            public bool IsOwner { get; set; }
            public WalletId Id { get; set; }
        }

        public List<WalletViewModel> Wallets { get; set; } = new List<WalletViewModel>();
    }
}
