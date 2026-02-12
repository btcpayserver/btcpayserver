using System.Text.Json.Serialization;
using Fido2NetLib;
using Fido2NetLib.Objects;

namespace BTCPayServer.Fido2.Models
{
    public class Fido2CredentialBlob
    {
        [JsonPropertyName("descriptor")]
        public PublicKeyCredentialDescriptor Descriptor { get; set; }
        [JsonConverter(typeof(Base64UrlConverter))]
        [JsonPropertyName("publicKey")]
        public byte[] PublicKey { get; set; }
        [JsonConverter(typeof(Base64UrlConverter))]
        [JsonPropertyName("userHandle")]
        public byte[] UserHandle { get; set; }
        [JsonPropertyName("signatureCounter")]
        public uint SignatureCounter { get; set; }
        [JsonPropertyName("aaGuid")]
        public string AaGuid { get; set; }
    }
}
