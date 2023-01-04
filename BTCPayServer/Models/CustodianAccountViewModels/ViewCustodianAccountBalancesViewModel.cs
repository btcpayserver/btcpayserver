using System.Collections.Generic;

namespace BTCPayServer.Models.CustodianAccountViewModels
{
    public class ViewCustodianAccountBalancesViewModel
    {
        public Dictionary<string, AssetBalanceInfo> AssetBalances { get; set; }
        public string AssetBalanceExceptionMessage { get; set; }

        public string StoreId { get; set; }
        public string StoreDefaultFiat { get; set; }
        public decimal DustThresholdInFiat { get; set; }
        public string[] DepositablePaymentMethods { get; set; }
    }
}
