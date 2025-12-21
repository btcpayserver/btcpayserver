using BTCPayServer.Fido2.Models;

namespace BTCPayServer.Models.AccountViewModels
{
    public class SecondaryLoginViewModel
    {
        public LoginWithFido2ViewModel LoginWithFido2ViewModel { get; set; }
        public LoginWith2faViewModel LoginWith2FaViewModel { get; set; }
        public LoginWithLNURLAuthViewModel LoginWithLNURLAuthViewModel { get; set; }
    }
}
