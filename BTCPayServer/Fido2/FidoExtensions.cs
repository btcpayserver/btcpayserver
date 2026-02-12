using BTCPayServer.Data;
using BTCPayServer.Fido2.Models;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Fido2
{
    public static class Fido2Extensions
    {
        public static Fido2CredentialBlob GetFido2Blob(this Fido2Credential credential)
        {
            var str = (credential.HasTypedBlob<JObject>().GetBlob() ?? new JObject()).ToString();
            return System.Text.Json.JsonSerializer.Deserialize<Fido2CredentialBlob>(str);
        }
        public static void SetBlob(this Fido2Credential credential, Fido2CredentialBlob descriptor)
        {
            var current = credential.GetFido2Blob();
            var a = System.Text.Json.JsonSerializer.Serialize(current);
            var b = System.Text.Json.JsonSerializer.Serialize(descriptor);
            if (a == b)
                return;
            credential.Type = Fido2Credential.CredentialType.FIDO2;
            credential.Blob2 = System.Text.Json.JsonSerializer.Serialize(descriptor);
        }
    }
}
