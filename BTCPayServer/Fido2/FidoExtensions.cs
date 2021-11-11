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
            var result = credential.Blob == null
                ? new Fido2CredentialBlob()
                : JObject.Parse(ZipUtils.Unzip(credential.Blob)).ToObject<Fido2CredentialBlob>();
            return result;
        }
        public static bool SetBlob(this Fido2Credential credential, Fido2CredentialBlob descriptor)
        {
            var original = new Serializer(null).ToString(credential.GetFido2Blob());
            var newBlob = new Serializer(null).ToString(descriptor);
            if (original == newBlob)
                return false;
            credential.Type = Fido2Credential.CredentialType.FIDO2;
            credential.Blob = ZipUtils.Zip(newBlob);
            return true;
        }



    }
}
