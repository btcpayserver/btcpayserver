using System;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Newtonsoft.Json;

namespace BTCPayServer.Fido2.Models
{
    public class Fido2CredentialBlob
    {
        public class Base64UrlConverter : JsonConverter<byte[]>
        {
            private readonly Required _requirement = Required.DisallowNull;

            public Base64UrlConverter()
            {
            }

            public Base64UrlConverter(Required required = Required.DisallowNull)
            {
                _requirement = required;
            }

            public override void WriteJson(JsonWriter writer, byte[] value, JsonSerializer serializer)
            {
                writer.WriteValue(Base64Url.Encode(value));
            }

            public override byte[] ReadJson(JsonReader reader, Type objectType, byte[] existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                byte[] ret = null;

                if (null == reader.Value && _requirement == Required.AllowNull)
                    return ret;

                if (null == reader.Value)
                    throw new Fido2VerificationException("json value must not be null");
                if (Type.GetType("System.String") != reader.ValueType)
                    throw new Fido2VerificationException("json valuetype must be string");
                try
                {
                    ret = Base64Url.Decode((string)reader.Value);
                }
                catch (FormatException ex)
                {
                    throw new Fido2VerificationException("json value must be valid base64 encoded string", ex);
                }
                return ret;
            }
        }
        public class DescriptorClass
        {
            public DescriptorClass(byte[] credentialId)
            {
                Id = credentialId;
            }

            public DescriptorClass()
            {

            }

            /// <summary>
            /// This member contains the type of the public key credential the caller is referring to.
            /// </summary>
            [JsonProperty("type")]
            public string Type { get; set; } = "public-key";

            /// <summary>
            /// This member contains the credential ID of the public key credential the caller is referring to.
            /// </summary>
            [JsonConverter(typeof(Base64UrlConverter))]
            [JsonProperty("id")]
            public byte[] Id { get; set; }

            /// <summary>
            /// This OPTIONAL member contains a hint as to how the client might communicate with the managing authenticator of the public key credential the caller is referring to.
            /// </summary>
            [JsonProperty("transports", NullValueHandling = NullValueHandling.Ignore)]
            public string[] Transports { get; set; }

            public PublicKeyCredentialDescriptor ToFido2()
            {
                var str = JsonConvert.SerializeObject(this);
                return System.Text.Json.JsonSerializer.Deserialize<PublicKeyCredentialDescriptor>(str);
            }
        }
        public DescriptorClass Descriptor { get; set; }
        [JsonConverter(typeof(Base64UrlConverter))]
        public byte[] PublicKey { get; set; }
        [JsonConverter(typeof(Base64UrlConverter))]
        public byte[] UserHandle { get; set; }
        public uint SignatureCounter { get; set; }
        public string CredType { get; set; }
        public string AaGuid { get; set; }
    }
}
