using System.Text;
using NBitcoin;
using NBitcoin.Crypto;
using NBitcoin.DataEncoders;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Events
{
    public class GreenFieldEvent<T> : IGreenFieldEvent
    {
        public string EventType { get; set; }

        [JsonIgnore]
        public object Payload
        {
            get
            {
                return PayloadParsed;
            }
            set
            {
                {
                    PayloadParsed = (T)value;
                }
            }
        }

        [JsonProperty("payload")] public T PayloadParsed { get; set; }
        public string Signature { get; set; }

        public void SetSignature(string url, Key key)
        {
            uint256 hash = new uint256(Hashes.SHA256(Encoding.UTF8.GetBytes(GetMessage(url))));
            Signature = Encoders.Hex.EncodeData(key.Sign(hash).ToDER());
        }

        public bool VerifySignature(string url, PubKey key)
        {
            uint256 hash = new uint256(Hashes.SHA256(Encoding.UTF8.GetBytes(GetMessage(url))));
            return key.Verify(hash, new ECDSASignature(Encoders.Hex.DecodeData(Signature)));
        }

        protected virtual string GetMessage(string url )
        {
            return Normalize($"{Normalize(url)}_{JsonConvert.SerializeObject(Payload)}");
        }

        private string Normalize(string str)
        {
            return str
                .Replace(" ", "")
                .Replace("\t", "")
                .Replace("\n", "")
                .Replace("\r", "")
                .ToLowerInvariant();
        }
    }
}
