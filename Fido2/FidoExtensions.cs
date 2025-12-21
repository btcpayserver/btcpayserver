using BTCPayServer.Data;
using BTCPayServer.Fido2.Models;
using NBXplorer;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Fido2
{
    public static class Fido2Extensions
    {
        public static Fido2CredentialBlob GetFido2Blob(this Fido2Credential credential)
        {
            return credential.HasTypedBlob<Fido2CredentialBlob>().GetBlob() ?? new Fido2CredentialBlob();
        }
        public static void SetBlob(this Fido2Credential credential, Fido2CredentialBlob descriptor)
        {
            var current = credential.GetFido2Blob();
            var a = JObject.FromObject(current);
            var b = JObject.FromObject(descriptor);
            if (JObject.DeepEquals(a, b))
                return;
            credential.Type = Fido2Credential.CredentialType.FIDO2;
            credential.HasTypedBlob<Fido2CredentialBlob>().SetBlob(descriptor);
        }
    }
}
