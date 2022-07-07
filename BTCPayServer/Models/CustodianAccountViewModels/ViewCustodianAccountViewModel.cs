using System;
using System.Collections.Generic;
using BTCPayServer.Abstractions.Custodians;
using BTCPayServer.Data;

namespace BTCPayServer.Models.CustodianAccountViewModels
{
    public class ViewCustodianAccountViewModel
    {
        public ICustodian Custodian { get; set; }
        public CustodianAccountData CustodianAccount { get; set; }
        public Dictionary<string,AssetBalanceInfo> AssetBalances { get; set; }
        public Exception GetAssetBalanceException { get; set; }
        public string DefaultCurrency { get; set; }
    }
}
