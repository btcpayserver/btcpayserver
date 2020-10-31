using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Events
{
    public class GreenFieldEvent<T> : IGreenFieldEvent
    {
        public string EventType { get; set; }
        public object Payload { get; set; }

        [JsonIgnore]
        public T PayloadParsed { get{ return (T) Payload;} set { Payload = value; } }
        public string Signature { get; set; }
        
        public void SetSignature(string url, Key key)
        {
            Signature = key.SignMessage($"{url}_{GetPayload()}");
        }

        public bool VerifySignature(string url, PubKey key)
        {
            return key.VerifyMessage($"{url}_{GetPayload()}", Signature);
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
