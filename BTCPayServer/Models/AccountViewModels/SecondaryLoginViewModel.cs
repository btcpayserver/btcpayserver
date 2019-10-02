using BTCPayServer.U2F.Models;

namespace BTCPayServer.Models.AccountViewModels
{
    public class SecondaryLoginViewModel
    {
        public LoginWith2faViewModel LoginWith2FaViewModel { get; set; }
        public LoginWithU2FViewModel LoginWithU2FViewModel { get; set; }
    }
}