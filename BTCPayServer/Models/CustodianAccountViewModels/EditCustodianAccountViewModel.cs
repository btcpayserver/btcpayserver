using BTCPayServer.Abstractions.Form;
using BTCPayServer.Data;

namespace BTCPayServer.Models.CustodianAccountViewModels
{
    public class EditCustodianAccountViewModel
    {

        public CustodianAccountData CustodianAccount { get; set; }
        public Form ConfigForm { get; set; }
        public string Config { get; set; }
    }
}
