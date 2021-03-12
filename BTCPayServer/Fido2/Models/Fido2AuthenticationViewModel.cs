using System.Collections.Generic;
using BTCPayServer.Data;

namespace BTCPayServer.U2F.Models
{
    public class Fido2AuthenticationViewModel
    {
        public List<Fido2Credential> Credentials { get; set; }
    }
}
