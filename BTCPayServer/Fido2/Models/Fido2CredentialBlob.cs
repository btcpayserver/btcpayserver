using Fido2NetLib;
using Fido2NetLib.Objects;
using Newtonsoft.Json;

namespace BTCPayServer.Fido2.Models
{
    public class Fido2CredentialBlob
    {
        public PublicKeyCredentialDescriptor Descriptor { get; set; }
        [JsonConverter(typeof(Base64UrlConverter))]
        public byte[] PublicKey { get; set; }
        [JsonConverter(typeof(Base64UrlConverter))]
        public byte[] UserHandle { get; set; }
        public uint SignatureCounter { get; set; }
        public string CredType { get; set; }
        public string AaGuid { get; set; }
    }
}
