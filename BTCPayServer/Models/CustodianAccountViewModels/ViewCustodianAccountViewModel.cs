using System;
using System.Collections.Generic;
using BTCPayServer.Data;

namespace BTCPayServer.Models.CustodianAccountViewModels
{
    public class ViewCustodianAccountViewModel
    {

        public CustodianAccountData CustodianAccount { get; set; }
        public Dictionary<string,decimal> AssetBalances { get; set; }
        public Dictionary<string,decimal> AssetBids { get; set; }
        public Dictionary<string,decimal> AssetAsks { get; set; }
        public Exception GetAssetBalanceException { get; set; }
        public string DefaultCurrency { get; set; }
    }
}
