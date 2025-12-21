using System.Collections.Generic;
using BTCPayServer.Data;

namespace BTCPayServer.Fido2.Models
{
    public class Fido2AuthenticationViewModel
    {
        public List<Fido2Credential> Credentials { get; set; }
    }
}
