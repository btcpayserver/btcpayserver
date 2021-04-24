using Fido2NetLib.Objects;

namespace BTCPayServer.U2F.Models
{
    public class AddFido2CredentialViewModel
    {
        public AuthenticatorAttachment? AuthenticatorAttachment { get; set; }
        public string Name { get; set; }
    }

}
