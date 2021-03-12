using Fido2NetLib.Objects;
using NBXplorer;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Data
{
    public static class Fido2Extensions
    {    
        public static PublicKeyCredentialDescriptor GetDescriptor(this Fido2Credential credential)
        {
            var result = credential.DescriptorBlob == null
                ? new PublicKeyCredentialDescriptor()
                : JObject.Parse(ZipUtils.Unzip(credential.DescriptorBlob)).ToObject<PublicKeyCredentialDescriptor>();
            return result;
        }
        public static bool SetDescriptor(this Fido2Credential credential, PublicKeyCredentialDescriptor descriptor)
        {
            var original = new Serializer(null).ToString(credential.GetDescriptor());
            var newBlob = new Serializer(null).ToString(descriptor);
            if (original == newBlob)
                return false;
            credential.DescriptorBlob = ZipUtils.Zip(newBlob);
            return true;
        }
    
    }
}
