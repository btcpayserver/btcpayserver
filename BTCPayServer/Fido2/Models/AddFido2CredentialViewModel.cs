using Fido2NetLib.Objects;

namespace BTCPayServer.Fido2.Models
{
    public class AddFido2CredentialViewModel
    {
        public AuthenticatorAttachment? AuthenticatorAttachment { get; set; }
        public string Name { get; set; }
    }

}
