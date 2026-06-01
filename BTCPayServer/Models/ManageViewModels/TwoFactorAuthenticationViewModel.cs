using System.Collections.Generic;
using BTCPayServer.Data;

namespace BTCPayServer.Models.ManageViewModels
{
    public class TwoFactorAuthenticationViewModel
    {

        public bool IsAuthenticatorEnabled { get; set; }

        public List<Fido2Credential> Credentials { get; set; }

        public string LoginCode { get; set; }
    }
}
