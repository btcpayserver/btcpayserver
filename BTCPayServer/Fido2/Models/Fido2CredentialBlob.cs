using System;
using System.Buffers.Text;
using System.Text;
using System.Text.Json.Serialization;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Newtonsoft.Json;

namespace BTCPayServer.Fido2.Models
{
    public class Fido2CredentialBlob
    {
        public class Base64UrlConverter : Newtonsoft.Json.JsonConverter<byte[]>
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
                writer.WriteValue(Base64Url.EncodeToUtf8(value));
            }

            public override byte[] ReadJson(JsonReader reader, Type objectType, byte[] existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                byte[] ret;

                if (null == reader.Value && _requirement == Required.AllowNull)
                    return null;

                if (null == reader.Value)
                    throw new Fido2VerificationException("json value must not be null");
                if (Type.GetType("System.String") != reader.ValueType)
                    throw new Fido2VerificationException("json valuetype must be string");
                try
                {
                    var base64Url = (string)reader.Value;
                    var utf8 = Encoding.UTF8.GetBytes(base64Url);

                    var output = new byte[utf8.Length];
                    Base64Url.DecodeFromUtf8(
                        utf8,
                        output,
                        out _,
                        out var written
                    );
                    ret = output[..written];
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
            [Newtonsoft.Json.JsonConverter(typeof(Base64UrlConverter))]
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
        [Newtonsoft.Json.JsonConverter(typeof(Base64UrlConverter))]
        public byte[] PublicKey { get; set; }
        [Newtonsoft.Json.JsonConverter(typeof(Base64UrlConverter))]
        public byte[] UserHandle { get; set; }
        public uint SignatureCounter { get; set; }
        public string AaGuid { get; set; }
    }
}
