using BTCPayServer.Abstractions.Custodians;
using BTCPayServer.Data;

namespace BTCPayServer.Models.CustodianAccountViewModels
{
    public class ViewCustodianAccountViewModel
    {
        public ICustodian Custodian { get; set; }
        public CustodianAccountData CustodianAccount { get; set; }

    }
}
