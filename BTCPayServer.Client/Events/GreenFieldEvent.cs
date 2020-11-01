using NBitcoin;
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
            Signature = key.SignMessage($"{Normalize(url)}_{GetPayload()}");
        }

        public bool VerifySignature(string url, BitcoinPubKeyAddress key)
        {
            return key.VerifyMessage($"{Normalize(url)}_{GetPayload()}", Signature);
        }

        public virtual string GetPayload()
        {
            return Normalize(JsonConvert.SerializeObject(Payload));
        }

        private string Normalize(string str)
        {
            return str.Replace(" ", "").Replace("    ", "").ToLowerInvariant();
        }
    }
}
