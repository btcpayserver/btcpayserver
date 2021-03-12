using BTCPayServer.Fido2.Models;
using BTCPayServer.U2F.Models;

namespace BTCPayServer.Models.AccountViewModels
{
    public class SecondaryLoginViewModel
    {
        public LoginWithFido2ViewModel LoginWithFido2ViewModel { get; set; }
        public LoginWith2faViewModel LoginWith2FaViewModel { get; set; }
        public LoginWithU2FViewModel LoginWithU2FViewModel { get; set; }
    }
}
